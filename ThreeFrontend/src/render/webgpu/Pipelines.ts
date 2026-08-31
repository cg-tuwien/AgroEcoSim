//Render pipelines for the scene and pick passes.

import { CONE_TRACE_WGSL } from './gi/shaders/coneTrace.wgsl';
import { GTAO_BLUR_WGSL, GTAO_WGSL, MESH_WGSL, PICK_WGSL, PREPASS_WGSL } from './shaders/mesh.wgsl';
import { VERTEX_LAYOUT } from './GeometryAtlas';
import { AO_FORMAT, DEPTH_BIAS_TOWARDS_CAMERA, DEPTH_COMPARE, DEPTH_FORMAT, GI_FORMAT, GBUFFER_DEPTH_FORMAT, GBUFFER_NORMAL_FORMAT, PICK_FORMAT, SAMPLE_COUNT, SHADOW_FORMAT } from './Targets';
import { PipelineVariant } from './InstanceBuffers';

export type PipelineSet = {
    /** Uniform + shadow map + comparison sampler. Scene pipelines only. */
    frameLayout: GPUBindGroupLayout;
    /** Uniform alone, for passes that must not read the shadow map - including the one that writes it. */
    frameLayoutBasic: GPUBindGroupLayout;
    instanceLayout: GPUBindGroupLayout;
    scene: Record<PipelineVariant, GPURenderPipeline>;
    pick: Record<PipelineVariant, GPURenderPipeline>;
    prepass: Record<PipelineVariant, GPURenderPipeline>;
    gtao: GPUComputePipeline;
    gtaoLayout: GPUBindGroupLayout;
    gtaoBlur: GPUComputePipeline;
    gtaoBlurLayout: GPUBindGroupLayout;
    coneTrace: GPUComputePipeline;
    coneTraceLayout: GPUBindGroupLayout;
};

const VARIANTS: PipelineVariant[] = ['opaqueFront', 'opaqueDouble', 'opaqueDoubleBias'];

function primitiveFor(variant: PipelineVariant): GPUPrimitiveState {
    return {
        topology: 'triangle-list',
        //three's default winding; nothing in this scene uses BackSide.
        frontFace: 'ccw',
        cullMode: variant === 'opaqueFront' ? 'back' : 'none',
    };
}

function depthStencilFor(variant: PipelineVariant, sampleCount: number): GPUDepthStencilState {
    const state: GPUDepthStencilState = {
        format: DEPTH_FORMAT,
        depthWriteEnabled: true,
        depthCompare: DEPTH_COMPARE,
    };
    if (variant === 'opaqueDoubleBias') {
        //Mirrors the obstacles' polygonOffsetFactor: -2. Obstacles sit flush on the terrain
        //they shade, so without this they z-fight with it. The magnitude is three's; the sign
        //comes from the depth range, because "towards the camera" is +1 under reversed Z and
        //-1 under a standard one. Getting it wrong does not fail - it just stops helping, and
        //the z-fighting it was there to cure comes back.
        state.depthBias = 0;
        state.depthBiasSlopeScale = 2 * DEPTH_BIAS_TOWARDS_CAMERA;
    }
    void sampleCount;
    return state;
}

