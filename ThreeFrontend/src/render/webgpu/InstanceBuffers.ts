//Packs an InstanceList into a GPU storage buffer, grouped into draw buckets.

import { IFlags, InstanceList } from '../core/InstanceList';
import { DataRef } from '../core/Picking';
import { Mat3, Mat4, normalMatrix3 } from '../core/Mat4';
import { GeometryAtlas, GeometrySlice } from './GeometryAtlas';

/**
 * 160 bytes per instance:
 *   mat4x4f model            64
 *   vec4f   normalMat rows   48   (3 x vec4, the 4th component padding)
 *   vec4f   color            16
 *   vec4f   emissive         16
 *   u32     pickId, flags, _pad x2  16
 */
export const INSTANCE_FLOATS = 40;
export const INSTANCE_BYTES = INSTANCE_FLOATS * 4;

export type DrawBucket = {
    variant: PipelineVariant;
    slice: GeometrySlice;
    /** The geometry every instance in this bucket draws; the GI uses it to size triangles. */
    geom: number;
    /** Largest linear scale among this bucket's instances; see pack(). */
    maxScale: number;
    firstInstance: number;
    instanceCount: number;
};

/** Only fixed-function state forks a pipeline; shading differences ride in `flags`. */
export type PipelineVariant = 'opaqueFront' | 'opaqueDouble' | 'opaqueDoubleBias' | 'opaqueDoubleMapping';

function alignTo(value: number, alignment: number): number {
    return Math.ceil(value / alignment) * alignment;
}

function variantFor(flags: number): PipelineVariant {
    if (flags & IFlags.MappingDepthBias) return 'opaqueDoubleMapping';
    if (flags & IFlags.DepthBias) return 'opaqueDoubleBias';
    return flags & IFlags.DoubleSided ? 'opaqueDouble' : 'opaqueFront';
}

export class InstanceBuffers {
    private device: GPUDevice;
    private staging = new Float32Array(0);
    /** Read-only view for the voxel-field reference; see GeometryAtlas.triangle. */
    get packed(): Float32Array { return this.staging; }
    private stagingU32 = new Uint32Array(0);
    buffer: GPUBuffer | undefined;
    private capacityBytes = 0;
    /** Latched so a scene that is too large logs once, not every frame. */
    private reportedOverflow = false;

    /** Maps a pick id back to what was drawn. Index 0 is reserved for the background. */
    slotRefs: (DataRef | undefined)[] = [];
    buckets: DrawBucket[] = [];
    count = 0;

    private normalScratch: Mat3 = new Float32Array(9);

    constructor(device: GPUDevice) {
        this.device = device;
    }

    /**
     * Packs the list, sorted so that instances sharing a pipeline variant and geometry are
     * contiguous and can be drawn with a single instanced call.
     *
     * Hidden instances are dropped entirely rather than drawn invisibly, which also removes
     * them from picking - matching the Three path, where a hidden mesh is excluded from
     * intersectObjects.
     */
    pack(list: InstanceList, atlas: GeometryAtlas) {
        //Group first: (variant, geom) -> indices into the list.
        const groups = new Map<string, { variant: PipelineVariant; geom: number; items: number[] }>();
        for (let i = 0; i < list.count; ++i) {
            const inst = list.items[i];
            if (inst.flags & IFlags.Hidden) continue;

            const variant = variantFor(inst.flags);
            const range = inst.indexRange;
            const key = range ? `${variant}|${inst.geom}|${range.first}|${range.count}` : `${variant}|${inst.geom}`;
            let g = groups.get(key);
            if (!g) { g = { variant, geom: inst.geom, items: [] }; groups.set(key, g); }
            g.items.push(i);
        }

        const total = Array.from(groups.values()).reduce((a, g) => a + g.items.length, 0);
        this.ensureStaging(total);
        this.slotRefs.length = 0;
        this.slotRefs.push(undefined);      // pick id 0 = background
        this.buckets.length = 0;

        let slot = 0;
        for (const g of groups.values()) {
            const fullSlice = atlas.ensure(g.geom);
            if (!fullSlice) continue;           // geometry not registered yet
            const range = list.items[g.items[0]].indexRange;
            const slice = range ? {
                ...fullSlice, firstIndex: fullSlice.firstIndex + range.first, indexCount: range.count,
            } : fullSlice;

            const firstInstance = slot;
            //Largest linear scale any instance in this bucket applies. Tracked here because
            //the loop already touches every model matrix, so it costs nothing - and the GI
            //injection needs it to size a triangle in world units without a second pass.
            let maxScale = 0;
            for (const index of g.items) {
                const m = list.items[index].model;
                maxScale = Math.max(maxScale,
                    Math.hypot(m[0], m[1], m[2]),
                    Math.hypot(m[4], m[5], m[6]),
                    Math.hypot(m[8], m[9], m[10]));
                this.writeInstance(slot, m, list.items[index].color,
                    list.items[index].emissive, list.items[index].flags, slot + 1);
                this.slotRefs.push(list.items[index].ref);
                slot++;
            }
            this.buckets.push({ variant: g.variant, slice, geom: g.geom, firstInstance, instanceCount: slot - firstInstance, maxScale });
        }

        this.count = slot;
        this.upload();
    }

