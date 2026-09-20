//The WebGPU viewport backend. Hand-written WGSL, no three anywhere in this tree.
//
//Its job is to be pixel-identical to the Three backend by default. Everything that decides
//what a frame looks like - geometry, colour, transforms, the draw list - comes from
//src/render/core, so the two backends cannot disagree except in the rasteriser itself.

import appstate from '../../appstate';
import {
    CameraPose, IViewportRenderer, ObstacleFrameInputs, ObstacleMappingInputs, PixelReadback,
    PlantFrameInputs, SeedFrameInputs, TerrainFrameInputs, VisibilityInputs,
} from '../IViewportRenderer';
import { Clicks, applyPick, toNDC } from '../core/Picking';
import { InstanceList, IFlags } from '../core/InstanceList';
import { buildPlants } from '../core/BuildScene';
import { Geom } from '../core/GeometryIds';
import { registerGeometry } from '../core/GeometryStore';
import { writeObstacleColors } from '../core/ObstacleColors';
//Side effect: fills the static geometry table. Imported here and not left to the Three
//adapter, which used to be the only thing populating it - so the WebGPU path silently
//depended on three having been loaded first.
import '../core/StaticGeometry';
import { createMat4, Mat4, identity, invertRigid, lookAt, multiply, orthographicZeroToOne, perspectiveZeroToOne, setPosition, scaleInPlace, transformDirection } from '../core/Mat4';
import { paletteColor } from '../core/SelectionPalette';
import { backgroundColor, neutralColor } from '../core/Theme';
import { fromHex, lerpHSL, linearToSrgb } from '../core/Color';
import { FrameLighting, HEMI_GROUND, HEMI_INTENSITY, HEMI_SKY, HEMI_WORLD_DIR, SHADOW_GRAZING_OFFSET_TEXELS, SHADOW_NORMAL_OFFSET_TEXELS, SHADOW_SIZE, shadowBounds } from '../core/Lighting';
import { Ray, rayFromNDC } from '../core/Ray';
import { Vec3 } from '../core/Vec3';

import { createGpuContext, GpuContext } from './Device';
import { OcclusionTargets, Targets, SAMPLE_COUNT, DEPTH_CLEAR_VALUE, DEPTH_FORMAT } from './Targets';
import { forceOpaque, reportOpaque, warnIfFlat } from '../offline/opaque';
import { VoxelField } from './gi/VoxelField';
import { VoxelDebug } from './gi/VoxelDebug';
import { DEFAULT_VOXEL_DEBUG, GIFrameInputs, MAX_SAMPLE_BATCHES, SAMPLES_PER_INVOCATION, VoxelDebugOptions, scatterTriangleArea } from '../core/VoxelGrid';
import { GeometryAtlas } from './GeometryAtlas';
import { obstacleMappingGeometry } from '../core/ObstacleMappingGeometry';
import { InstanceBuffers, INSTANCE_FLOATS } from './InstanceBuffers';
import { createPipelines, createOffscreenPipelines, PipelineSet } from './Pipelines';
import { OrbitCamera } from './OrbitCamera';
import { OfflineSizeError } from '../offline/types';

//viewProj, view, lightViewProj, sky, ground, hemiDir, sunDirView, sunColor, params, projParams
/** Told why the device went away, so the fallback can say more than "it failed". */
export type DeviceLostHandler = (info?: { reason?: string; message?: string }) => void;

/** The two bind groups that drive one occlusion set's compute passes. */
interface OcclusionBinds {
    ao: GPUBindGroup;
    blur: GPUBindGroup;
}

//Three 4x4 matrices, then sky, ground, hemiDir, sunDirView, sunColor, params, projParams,
//sunDirWorld, shadowParams. Growing this without growing the buffer is rejected at draw time
//with "bound with size N ... is too small", naming the pipeline rather than the field added.
export const FRAME_FLOATS = 16 * 3 + 4 * 9;

/**
 * The order `writeFrameUniform` packs the frame uniform, which MUST match `struct Frame`.
 *
 * A mismatch is completely silent - no validation error, no warning, just members reading each
 * other's floats. Adding `sunDirWorld` and `shadowParams` in front of `projParams` in the WGSL
 * while appending them after it here made the ambient-occlusion lookup divide the fragment
 * coordinate by (4, 0.0005), so it sampled the texture's corner and returned 1 everywhere.
 * Occlusion, indirect light and the shadow bias all went dead at once and every one of them
 * looked like an unrelated bug in its own pass.
 */
export const FRAME_MEMBERS = [
    'viewProj', 'view', 'lightViewProj',
    'hemiSky', 'hemiGround', 'hemiDir', 'sunDirView', 'sunColor',
    'params', 'projParams', 'sunDirWorld', 'shadowParams',
] as const;
const FRAME_BYTES = FRAME_FLOATS * 4;


const TERRAIN_COLOR = fromHex('#573600');

/**
 * The clear colour, sRGB-ENCODED.
 *
 * The surface format is non-sRGB, so whatever is written here reaches the screen verbatim.
 * three does the same thing: WebGLBackground pushes the clear colour through the unlit
 * (sRGB) path, so it is the raw CSS byte value. Clearing with the linear value instead makes
 * the background a few units too dark - 243 where the WebGL viewport shows 250.
 */
const CLEAR_COLOR = {
    r: linearToSrgb(backgroundColor.r),
    g: linearToSrgb(backgroundColor.g),
    b: linearToSrgb(backgroundColor.b),
};
//Matches helpers/Plant.ts's `neutral`, used for primitives without stats.
const NEUTRAL = lerpHSL(neutralColor, backgroundColor, 0.1);

export class WebGPURenderer implements IViewportRenderer {
    readonly kind = 'webgpu' as const;

    private gpu: GpuContext | undefined;
    private targets: Targets | undefined;
    private pipelines: PipelineSet | undefined;
    private atlas: GeometryAtlas | undefined;
    private container: HTMLElement | undefined;
    private onDeviceLost: DeviceLostHandler | undefined;

    private plants = new InstanceBuffers(undefined as unknown as GPUDevice);
    private statics = new InstanceBuffers(undefined as unknown as GPUDevice);
    private plantList = new InstanceList();
    private staticList = new InstanceList();

    private frameBuffer: GPUBuffer | undefined;
    private frameData = new Float32Array(FRAME_FLOATS);
    private frameBind: GPUBindGroup | undefined;
    /** Uniform only, for the passes that must not sample the shadow map. */
    private frameBindBasic: GPUBindGroup | undefined;
    private plantBind: GPUBindGroup | undefined;
    private staticBind: GPUBindGroup | undefined;

    camera = new OrbitCamera();
    /** Tier 0 until the host says otherwise, so the default look is the historical one. */
    private lighting: FrameLighting = {
        shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS,
        hemiSky: { r: HEMI_SKY.r * HEMI_INTENSITY, g: HEMI_SKY.g * HEMI_INTENSITY, b: HEMI_SKY.b * HEMI_INTENSITY },
        hemiGround: { r: HEMI_GROUND.r * HEMI_INTENSITY, g: HEMI_GROUND.g * HEMI_INTENSITY, b: HEMI_GROUND.b * HEMI_INTENSITY },
        ambientOcclusion: false,
        voxelGI: false,
        bounces: 1,
        leafTranslucency: 0,
        sunDirection: { x: 0, y: 1, z: 0 },
        sunColor: { r: 0, g: 0, b: 0 },
        shadows: false,
    };
    private aoBuffer: GPUBuffer | undefined;
    private aoBinds: OcclusionBinds | undefined;