export function createPipelines(device: GPUDevice, format: GPUTextureFormat): PipelineSet {
    const frameLayout = device.createBindGroupLayout({
        label: 'frame',
        entries: [
            {
                binding: 0,
                visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
                buffer: { type: 'uniform' },
            },
            {
                binding: 1,
                visibility: GPUShaderStage.FRAGMENT,
                texture: { sampleType: 'depth' },
            },
            {
                binding: 2,
                visibility: GPUShaderStage.FRAGMENT,
                sampler: { type: 'comparison' },
            },
            {
                binding: 3,
                visibility: GPUShaderStage.FRAGMENT,
                texture: { sampleType: 'float' },
            },
            {
                binding: 4,
                visibility: GPUShaderStage.FRAGMENT,
                sampler: { type: 'filtering' },
            },
            {
                //Cone-traced indirect light. Shares the AO sampler: both are half-resolution
                //and want the same bilinear upsample.
                binding: 5,
                visibility: GPUShaderStage.FRAGMENT,
                texture: { sampleType: 'float' },
            },
        ],
    });

    const instanceLayout = device.createBindGroupLayout({
        label: 'instances',
        entries: [{
            binding: 0,
            //Read in the vertex stage. WebGPU compatibility mode can report
            //maxStorageBuffersInVertexStage 0; the Instance struct is a flat run of vec4f
            //precisely so it could be reinterpreted as instance-stepped vertex attributes
            //if that ever needs a fallback.
            visibility: GPUShaderStage.VERTEX,
            buffer: { type: 'read-only-storage' },
        }],
    });

    //A pass cannot both sample a texture and render into it, so the shadow and pick passes
    //get a layout without the shadow bindings at all.
    const frameLayoutBasic = device.createBindGroupLayout({
        label: 'frame-basic',
        entries: [{
            binding: 0,
            visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
            buffer: { type: 'uniform' },
        }],
    });

    const layout = device.createPipelineLayout({ bindGroupLayouts: [frameLayout, instanceLayout] });
    const basicLayout = device.createPipelineLayout({ bindGroupLayouts: [frameLayoutBasic, instanceLayout] });
    const meshModule = device.createShaderModule({ label: 'mesh', code: MESH_WGSL });
    const pickModule = device.createShaderModule({ label: 'pick', code: PICK_WGSL });

    const scene = {} as Record<PipelineVariant, GPURenderPipeline>;
    const pick = {} as Record<PipelineVariant, GPURenderPipeline>;

    for (const variant of VARIANTS) {
        scene[variant] = device.createRenderPipeline({
            label: `scene:${variant}`,
            layout,
            vertex: { module: meshModule, entryPoint: 'vs', buffers: [VERTEX_LAYOUT] },
            fragment: { module: meshModule, entryPoint: 'fs', targets: [{ format }] },
            primitive: primitiveFor(variant),
            depthStencil: depthStencilFor(variant, SAMPLE_COUNT),
            multisample: { count: SAMPLE_COUNT },
        });

        pick[variant] = device.createRenderPipeline({
            label: `pick:${variant}`,
            layout: basicLayout,
            vertex: { module: pickModule, entryPoint: 'vs', buffers: [VERTEX_LAYOUT] },
            //Integer targets cannot blend, which is fine: the pass writes an id, not a colour.
            fragment: { module: pickModule, entryPoint: 'fs', targets: [{ format: PICK_FORMAT }] },
            primitive: primitiveFor(variant),
            depthStencil: depthStencilFor(variant, 1),
            multisample: { count: 1 },
        });
    }


    //Everything below this point belongs to a lighting tier above the default, and is built
    //on FIRST USE rather than at mount.
    //
    //Not an optimisation. Creating a pipeline compiles its shader in the driver, and mounting
    //used to compile three modules where it now has seven. A driver that chokes on any one of
    //the new ones would take the whole viewport down at startup, including the parity tier
    //that never runs them - which is exactly the shape of "WebGPU fails immediately". Built
    //lazily, a shader can only break the tier that actually asks for it, and which tier that
    //is says which shader.
    //
    //Bind group LAYOUTS stay eager: they compile nothing, and the resize path builds bind
    //groups from them before any tier has been chosen.
    let prepassCache: Record<PipelineVariant, GPURenderPipeline> | undefined;
    const makePrepass = () => {
        const prepassModule = device.createShaderModule({ label: 'prepass', code: PREPASS_WGSL });
        const built = {} as Record<PipelineVariant, GPURenderPipeline>;
        for (const variant of VARIANTS) {
            built[variant] = device.createRenderPipeline({
                label: `prepass:${variant}`,
                layout: basicLayout,
                vertex: { module: prepassModule, entryPoint: 'vs', buffers: [VERTEX_LAYOUT] },
                fragment: { module: prepassModule, entryPoint: 'fs', targets: [{ format: GBUFFER_NORMAL_FORMAT }, { format: GBUFFER_DEPTH_FORMAT }] },
                primitive: primitiveFor(variant),
                depthStencil: depthStencilFor(variant, 1),
                //No multisampling: averaging normals across an edge yields a direction that
                //belongs to neither surface.
                multisample: { count: 1 },
            });
        }
        return built;
    };

    const gtaoLayout = device.createBindGroupLayout({
        label: 'gtao',
        entries: [
            { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
            { binding: 1, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'unfilterable-float' } },
            { binding: 2, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'unfilterable-float' } },
            { binding: 3, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: AO_FORMAT } },
        ],
    });
    let gtaoCache: GPUComputePipeline | undefined;
    const makeGtao = () => device.createComputePipeline({
        label: 'gtao',
        layout: device.createPipelineLayout({ bindGroupLayouts: [gtaoLayout] }),
        compute: { module: device.createShaderModule({ label: 'gtao', code: GTAO_WGSL }), entryPoint: 'main' },
    });

    const gtaoBlurLayout = device.createBindGroupLayout({
        label: 'gtao-blur',
        entries: [
            { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
            { binding: 1, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'unfilterable-float' } },
            { binding: 2, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float' } },
            { binding: 3, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: AO_FORMAT } },
        ],
    });
    let gtaoBlurCache: GPUComputePipeline | undefined;
    const makeGtaoBlur = () => device.createComputePipeline({
        label: 'gtao-blur',
        layout: device.createPipelineLayout({ bindGroupLayouts: [gtaoBlurLayout] }),
        compute: { module: device.createShaderModule({ label: 'gtao-blur', code: GTAO_BLUR_WGSL }), entryPoint: 'main' },
    });

    const coneTraceLayout = device.createBindGroupLayout({
        label: 'cone-trace',
        entries: [
            { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
            { binding: 1, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'unfilterable-float' } },
            { binding: 2, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'unfilterable-float' } },
            { binding: 3, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float', viewDimension: '3d' } },
            { binding: 4, visibility: GPUShaderStage.COMPUTE, sampler: { type: 'filtering' } },
            { binding: 5, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: GI_FORMAT } },
        ],
    });
    let coneTraceCache: GPUComputePipeline | undefined;
    const makeConeTrace = () => device.createComputePipeline({
        label: 'cone-trace',
        layout: device.createPipelineLayout({ bindGroupLayouts: [coneTraceLayout] }),
        compute: { module: device.createShaderModule({ label: 'cone-trace', code: CONE_TRACE_WGSL }), entryPoint: 'main' },
    });

    return {
        frameLayout, frameLayoutBasic, instanceLayout, scene, pick,
        gtaoLayout, gtaoBlurLayout, coneTraceLayout,
        get prepass() { return (prepassCache ??= makePrepass()); },
        get gtao() { return (gtaoCache ??= makeGtao()); },
        get gtaoBlur() { return (gtaoBlurCache ??= makeGtaoBlur()); },
        get coneTrace() { return (coneTraceCache ??= makeConeTrace()); },
    };
}


