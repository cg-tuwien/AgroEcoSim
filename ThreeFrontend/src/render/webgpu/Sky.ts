//The sky background pass for the WebGPU backend.
//
//Shaped after gi/VoxelDebug: a self-contained class owning its shader module, bind group
//layout, uniform buffer and a pipeline cache keyed by target format, sample count and depth
//format, with a draw() the renderer calls from inside an existing render pass.
//
//It samples the SAME six CPU-built faces the Three backend hands to three's background shader,
//so the two backends cannot drift apart - which is the whole reason the sky model lives on the
//CPU in core/SkyTexture rather than being written twice in two shading languages.

import { CameraPose } from '../IViewportRenderer';
import type { SkyCube } from '../core/SkyTexture';
import { SKY_WGSL } from './shaders/sky.wgsl';

//right, up, forward.
const FLOATS = 4 * 3;

export class Sky {
    private device: GPUDevice;
    private module: GPUShaderModule;
    private layout: GPUBindGroupLayout;
    private pipelines = new Map<string, GPURenderPipeline>();
    private uniform: GPUBuffer;
    private sampler: GPUSampler;
    private data = new Float32Array(FLOATS);

    private texture: GPUTexture | undefined;
    private view: GPUTextureView | undefined;
    private bind: GPUBindGroup | undefined;
    private size = 0;
    /** The `revision` last uploaded. See SkyCube: the cube's identity never changes. */
    private uploaded = -1;

    constructor(device: GPUDevice) {
        this.device = device;
        this.module = device.createShaderModule({ label: 'sky', code: SKY_WGSL });
        this.layout = device.createBindGroupLayout({
            label: 'sky',
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float', viewDimension: 'cube' } },
                { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            ],
        });
        this.uniform = device.createBuffer({
            label: 'sky',
            size: FLOATS * 4,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear' });
    }

    /** Pipelines are keyed by target format and sample count, since the offline path differs. */
    private pipeline(format: GPUTextureFormat, sampleCount: number,
                     depthFormat: GPUTextureFormat): GPURenderPipeline {
        const key = `${format}|${sampleCount}|${depthFormat}`;
        let p = this.pipelines.get(key);
        if (p) return p;
        p = this.device.createRenderPipeline({
            label: 'sky',
            layout: this.device.createPipelineLayout({ bindGroupLayouts: [this.layout] }),
            vertex: { module: this.module, entryPoint: 'vs' },
            //No blending: the sky replaces whatever the pass cleared to.
            fragment: { module: this.module, entryPoint: 'fs', targets: [{ format }] },
            primitive: { topology: 'triangle-list' },
            //A depth state is mandatory because the pass it joins has a depth attachment, but it
            //neither tests nor writes: the sky is drawn FIRST and every later draw covers it.
            //Writing no depth is what leaves the reversed-Z clear intact for the geometry.
            depthStencil: { format: depthFormat, depthWriteEnabled: false, depthCompare: 'always' },
            multisample: { count: sampleCount },
        });
        this.pipelines.set(key, p);
        return p;
    }

    private upload(cube: SkyCube) {
        if (this.size !== cube.size) {
            this.texture?.destroy();
            this.size = cube.size;
            this.texture = this.device.createTexture({
                label: 'sky',
                size: { width: cube.size, height: cube.size, depthOrArrayLayers: 6 },
                //-srgb, NOT rgba8unorm: the sampler then decodes to linear and FILTERS in
                //linear space, which is what WebGL does with the SRGB8_ALPHA8 texture the Three
                //backend gets. Filtering the encoded bytes instead would diverge from it by up
                //to ~0.6 of a byte across the steepest sky gradients, for no gain.
                format: 'rgba8unorm-srgb',
                usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST,
            });
            this.view = this.texture.createView({ dimension: 'cube' });
            this.bind = this.device.createBindGroup({
                layout: this.layout,
                entries: [
                    { binding: 0, resource: { buffer: this.uniform } },
                    { binding: 1, resource: this.view },
                    { binding: 2, resource: this.sampler },
                ],
            });
            this.uploaded = -1;
        }

        if (this.uploaded === cube.revision) return;
        for (let face = 0; face < 6; ++face)
            this.device.queue.writeTexture(
                { texture: this.texture!, origin: { x: 0, y: 0, z: face } },
                cube.faces[face],
                { bytesPerRow: cube.size * 4, rowsPerImage: cube.size },
                { width: cube.size, height: cube.size, depthOrArrayLayers: 1 });
        this.uploaded = cube.revision;
    }

    /**
     * Draws the sky into an already-open pass.
     *
     * The pose is an ARGUMENT rather than read from the renderer's camera on purpose: the
     * offline path restores the viewport camera before it draws its overlays, so anything
     * reading `camera.getPose()` there renders the wrong view at the wrong aspect. (That is a
     * live bug in the voxel overlay next door; this is written not to inherit it.)
     */
    draw(pass: GPURenderPassEncoder, format: GPUTextureFormat, sampleCount: number,
         depthFormat: GPUTextureFormat, cube: SkyCube, pose: CameraPose, aspect: number) {
        this.upload(cube);

        //The same basis core/Mat4.lookAt builds, so the horizon lines up with the terrain:
        //lookAt's x axis is cross(up, eye - target), which is cross(forward, up). Taking `up`
        //from the pose rather than assuming world up costs nothing and keeps it correct if the
        //camera ever rolls.
        let fx = pose.target.x - pose.position.x;
        let fy = pose.target.y - pose.position.y;
        let fz = pose.target.z - pose.position.z;
        let len = Math.hypot(fx, fy, fz);
        if (len < 1e-9) { fx = 0; fy = 0; fz = -1; len = 1; }
        fx /= len; fy /= len; fz /= len;

        let rx = fy * pose.up.z - fz * pose.up.y;
        let ry = fz * pose.up.x - fx * pose.up.z;
        let rz = fx * pose.up.y - fy * pose.up.x;
        len = Math.hypot(rx, ry, rz);
        //up parallel to the view direction; match lookAt's nudge rather than emitting NaNs.
        if (len < 1e-9) { rx = 1; ry = 0; rz = 0; } else { rx /= len; ry /= len; rz /= len; }

        const ux = ry * fz - rz * fy;
        const uy = rz * fx - rx * fz;
        const uz = rx * fy - ry * fx;

        const tanY = Math.tan((pose.fovY * Math.PI) / 360);
        const f = this.data;
        f[0] = rx * tanY * aspect; f[1] = ry * tanY * aspect; f[2] = rz * tanY * aspect; f[3] = 0;
        f[4] = ux * tanY; f[5] = uy * tanY; f[6] = uz * tanY; f[7] = 0;
        f[8] = fx; f[9] = fy; f[10] = fz; f[11] = 0;
        this.device.queue.writeBuffer(this.uniform, 0, f.buffer, 0, f.byteLength);

        pass.setPipeline(this.pipeline(format, sampleCount, depthFormat));
        pass.setBindGroup(0, this.bind!);
        pass.draw(3);
    }

    dispose() {
        this.texture?.destroy();
        this.texture = undefined;
        this.view = undefined;
        this.bind = undefined;
        this.size = 0;
        this.uploaded = -1;
        this.pipelines.clear();
    }
}
