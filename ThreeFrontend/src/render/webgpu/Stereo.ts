//Checkerboard stereo for the WebGPU backend: the two eye images and the pass that interleaves them.
//
//The renderer draws each eye through the ordinary frame path - prepass, occlusion, cone trace,
//main pass - with an off-axis projection, resolving into one of the eye textures here, and
//submits each eye separately: every per-view uniform goes through queue.writeBuffer, which lands
//before the NEXT submit, so encoding both eyes into one submit would give both the second eye's
//camera. The eye textures share the canvas format and hold the same encoded sRGB bytes the canvas
//would, so the interleave is a copy, with no colour conversion to get wrong.

const COMPOSITE_WGSL = /* wgsl */`
struct Params {
  //x: swap the eyes. y: test pattern (left red, right blue).
  flags : vec4u,
};

@group(0) @binding(0) var<uniform> params : Params;
@group(0) @binding(1) var leftEye : texture_2d<f32>;
@group(0) @binding(2) var rightEye : texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) vi : u32) -> @builtin(position) vec4f {
  var pts = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
  return vec4f(pts[vi], 0.0, 1.0);
}

//WebGPU's framebuffer origin is the top-left, so the row index already counts from the top, as
//core/Stereo.ts eyeAt does: row 0 reads L R L R ..., row 1 reads R L R L ...
@fragment
fn fs(@builtin(position) pos : vec4f) -> @location(0) vec4f {
  let p = vec2i(pos.xy);
  var left = ((p.x + p.y) & 1) == 0;
  if (params.flags.x != 0u) { left = !left; }
  if (params.flags.y != 0u) {
    return select(vec4f(0.0, 0.0, 1.0, 1.0), vec4f(1.0, 0.0, 0.0, 1.0), left);
  }
  let c = select(textureLoad(rightEye, p, 0), textureLoad(leftEye, p, 0), left);
  return vec4f(c.rgb, 1.0);
}
`;

export class WebGPUStereo {
    private device: GPUDevice;
    private module: GPUShaderModule;
    private layout: GPUBindGroupLayout;
    private pipelines = new Map<GPUTextureFormat, GPURenderPipeline>();
    private uniform: GPUBuffer;
    private flags = new Uint32Array(4);
    private eyes: GPUTexture[] = [];
    private bind: GPUBindGroup | undefined;
    /** Resolve targets for the two eyes, in the canvas format. */
    eyeViews: GPUTextureView[] = [];
    width = 0;
    height = 0;

    constructor(device: GPUDevice) {
        this.device = device;
        this.module = device.createShaderModule({ label: 'stereo-composite', code: COMPOSITE_WGSL });
        this.layout = device.createBindGroupLayout({
            label: 'stereo-composite',
            entries: [
                { binding: 0, visibility: GPUShaderStage.FRAGMENT, buffer: { type: 'uniform' } },
                { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
                { binding: 2, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
            ],
        });
        this.uniform = device.createBuffer({
            label: 'stereo-composite', size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
        });
    }

    /** Eye textures the size of the canvas; reallocated only when that changes. */
    ensureEyes(width: number, height: number, format: GPUTextureFormat) {
        if (this.eyes.length === 2 && this.width === width && this.height === height) return;
        this.releaseEyes();
        for (let i = 0; i < 2; ++i) {
            const t = this.device.createTexture({
                label: i === 0 ? 'stereo-left' : 'stereo-right',
                size: { width, height },
                format,
                usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.TEXTURE_BINDING,
            });
            this.eyes.push(t);
            this.eyeViews.push(t.createView());
        }
        this.width = width;
        this.height = height;
        this.bind = this.device.createBindGroup({
            layout: this.layout,
            entries: [
                { binding: 0, resource: { buffer: this.uniform } },
                { binding: 1, resource: this.eyeViews[0] },
                { binding: 2, resource: this.eyeViews[1] },
            ],
        });
        console.log(`Stereo eye textures: 2 x ${width}x${height}, ~${Math.round(2 * width * height * 4 / 1048576)} MB`);
    }

    /** Interleaves the two eyes into `target`, which must be `width` x `height`. */
    composite(target: GPUTextureView, format: GPUTextureFormat, swapEyes: boolean, testPattern: boolean) {
        if (!this.bind) return;
        this.flags[0] = swapEyes ? 1 : 0;
        this.flags[1] = testPattern ? 1 : 0;
        this.device.queue.writeBuffer(this.uniform, 0, this.flags.buffer, 0, 16);
        const encoder = this.device.createCommandEncoder({ label: 'stereo-composite' });
        const pass = encoder.beginRenderPass({
            colorAttachments: [{ view: target, loadOp: 'clear', storeOp: 'store', clearValue: { r: 0, g: 0, b: 0, a: 1 } }],
        });
        pass.setPipeline(this.pipeline(format));
        pass.setBindGroup(0, this.bind);
        pass.draw(3);
        pass.end();
        this.device.queue.submit([encoder.finish()]);
    }

    releaseEyes() {
        for (const t of this.eyes) t.destroy();
        this.eyes = [];
        this.eyeViews = [];
        this.bind = undefined;
        this.width = 0;
        this.height = 0;
    }

    dispose() {
        this.releaseEyes();
        this.uniform.destroy();
    }

    private pipeline(format: GPUTextureFormat): GPURenderPipeline {
        let p = this.pipelines.get(format);
        if (!p) {
            p = this.device.createRenderPipeline({
                label: `stereo-composite-${format}`,
                layout: this.device.createPipelineLayout({ bindGroupLayouts: [this.layout] }),
                vertex: { module: this.module, entryPoint: 'vs' },
                fragment: { module: this.module, entryPoint: 'fs', targets: [{ format }] },
                primitive: { topology: 'triangle-list' },
            });
            this.pipelines.set(format, p);
        }
        return p;
    }
}