/**
 * Pipelines targeting an offscreen texture, which may differ from the canvas in both format
 * and sample count. Built per call because offline renders are rare and their settings vary;
 * caching them would outlive the settings that produced them.
 */
export function createOffscreenPipelines(device: GPUDevice, format: GPUTextureFormat, sampleCount: number): Record<PipelineVariant, GPURenderPipeline> {
    //Must match the on-screen frame layout exactly, or the bind group built against that one
    //is not compatible with these pipelines.
    const frameLayout = device.createBindGroupLayout({
        entries: [
            { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
            { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'depth' } },
            { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'comparison' } },
            { binding: 3, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            { binding: 4, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            { binding: 5, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
        ],
    });
    const instanceLayout = device.createBindGroupLayout({
        entries: [{ binding: 0, visibility: GPUShaderStage.VERTEX, buffer: { type: 'read-only-storage' } }],
    });
    const layout = device.createPipelineLayout({ bindGroupLayouts: [frameLayout, instanceLayout] });
    const module = device.createShaderModule({ label: 'mesh-offscreen', code: MESH_WGSL });

    const out = {} as Record<PipelineVariant, GPURenderPipeline>;
    for (const variant of VARIANTS) {
        out[variant] = device.createRenderPipeline({
            label: `offscreen:${variant}`,
            layout,
            vertex: { module, entryPoint: 'vs', buffers: [VERTEX_LAYOUT] },
            fragment: { module, entryPoint: 'fs', targets: [{ format }] },
            primitive: primitiveFor(variant),
            depthStencil: depthStencilFor(variant, sampleCount),
            multisample: { count: sampleCount },
        });
    }
    return out;
}
