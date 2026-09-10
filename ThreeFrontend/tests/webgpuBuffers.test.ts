import { FRAME_FLOATS, FRAME_MEMBERS } from '../src/render/webgpu/WebGPURenderer';
import { MESH_WGSL } from '../src/render/webgpu/shaders/mesh.wgsl';
//Regression tests for the WebGPU instance buffer's sizing.
//
//These exist because the bug they cover only appeared once a scene had grown past the
//initial allocation - roughly four simulated months in - and then failed as an opaque
//validation message rather than anything the type checker or a small scene would reveal.

import { INSTANCE_BYTES, InstanceBuffers } from '../src/render/webgpu/InstanceBuffers';
import { InstanceList, IFlags } from '../src/render/core/InstanceList';

/** Records what the renderer asked the device for, without needing a real GPU. */
function fakeDevice(maxStorageBufferBindingSize = 134217728) {
    const created: { size: number; usage: number }[] = [];
    const writes: { offset: number; size: number }[] = [];
    return {
        created,
        writes,
        device: {
            //Real devices report these, and the upload path clamps against them.
            limits: { maxStorageBufferBindingSize },
            createBuffer: (d: { size: number; usage: number }) => {
                created.push(d);
                return { destroy() { /* noop */ } };
            },
            queue: {
                writeBuffer: (_b: unknown, _o: number, _data: unknown, dataOffset: number, size: number) =>
                    writes.push({ offset: dataOffset, size }),
            },
        } as unknown as GPUDevice,
    };
}

/** An atlas stub: every geometry resolves to the same one-triangle slice. */
const atlas = { ensure: () => ({ firstIndex: 0, indexCount: 3, baseVertex: 0 }) } as any;

test('index ranges share atlas vertices and remain separate in draws and picking', () => {
    const { device } = fakeDevice();
    const buffers = new InstanceBuffers(device);
    const list = listOf(3, IFlags.DoubleSided);
    list.items[0].indexRange = { first: 0, count: 6 };
    list.items[1].indexRange = { first: 6, count: 9 };
    list.items[2].indexRange = { first: 15, count: 3 };
    list.items[2].flags |= IFlags.MappingDepthBias;
    const shared = { firstIndex: 30, indexCount: 18, baseVertex: 42, vertexCount: 12 };
    buffers.pack(list, { ensure: () => shared } as any);
    expect(buffers.buckets.map(b => [b.slice.firstIndex, b.slice.indexCount, b.slice.baseVertex]))
        .toEqual([[30, 6, 42], [36, 9, 42], [45, 3, 42]]);
    expect(buffers.buckets[2].variant).toBe('opaqueDoubleMapping');
    expect(buffers.refFor(3)).toBe(list.items[2].ref);
    expect(shared.indexCount).toBe(18);
    list.reset();
    expect(list.push().indexRange).toBeUndefined();
});

function listOf(count: number, flags = IFlags.None): InstanceList {
    const list = new InstanceList();
    for (let i = 0; i < count; ++i) {
        const inst = list.push();
        inst.geom = 0;
        inst.flags = flags;
        inst.color = { r: 0, g: 0, b: 0 };
        inst.emissive = { r: 0, g: 0, b: 0 };
        inst.ref = { type: 'plant', index: { entity: 0, primitive: i } };
    }
    return list;
}

describe('InstanceBuffers sizing', () => {
    test('every allocation is a multiple of 4, across many growth steps', () => {
        const { device, created } = fakeDevice();
        const buffers = new InstanceBuffers(device);

        //Grow gradually, which is what a plant does: each step may trigger the 1.5x growth
        //whose rounding produced the original failure (floor(4860 * 1.5) = 7290).
        for (let count = 1; count < 400; ++count)
            buffers.pack(listOf(count), atlas);

        expect(created.length).toBeGreaterThan(3);   // it really did reallocate
        for (const c of created) {
            expect(c.size % 4).toBe(0);
            //Aligning to the struct size keeps whole instances addressable too.
            expect(c.size % INSTANCE_BYTES).toBe(0);
        }
    });

    test('capacity never shrinks below what was written', () => {
        const { device, created, writes } = fakeDevice();
        const buffers = new InstanceBuffers(device);

        for (const count of [10, 200, 50, 900, 3]) {
            buffers.pack(listOf(count), atlas);
            const capacity = created[created.length - 1].size;
            const written = writes[writes.length - 1].size;
            expect(written).toBe(count * INSTANCE_BYTES);
            expect(capacity).toBeGreaterThanOrEqual(written);
        }
    });

    test('hidden instances are dropped and do not consume pick ids', () => {
        const { device } = fakeDevice();
        const buffers = new InstanceBuffers(device);

        const list = listOf(4);
        list.items[1].flags = IFlags.Hidden;
        buffers.pack(list, atlas);

        expect(buffers.count).toBe(3);
        //Pick id 0 is the background, so ids run 1..count and every one resolves.
        expect(buffers.refFor(0)).toBeUndefined();
        for (let id = 1; id <= 3; ++id) expect(buffers.refFor(id)).toBeDefined();
        expect(buffers.refFor(4)).toBeUndefined();
    });

    test('instances are grouped into one bucket per pipeline variant and geometry', () => {
        const { device } = fakeDevice();
        const buffers = new InstanceBuffers(device);

        const list = new InstanceList();
        const add = (flags: number) => {
            const i = list.push();
            i.geom = 0; i.flags = flags;
            i.color = { r: 0, g: 0, b: 0 }; i.emissive = { r: 0, g: 0, b: 0 };
            i.ref = undefined;
        };
        add(IFlags.None); add(IFlags.DoubleSided); add(IFlags.None); add(IFlags.DoubleSided);
        buffers.pack(list, atlas);

        //Two variants, one geometry -> two draw calls rather than four.
        expect(buffers.buckets.length).toBe(2);
        expect(buffers.buckets.reduce((a, b) => a + b.instanceCount, 0)).toBe(4);
        //Each bucket must address a contiguous run, or firstInstance is meaningless.
        const sorted = buffers.buckets.slice().sort((a, b) => a.firstInstance - b.firstInstance);
        let next = 0;
        for (const b of sorted) {
            expect(b.firstInstance).toBe(next);
            next += b.instanceCount;
        }
    });
});