    private writeInstance(slot: number, model: Mat4, color: { r: number; g: number; b: number },
                          emissive: { r: number; g: number; b: number }, flags: number, pickId: number) {
        const f = this.staging;
        const u = this.stagingU32;
        const at = slot * INSTANCE_FLOATS;

        f.set(model, at);                                    // 0..15, column-major

        //Cofactor of the upper 3x3: proportional to the inverse-transpose, and the shader
        //normalizes anyway so the determinant factor cancels. Stays finite for the
        //degenerate transforms a zero-length organ produces.
        normalMatrix3(model, this.normalScratch);
        const n = this.normalScratch;
        f[at + 16] = n[0]; f[at + 17] = n[1]; f[at + 18] = n[2]; f[at + 19] = 0;
        f[at + 20] = n[3]; f[at + 21] = n[4]; f[at + 22] = n[5]; f[at + 23] = 0;
        f[at + 24] = n[6]; f[at + 25] = n[7]; f[at + 26] = n[8]; f[at + 27] = 0;

        f[at + 28] = color.r; f[at + 29] = color.g; f[at + 30] = color.b; f[at + 31] = 1;
        f[at + 32] = emissive.r; f[at + 33] = emissive.g; f[at + 34] = emissive.b; f[at + 35] = 1;

        u[at + 36] = pickId;
        u[at + 37] = flags;
        u[at + 38] = 0;
        u[at + 39] = 0;
    }

    private ensureStaging(instances: number) {
        const floats = Math.max(1, instances) * INSTANCE_FLOATS;
        if (this.staging.length >= floats) return;
        //1.5x growth: a steadily growing plant should not reallocate every frame.
        const next = Math.max(floats, Math.floor(this.staging.length * 1.5), INSTANCE_FLOATS * 256);
        const buf = new ArrayBuffer(next * 4);
        const f = new Float32Array(buf);
        f.set(this.staging);
        this.staging = f;
        this.stagingU32 = new Uint32Array(buf);
    }

    private upload() {
        //Past the device's storage-buffer limit, Dawn rejects the bind group and every submit
        //after it, and the viewport fills with garbage instead of reporting anything - the only
        //trace is an async validation message. Clamp and say so, once, rather than draw rubbish.
        //Device.ts already raises this limit to the adapter's maximum, so hitting it here means
        //the scene genuinely exceeds what the hardware can bind.
        //128 MiB is WebGPU's guaranteed minimum, and the right assumption when a device does
        //not report limits at all.
        const limit = this.device.limits?.maxStorageBufferBindingSize ?? 134217728;
        const maxInstances = Math.floor(limit / INSTANCE_BYTES);
        if (this.count > maxInstances) {
            if (!this.reportedOverflow) {
                this.reportedOverflow = true;
                console.error(
                    `Scene has ${this.count} primitives, which needs ${(this.count * INSTANCE_BYTES / 1e6).toFixed(0)} MB ` +
                    `of instance data, but this device can bind at most ${(limit / 1e6).toFixed(0)} MB ` +
                    `(${maxInstances} primitives). Drawing the first ${maxInstances} and dropping the rest.`);
            }
            this.count = maxInstances;
            //Buckets index into the same buffer, so anything starting past the clamp must go
            //too, and a bucket straddling it has to be trimmed rather than left overlong.
            for (const b of this.buckets) {
                if (b.firstInstance >= maxInstances) b.instanceCount = 0;
                else if (b.firstInstance + b.instanceCount > maxInstances)
                    b.instanceCount = maxInstances - b.firstInstance;
            }
        }

        const bytes = Math.max(INSTANCE_BYTES, this.count * INSTANCE_BYTES);
        if (!this.buffer || this.capacityBytes < bytes) {
            this.buffer?.destroy();
            //Align up to the struct size. A storage buffer's binding size must be a multiple
            //of 4, and the 1.5x growth factor does not preserve that on its own:
            //floor(4860 * 1.5) = 7290, which WebGPU rejects outright. That only bites once a
            //scene grows past the initial capacity, so it looks like a crash "after a while"
            //rather than an obvious sizing bug.
            this.capacityBytes = alignTo(Math.max(bytes, Math.floor(this.capacityBytes * 1.5)), INSTANCE_BYTES);
            this.buffer = this.device.createBuffer({
                label: 'instances',
                size: this.capacityBytes,
                usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });
        }
        if (this.count > 0)
            this.device.queue.writeBuffer(this.buffer, 0, this.staging.buffer, 0, this.count * INSTANCE_BYTES);
    }

    refFor(pickId: number): DataRef | undefined {
        return pickId > 0 && pickId < this.slotRefs.length ? this.slotRefs[pickId] : undefined;
    }

    destroy() {
        this.buffer?.destroy();
    }
}