    //Voxel GI. Allocated lazily: the field is 128^3 and there is no reason to pay for it on a
    //machine that never turns the overlay or a GI tier on.
    private voxels: VoxelField | undefined;
    private voxelDebug: VoxelDebug | undefined;
    private voxelOptions: VoxelDebugOptions = DEFAULT_VOXEL_DEBUG;
    private showVoxels = false;
    private giInputs: GIFrameInputs = { fieldSizeX: 1, fieldSizeZ: 1, height: 1 };
    /** Set whenever the draw lists change, so the field is rebuilt exactly once per change. */
    private voxelsDirty = true;
    /** rAF handle for the coalesced render; 0 when none is queued. */
    private renderPending = 0;
    private traceBuffer: GPUBuffer | undefined;
    //projParams, settings, gridOrigin, gridCell, sunDir, then a 4x4 invView. The order
    //MUST match struct Trace; see FRAME_MEMBERS for what a mismatch costs.
    private traceData = new Float32Array(4 * 5 + 16);
    private traceBind: GPUBindGroup | undefined;
    private traceBoundView: GPUTextureView | undefined;
    private traceBoundGi: GPUTextureView | undefined;
    private voxelSampler: GPUSampler | undefined;
    private invViewScratch: Mat4 = createMat4();
    private aoData = new Float32Array(8);
    private lightViewProj: Mat4 = createMat4();
    private lightViewScratch: Mat4 = createMat4();
    private lightProjScratch: Mat4 = createMat4();
    private scratch: Mat4 = createMat4();
    private pickScratch: Mat4 = createMat4();
    private disposed = false;
    private pickInFlight = false;

    private visibility: VisibilityInputs = { terrain: true, seeds: true, obstacles: true };

    constructor(onDeviceLost?: DeviceLostHandler) {
        this.onDeviceLost = onDeviceLost;
    }

    //--- lifecycle -------------------------------------------------------------------------

    /** The mount step in progress, reported if the device is lost while mounting. */
    private mountStage = 'not started';

