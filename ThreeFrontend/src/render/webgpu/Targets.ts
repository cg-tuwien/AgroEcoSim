import { SHADOW_SIZE } from '../core/Lighting';
//Offscreen attachments for the WebGPU backend: MSAA colour, depth, and the 1x1 pick target.

export const SAMPLE_COUNT = 4;          // matches the WebGL renderer's antialias: true
/**
 * Depth is reversed: the near plane is 1 and the far plane 0. See perspectiveReversedZ in
 * core/Mat4 for why. These three constants have to move together - a float format without the
 * reversal is the worst of both worlds, and the reversal without a matching compare function
 * renders the scene inside-out - so they are declared as one group and never inlined.
 */
export const REVERSED_Z = true;
export const DEPTH_FORMAT: GPUTextureFormat = REVERSED_Z ? 'depth32float' : 'depth24plus';
export const DEPTH_CLEAR_VALUE = REVERSED_Z ? 0 : 1;
export const DEPTH_COMPARE: GPUCompareFunction = REVERSED_Z ? 'greater' : 'less';
/** Sign of a "pull towards the camera" depth bias, which reverses along with the range. */
export const DEPTH_BIAS_TOWARDS_CAMERA = REVERSED_Z ? 1 : -1;
export const PICK_FORMAT: GPUTextureFormat = 'r32uint';
export const SHADOW_FORMAT: GPUTextureFormat = 'depth32float';
/**
 * The prepass writes two small targets rather than one wide one.
 *
 * Depth must be 32-bit: it holds linear view depth in metres, and GTAO reconstructs a view
 * position per sample. A 16-bit mantissa puts the ulp at ~16 mm at 20 m, and because the
 * horizon search takes a max over ~48 samples, that quantisation is rectified into a
 * systematically positive false horizon - it rendered as hard banding across flat terrain.
 *
 * The normal does not need that: octahedral encoding fits in two 16-bit channels with far
 * more angular precision than the search resolves. Splitting them costs 8 bytes per pixel
 * instead of the 16 a single rgba32float target needed, which at 4K is 66 MB less - and this
 * is a full-resolution target, so it is one of the largest allocations the backend makes.
 *
 * Both are renderable in core WebGPU. Neither is filterable, which does not matter: the pass
 * only ever textureLoads them.
 */
export const GBUFFER_NORMAL_FORMAT: GPUTextureFormat = 'rg16float';
export const GBUFFER_DEPTH_FORMAT: GPUTextureFormat = 'r32float';
export const GBUFFER_BYTES_PER_PIXEL = 4 + 4;
/**
 * Occlusion target format.
 *
 * `r8unorm` would be the natural choice for a single channel, but core WebGPU does not allow
 * it as a write-only storage texture. Of the formats that ARE storage-capable, rgba8unorm is
 * the only one that is also filterable, and filtering is what makes the half-resolution
 * upsample cheap. Three of its four channels go unused.
 */
export const AO_FORMAT: GPUTextureFormat = 'rgba8unorm';
/** Cone-trace output. Storage-capable and filterable, and HDR because the bounce is radiance. */
export const GI_FORMAT: GPUTextureFormat = 'rgba16float';
/** Occlusion runs at half resolution; a bilateral upsample hides the difference. */
export const AO_SCALE = 2;

/**
 * The depth+normal prepass and ambient-occlusion attachments for one view.
 *
 * Deliberately standalone rather than fields on Targets: `renderToPixels` renders at a
 * resolution and camera pose unrelated to the canvas, and occlusion is a function of both.
 * Reusing the canvas-sized set there produced a still lit by whatever the viewport last
 * showed - correct only in the accidental case where the two happened to match exactly.
 */
export class OcclusionTargets {
    private normalTexture: GPUTexture;
    private linearDepthTexture: GPUTexture;
    private gbufferDepthTexture: GPUTexture;
    normalView: GPUTextureView;
    linearDepthView: GPUTextureView;
    /** The prepass's own depth attachment, not part of the gbuffer that GTAO reads. */
    gbufferDepthView: GPUTextureView;

    private aoTexture: GPUTexture;
    aoView: GPUTextureView;
    private aoBlurTexture: GPUTexture;
    /** Written by the blur pass; this is the one the shading pass samples. */
    aoBlurView: GPUTextureView;
    private giTexture: GPUTexture;
    /** rgb: cone-traced bounce. a: the unoccluded fraction of the hemisphere. */
    giView: GPUTextureView;
    aoWidth: number;
    aoHeight: number;
    width: number;
    height: number;

