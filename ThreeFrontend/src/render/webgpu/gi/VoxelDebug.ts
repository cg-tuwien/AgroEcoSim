//The debug overlay for the voxel field: one blended fullscreen ray-march.
//
//Split from VoxelField because it is diagnostic, not part of the light transport, and should
//be deletable without touching the field itself.

import { Vec3, cross, normalize, scale, sub } from '../../core/Vec3';
import { CameraPose } from '../../IViewportRenderer';
import { VoxelDebugOptions, VoxelGrid } from '../../core/VoxelGrid';
import { VOXEL_DEBUG_WGSL } from './shaders/voxelDebug.wgsl';

//origin, right, up, forward, gridOrigin, gridCell, gridExtent, options.
const FLOATS = 4 * 8;

export class VoxelDebug {
    private device: GPUDevice;
    private layout: GPUBindGroupLayout;
    private pipelines = new Map<string, GPURenderPipeline>();
    private uniform: GPUBuffer;
    private sampler: GPUSampler;
    private data = new Float32Array(FLOATS);
    private bind: GPUBindGroup | undefined;
    private boundView: GPUTextureView | undefined;
    private module: GPUShaderModule;

    constructor(device: GPUDevice) {
        this.device = device;
        this.module = device.createShaderModule({ label: 'voxel-debug', code: VOXEL_DEBUG_WGSL });
        this.layout = device.createBindGroupLayout({
            label: 'voxel-debug',
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float', viewDimension: '3d' } },
                { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
            ],
        });
        this.uniform = device.createBuffer({
            label: 'voxel-debug',
            size: FLOATS * 4,
            usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
        this.sampler = device.createSampler({ magFilter: 'linear', minFilter: 'linear', mipmapFilter: 'linear' });
    }

    /** Pipelines are keyed by target format and sample count, since the offline path differs. */
    private pipeline(format: GPUTextureFormat, sampleCount: number, depthFormat: GPUTextureFormat): GPURenderPipeline {
        const key = `${format}|${sampleCount}|${depthFormat}`;
        let p = this.pipelines.get(key);
        if (p) return p;
        p = this.device.createRenderPipeline({
            label: 'voxel-debug',
            layout: this.device.createPipelineLayout({ bindGroupLayouts: [this.layout] }),
            vertex: { module: this.module, entryPoint: 'vs' },
            fragment: {
                module: this.module,
                entryPoint: 'fs',
                targets: [{
                    format,
                    //Premultiplied over: the shader already multiplies colour by coverage.
                    blend: {
                        color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                        alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
                    },
                }],
            },
            primitive: { topology: 'triangle-list' },
            //A depth state is mandatory because the pass it joins has a depth attachment, but
            //it neither tests nor writes: the overlay deliberately shows the whole field,
            //including the part behind the canopy, which is the part worth checking.
            depthStencil: { format: depthFormat, depthWriteEnabled: false, depthCompare: 'always' },
            multisample: { count: sampleCount },
        });
        this.pipelines.set(key, p);
        return p;
    }

    draw(pass: GPURenderPassEncoder, format: GPUTextureFormat, sampleCount: number,
         depthFormat: GPUTextureFormat, view: GPUTextureView, grid: VoxelGrid, pose: CameraPose,
         fovY: number, aspect: number, options: VoxelDebugOptions) {
        if (this.boundView !== view) {
            this.bind = this.device.createBindGroup({
                layout: this.layout,
                entries: [
                    { binding: 0, resource: { buffer: this.uniform } },
                    { binding: 1, resource: view },
                    { binding: 2, resource: this.sampler },
                ],
            });
            this.boundView = view;
        }

        const forward = normalize(sub(pose.target, pose.position));
        //Any up vector works as long as it is not parallel to forward; the camera never rolls,
        //so world up is safe except looking straight down, which the orbit camera prevents.
        const right = normalize(cross(forward, { x: 0, y: 1, z: 0 } as Vec3));
        const up = cross(right, forward);
        const tanY = Math.tan((fovY * Math.PI) / 360);

        const f = this.data;
        f[0] = pose.position.x; f[1] = pose.position.y; f[2] = pose.position.z; f[3] = 0;
        const r = scale(right, tanY * aspect);
        f[4] = r.x; f[5] = r.y; f[6] = r.z; f[7] = 0;
        const u = scale(up, tanY);
        f[8] = u.x; f[9] = u.y; f[10] = u.z; f[11] = 0;
        f[12] = forward.x; f[13] = forward.y; f[14] = forward.z; f[15] = 0;
        f[16] = grid.origin.x; f[17] = grid.origin.y; f[18] = grid.origin.z; f[19] = 0;
        f[20] = grid.cell.x; f[21] = grid.cell.y; f[22] = grid.cell.z; f[23] = 0;
        //World extent rather than cells per axis: the grid is no longer a cube.
        f[24] = grid.size.x; f[25] = grid.size.y; f[26] = grid.size.z; f[27] = 0;
        f[28] = options.mip; f[29] = options.gain; f[30] = options.albedo ? 1 : 0; f[31] = 0;
        this.device.queue.writeBuffer(this.uniform, 0, f.buffer, 0, f.byteLength);

        pass.setPipeline(this.pipeline(format, sampleCount, depthFormat));
        pass.setBindGroup(0, this.bind!);
        pass.draw(3);
    }

    dispose() {
        this.uniform.destroy();
    }
}