describe('the frame uniform is big enough for what the shader declares', () => {
    //Adding a field to the WGSL Frame struct without growing FRAME_FLOATS is rejected only at
    //draw time, with an error naming the pipeline rather than the field that was added - and
    //the viewport simply stops rendering. Counting the declared members catches it here.
    test('FRAME_FLOATS covers every member of struct Frame', () => {
        const src = MESH_WGSL;
        const body = src.slice(src.indexOf('struct Frame'), src.indexOf('};', src.indexOf('struct Frame')));
        const mats = (body.match(/:\s*mat4x4f/g) || []).length;
        const vecs = (body.match(/:\s*vec4f/g) || []).length;
        expect(mats).toBeGreaterThan(0);
        expect(vecs).toBeGreaterThan(0);
        expect(FRAME_FLOATS).toBeGreaterThanOrEqual(mats * 16 + vecs * 4);
    });
});

describe('the frame uniform struct matches the order the CPU packs it', () => {
    //Silent when wrong: members simply read each other's floats. That is how occlusion,
    //indirect light and the shadow bias all stopped working at once, each looking like a
    //separate bug in its own pass.
    const members = () => {
        const src = MESH_WGSL;
        const start = src.indexOf('struct Frame {');
        const body = src.slice(start, src.indexOf('};', start));
        return Array.from(body.matchAll(/^\s*(\w+)\s*:\s*(mat4x4f|vec4f)\s*,/gm)).map(m => m[1]);
    };

    test('member names and order are identical', () => {
        expect(members()).toEqual([...FRAME_MEMBERS]);
    });

    test('the declared floats add up to FRAME_FLOATS', () => {
        const src = MESH_WGSL;
        const start = src.indexOf('struct Frame {');
        const body = src.slice(start, src.indexOf('};', start));
        const mats = (body.match(/:\s*mat4x4f/g) || []).length;
        const vecs = (body.match(/:\s*vec4f/g) || []).length;
        expect(mats * 16 + vecs * 4).toBe(FRAME_FLOATS);
    });
});

//The production test site renders 7,700 plants at once, roughly 163 primitives each, which is
//200 MB of instance data against a default 128 MiB storage-buffer binding limit. Past the limit
//Dawn rejects the bind group and every submit after it, so the viewport shows garbage with no
//synchronous error at all - the scene must be clamped and reported instead.
describe('InstanceBuffers capacity ceiling', () => {
    const { INSTANCE_BYTES } = require('../src/render/webgpu/InstanceBuffers');

    test('a scene larger than the binding limit is clamped, not silently corrupted', () => {
        //A small limit keeps the test fast; the arithmetic is identical at 128 MiB.
        const limit = INSTANCE_BYTES * 10;
        const { device, created } = fakeDevice(limit);
        const bufs = new InstanceBuffers(device);
        const errors: string[] = [];
        const realError = console.error;
        console.error = (m: string) => errors.push(String(m));
        try {
            bufs.pack(listOf(25), atlas);
        } finally { console.error = realError; }

        expect(bufs.count).toBe(10);
        expect(created.every(c => c.size <= limit)).toBe(true);
        //Silence here would be the actual bug: the user would see a wrong picture and no reason.
        expect(errors.length).toBe(1);
        expect(errors[0]).toMatch(/25 primitives/);
        expect(errors[0]).toMatch(/10 primitives/);
    });

    test('the overflow is reported once, not once per frame', () => {
        const { device } = fakeDevice(INSTANCE_BYTES * 10);
        const bufs = new InstanceBuffers(device);
        const errors: string[] = [];
        const realError = console.error;
        console.error = (m: string) => errors.push(String(m));
        try {
            for (let i = 0; i < 5; ++i) bufs.pack(listOf(25), atlas);
        } finally { console.error = realError; }
        expect(errors.length).toBe(1);
    });

    test('buckets are trimmed to the clamp so none draws past the buffer', () => {
        const { device } = fakeDevice(INSTANCE_BYTES * 10);
        const bufs = new InstanceBuffers(device);
        const realError = console.error;
        console.error = () => { /* silenced */ };
        try { bufs.pack(listOf(25), atlas); } finally { console.error = realError; }

        for (const b of bufs.buckets)
            expect(b.firstInstance + b.instanceCount).toBeLessThanOrEqual(10);
    });

    test('a scene within the limit is untouched', () => {
        const { device } = fakeDevice(INSTANCE_BYTES * 100);
        const bufs = new InstanceBuffers(device);
        const errors: string[] = [];
        const realError = console.error;
        console.error = (m: string) => errors.push(String(m));
        try { bufs.pack(listOf(25), atlas); } finally { console.error = realError; }
        expect(bufs.count).toBe(25);
        expect(errors).toEqual([]);
    });
});
