//GPU resources and passes for the leaf-area-density field.
//
//Deliberately knows nothing about appstate: everything it needs arrives as GIFrameInputs.
//That boundary exists because the field must never be mistaken for, or fed back into, the
//irradiance the simulation computes - it is a rendering approximation, and keeping it unable
//to reach the simulation's state is cheaper than remembering not to.

import { GIFrameInputs, GridBounds, VoxelGrid, batchesForBucket, cellCount, cellVolume, fitVoxelGrid, makeVoxelGrid } from '../../core/VoxelGrid';
import { GeometryAtlas } from '../GeometryAtlas';
import { DrawBucket, InstanceBuffers } from '../InstanceBuffers';
import { Mat4 } from '../../core/Mat4';
import { RGB } from '../../core/Color';
import { Vec3 } from '../../core/Vec3';
import { VOXEL_ARGS_WGSL, VOXEL_BOUNCE_WGSL, VOXEL_INJECT_WGSL, VOXEL_MIP_WGSL, VOXEL_RESOLVE_WGSL } from './shaders/voxel.wgsl';

export const VOXEL_FORMAT: GPUTextureFormat = 'rgba16float';
/** area + colour*area. */
const CHANNELS = 4;
/** IEEE 754 binary16 to a JS number. The radiance texture is rgba16float. */
function fromHalf(bits: number): number {
    const sign = (bits & 0x8000) ? -1 : 1;
    const exp = (bits >> 10) & 0x1f;
    const frac = bits & 0x3ff;
    if (exp === 0) return sign * frac * 2 ** -24;
    if (exp === 0x1f) return frac ? NaN : sign * Infinity;
    return sign * (1 + frac / 1024) * 2 ** (exp - 15);
}

/** Uniform bind groups with a dynamic offset must be aligned to this in core WebGPU. */
const UNIFORM_ALIGN = 256;
/** sunDir, sunColor, sky, ground, then a 4x4 light matrix. */
const LIGHT_FLOATS = 4 * 4 + 16;

/** Grid resolutions by tier. Kept powers of two so the mip chain halves exactly. */
export const VOXEL_DIM_MEDIUM = 128;

export class VoxelField {
    private device: GPUDevice;
    grid: VoxelGrid;

    private gridBuffer: GPUBuffer;
    private fieldBuffer: GPUBuffer | undefined;
    private bucketBuffer: GPUBuffer | undefined;
    private bucketCapacity = 0;

    private texture: GPUTexture | undefined;
    /** Sampled by the cone trace: the whole mip chain of whichever bounce is final. */
    view: GPUTextureView | undefined;
    private mipViews: GPUTextureView[] = [];
    private primaryView: GPUTextureView | undefined;
    mipCount = 1;

    //Second-bounce target. A pass cannot sample the storage texture it writes, so the bounce
    //reads the first-bounce texture and writes this one. Allocated only when asked for.
    private bounceTexture: GPUTexture | undefined;
    private bounceView: GPUTextureView | undefined;
    private bounceMipViews: GPUTextureView[] = [];
    private bounceMipBinds: GPUBindGroup[] = [];
    private bounceBind: GPUBindGroup | undefined;
    private bounces = 1;

    private occupiedBuffer: GPUBuffer | undefined;
    private countBuffer: GPUBuffer | undefined;
    private argsBuffer: GPUBuffer | undefined;
    private countReadback: GPUBuffer | undefined;
    private argsBind: GPUBindGroup | undefined;

    private injectLayout: GPUBindGroupLayout;
    private injectPipeline: GPUComputePipeline;
    private resolveLayout: GPUBindGroupLayout;
    private resolvePipeline: GPUComputePipeline;
    private mipLayout: GPUBindGroupLayout;
    private mipPipeline: GPUComputePipeline;
    private argsLayout: GPUBindGroupLayout;
    private argsPipeline: GPUComputePipeline;
    private bounceLayout: GPUBindGroupLayout;
    private bouncePipeline: GPUComputePipeline;
    private voxelSampler: GPUSampler;

    private resolveBind: GPUBindGroup | undefined;
    private lightBuffer: GPUBuffer;
    private lightData = new Float32Array(LIGHT_FLOATS);
    private boundShadow: GPUTextureView | undefined;
    private mipBinds: GPUBindGroup[] = [];
    private reportedDispatchLimit = false;
    /**
     * Cells per axis the FIELD alone is divided into, before any refit.
     *
     * Kept apart from `grid.dim`, which the refit changes per axis: re-deriving the base from
     * the current grid would let each refit compound on the last and ratchet the cell size.
     */
    private readonly baseDim: number;