    constructor(device: GPUDevice, w: number, h: number) {
        this.width = w;
        this.height = h;
        this.normalTexture = device.createTexture({
            label: 'gbuffer-normal',
            size: { width: w, height: h },
            format: GBUFFER_NORMAL_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.linearDepthTexture = device.createTexture({
            label: 'gbuffer-depth-linear',
            size: { width: w, height: h },
            format: GBUFFER_DEPTH_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.gbufferDepthTexture = device.createTexture({
            label: 'gbuffer-depth',
            size: { width: w, height: h },
            format: DEPTH_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });
        this.normalView = this.normalTexture.createView();
        this.linearDepthView = this.linearDepthTexture.createView();
        this.gbufferDepthView = this.gbufferDepthTexture.createView();

        this.aoWidth = Math.max(1, Math.ceil(w / AO_SCALE));
        this.aoHeight = Math.max(1, Math.ceil(h / AO_SCALE));
        this.aoTexture = device.createTexture({
            label: 'ambient-occlusion',
            size: { width: this.aoWidth, height: this.aoHeight },
            format: AO_FORMAT,
            usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.aoView = this.aoTexture.createView();

        //The rotation pattern repeats over a 4x4 tile, so the blur's 4x4 box averages all 16
        //rotations exactly - 8 directions become an effective 128.
        this.aoBlurTexture = device.createTexture({
            label: 'ambient-occlusion-blurred',
            size: { width: this.aoWidth, height: this.aoHeight },
            format: AO_FORMAT,
            usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.aoBlurView = this.aoBlurTexture.createView();

        //Half resolution like the AO, and rgba16float because the bounce is HDR radiance, not
        //a 0..1 factor. Allocated with the occlusion set because the cone trace reads the same
        //prepass and there is no tier that wants one without the other.
        this.giTexture = device.createTexture({
            label: 'gi-cone-trace',
            size: { width: this.aoWidth, height: this.aoHeight },
            format: GI_FORMAT,
            usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.giView = this.giTexture.createView();
    }

    destroy() {
        this.normalTexture.destroy();
        this.linearDepthTexture.destroy();
        this.gbufferDepthTexture.destroy();
        this.aoTexture.destroy();
        this.aoBlurTexture.destroy();
        this.giTexture.destroy();
    }
}

export class Targets {
    private device: GPUDevice;
    private format: GPUTextureFormat;

    private colorTexture: GPUTexture | undefined;
    private depthTexture: GPUTexture | undefined;
    colorView: GPUTextureView | undefined;
    depthView: GPUTextureView | undefined;

    //The pick pass renders exactly one pixel, using a projection skewed so that pixel is the
    //one under the cursor. That avoids a full-resolution id buffer entirely and composes
    //with render-on-demand, since nothing needs redrawing when the camera moves.
    /** Public so the renderer can name it as a copy source. */
    pickTexture: GPUTexture | undefined;
    private pickDepth: GPUTexture | undefined;
    pickView: GPUTextureView | undefined;
    pickDepthView: GPUTextureView | undefined;
    pickReadback: GPUBuffer | undefined;

    //Depth+normal prepass output, at full resolution and without MSAA: resolving a normal
    //buffer would average directions across an edge and produce normals that point nowhere.
    occlusion: OcclusionTargets | undefined;
    aoSampler: GPUSampler | undefined;
    private shadowEnabled = false;
    private occlusionEnabled = false;

    private shadowTexture: GPUTexture | undefined;
    shadowView: GPUTextureView | undefined;
    shadowSampler: GPUSampler | undefined;

    width = 0;
    height = 0;

    constructor(device: GPUDevice, format: GPUTextureFormat) {
        this.device = device;
        this.format = format;
        this.createPickTargets();
        //Both start at 1x1. They are only grown once a lighting tier actually asks for them -
        //see setLightingNeeds - but they must EXIST from the start, because mount() builds the
        //frame bind group before the first resize and createBindGroup rejects undefined views.
        this.createShadowTargets();
        this.resizeOcclusion(1, 1);
    }

    /**
     * Allocates the shadow and occlusion targets the current tier needs, and frees the rest.
     *
     * These are the two largest allocations the backend makes, and they used to be created
     * unconditionally: a 4096 depth32float shadow map is 67 MB, and the occlusion gbuffer runs
     * at full resolution. Together with the MSAA colour and depth buffers that came to roughly
     * half a gigabyte of render targets at 4K - on the DEFAULT tier, which uses neither. On a
     * GPU where that does not fit, the process dies rather than reporting an error, and the
     * canvas is left showing whatever was in the reclaimed memory.
     *
     * @returns whether anything was reallocated, so the caller can rebuild its bind groups.
     */
    setLightingNeeds(shadows: boolean, occlusion: boolean): boolean {
        let changed = false;
        if (shadows !== this.shadowEnabled) {
            this.shadowEnabled = shadows;
            this.shadowTexture?.destroy();
            this.createShadowTargets();
            changed = true;
        }
        if (occlusion !== this.occlusionEnabled) {
            this.occlusionEnabled = occlusion;
            this.resizeOcclusion(this.width, this.height);
            changed = true;
        }
        return changed;
    }

    /** Bytes of render-target memory currently held, for the diagnostic log. */
    targetBytes(): number {
        const px = this.width * this.height;
        //MSAA colour and depth are 4 samples of 4 bytes each.
        let bytes = px * (4 * 4 + 4 * 4);
        bytes += this.shadowSize * this.shadowSize * 4;
        if (this.occlusion) {
            const o = this.occlusion;
            bytes += o.width * o.height * (GBUFFER_BYTES_PER_PIXEL + 4);
            bytes += o.aoWidth * o.aoHeight * (4 * 2 + 8);
        }
        return bytes;
    }

    private createShadowTargets() {
        const size = this.shadowEnabled
            ? Math.min(SHADOW_SIZE, this.device.limits.maxTextureDimension2D)
            : 1;
        this.shadowTexture = this.device.createTexture({
            label: 'shadow-map',
            size: { width: size, height: size },
            format: SHADOW_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING,
        });
        this.shadowView = this.shadowTexture.createView();
        //A comparison sampler does the depth test in hardware, which is what makes PCF a
        //single instruction per tap rather than a fetch plus a compare.
        this.shadowSampler = this.device.createSampler({
            compare: 'less',
            magFilter: 'linear',
            minFilter: 'linear',
        });
        this.shadowSize = size;
    }

    shadowSize = SHADOW_SIZE;

    resize(width: number, height: number) {
        const max = this.device.limits.maxTextureDimension2D;
        const w = Math.max(1, Math.min(width, max));
        const h = Math.max(1, Math.min(height, max));
        if (w === this.width && h === this.height && this.colorView) return;

        this.colorTexture?.destroy();
        this.depthTexture?.destroy();

        this.colorTexture = this.device.createTexture({
            label: 'msaa-color',
            size: { width: w, height: h },
            format: this.format,
            sampleCount: SAMPLE_COUNT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });
        this.depthTexture = this.device.createTexture({
            label: 'msaa-depth',
            size: { width: w, height: h },
            format: DEPTH_FORMAT,
            sampleCount: SAMPLE_COUNT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });

        this.colorView = this.colorTexture.createView();
        this.depthView = this.depthTexture.createView();
        this.width = w;
        this.height = h;

        this.resizeOcclusion(w, h);
    }

    private resizeOcclusion(w: number, h: number) {
        this.occlusion?.destroy();
        //1x1 when the tier does not use occlusion: the views must exist for the bind group,
        //but at full resolution the gbuffer alone is the largest single allocation here.
        const on = this.occlusionEnabled;
        this.occlusion = new OcclusionTargets(this.device, on ? w : 1, on ? h : 1);

        if (!this.aoSampler)
            //Linear filtering is the cheap half of the bilateral upsample; the depth-aware
            //half happens in the shader.
            this.aoSampler = this.device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
    }

    private createPickTargets() {
        this.pickTexture = this.device.createTexture({
            label: 'pick-id',
            size: { width: 1, height: 1 },
            format: PICK_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC,
        });
        this.pickDepth = this.device.createTexture({
            label: 'pick-depth',
            size: { width: 1, height: 1 },
            format: DEPTH_FORMAT,
            usage: GPUTextureUsage.RENDER_ATTACHMENT,
        });
        this.pickView = this.pickTexture.createView();
        this.pickDepthView = this.pickDepth.createView();
        //copyTextureToBuffer requires bytesPerRow to be a multiple of 256 even for one pixel.
        this.pickReadback = this.device.createBuffer({
            label: 'pick-readback',
            size: 256,
            usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
        });
    }

    destroy() {
        this.occlusion?.destroy();
        this.shadowTexture?.destroy();
        this.colorTexture?.destroy();
        this.depthTexture?.destroy();
        this.pickTexture?.destroy();
        this.pickDepth?.destroy();
        this.pickReadback?.destroy();
    }
}