    async mount(container: HTMLElement) {
        this.container = container;

        const canvas = document.createElement('canvas');
        canvas.style.position = 'absolute';
        canvas.style.inset = '0';
        canvas.style.width = '100%';
        canvas.style.height = '100%';
        container.appendChild(canvas);

        const gpu = await createGpuContext(canvas, info => {
            console.error(`WebGPU device lost during/after mount stage "${this.mountStage}".`);
            this.onDeviceLost?.(info);
        });
        if (!gpu) {
            container.removeChild(canvas);
            throw new Error('WebGPU is not available');
        }

        //Each step is labelled because a device lost during mount reports nothing about where
        //it happened, and "WebGPU fails immediately" is otherwise indistinguishable between a
        //bad format, a shader the driver rejects, and a context that never configured. The
        //last label logged is the step that did it.
        const stage = (name: string) => { this.mountStage = name; };

        this.gpu = gpu;
        stage('targets');
        this.targets = new Targets(gpu.device, gpu.format);
        stage('pipelines');
        this.pipelines = createPipelines(gpu.device, gpu.format);
        stage('geometry atlas');
        this.atlas = new GeometryAtlas(gpu.device);
        this.plants = new InstanceBuffers(gpu.device);
        this.statics = new InstanceBuffers(gpu.device);
        stage('frame bindings');

        this.frameBuffer = gpu.device.createBuffer({
            label: 'frame-uniform',
            size: FRAME_BYTES,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.frameBind = gpu.device.createBindGroup({
            layout: this.pipelines.frameLayout,
            entries: [
                { binding: 0, resource: { buffer: this.frameBuffer } },
                { binding: 1, resource: this.targets.shadowView! },
                { binding: 2, resource: this.targets.shadowSampler! },
                { binding: 3, resource: this.targets.occlusion!.aoBlurView },
                { binding: 4, resource: this.targets.aoSampler! },
                { binding: 5, resource: this.targets.occlusion!.giView },
            ],
        });
        this.frameBindBasic = gpu.device.createBindGroup({
            layout: this.pipelines.frameLayoutBasic,
            entries: [{ binding: 0, resource: { buffer: this.frameBuffer } }],
        });

        this.traceBuffer = gpu.device.createBuffer({
            label: 'cone-trace-uniform',
            size: this.traceData.byteLength,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        //Trilinear across mips: the cone trace picks a fractional level from its diameter, and
        //snapping that to an integer level makes the bounce band visibly with distance.
        this.voxelSampler = gpu.device.createSampler({
            magFilter: 'linear', minFilter: 'linear', mipmapFilter: 'linear',
        });
        this.aoBuffer = gpu.device.createBuffer({
            label: 'gtao-uniform',
            size: this.aoData.byteLength,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.rebuildAoBind();

        this.attachInput(canvas);
        this.resize(container.clientWidth || window.innerWidth,
                    container.clientHeight || window.innerHeight,
                    window.devicePixelRatio || 1);
    }

    dispose() {
        this.disposed = true;
        //Cancel the coalesced render before the device goes away.
        if (this.renderPending !== 0) { cancelAnimationFrame(this.renderPending); this.renderPending = 0; }
        this.detachInput();
        this.targets?.destroy();
        this.atlas?.destroy();
        this.plants.destroy();
        this.statics.destroy();
        this.obstacleIndexVersions.clear();
        this.frameBuffer?.destroy();
        this.aoBuffer?.destroy();
        this.traceBuffer?.destroy();

        const canvas = this.gpu?.canvas;
        if (canvas && canvas.parentNode === this.container) this.container?.removeChild(canvas);
        //Destroying resolves device.lost with reason 'destroyed', which Device.ts ignores.
        this.gpu?.device.destroy();

        this.gpu = undefined;
        this.targets = undefined;
        this.pipelines = undefined;
        this.container = undefined;
    }

    resize(cssWidth: number, cssHeight: number, dpr: number) {
        if (!this.gpu || !this.targets || cssWidth <= 0 || cssHeight <= 0) return;
        const w = Math.round(cssWidth * dpr);
        const h = Math.round(cssHeight * dpr);
        this.gpu.canvas.width = w;
        this.gpu.canvas.height = h;
        this.targets.resize(w, h);
        //The occlusion targets were recreated, so the bind groups referencing them are stale.
        this.rebuildAoBind();
        this.rebuildFrameBind();
        this.logTargetMemory('resize');
        this.renderOnce();
    }

    /** Bind groups for one occlusion set. Both passes share the one uniform buffer. */
    private makeOcclusionBinds(occ: OcclusionTargets): OcclusionBinds {
        const device = this.gpu!.device;
        return {
            ao: device.createBindGroup({
                layout: this.pipelines!.gtaoLayout,
                entries: [
                    { binding: 0, resource: { buffer: this.aoBuffer! } },
                    { binding: 1, resource: occ.normalView },
                    { binding: 2, resource: occ.linearDepthView },
                    { binding: 3, resource: occ.aoView },
                ],
            }),
            blur: device.createBindGroup({
                layout: this.pipelines!.gtaoBlurLayout,
                entries: [
                    { binding: 0, resource: { buffer: this.aoBuffer! } },
                    { binding: 1, resource: occ.linearDepthView },
                    { binding: 2, resource: occ.aoView },
                    { binding: 3, resource: occ.aoBlurView },
                ],
            }),
        };
    }

    private rebuildAoBind() {
        if (!this.gpu || !this.pipelines || !this.targets?.occlusion || !this.aoBuffer) return;
        this.aoBinds = this.makeOcclusionBinds(this.targets.occlusion);
    }

    private rebuildFrameBind() {
        if (!this.gpu || !this.pipelines || !this.frameBuffer || !this.targets?.occlusion) return;
        this.frameBind = this.gpu.device.createBindGroup({
            layout: this.pipelines.frameLayout,
            entries: [
                { binding: 0, resource: { buffer: this.frameBuffer } },
                { binding: 1, resource: this.targets.shadowView! },
                { binding: 2, resource: this.targets.shadowSampler! },
                { binding: 3, resource: this.targets.occlusion.aoBlurView },
                { binding: 4, resource: this.targets.aoSampler! },
                { binding: 5, resource: this.targets.occlusion.giView },
            ],
        });
    }

    //--- frame -----------------------------------------------------------------------------

    /**
     * @param renderWidth  Size of the target being rendered into, which is the canvas for the
     *                     viewport but an arbitrary offline resolution for `renderToPixels`.
     *                     The occlusion lookup divides gl_FragCoord by it, so passing the
     *                     canvas size while rendering a 4K still would misplace every sample.
     */
    private writeFrameUniform(viewProj: Mat4, view: Mat4, renderWidth?: number, renderHeight?: number) {
        const f = this.frameData;
        const light = this.lighting;

        f.set(viewProj, 0);
        f.set(view, 16);
        f.set(this.lightViewProj, 32);

        //Sky and ground carry the intensity. three multiplies them by PI (useLegacyLights)
        //and BRDF_Lambert divides by PI; both are omitted because they cancel exactly.
        f[48] = light.hemiSky.r;
        f[49] = light.hemiSky.g;
        f[50] = light.hemiSky.b;
        f[51] = 1;
        f[52] = light.hemiGround.r;
        f[53] = light.hemiGround.g;
        f[54] = light.hemiGround.b;
        f[55] = 1;

        //three's uniforms.direction: the light's world position as a direction, transformed
        //into view space and normalized.
        const hemi = transformDirection(view, HEMI_WORLD_DIR);
        f[56] = hemi.x; f[57] = hemi.y; f[58] = hemi.z; f[59] = 0;

        const sun = transformDirection(view, light.sunDirection);
        f[60] = sun.x; f[61] = sun.y; f[62] = sun.z; f[63] = 0;

        //Black at Tier 0, which is what keeps the result identical to the WebGL renderer.
        f[64] = light.sunColor.r; f[65] = light.sunColor.g; f[66] = light.sunColor.b; f[67] = 1;

        //params.x: how much the brighter face of a lamina bleeds into the darker one. Zero
        //unless the translucency switch is on, and the shader multiplies the whole term by it,
        //so the default stays bit-identical. params.y is spare - both used to describe the
        //shadow map, which neither backend has any more.
        f[68] = light.leafTranslucency;
        f[69] = 0;
        f[70] = light.ambientOcclusion ? 1 : 0;
        f[71] = (light.voxelGI || light.shadows) ? 1 : 0;

        //tan(fovY/2) scaled by aspect, for rebuilding a view position from a depth value.
        const rw = renderWidth ?? this.targets?.width ?? 1;
        const rh = renderHeight ?? this.targets?.height ?? 1;
        const tanHalf = Math.tan((this.camera.fovY * Math.PI) / 360);
        f[72] = tanHalf * (rw / Math.max(1, rh));
        f[73] = tanHalf;
        f[74] = rw;
        f[75] = rh;

        //Direction TO the sun in world space, for the shadow lookup's normal offset.
        f[76] = light.sunDirection.x; f[77] = light.sunDirection.y; f[78] = light.sunDirection.z; f[79] = 0;

        //One shadow texel in WORLD units: the ortho frustum spans 2*radius across the map.
        const bounds = shadowBounds(appstate.fieldSizeX.peek(), appstate.fieldSizeZ.peek());
        f[80] = (2 * bounds.radius) / (this.targets?.shadowSize ?? SHADOW_SIZE);
        f[81] = light.shadowBiasTexels;
        f[82] = SHADOW_GRAZING_OFFSET_TEXELS;
        //Depth bias in normalised units: a world offset divided by the shadow camera's depth
        //range, which is near 0.1 to radius*4 - the same conversion the Three path makes.
        f[83] = (f[80] * light.shadowBiasTexels) / Math.max(1e-6, bounds.radius * 4 - 0.1);

        this.gpu!.device.queue.writeBuffer(this.frameBuffer!, 0, f.buffer, 0, FRAME_BYTES);
    }


    /**
     * Fills the depth+normal buffer and computes ambient occlusion from it.
     *
     * Both are skipped entirely at the lower tiers, which is what keeps their cost - and
     * their influence on the image - at exactly zero.
     */
    /**
     * Whether the depth+normal gbuffer is needed this frame.
     *
     * Not just occlusion. The cone trace reconstructs a world position per pixel from this
     * buffer, and the sun is traced from that position - so shadows need it too. Gating the
     * prepass on occlusion alone left the gbuffer holding whatever camera last wrote it, so
     * with occlusion off the shadows belonged to a previous view: none at one zoom level,
     * correct after zooming in, and "fixed" by toggling occlusion, which happened to write the
     * buffer once.
     */
    private needsPrepass(): boolean {
        return this.lighting.ambientOcclusion || this.lighting.voxelGI || this.lighting.shadows;
    }

    private renderOcclusion(occ: OcclusionTargets, binds: OcclusionBinds, frameBindBasic: GPUBindGroup) {
        if (!this.gpu || !this.pipelines || !this.needsPrepass()) return;
        if (!this.atlas!.vertexBuffer || !this.atlas!.indexBuffer) return;

        const { device } = this.gpu;
        const encoder = device.createCommandEncoder();

        const prepass = encoder.beginRenderPass({
            colorAttachments: [
                {
                    view: occ.normalView,
                    clearValue: { r: 0, g: 0, b: 0, a: 0 },
                    loadOp: 'clear',
                    storeOp: 'store',
                },
                {
                    //Zero distance means "nothing here", which the occlusion pass skips.
                    view: occ.linearDepthView,
                    clearValue: { r: 0, g: 0, b: 0, a: 0 },
                    loadOp: 'clear',
                    storeOp: 'store',
                },
            ],
            depthStencilAttachment: {
                view: occ.gbufferDepthView,
                depthClearValue: DEPTH_CLEAR_VALUE,
                depthLoadOp: 'clear',
                depthStoreOp: 'store',
            },
        });
        prepass.setVertexBuffer(0, this.atlas!.vertexBuffer);
        prepass.setIndexBuffer(this.atlas!.indexBuffer, 'uint32');
        prepass.setBindGroup(0, frameBindBasic);
        this.drawSetWith(prepass, this.statics, this.staticBind, this.pipelines.prepass);
        this.drawSetWith(prepass, this.plants, this.plantBind, this.pipelines.prepass);
        prepass.end();

        //The gbuffer is written above for anything that needs it; the horizon search below is
        //only for occlusion.
        if (!this.lighting.ambientOcclusion) {
            device.queue.submit([encoder.finish()]);
            return;
        }

        this.writeAoUniform(occ);
        const compute = encoder.beginComputePass();
        const groupsX = Math.ceil(occ.aoWidth / 8);
        const groupsY = Math.ceil(occ.aoHeight / 8);
        compute.setPipeline(this.pipelines.gtao);
        compute.setBindGroup(0, binds.ao);
        compute.dispatchWorkgroups(groupsX, groupsY);
        //Separate dispatch, not just a second setPipeline: the blur reads the texture the
        //first dispatch writes, and only a dispatch boundary orders those.
        compute.setPipeline(this.pipelines.gtaoBlur);
        compute.setBindGroup(0, binds.blur);
        compute.dispatchWorkgroups(groupsX, groupsY);
        compute.end();

        device.queue.submit([encoder.finish()]);
    }

    private writeAoUniform(occ: OcclusionTargets) {
        if (!this.gpu) return;
        const aspect = occ.width / Math.max(1, occ.height);
        const tanHalf = Math.tan((this.camera.fovY * Math.PI) / 360);
        const f = this.aoData;
        f[0] = tanHalf * aspect;
        f[1] = tanHalf;
        f[2] = occ.aoWidth;
        f[3] = occ.aoHeight;
        //Radius in world units. The field is ~10 m across and organs are centimetres, so a
        //quarter of a metre is roughly "one leaf cluster".
        f[4] = 0.25;
        f[5] = 1.4;                      // intensity
        f[6] = occ.width;
        f[7] = occ.height;
        this.gpu.device.queue.writeBuffer(this.aoBuffer!, 0, f.buffer, 0, f.byteLength);
    }

    /** Draws every bucket with whichever pipeline is already bound. */
    private drawAll(pass: GPURenderPassEncoder, set: InstanceBuffers, bind: GPUBindGroup | undefined) {
        if (!bind || set.count === 0) return;
        pass.setBindGroup(1, bind);
        for (const bucket of set.buckets)
            pass.drawIndexed(bucket.slice.indexCount, bucket.instanceCount,
                             bucket.slice.firstIndex, bucket.slice.baseVertex, bucket.firstInstance);
    }

    /**
     * Coalesced to one render per animation frame; see the note on the Three renderer's.
     *
     * It matters more here: a render is the shadow map, the depth+normal prepass, occlusion,
     * the cone trace and the visible pass, and a lighting change also re-injects the whole
     * density field. Two of those per simulated timestep is what a growing canopy cannot
     * afford.
     */
    renderOnce = () => {
        if (this.disposed || !this.gpu || !this.targets?.colorView || !this.pipelines) return;
        if (this.renderPending !== 0) return;
        this.renderPending = requestAnimationFrame(() => {
            this.renderPending = 0;
            this.renderFrame();
        });
    };

    /**
     * Everything the depth+normal prepass, the occlusion pass and the cone trace read, brought
     * up to date BEFORE any of them runs.
     *
     * The ordering is the whole point. The prepass draws through `frameBindBasic`, which is the
     * shared frame uniform, so if the camera is written after it the prepass renders from
     * whatever matrix was there last - the previous frame's camera, or, right after a hover,
     * the 1x1 skewed PICK matrix. Shadows and occlusion then appeared to follow the mouse, or
     * lagged a frame and "unfroze" when the animation advanced. It also drew with stale
     * instance buffers, which is where the phantom terrain-coloured leaves came from.
     *
     * Both the on-screen and the offline paths call this, so there is one order rather than two
     * that have to agree - the offline one happened to be right, which is why every offscreen
     * measurement looked correct while the viewport was visibly wrong.
     */
    private prepareFrame(viewProj: Mat4, view: Mat4, renderWidth?: number, renderHeight?: number) {
        this.atlas!.flush();
        this.rebindInstances();
        this.writeFrameUniform(viewProj, view, renderWidth, renderHeight);
        this.buildVoxelField();
    }

    private renderFrame() {
        if (this.disposed || !this.gpu || !this.targets?.colorView || !this.pipelines) return;
        const { device, context } = this.gpu;

        const aspect = this.targets.width / Math.max(1, this.targets.height);
        const view = this.camera.view();
        this.prepareFrame(this.camera.viewProjection(aspect), view);

        this.renderOcclusion(this.targets!.occlusion!, this.aoBinds!, this.frameBindBasic!);
        this.renderConeTrace(this.targets!.occlusion!, view);

        const encoder = device.createCommandEncoder();
        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: this.targets.colorView,
                resolveTarget: context.getCurrentTexture().createView(),
                //The canvas format is non-sRGB, so this is the raw CSS byte value, exactly
                //what WebGLBackground clears to.
                clearValue: { r: CLEAR_COLOR.r, g: CLEAR_COLOR.g, b: CLEAR_COLOR.b, a: 1 },
                loadOp: 'clear',
                storeOp: 'store',
            }],
            depthStencilAttachment: {
                view: this.targets.depthView!,
                depthClearValue: DEPTH_CLEAR_VALUE,
                depthLoadOp: 'clear',
                depthStoreOp: 'store',
            },
        });

        //The pass still runs with nothing to draw: that is what clears the canvas on the
        //first frame, before any geometry has been registered.
        if (this.atlas!.vertexBuffer && this.atlas!.indexBuffer) {
            pass.setVertexBuffer(0, this.atlas!.vertexBuffer);
            pass.setIndexBuffer(this.atlas!.indexBuffer, 'uint32');
            pass.setBindGroup(0, this.frameBind!);

            this.drawSet(pass, this.statics, this.staticBind, false);
            this.drawSet(pass, this.plants, this.plantBind, false);
        }

        this.drawVoxelOverlay(pass, this.gpu.format, SAMPLE_COUNT, DEPTH_FORMAT,
                              this.targets.width / Math.max(1, this.targets.height));

        pass.end();
        device.queue.submit([encoder.finish()]);
    }

    //--- voxel GI ------------------------------------------------------------------------

    /**
     * Rebuilds the density field when something it depends on changed.
     *
     * Gated on a dirty flag rather than rebuilt per frame: injection walks every triangle in
     * the scene, so at 50 fps playback it would cost more than everything else combined, and
     * the field only changes when the draw lists do.
     */
    private buildVoxelField() {
        if (!this.gpu || !this.atlas || !this.needsVoxels()) return;
        if (!this.voxels) {
            this.voxels = new VoxelField(this.gpu.device);
            this.voxelsDirty = true;
        }
        if (!this.voxelsDirty) return;

        this.voxels.setWorld(this.giInputs);
        this.voxels.setBounces(this.lighting.bounces);
        const light = this.lighting;
        //The same values the frame uniform carries, so a voxel is lit by the hemisphere the
        //surfaces see rather than by a second, differently-scaled one.
        //The flag now means "the sun contributes", not "a shadow map is valid".
        this.voxels.setLight(light.sunDirection, light.sunColor, light.hemiSky, light.hemiGround,
                             light.shadows);
        const encoder = this.gpu.device.createCommandEncoder({ label: 'voxel-build' });
        this.voxels.build(encoder, this.atlas, [this.statics, this.plants],
                          this.targets!.shadowView!, this.targets!.shadowSampler!);
        this.gpu.device.queue.submit([encoder.finish()]);
        this.voxelsDirty = false;
    }

    private needsVoxels(): boolean {
        //Shadows come from the field now, so they need it built too.
        return this.showVoxels || this.lighting.voxelGI || this.lighting.shadows;
    }

    /**
     * Cone-traces the density field into a half-resolution indirect-light buffer.
     *
     * Runs off the same depth+normal prepass GTAO uses, so enabling GI costs one more compute
     * pass rather than another geometry pass. Half resolution because a diffuse bounce is
     * low-frequency by construction - the sharp detail comes from the shadow map and the
     * screen-space AO, which is the whole reason the lighting is split across three scales.
     */
    private renderConeTrace(occ: OcclusionTargets, view: Mat4) {
        if (!this.gpu || !this.pipelines || !this.voxels?.view) return;
        if (!this.lighting.shadows && !this.lighting.voxelGI) return;

        const device = this.gpu.device;
        this.writeTraceUniform(occ, view);
        if (!this.traceBind || this.traceBoundView !== this.voxels.view || this.traceBoundGi !== occ.giView) {
            this.traceBind = device.createBindGroup({
                layout: this.pipelines.coneTraceLayout,
                entries: [
                    { binding: 0, resource: { buffer: this.traceBuffer! } },
                    { binding: 1, resource: occ.normalView },
                    { binding: 2, resource: occ.linearDepthView },
                    { binding: 3, resource: this.voxels.view },
                    { binding: 4, resource: this.voxelSampler! },
                    { binding: 5, resource: occ.giView },
                ],
            });
            this.traceBoundView = this.voxels.view;
            this.traceBoundGi = occ.giView;
        }

        const encoder = device.createCommandEncoder({ label: 'cone-trace' });
        const pass = encoder.beginComputePass({ label: 'cone-trace' });
        pass.setPipeline(this.pipelines.coneTrace);
        pass.setBindGroup(0, this.traceBind);
        pass.dispatchWorkgroups(Math.ceil(occ.aoWidth / 8), Math.ceil(occ.aoHeight / 8));
        pass.end();
        device.queue.submit([encoder.finish()]);
    }

    private writeTraceUniform(occ: OcclusionTargets, view: Mat4) {
        const grid = this.voxels!.grid;
        const aspect = occ.width / Math.max(1, occ.height);
        const tanHalf = Math.tan((this.camera.fovY * Math.PI) / 360);
        const f = this.traceData;
        f[0] = tanHalf * aspect;
        f[1] = tanHalf;
        f[2] = occ.aoWidth;
        f[3] = occ.aoHeight;
        f[4] = occ.width;
        f[5] = occ.height;
        //Cone length. Beyond a few metres a cone in this scene is looking at sky anyway, and
        //every extra metre costs steps in every cone of every pixel.
        f[6] = 4;
        //Indirect intensity, and the gate on the five diffuse cones: shadows alone want the
        //sun trace without paying for them.
        f[7] = this.lighting.voxelGI ? 1 : 0;
        f[8] = grid.origin.x; f[9] = grid.origin.y; f[10] = grid.origin.z; f[11] = grid.dim;
        f[12] = grid.cell.x; f[13] = grid.cell.y; f[14] = grid.cell.z; f[15] = 0;
        //Direction TO the sun, and whether it is worth tracing at all.
        const sun = this.lighting.sunDirection;
        const sunLit = this.lighting.shadows ? 1 : 0;
        f[16] = sun.x; f[17] = sun.y; f[18] = sun.z; f[19] = sunLit;
        //The gbuffer is in view space and the field is in world space; this takes it back.
        invertRigid(view, this.invViewScratch);
        f.set(this.invViewScratch, 20);
        this.gpu!.device.queue.writeBuffer(this.traceBuffer!, 0, f.buffer, 0, f.byteLength);
    }

    private drawVoxelOverlay(pass: GPURenderPassEncoder, format: GPUTextureFormat, sampleCount: number,
                             depthFormat: GPUTextureFormat, aspect: number) {
        if (!this.showVoxels || !this.gpu || !this.voxels?.view) return;
        if (!this.voxelDebug) this.voxelDebug = new VoxelDebug(this.gpu.device);
        this.voxelDebug.draw(pass, format, sampleCount, depthFormat, this.voxels.view, this.voxels.grid,
                             this.camera.getPose(), this.camera.fovY, aspect, this.voxelOptions);
    }

    setVoxelDebug(show: boolean, options: VoxelDebugOptions, inputs: GIFrameInputs) {
        const worldChanged = inputs.fieldSizeX !== this.giInputs.fieldSizeX
            || inputs.fieldSizeZ !== this.giInputs.fieldSizeZ
            || inputs.height !== this.giInputs.height;
        this.giInputs = inputs;
        this.voxelOptions = options;
        if (worldChanged) this.voxelsDirty = true;
        //Turning the overlay on has to force a rebuild: the field was never built while it
        //was off, so whatever the texture holds is from a previous scene or is empty.
        if (show && !this.showVoxels) this.voxelsDirty = true;
        this.showVoxels = show;
        this.renderOnce();
    }

    /** For the visual harness: mean radiance over the grid, the direct measure of a bounce. */
    async readVoxelMeanRadiance() {
        if (!this.voxels) return undefined;
        return this.voxels.readMeanRadiance();
    }

    /** For the visual harness: how many cells the compaction pass found. */
    async readVoxelOccupancy(): Promise<number | undefined> {
        if (!this.voxels) return undefined;
        return this.voxels.readOccupiedCount();
    }

    /** For the visual harness: the raw per-cell leaf area, in square metres. */
    async readVoxelArea(): Promise<Float32Array | undefined> {
        if (!this.voxels) return undefined;
        return this.voxels.readArea();
    }

    /**
     * The same scatter, run on the CPU over the same buffers.
     *
     * The GPU field cannot be unit-tested - jsdom has no device - so the analytic property
     * that matters (area in equals area out, cell for cell) is checked by running the
     * reference from core/VoxelGrid against the identical inputs and diffing. Any divergence
     * is a bug in the WGSL port, since the algorithm itself is covered by unit tests.
     */
    debugVoxelReference(): Float32Array | undefined {
        if (!this.voxels || !this.atlas) return undefined;
        const grid = this.voxels.grid;
        const out = new Float32Array(grid.dim ** 3);
        const tri = new Float32Array(9);
        const m = createMat4();

        const cellArea = grid.cell.x * grid.cell.z;
        for (const set of [this.statics, this.plants]) {
            const packed = set.packed;
            for (const bucket of set.buckets) {
                //Mirrors VoxelField.batchesFor; the two must agree or the diff is meaningless.
                const worldArea = this.atlas.maxTriangleArea(bucket.geom) * bucket.maxScale * bucket.maxScale;
                const batches = Math.max(1, Math.min(MAX_SAMPLE_BATCHES,
                    Math.ceil(Math.ceil((worldArea / cellArea) * 4) / SAMPLES_PER_INVOCATION)));
                const triangles = Math.floor(bucket.slice.indexCount / 3);
                for (let n = 0; n < bucket.instanceCount; ++n) {
                    const base = (bucket.firstInstance + n) * INSTANCE_FLOATS;
                    //Flags live in the u32 lane after pickId; Hidden instances are skipped by
                    //the kernel, so they must be skipped here too.
                    const flags = new Uint32Array(packed.buffer, (base + 37) * 4, 1)[0];
                    if ((flags & (IFlags.Hidden | IFlags.ExcludeFromGI)) !== 0) continue;
                    for (let i = 0; i < 16; ++i) m[i] = packed[base + i];

                    for (let t = 0; t < triangles; ++t) {
                        if (!this.atlas.triangle(bucket.slice, t, tri)) continue;
                        const p: Vec3[] = [];
                        for (let k = 0; k < 3; ++k) {
                            const x = tri[k * 3], y = tri[k * 3 + 1], z = tri[k * 3 + 2];
                            p.push({
                                x: m[0] * x + m[4] * y + m[8] * z + m[12],
                                y: m[1] * x + m[5] * y + m[9] * z + m[13],
                                z: m[2] * x + m[6] * y + m[10] * z + m[14],
                            });
                        }
                        scatterTriangleArea(grid, p[0], p[1], p[2], out, batches);
                    }
                }
            }
        }
        return out;
    }

    private drawSet(pass: GPURenderPassEncoder, set: InstanceBuffers, bind: GPUBindGroup | undefined, picking: boolean) {
        if (!bind || set.count === 0) return;
        pass.setBindGroup(1, bind);
        const table = picking ? this.pipelines!.pick : this.pipelines!.scene;
        for (const bucket of set.buckets) {
            pass.setPipeline(table[bucket.variant]);
            pass.drawIndexed(bucket.slice.indexCount, bucket.instanceCount,
                             bucket.slice.firstIndex, bucket.slice.baseVertex, bucket.firstInstance);
        }
    }

    private rebindInstances() {
        if (!this.pipelines || !this.gpu) return;
        if (this.plants.buffer)
            this.plantBind = this.gpu.device.createBindGroup({
                layout: this.pipelines.instanceLayout,
                entries: [{ binding: 0, resource: { buffer: this.plants.buffer } }],
            });
        if (this.statics.buffer)
            this.staticBind = this.gpu.device.createBindGroup({
                layout: this.pipelines.instanceLayout,
                entries: [{ binding: 0, resource: { buffer: this.statics.buffer } }],
            });
    }

    //--- draw list -------------------------------------------------------------------------

    setLighting(lighting: FrameLighting) {
        this.lighting = lighting;
        //The cells carry radiance, not just density, so a change of sun invalidates the field
        //even when no geometry moved. Re-injecting as well as re-resolving is wasteful; it is
        //correct, and lighting changes far less often than geometry does.
        this.voxelsDirty = true;
        //The shadow map and the occlusion gbuffer are the two largest allocations here, so
        //they are sized to what the tier actually uses rather than held permanently.
        //No shadow map: sun visibility is traced through the density field, so the 4096 depth
        //target is dead weight. Occlusion still needs its prepass and AO buffers, and the cone
        //trace reads the same prepass, so it is required whenever the trace runs.
        if (this.targets?.setLightingNeeds(false, this.needsPrepass())) {
            this.rebuildAoBind();
            this.rebuildFrameBind();
            this.logTargetMemory('lighting');
        }
        this.renderOnce();
    }

    /**
     * Reports render-target memory once per change.
     *
     * A GPU-process crash from exhausting VRAM produces no JavaScript error at all - the tab's
     * canvas is simply left showing whatever was in the memory it lost. This number is the
     * only thing that makes such a report diagnosable after the fact.
     */
    private logTargetMemory(why: string) {
        if (!this.targets) return;
        const mb = (this.targets.targetBytes() / (1024 * 1024)).toFixed(1);
        console.info(`WebGPU viewport: ${mb} MB of render targets at ${this.targets.width}x${this.targets.height} (${why}).`);
    }

    rebuildPlants(inputs: PlantFrameInputs) {
        if (!this.atlas) return;
        this.voxelsDirty = true;
        buildPlants(this.plantList, inputs);
        this.plants.pack(this.plantList, this.atlas);
        this.renderOnce();
    }

    /**
     * Terrain, seeds and obstacles share one buffer.
     *
     * They change together far less often than the plants do, and keeping them separate from
     * the volatile plant buffer means a hover recolour does not re-upload the whole canopy.
     */
    private rebuildStatics() {
        this.voxelsDirty = true;
        if (!this.atlas) return;
        const list = this.staticList;
        list.reset();

        if (this.visibility.terrain) this.appendTerrain(list);
        if (this.visibility.seeds) this.appendSeeds(list);
        if (this.visibility.obstacles) this.appendObstacles(list);

        this.statics.pack(list, this.atlas);
        this.renderOnce();
    }

    private appendTerrain(list: InstanceList) {
        const items = appstate.terrainList;
        //See IFlags.ExcludeFromGI: a ground plane is not a leaf-area density, and injecting it
        //makes every point on it occlude itself.
        const flags = IFlags.DoubleSided | IFlags.FlatShaded | IFlags.ExcludeFromGI;

        if (items?.length > 0) {
            for (let i = 0; i < items.length; ++i) {
                const t = items[i] as any;
                if (t.meshData) t.meshGeom = registerGeometry(`terrain:${i}:${t.id}`, t.meshData);
                const inst = list.push();
                inst.geom = t.geomId();
                t.writeModel(inst.model);
                inst.color = TERRAIN_COLOR;
                inst.emissive = { r: 0, g: 0, b: 0 };
                inst.flags = flags;
                inst.ref = { type: 'terrain', index: i, terrain: t };
            }
        } else {
            //No imported terrain: one box sized by the configured field.
            const inst = list.push();
            const w = appstate.fieldSizeX.peek();
            const d = appstate.fieldSizeD.peek();
            const l = appstate.fieldSizeZ.peek();
            identity(inst.model);
            scaleInPlace(inst.model, w, d, l);
            setPosition(inst.model, 0, -d, 0);
            inst.geom = Geom.BoxCorner;
            inst.color = TERRAIN_COLOR;
            inst.emissive = { r: 0, g: 0, b: 0 };
            inst.flags = flags;
            inst.ref = { type: 'terrain' };
        }
    }

    private appendSeeds(list: InstanceList) {
        for (const seed of appstate.seeds.peek()) {
            const inst = list.push();
            const p = seed.worldPosition();
            identity(inst.model);
            setPosition(inst.model, p.x, p.y, p.z);
            inst.geom = Geom.Dodecahedron;
            inst.color = paletteColor(seed.palette, seed.state.peek());
            inst.emissive = { r: 0, g: 0, b: 0 };
            inst.flags = IFlags.None;
            inst.ref = { type: 'seed', seed };
        }
    }

    private obstacleIndexVersions = new Map<number, number>();

    private appendObstacles(list: InstanceList) {
        const mapping = this.obstacleMapping;
        for (const obstacle of appstate.obstacles.peek()) {
            const o = obstacle as any;
            const layout = mapping.values !== undefined && o.meshData && o.groups
                ? obstacleMappingGeometry(o.meshData, o.groups, mapping.values)
                : undefined;
            if (o.meshData) {
                o.meshGeom = registerGeometry(`obstacle:${o.uid}`, layout?.mesh ?? o.meshData);
            }
            const inst = list.push();
            inst.geom = o.geomId();
            o.writeModel(inst.model);

            //A measured obstacle is painted per named object and drawn unlit, so the value is not
            //modulated by which way the wall faces.
            const paint = mapping.values !== undefined
                ? writeObstacleColors(o.meshData, o.groups, mapping.values, mapping.range[0], mapping.range[1])
                : 'unpaintable';
            const painted = paint !== 'unpaintable';
            if (painted) {
                //The atlas caches by geometry id and never reclaims, so re-registering a
                //million-vertex facade is not an option: the colours are refreshed where they
                //already are. And only when they moved - this runs on every mouse move, and a
                //42 MB re-upload per event is what made orbiting flicker.
                if (paint === 'repainted') {
                    ++this.obstacleRepaints;
                    this.atlas?.refreshVertexColors(inst.geom);
                }
                inst.color = { r: 0, g: 0, b: 0 };
                inst.emissive = { r: 0, g: 0, b: 0 };
                inst.flags = IFlags.DoubleSided | IFlags.FlatShaded
                    | IFlags.VertexColorsEmissive | IFlags.ExcludeFromGI;
                if (layout) {
                    const measuredCount = layout.mesh.indices.length - layout.contextCount;
                    if (layout.contextCount && measuredCount) {
                        inst.indexRange = { first: 0, count: layout.contextCount };
                        const measured = list.push();
                        measured.geom = inst.geom;
                        measured.model.set(inst.model);
                        measured.color = inst.color;
                        measured.emissive = inst.emissive;
                        measured.flags = inst.flags | IFlags.MappingDepthBias;
                        measured.indexRange = { first: layout.contextCount, count: measuredCount };
                        measured.ref = { type: 'obstacle', obstacle };
                    } else if (measuredCount) inst.flags |= IFlags.MappingDepthBias;
                }
            } else {
                inst.color = paletteColor(obstacle.palette, obstacle.state.peek());
                inst.emissive = { r: 0, g: 0, b: 0 };
                inst.flags = IFlags.DoubleSided | IFlags.FlatShaded | IFlags.DepthBias;
            }
            inst.ref = { type: 'obstacle', obstacle };
            if (o.meshData) {
                const version = layout?.version ?? 0;
                if (this.obstacleIndexVersions.get(o.meshGeom) !== version) {
                    //After colours: if they dirtied the atlas, fold the index change into that
                    //upload instead of uploading this large slice twice.
                    this.atlas?.refreshIndices(o.meshGeom);
                    this.obstacleIndexVersions.set(o.meshGeom, version);
                }
            }
        }
    }

    rebuildTerrain(inputs: TerrainFrameInputs) {
        this.visibility = { ...this.visibility, terrain: inputs.visible };
        this.rebuildStatics();
    }

    rebuildSeeds(inputs: SeedFrameInputs) {
        this.visibility = { ...this.visibility, seeds: inputs.visible };
        this.rebuildStatics();
    }

    /**
     * Latest obstacle mapping, kept because rebuildStatics rebuilds every static instance and has
     * no inputs of its own. The host owns the subscription; this is only the last value it sent.
     */
    private obstacleMapping: ObstacleMappingInputs = { values: undefined, range: [0, 1] };

    /** How many times the obstacle colours were actually rewritten. See GeometryAtlas.uploads. */
    private obstacleRepaints = 0;

    /**
     * Counters for diagnosing viewport flicker, read through the debug hook.
     *
     * Orbiting must not repaint or re-upload anything: nothing about a camera move changes a
     * measured value. If `repaints` or `uploads` climb while only the camera moves, the guard that
     * is supposed to stop that has been defeated again, and the numbers say which one.
     */
    debugStats() {
        return {
            obstacleRepaints: this.obstacleRepaints,
            atlasUploads: this.atlas?.uploads ?? 0,
            atlasUploadedMb: +(((this.atlas?.uploadedBytes ?? 0) / 1048576).toFixed(1)),
            staticInstances: this.staticList.count,
        };
    }

    rebuildObstacles(inputs: ObstacleFrameInputs) {
        this.visibility = { ...this.visibility, obstacles: inputs.visible };
        this.obstacleMapping = inputs.mapping;
        this.rebuildStatics();
    }

    refreshVisibility(inputs: VisibilityInputs) {
        this.visibility = inputs;
        this.rebuildStatics();
    }

    //--- camera ----------------------------------------------------------------------------

    getCameraPose(): CameraPose { return this.camera.getPose(); }

    setCameraPose(pose: CameraPose) {
        this.camera.setPose(pose);
        this.renderOnce();
    }

    //--- input -----------------------------------------------------------------------------

    private dragging: 'orbit' | 'pan' | 'dolly' | undefined;
    private lastX = 0;
    private lastY = 0;
    private listeners: [string, EventListener][] = [];

    private attachInput(canvas: HTMLCanvasElement) {
        const add = (type: string, fn: EventListener, opts?: AddEventListenerOptions) => {
            canvas.addEventListener(type, fn, opts);
            this.listeners.push([type, fn]);
        };

        //Right-drag pans, so the browser menu has to be suppressed or it appears mid-drag
        //and the pan never starts. OrbitControls does exactly the same thing.
        add('contextmenu', ((e: Event) => e.preventDefault()) as EventListener);

        add('pointerdown', ((e: PointerEvent) => {
            //Without this the browser begins a text selection or image drag, which steals
            //the subsequent pointermove events.
            e.preventDefault();

            //OrbitControls' button mapping: left rotates, middle dollies, right pans.
            //Shift+left also pans, which OrbitControls does not offer but trackpad users
            //have no middle or right button to reach for.
            if (e.button === 1) this.dragging = 'dolly';
            else if (e.button === 2 || e.shiftKey) this.dragging = 'pan';
            else if (e.button === 0) this.dragging = 'orbit';
            else return;

            this.lastX = e.clientX;
            this.lastY = e.clientY;
            try { canvas.setPointerCapture(e.pointerId); } catch { /* capture is optional */ }
        }) as EventListener);

        add('pointermove', ((e: PointerEvent) => {
            if (!this.dragging) return;
            const dx = e.clientX - this.lastX;
            const dy = e.clientY - this.lastY;
            this.lastX = e.clientX;
            this.lastY = e.clientY;
            const h = canvas.clientHeight || 1;

            if (this.dragging === 'orbit') this.camera.rotate(dx, dy, h);
            else if (this.dragging === 'pan') this.camera.pan(dx, dy, h);
            //Drag-dolly: downward drag pulls back, matching the wheel's direction.
            else this.camera.dolly(dy / 40);

            this.renderOnce();
        }) as EventListener);

        const endDrag = ((e: PointerEvent) => {
            this.dragging = undefined;
            try { canvas.releasePointerCapture(e.pointerId); } catch { /* already released */ }
        }) as EventListener;
        add('pointerup', endDrag);
        add('pointercancel', endDrag);
        add('pointerleave', endDrag);

        add('wheel', ((e: WheelEvent) => {
            e.preventDefault();
            //deltaY > 0 is a scroll DOWN, which zooms OUT - the same direction as
            //OrbitControls, where deltaY < 0 dollies in. The sign was inverted here at first
            //and reads as broken immediately, so it is worth stating.
            this.camera.dolly(e.deltaY > 0 ? 1 : -1);
            this.renderOnce();
        }) as EventListener, { passive: false });
    }

    private detachInput() {
        const canvas = this.gpu?.canvas;
        if (!canvas) return;
        for (const [type, fn] of this.listeners) canvas.removeEventListener(type, fn);
        this.listeners.length = 0;
    }

    //--- picking ---------------------------------------------------------------------------

    /**
     * Renders one pixel through a skewed projection so that pixel is the one under the
     * cursor, then reads back its instance id.
     *
     * No full-resolution id buffer is needed, and nothing has to be redrawn when the camera
     * moves - which is what makes this compatible with render-on-demand.
     */
    pick(clientX: number, clientY: number, clicks: Clicks) {
        if (!this.gpu || !this.targets || !this.pipelines || !appstate.performPicking.peek()) return;
        //Dropping a pick while one is in flight keeps latency bounded; a hover is a
        //continuous signal, so a missed sample is invisible.
        if (this.pickInFlight) return;

        const canvas = this.gpu.canvas;
        const rect = canvas.getBoundingClientRect();
        const ndc = toNDC(clientX, clientY, rect);

        const ray = this.rayThrough(ndc.x, ndc.y);
        appstate.grabbed.peek()?.forEach((s: any) => s.move(ray));

        const aspect = this.targets.width / Math.max(1, this.targets.height);
        const proj = this.camera.projection(aspect);
        //gluPickMatrix, expressed as a scale+translate that maps the cursor's NDC to the
        //whole of the 1x1 viewport.
        buildPickMatrix(ndc.x, ndc.y, this.targets.width, this.targets.height, this.pickScratch);
        const skewed = multiply(this.pickScratch, proj, createMat4());
        const viewProj = multiply(skewed, this.camera.view(), createMat4());

        this.writeFrameUniform(viewProj, this.camera.view());
        this.atlas!.flush();
        this.rebindInstances();

        const { device } = this.gpu;
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginRenderPass({
            colorAttachments: [{
                view: this.targets.pickView!,
                clearValue: { r: 0, g: 0, b: 0, a: 0 },
                loadOp: 'clear',
                storeOp: 'store',
            }],
            depthStencilAttachment: {
                view: this.targets.pickDepthView!,
                depthClearValue: DEPTH_CLEAR_VALUE,
                depthLoadOp: 'clear',
                depthStoreOp: 'store',
            },
        });
        if (this.atlas!.vertexBuffer && this.atlas!.indexBuffer) {
            pass.setVertexBuffer(0, this.atlas!.vertexBuffer);
            pass.setIndexBuffer(this.atlas!.indexBuffer, 'uint32');
            pass.setBindGroup(0, this.frameBindBasic!);
            this.drawSet(pass, this.statics, this.staticBind, true);
            this.drawSet(pass, this.plants, this.plantBind, true);
        }
        pass.end();

        encoder.copyTextureToBuffer(
            { texture: this.targets.pickTexture! },
            { buffer: this.targets.pickReadback!, bytesPerRow: 256, rowsPerImage: 1 },
            { width: 1, height: 1, depthOrArrayLayers: 1 },
        );
        device.queue.submit([encoder.finish()]);
        //Put the camera's own matrices back. writeBuffer is ordered with submits, so the pick
        //pass keeps the skewed matrix it was recorded with, while anything recorded afterwards
        //- notably the depth+normal prepass - sees the real camera again. Leaving the skew in
        //place is what made shadows and occlusion follow the cursor.
        this.writeFrameUniform(this.camera.viewProjection(aspect), this.camera.view());

        this.pickInFlight = true;
        this.targets.pickReadback!.mapAsync(GPUMapMode.READ).then(() => {
            const id = new Uint32Array(this.targets!.pickReadback!.getMappedRange(0, 4))[0];
            this.targets!.pickReadback!.unmap();
            this.pickInFlight = false;

            const ref = this.plants.refFor(id) ?? this.statics.refFor(id);
            applyPick(ref, clicks, ray);
            //The frame uniform still holds the pick projection; restore the normal view.
            this.renderOnce();
        }).catch(() => { this.pickInFlight = false; });
    }

    /**
     * Renders offscreen at an arbitrary size and reads the pixels back.
     *
     * This path never touches the canvas, which also makes it the way to verify the
     * renderer where canvas presentation is unavailable (headless SwiftShader).
     *
     * Satisfies the PixelReadback contract: RGBA8, top-left rows, straight alpha, already
     * sRGB-encoded. WebGPU's framebuffer origin is already top-left, so unlike the WebGL
     * backend there is no row flip here.
     */
    async renderToPixels(width: number, height: number, ssaa: number, msaa: 1 | 4, pose: CameraPose): Promise<PixelReadback> {
        if (!this.gpu || !this.pipelines || !this.atlas) throw new Error('renderToPixels before mount');
        const { device } = this.gpu;

        const rw = Math.max(1, Math.round(width * ssaa));
        const rh = Math.max(1, Math.round(height * ssaa));
        const max = device.limits.maxTextureDimension2D;
        if (rw > max || rh > max) throw new OfflineSizeError(rw, rh, max);

        //Offscreen colour must be renderable AND copyable; the canvas format is not
        //guaranteed to be either offscreen, so pin rgba8unorm and let the shader keep doing
        //the sRGB encode exactly as it does on screen.
        const colorFormat: GPUTextureFormat = 'rgba8unorm';
        const sampleCount = msaa > 1 ? 4 : 1;

        const offscreenPipelines = createOffscreenPipelines(device, colorFormat, sampleCount);
        const resolved = device.createTexture({
            size: { width: rw, height: rh }, format: colorFormat,
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
        });
        const multisampled = sampleCount > 1 ? device.createTexture({
            size: { width: rw, height: rh }, format: colorFormat, sampleCount,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        }) : undefined;
        const depth = device.createTexture({
            size: { width: rw, height: rh }, format: DEPTH_FORMAT, sampleCount,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });

        //copyTextureToBuffer requires a 256-byte row stride; the padding is stripped below.
        const bytesPerRow = Math.ceil(rw * 4 / 256) * 256;
        const readback = device.createBuffer({
            size: bytesPerRow * rh,
            usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
        });

        //Occlusion is a function of resolution and camera pose, so the offline path cannot
        //borrow the canvas set: it would light a 4K still with whatever the viewport last
        //showed. Allocated only when the tier actually asks for it.
        //Needed for shadows and indirect light too, not just occlusion: all three read the
        //gbuffer this set carries. Gating it on occlusion alone made a shadows-only export
        //fall back to the CANVAS set - a different resolution, filled for a different camera.
        const occ = this.needsPrepass() ? new OcclusionTargets(device, rw, rh) : undefined;
        let offlineFrameBind: GPUBindGroup | undefined;

        try {
            const saved = this.camera.getPose();
            this.camera.setPose(pose);
            const aspect = rw / rh;
            const viewProj = this.camera.viewProjection(aspect);
            const view = this.camera.view();
            this.camera.setPose(saved);

            this.prepareFrame(viewProj, view, rw, rh);
            if (occ) {
                const binds = this.makeOcclusionBinds(occ);
                //The prepass reads the frame uniform we just wrote, so frameBindBasic - which
                //only ever binds that buffer - is already pointing at the offline view.
                this.renderOcclusion(occ, binds, this.frameBindBasic!);
                this.renderConeTrace(occ, view);
                offlineFrameBind = device.createBindGroup({
                    layout: this.pipelines.frameLayout,
                    entries: [
                        { binding: 0, resource: { buffer: this.frameBuffer! } },
                        { binding: 1, resource: this.targets!.shadowView! },
                        { binding: 2, resource: this.targets!.shadowSampler! },
                        { binding: 3, resource: occ.aoBlurView },
                        { binding: 4, resource: this.targets!.aoSampler! },
                        { binding: 5, resource: occ.giView },
                    ],
                });
            }
            const encoder = device.createCommandEncoder();
            const pass = encoder.beginRenderPass({
                colorAttachments: [{
                    view: (multisampled ?? resolved).createView(),
                    resolveTarget: multisampled ? resolved.createView() : undefined,
                    clearValue: { r: CLEAR_COLOR.r, g: CLEAR_COLOR.g, b: CLEAR_COLOR.b, a: 1 },
                    loadOp: 'clear',
                    storeOp: 'store',
                }],
                depthStencilAttachment: {
                    view: depth.createView(),
                    depthClearValue: DEPTH_CLEAR_VALUE,
                    depthLoadOp: 'clear',
                    depthStoreOp: 'store',
                },
            });

            if (this.atlas.vertexBuffer && this.atlas.indexBuffer) {
                pass.setVertexBuffer(0, this.atlas.vertexBuffer);
                pass.setIndexBuffer(this.atlas.indexBuffer, 'uint32');
                pass.setBindGroup(0, offlineFrameBind ?? this.frameBind!);
                this.drawSetWith(pass, this.statics, this.staticBind, offscreenPipelines);
                this.drawSetWith(pass, this.plants, this.plantBind, offscreenPipelines);
            }
            //The still is meant to match the viewport, and that includes the diagnostic
            //overlay when it is on - otherwise there is no way to capture what it shows.
            this.drawVoxelOverlay(pass, colorFormat, sampleCount, DEPTH_FORMAT, aspect);
            pass.end();

            encoder.copyTextureToBuffer(
                { texture: resolved },
                { buffer: readback, bytesPerRow, rowsPerImage: rh },
                { width: rw, height: rh, depthOrArrayLayers: 1 },
            );
            device.queue.submit([encoder.finish()]);

            await readback.mapAsync(GPUMapMode.READ);
            const src = new Uint8Array(readback.getMappedRange());

            //Strip the row padding, and box-average the supersampled image in encoded sRGB -
            //the same space the WebGL backend averages in, so the two agree.
            const out = downsample(src, bytesPerRow, rw, rh, ssaa);
            readback.unmap();

            reportOpaque(forceOpaque(out), out.length / 4, 'webgpu');
            const ow = Math.max(1, Math.floor(rw / ssaa));
            warnIfFlat(out, ow, Math.max(1, Math.floor(rh / ssaa)), 'webgpu');
            return { data: out, width: ow, height: Math.max(1, Math.floor(rh / ssaa)) };
        } finally {
            resolved.destroy();
            multisampled?.destroy();
            depth.destroy();
            readback.destroy();
            occ?.destroy();
            this.renderOnce();
        }
    }

    private drawSetWith(pass: GPURenderPassEncoder, set: InstanceBuffers, bind: GPUBindGroup | undefined,
                        table: Record<string, GPURenderPipeline>) {
        if (!bind || set.count === 0) return;
        pass.setBindGroup(1, bind);
        for (const bucket of set.buckets) {
            pass.setPipeline(table[bucket.variant]);
            pass.drawIndexed(bucket.slice.indexCount, bucket.instanceCount,
                             bucket.slice.firstIndex, bucket.slice.baseVertex, bucket.firstInstance);
        }
    }

    pickCancel() {
        applyPick(undefined, Clicks.None, { o: { x: 0, y: 0, z: 0 }, d: { x: 0, y: 0, z: 1 } });
    }

    private rayThrough(ndcX: number, ndcY: number): Ray {
        const aspect = this.targets!.width / Math.max(1, this.targets!.height);
        const viewProj = this.camera.viewProjection(aspect);
        const inv = invert(viewProj, createMat4());
        return rayFromNDC(inv, this.camera.position, ndcX, ndcY);
    }
}

/**
 * gluPickMatrix for a 1x1 viewport centred on an NDC point.
 *
 * Both parts are needed. The translation moves the cursor's NDC to the origin; the scale by
 * the viewport size is what narrows the clip volume to a single pixel's worth of the scene.
 * Translating alone would leave the whole frustum mapped onto that one pixel, so the pick
 * would return whatever happened to be frontmost anywhere on screen.
 */
function buildPickMatrix(ndcX: number, ndcY: number, viewportW: number, viewportH: number, out: Mat4): Mat4 {
    identity(out);
    out[0] = viewportW;
    out[5] = viewportH;
    out[12] = -ndcX * viewportW;
    out[13] = -ndcY * viewportH;
    return out;
}

/** General 4x4 inverse, column-major. Used once per pick, so clarity beats cleverness. */
function invert(m: Mat4, out: Mat4): Mat4 {
    const a00 = m[0], a01 = m[1], a02 = m[2], a03 = m[3];
    const a10 = m[4], a11 = m[5], a12 = m[6], a13 = m[7];
    const a20 = m[8], a21 = m[9], a22 = m[10], a23 = m[11];
    const a30 = m[12], a31 = m[13], a32 = m[14], a33 = m[15];

    const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10;
    const b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
    const b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12;
    const b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
    const b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31;
    const b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;

    let det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
    if (!det) return identity(out);
    det = 1.0 / det;

    out[0] = (a11 * b11 - a12 * b10 + a13 * b09) * det;
    out[1] = (a02 * b10 - a01 * b11 - a03 * b09) * det;
    out[2] = (a31 * b05 - a32 * b04 + a33 * b03) * det;
    out[3] = (a22 * b04 - a21 * b05 - a23 * b03) * det;
    out[4] = (a12 * b08 - a10 * b11 - a13 * b07) * det;
    out[5] = (a00 * b11 - a02 * b08 + a03 * b07) * det;
    out[6] = (a32 * b02 - a30 * b05 - a33 * b01) * det;
    out[7] = (a20 * b05 - a22 * b02 + a23 * b01) * det;
    out[8] = (a10 * b10 - a11 * b08 + a13 * b06) * det;
    out[9] = (a01 * b08 - a00 * b10 - a03 * b06) * det;
    out[10] = (a30 * b04 - a31 * b02 + a33 * b00) * det;
    out[11] = (a21 * b02 - a20 * b04 - a23 * b00) * det;
    out[12] = (a11 * b07 - a10 * b09 - a12 * b06) * det;
    out[13] = (a00 * b09 - a01 * b07 + a02 * b06) * det;
    out[14] = (a31 * b01 - a30 * b03 - a32 * b00) * det;
    out[15] = (a20 * b03 - a21 * b01 + a22 * b00) * det;
    return out;
}


/**
 * Removes copyTextureToBuffer's row padding and box-averages the supersampled image.
 *
 * The average is deliberately taken in ENCODED sRGB, matching the WebGL backend, so an
 * offline still is identical whichever renderer produced it.
 */
function downsample(src: Uint8Array, bytesPerRow: number, rw: number, rh: number, ssaa: number): Uint8ClampedArray {
    const ow = Math.max(1, Math.floor(rw / ssaa));
    const oh = Math.max(1, Math.floor(rh / ssaa));
    const out = new Uint8ClampedArray(ow * oh * 4);
    const n = Math.max(1, Math.round(ssaa));

    for (let y = 0; y < oh; ++y) {
        for (let x = 0; x < ow; ++x) {
            let r = 0, g = 0, b = 0, a = 0, count = 0;
            for (let sy = 0; sy < n; ++sy) {
                const srcY = y * n + sy;
                if (srcY >= rh) continue;
                for (let sx = 0; sx < n; ++sx) {
                    const srcX = x * n + sx;
                    if (srcX >= rw) continue;
                    const at = srcY * bytesPerRow + srcX * 4;
                    r += src[at]; g += src[at + 1]; b += src[at + 2]; a += src[at + 3];
                    count++;
                }
            }
            const at = (y * ow + x) * 4;
            out[at] = r / count; out[at + 1] = g / count; out[at + 2] = b / count; out[at + 3] = a / count;
        }
    }
    return out;
}