    constructor(device: GPUDevice, dim = VOXEL_DIM_MEDIUM) {
        this.device = device;
        this.baseDim = dim;
        this.grid = makeVoxelGrid(1, 1, 1, dim);

        this.gridBuffer = device.createBuffer({
            label: 'voxel-grid',
            //origin+pad, cell+invVolume, dim+pad: 3 vec4f. The dims were packed into origin.w
            //while the grid was a cube and one number described all three axes.
            size: 48,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });

        this.injectLayout = device.createBindGroupLayout({
            label: 'voxel-inject',
            entries: [
                { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 4, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
                //One dispatch per geometry bucket, each reading its own slice of this buffer.
                { binding: 5, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform', hasDynamicOffset: true } },
            ],
        });
        this.injectPipeline = device.createComputePipeline({
            label: 'voxel-inject',
            layout: device.createPipelineLayout({ bindGroupLayouts: [this.injectLayout] }),
            compute: { module: device.createShaderModule({ label: 'voxel-inject', code: VOXEL_INJECT_WGSL }), entryPoint: 'main' },
        });

        this.resolveLayout = device.createBindGroupLayout({
            label: 'voxel-resolve',
            entries: [
                { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 2, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: VOXEL_FORMAT, viewDimension: '3d' } },
                //The cells are lit here rather than during the trace: a voxel's radiance does
                //not depend on who is looking at it, so doing it once per cell beats doing it
                //once per cone step.
                { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
                { binding: 4, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'depth' } },
                { binding: 5, visibility: GPUShaderStage.COMPUTE, sampler: { type: 'comparison' } },
                { binding: 6, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
                { binding: 7, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
            ],
        });
        this.lightBuffer = device.createBuffer({
            label: 'voxel-light',
            size: LIGHT_FLOATS * 4,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.resolvePipeline = device.createComputePipeline({
            label: 'voxel-resolve',
            layout: device.createPipelineLayout({ bindGroupLayouts: [this.resolveLayout] }),
            compute: { module: device.createShaderModule({ label: 'voxel-resolve', code: VOXEL_RESOLVE_WGSL }), entryPoint: 'main' },
        });

        this.mipLayout = device.createBindGroupLayout({
            label: 'voxel-mip',
            entries: [
                { binding: 0, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float', viewDimension: '3d' } },
                { binding: 1, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: VOXEL_FORMAT, viewDimension: '3d' } },
            ],
        });
        this.mipPipeline = device.createComputePipeline({
            label: 'voxel-mip',
            layout: device.createPipelineLayout({ bindGroupLayouts: [this.mipLayout] }),
            compute: { module: device.createShaderModule({ label: 'voxel-mip', code: VOXEL_MIP_WGSL }), entryPoint: 'main' },
        });

        this.argsLayout = device.createBindGroupLayout({
            label: 'voxel-args',
            entries: [
                { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'storage' } },
            ],
        });
        this.argsPipeline = device.createComputePipeline({
            label: 'voxel-args',
            layout: device.createPipelineLayout({ bindGroupLayouts: [this.argsLayout] }),
            compute: { module: device.createShaderModule({ label: 'voxel-args', code: VOXEL_ARGS_WGSL }), entryPoint: 'main' },
        });

        this.bounceLayout = device.createBindGroupLayout({
            label: 'voxel-bounce',
            entries: [
                { binding: 0, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 2, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 3, visibility: GPUShaderStage.COMPUTE, buffer: { type: 'read-only-storage' } },
                { binding: 4, visibility: GPUShaderStage.COMPUTE, texture: { sampleType: 'float', viewDimension: '3d' } },
                { binding: 5, visibility: GPUShaderStage.COMPUTE, sampler: { type: 'filtering' } },
                { binding: 6, visibility: GPUShaderStage.COMPUTE, storageTexture: { access: 'write-only', format: VOXEL_FORMAT, viewDimension: '3d' } },
            ],
        });
        this.bouncePipeline = device.createComputePipeline({
            label: 'voxel-bounce',
            layout: device.createPipelineLayout({ bindGroupLayouts: [this.bounceLayout] }),
            compute: { module: device.createShaderModule({ label: 'voxel-bounce', code: VOXEL_BOUNCE_WGSL }), entryPoint: 'main' },
        });
        this.voxelSampler = device.createSampler({
            magFilter: 'linear', minFilter: 'linear', mipmapFilter: 'linear',
        });

        this.allocate();
    }

    /** 1 or 2. Allocates the second-bounce texture the first time it is asked for. */
    setBounces(bounces: number) {
        const want = bounces >= 2 ? 2 : 1;
        if (want === this.bounces) return;
        this.bounces = want;
        if (want === 2) this.allocateBounce();
        else {
            this.bounceTexture?.destroy();
            this.bounceTexture = undefined;
            this.bounceView = undefined;
            this.bounceMipViews = [];
            this.bounceMipBinds = [];
            this.bounceBind = undefined;
        }
        this.view = this.finalView();
    }

    private finalView(): GPUTextureView | undefined {
        return this.bounces === 2 && this.bounceView ? this.bounceView : this.primaryView;
    }

    private allocate() {
        const dim = this.grid.dim;
        this.fieldBuffer?.destroy();
        this.fieldBuffer = this.device.createBuffer({
            label: 'voxel-field',
            size: cellCount(this.grid) * CHANNELS * 4,
            usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
        });

        this.texture?.destroy();
        //Levels until every axis has reached 1. A non-cubic grid halves each axis
        //independently, so the count follows the LARGEST of them.
        this.mipCount = Math.floor(Math.log2(Math.max(dim.x, dim.y, dim.z))) + 1;
        this.texture = this.device.createTexture({
            label: 'voxel-radiance',
            dimension: '3d',
            size: { width: dim.x, height: dim.y, depthOrArrayLayers: dim.z },
            format: VOXEL_FORMAT,
            mipLevelCount: this.mipCount,
            //COPY_SRC so the second bounce can seed its destination with this one; the cells
            //it skips must keep their first-bounce values rather than read as uninitialised.
            usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_SRC,
        });
        this.primaryView = this.texture.createView({ dimension: '3d' });
        this.mipViews = [];
        for (let m = 0; m < this.mipCount; ++m)
            this.mipViews.push(this.texture.createView({ dimension: '3d', baseMipLevel: m, mipLevelCount: 1 }));

        //Deferred until build(), which is the first point the shadow map is known.
        this.resolveBind = undefined;
        this.boundShadow = undefined;
        this.mipBinds = [];
        for (let m = 1; m < this.mipCount; ++m)
            this.mipBinds.push(this.device.createBindGroup({
                layout: this.mipLayout,
                entries: [
                    { binding: 0, resource: this.mipViews[m - 1] },
                    { binding: 1, resource: this.mipViews[m] },
                ],
            }));

        //Compaction storage. Sized for the whole grid: a canopy never fills it, but a clamp in
        //the shader is a poor substitute for having room, and 8 MB at 128 cubed is small next
        //to the textures it saves work on.
        this.occupiedBuffer?.destroy();
        this.occupiedBuffer = this.device.createBuffer({
            label: 'voxel-occupied',
            size: cellCount(this.grid) * 4,
            usage: GPUBufferUsage.STORAGE,
        });
        if (!this.countBuffer) {
            this.countBuffer = this.device.createBuffer({
                label: 'voxel-occupied-count',
                size: 4,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC | GPUBufferUsage.COPY_DST,
            });
            this.argsBuffer = this.device.createBuffer({
                label: 'voxel-bounce-args',
                size: 12,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.INDIRECT | GPUBufferUsage.COPY_DST,
            });
            this.countReadback = this.device.createBuffer({
                label: 'voxel-occupied-count-readback',
                size: 4,
                usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
            });
            this.argsBind = this.device.createBindGroup({
                layout: this.argsLayout,
                entries: [
                    { binding: 0, resource: { buffer: this.countBuffer } },
                    { binding: 1, resource: { buffer: this.argsBuffer } },
                ],
            });
        }
        if (this.bounces === 2) this.allocateBounce();
        this.view = this.finalView();
    }

    private allocateBounce() {
        const dim = this.grid.dim;
        this.bounceTexture?.destroy();
        this.bounceTexture = this.device.createTexture({
            label: 'voxel-radiance-bounce',
            dimension: '3d',
            size: { width: dim.x, height: dim.y, depthOrArrayLayers: dim.z },
            format: VOXEL_FORMAT,
            mipLevelCount: this.mipCount,
            usage: GPUTextureUsage.STORAGE_BINDING | GPUTextureUsage.TEXTURE_BINDING
                 | GPUTextureUsage.COPY_DST | GPUTextureUsage.COPY_SRC,
        });
        this.bounceView = this.bounceTexture.createView({ dimension: '3d' });
        this.bounceMipViews = [];
        for (let m = 0; m < this.mipCount; ++m)
            this.bounceMipViews.push(this.bounceTexture.createView({ dimension: '3d', baseMipLevel: m, mipLevelCount: 1 }));
        this.bounceMipBinds = [];
        for (let m = 1; m < this.mipCount; ++m)
            this.bounceMipBinds.push(this.device.createBindGroup({
                layout: this.mipLayout,
                entries: [
                    { binding: 0, resource: this.bounceMipViews[m - 1] },
                    { binding: 1, resource: this.bounceMipViews[m] },
                ],
            }));
        this.bounceBind = this.device.createBindGroup({
            layout: this.bounceLayout,
            entries: [
                { binding: 0, resource: { buffer: this.gridBuffer } },
                { binding: 1, resource: { buffer: this.fieldBuffer! } },
                { binding: 2, resource: { buffer: this.occupiedBuffer! } },
                { binding: 3, resource: { buffer: this.countBuffer! } },
                { binding: 4, resource: this.primaryView! },
                { binding: 5, resource: this.voxelSampler },
                { binding: 6, resource: this.bounceMipViews[0] },
            ],
        });
        this.view = this.finalView();
    }

    /**
     * Refits the grid to the field and to the STATIC geometry. Idempotent when neither moved.
     *
     * `statics` is what makes an imported building castable: the field alone is a 10 m box and
     * a facade is 37 m wide, so three quarters of it used to fall outside the grid and neither
     * deposited density nor received a shadow. See fitVoxelGrid for why taking static bounds
     * does not reintroduce the crawl that keeps PLANT bounds out of this.
     */
    setWorld(inputs: GIFrameInputs, statics?: GridBounds) {
        const base = makeVoxelGrid(inputs.fieldSizeX, inputs.fieldSizeZ, inputs.height, this.baseDim);
        const next = fitVoxelGrid(base, statics);
        const resized = next.dim.x !== this.grid.dim.x
            || next.dim.y !== this.grid.dim.y || next.dim.z !== this.grid.dim.z;
        this.grid = next;
        //The textures and buffers are sized by the cell counts, so a refit that changed them
        //has to reallocate before anything binds them. allocate() clears the bind groups it
        //invalidates, and both the trace and the debug overlay rebind on texture identity.
        if (resized) {
            this.allocate();
            console.info(
                `Voxel grid refit: ${next.dim.x}x${next.dim.y}x${next.dim.z} cells at `
                + `${next.cell.x.toFixed(4)} m over `
                + `${next.size.x.toFixed(1)}x${next.size.y.toFixed(1)}x${next.size.z.toFixed(1)} m `
                + `(${(cellCount(next) * (CHANNELS * 4 + 4) / 1e6).toFixed(1)} MB of buffers)`);
        }
        const u = new Float32Array(12);
        u[0] = next.origin.x; u[1] = next.origin.y; u[2] = next.origin.z; u[3] = 0;
        u[4] = next.cell.x; u[5] = next.cell.y; u[6] = next.cell.z; u[7] = 1 / cellVolume(next);
        u[8] = next.dim.x; u[9] = next.dim.y; u[10] = next.dim.z; u[11] = 0;
        this.device.queue.writeBuffer(this.gridBuffer, 0, u.buffer, 0, u.byteLength);
    }

    /** Grows the per-bucket uniform buffer. Each bucket gets its own aligned slot. */
    private ensureBuckets(count: number) {
        if (count <= this.bucketCapacity && this.bucketBuffer) return;
        this.bucketBuffer?.destroy();
        this.bucketCapacity = Math.max(count, Math.floor(this.bucketCapacity * 1.5), 32);
        this.bucketBuffer = this.device.createBuffer({
            label: 'voxel-buckets',
            size: this.bucketCapacity * UNIFORM_ALIGN,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
    }

    /**
     * Rebuilds the whole field: clear, inject every bucket, resolve, then mip.
     *
     * `encoder.clearBuffer` rather than a clearing compute pass - it is one command instead of
     * a dispatch over two million cells.
     */
    /**
     * How many sample batches a bucket's triangles need.
     *
     * Derived from the geometry rather than assumed per set. A blanket count for statics gave
     * an imported mesh terrain the same 256 batches as a single 100 m2 quad, multiplying its
     * dispatch by 256 for triangles that each need one sample - which is how a heavy scene
     * turns a rebuild into a driver timeout.
     */
    private batchesFor(atlas: GeometryAtlas, bucket: DrawBucket): number {
        const cellArea = this.grid.cell.x * this.grid.cell.z;
        //maxScale is a linear scale, so it squares into an area.
        const worldArea = atlas.maxTriangleArea(bucket.geom) * bucket.maxScale * bucket.maxScale;
        return batchesForBucket(worldArea, cellArea,
                                Math.floor(bucket.slice.indexCount / 3), bucket.instanceCount);
    }

    /** Lighting applied to each cell during resolve. Mirrors FrameLighting, minus the tiers. */
    setLight(sunDirection: Vec3, sunColor: RGB, sky: RGB, ground: RGB, sunUp: boolean) {
        const f = this.lightData;
        f[0] = sunDirection.x; f[1] = sunDirection.y; f[2] = sunDirection.z;
        f[3] = sunUp ? 1 : 0;
        f[4] = sunColor.r; f[5] = sunColor.g; f[6] = sunColor.b; f[7] = 1;
        f[8] = sky.r; f[9] = sky.g; f[10] = sky.b; f[11] = 1;
        f[12] = ground.r; f[13] = ground.g; f[14] = ground.b; f[15] = 1;
        this.device.queue.writeBuffer(this.lightBuffer, 0, f.buffer, 0, f.byteLength);
    }

    build(encoder: GPUCommandEncoder, atlas: GeometryAtlas, sets: InstanceBuffers[],
          shadowView: GPUTextureView, shadowSampler: GPUSampler) {
        if (!this.fieldBuffer || !atlas.vertexBuffer || !atlas.indexBuffer) return;

        const maxPerDim = this.device.limits.maxComputeWorkgroupsPerDimension;
        //An upper bound on the slots needed: one per bucket, plus the splits a bucket whose
        //instance count exceeds a dispatch dimension will need.
        const total = sets.reduce((t, s) =>
            t + s.buckets.reduce((n, b) => n + Math.ceil(Math.max(1, b.instanceCount) / maxPerDim), 0), 0);
        if (total === 0) return;
        this.ensureBuckets(total);

        //Fill the uniform slots first: writeBuffer is queue-ordered ahead of this submit, so
        //all of them are visible by the time any dispatch reads one.
        const slot = new Uint32Array(UNIFORM_ALIGN / 4);
        const offsets: number[] = [];
        const dispatches: { set: InstanceBuffers; triangles: number; instances: number; batches: number }[] = [];
        let at = 0;
        for (const set of sets) {
            for (const b of set.buckets) {
                const batches = this.batchesFor(atlas, b);
                slot.fill(0);
                slot[0] = b.slice.firstIndex;
                slot[1] = Math.floor(b.slice.indexCount / 3);
                slot[2] = b.slice.baseVertex;
                slot[3] = b.firstInstance;
                slot[4] = b.instanceCount;
                //A dispatch dimension is capped at maxComputeWorkgroupsPerDimension (65535 by
                //default), and a mature canopy can put more instances than that in one bucket.
                //Exceeding it is a validation error that invalidates the whole command buffer,
                //so the bucket is split into chunks that each fit.
                for (let done = 0; done < b.instanceCount; done += maxPerDim) {
                    const chunk = Math.min(maxPerDim, b.instanceCount - done);
                    slot[3] = b.firstInstance + done;
                    slot[4] = chunk;
                    slot[5] = batches;
                    this.device.queue.writeBuffer(this.bucketBuffer!, at * UNIFORM_ALIGN, slot.buffer, 0, UNIFORM_ALIGN);
                    offsets.push(at * UNIFORM_ALIGN);
                    dispatches.push({ set, triangles: slot[1], instances: chunk, batches });
                    ++at;
                }
            }
        }

        encoder.clearBuffer(this.fieldBuffer);
        //The compaction counter accumulates during resolve, so it has to start at zero.
        encoder.clearBuffer(this.countBuffer!);

        const pass = encoder.beginComputePass({ label: 'voxel-inject' });
        pass.setPipeline(this.injectPipeline);
        //One bind group per instance buffer, because the storage binding differs; the bucket
        //uniform varies within it by dynamic offset.
        const binds = new Map<InstanceBuffers, GPUBindGroup>();
        for (let i = 0; i < dispatches.length; ++i) {
            const d = dispatches[i];
            if (d.triangles === 0 || d.instances === 0) continue;
            let bind = binds.get(d.set);
            if (!bind) {
                if (!d.set.buffer) continue;
                bind = this.device.createBindGroup({
                    layout: this.injectLayout,
                    entries: [
                        { binding: 0, resource: { buffer: this.gridBuffer } },
                        { binding: 1, resource: { buffer: atlas.vertexBuffer } },
                        { binding: 2, resource: { buffer: atlas.indexBuffer } },
                        { binding: 3, resource: { buffer: d.set.buffer } },
                        { binding: 4, resource: { buffer: this.fieldBuffer } },
                        { binding: 5, resource: { buffer: this.bucketBuffer!, size: UNIFORM_ALIGN } },
                    ],
                });
                binds.set(d.set, bind);
            }
            const groupsX = Math.ceil(d.triangles / 64);
            if (groupsX > maxPerDim || d.batches > maxPerDim) {
                //A bucket over the dispatch limit used to vanish from the field with no log at
                //all, so a model large enough to trip it simply stopped casting shadows and
                //looked like a lighting bug. Say so once.
                if (!this.reportedDispatchLimit) {
                    this.reportedDispatchLimit = true;
                    console.warn(
                        `Voxel field: a bucket of ${d.triangles} triangles x ${d.batches} sample `
                        + `batches exceeds the ${maxPerDim} workgroups-per-dimension limit and was `
                        + `skipped. That geometry will not cast shadows or occlude.`);
                }
                continue;
            }
            pass.setBindGroup(0, bind, [offsets[i]]);
            pass.dispatchWorkgroups(groupsX, d.instances, d.batches);
        }
        pass.end();

        if (!this.resolveBind || this.boundShadow !== shadowView) {
            this.resolveBind = this.device.createBindGroup({
                layout: this.resolveLayout,
                entries: [
                    { binding: 0, resource: { buffer: this.gridBuffer } },
                    { binding: 1, resource: { buffer: this.fieldBuffer } },
                    { binding: 2, resource: this.mipViews[0] },
                    { binding: 3, resource: { buffer: this.lightBuffer } },
                    { binding: 4, resource: shadowView },
                    { binding: 5, resource: shadowSampler },
                    { binding: 6, resource: { buffer: this.occupiedBuffer! } },
                    { binding: 7, resource: { buffer: this.countBuffer! } },
                ],
            });
            this.boundShadow = shadowView;
        }

        const resolve = encoder.beginComputePass({ label: 'voxel-resolve' });
        resolve.setPipeline(this.resolvePipeline);
        resolve.setBindGroup(0, this.resolveBind!);
        resolve.dispatchWorkgroups(
            Math.ceil(this.grid.dim.x / 4),
            Math.ceil(this.grid.dim.y / 4),
            Math.ceil(this.grid.dim.z / 4));
        resolve.end();

        this.generateMips(encoder, this.mipBinds);

        if (this.bounces === 2 && this.bounceBind && this.bounceTexture && this.texture) {
            //Seed the destination with the first bounce so the cells this pass skips - the
            //empty majority - keep their values instead of reading as uninitialised.
            encoder.copyTextureToTexture(
                { texture: this.texture, mipLevel: 0 },
                { texture: this.bounceTexture, mipLevel: 0 },
                { width: this.grid.dim.x, height: this.grid.dim.y, depthOrArrayLayers: this.grid.dim.z });

            const args = encoder.beginComputePass({ label: 'voxel-bounce-args' });
            args.setPipeline(this.argsPipeline);
            args.setBindGroup(0, this.argsBind!);
            args.dispatchWorkgroups(1);
            args.end();

            const bounce = encoder.beginComputePass({ label: 'voxel-bounce' });
            bounce.setPipeline(this.bouncePipeline);
            bounce.setBindGroup(0, this.bounceBind);
            //Indirect: the occupied count is only known on the GPU, and reading it back to
            //size a dispatch would stall the frame it belongs to.
            bounce.dispatchWorkgroupsIndirect(this.argsBuffer!, 0);
            bounce.end();

            this.generateMips(encoder, this.bounceMipBinds);
        }

        this.view = this.finalView();
    }

    private generateMips(encoder: GPUCommandEncoder, binds: GPUBindGroup[]) {
        if (binds.length === 0) return;
        const mips = encoder.beginComputePass({ label: 'voxel-mip' });
        mips.setPipeline(this.mipPipeline);
        for (let m = 1; m < this.mipCount; ++m) {
            //Each axis halves on its own and stops at 1, which is what a 3D texture's mip chain
            //does for a non-cubic size. Deriving all three from one dim would over-dispatch the
            //short axes and, worse, under-dispatch the long one.
            mips.setBindGroup(0, binds[m - 1]);
            mips.dispatchWorkgroups(
                Math.ceil(Math.max(1, this.grid.dim.x >> m) / 4),
                Math.ceil(Math.max(1, this.grid.dim.y >> m) / 4),
                Math.ceil(Math.max(1, this.grid.dim.z >> m) / 4));
        }
        mips.end();
    }

    /**
     * Mean radiance and density over the whole grid, read from the coarsest mip.
     *
     * One texel, so the readback is trivial - and because the mip chain is a plain arithmetic
     * mean of a sigma-premultiplied quantity, that texel IS the volume average. Screen pixels
     * are a blunt way to measure a second bounce on a sparse plant; this measures the field
     * directly, which is where the bounce actually happens.
     */
    async readMeanRadiance(): Promise<{ r: number; g: number; b: number; sigma: number } | undefined> {
        const tex = this.bounces === 2 && this.bounceTexture ? this.bounceTexture : this.texture;
        if (!tex) return undefined;
        const last = this.mipCount - 1;
        const bytesPerRow = 256;
        const staging = this.device.createBuffer({
            size: bytesPerRow,
            usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
        });
        try {
            const encoder = this.device.createCommandEncoder();
            encoder.copyTextureToBuffer(
                { texture: tex, mipLevel: last },
                { buffer: staging, bytesPerRow, rowsPerImage: 1 },
                { width: 1, height: 1, depthOrArrayLayers: 1 });
            this.device.queue.submit([encoder.finish()]);
            await staging.mapAsync(GPUMapMode.READ);
            const half = new Uint16Array(staging.getMappedRange().slice(0, 8));
            staging.unmap();
            return { r: fromHalf(half[0]), g: fromHalf(half[1]), b: fromHalf(half[2]), sigma: fromHalf(half[3]) };
        } finally {
            staging.destroy();
        }
    }

    /** For the harness: how many cells the compaction pass found. */
    async readOccupiedCount(): Promise<number> {
        if (!this.countBuffer || !this.countReadback) return 0;
        const encoder = this.device.createCommandEncoder();
        encoder.copyBufferToBuffer(this.countBuffer, 0, this.countReadback, 0, 4);
        this.device.queue.submit([encoder.finish()]);
        await this.countReadback.mapAsync(GPUMapMode.READ);
        const n = new Uint32Array(this.countReadback.getMappedRange())[0];
        this.countReadback.unmap();
        return n;
    }

    /**
     * Copies the raw area accumulator back to the CPU.
     *
     * The analytic check - total deposited area must equal the scene's actual leaf area -
     * cannot be a unit test because it needs a real device, so it is exposed here for the
     * visual harness to call instead.
     */
    async readArea(): Promise<Float32Array> {
        const cells = cellCount(this.grid);
        const staging = this.device.createBuffer({
            size: cells * CHANNELS * 4,
            usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST,
        });
        try {
            const encoder = this.device.createCommandEncoder();
            encoder.copyBufferToBuffer(this.fieldBuffer!, 0, staging, 0, cells * CHANNELS * 4);
            this.device.queue.submit([encoder.finish()]);
            await staging.mapAsync(GPUMapMode.READ);
            const raw = new Uint32Array(staging.getMappedRange().slice(0));
            staging.unmap();
            const out = new Float32Array(cells);
            for (let i = 0; i < cells; ++i) out[i] = raw[i * CHANNELS] / 1e6;
            return out;
        } finally {
            staging.destroy();
        }
    }

    dispose() {
        this.gridBuffer.destroy();
        this.lightBuffer.destroy();
        this.occupiedBuffer?.destroy();
        this.countBuffer?.destroy();
        this.argsBuffer?.destroy();
        this.countReadback?.destroy();
        this.bounceTexture?.destroy();
        this.fieldBuffer?.destroy();
        this.bucketBuffer?.destroy();
        this.texture?.destroy();
    }
}
