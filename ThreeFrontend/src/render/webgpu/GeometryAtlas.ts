//One vertex buffer and one index buffer for every geometry in the scene.
//
//Everything is drawn from a single pair of buffers so that a frame is a handful of
//drawIndexed calls with different (firstIndex, baseVertex) rather than thousands of buffer
//rebinds. That is the main reason the WebGPU path can be faster than the Three one, which
//issues one draw per primitive.

import { MeshData } from '../core/Geometry';
import { getGeometry } from '../core/GeometryStore';

/** pos(3) + nrm(3) + uv(2) + col(3) = 11 floats. */
export const VERTEX_FLOATS = 11;
export const VERTEX_BYTES = VERTEX_FLOATS * 4;

export type GeometrySlice = { firstIndex: number; indexCount: number; baseVertex: number };

export const VERTEX_LAYOUT: GPUVertexBufferLayout = {
    arrayStride: VERTEX_BYTES,
    attributes: [
        { shaderLocation: 0, offset: 0, format: 'float32x3' },   // position
        { shaderLocation: 1, offset: 12, format: 'float32x3' },  // normal
        { shaderLocation: 2, offset: 24, format: 'float32x2' },  // uv
        { shaderLocation: 3, offset: 32, format: 'float32x3' },  // colour
    ],
};

export class GeometryAtlas {
    private device: GPUDevice;
    private slices = new Map<number, GeometrySlice>();
    /** Memoised per geometry; see maxTriangleArea. */
    private maxTriArea = new Map<number, number>();

    private vertexData: Float32Array = new Float32Array(0);
    private indexData: Uint32Array = new Uint32Array(0);
    private vertexCount = 0;
    private indexCount = 0;

    vertexBuffer: GPUBuffer | undefined;
    indexBuffer: GPUBuffer | undefined;
    private vertexCapacity = 0;
    private indexCapacity = 0;

    constructor(device: GPUDevice) {
        this.device = device;
    }

    /**
     * Ensures the geometry with this id is resident, uploading it on first use.
     *
     * Ids come from the shared GeometryStore, so the atlas never needs to know whether a
     * geometry is one of the static prototypes or a per-species leaf mesh.
     */
    /**
     * Largest triangle in a geometry, in its own local units. Computed once per geometry.
     *
     * The GI injection needs to know how many cells a triangle can span before it dispatches,
     * and this times the bucket's max instance scale squared bounds that without touching a
     * single instance. It is what separates a terrain quad, which needs thousands of splat
     * samples, from an imported mesh of the same total size whose triangles each need one.
     */
    maxTriangleArea(geomId: number): number {
        const cached = this.maxTriArea.get(geomId);
        if (cached !== undefined) return cached;

        const slice = this.slices.get(geomId);
        if (!slice) return 0;

        const tri = new Float32Array(9);
        let max = 0;
        for (let t = 0; t * 3 < slice.indexCount; ++t) {
            if (!this.triangle(slice, t, tri)) break;
            const ux = tri[3] - tri[0], uy = tri[4] - tri[1], uz = tri[5] - tri[2];
            const vx = tri[6] - tri[0], vy = tri[7] - tri[1], vz = tri[8] - tri[2];
            const cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            max = Math.max(max, 0.5 * Math.hypot(cx, cy, cz));
        }
        this.maxTriArea.set(geomId, max);
        return max;
    }

    /**
     * Reads one triangle's world-space-agnostic positions out of the staging arrays.
     *
     * Exists for the voxel-field reference implementation, which has to walk exactly the
     * triangles the injection kernel walks - reading the same arrays is what makes the two
     * comparable rather than merely similar.
     */
    triangle(slice: GeometrySlice, tri: number, out: Float32Array): boolean {
        const i0 = slice.firstIndex + tri * 3;
        if (i0 + 2 >= this.indexCount) return false;
        for (let k = 0; k < 3; ++k) {
            const v = (slice.baseVertex + this.indexData[i0 + k]) * VERTEX_FLOATS;
            out[k * 3] = this.vertexData[v];
            out[k * 3 + 1] = this.vertexData[v + 1];
            out[k * 3 + 2] = this.vertexData[v + 2];
        }
        return true;
    }

    ensure(geomId: number): GeometrySlice | undefined {
        const existing = this.slices.get(geomId);
        if (existing) return existing;

        const mesh = getGeometry(geomId);
        if (!mesh) return undefined;

        const slice = this.append(mesh);
        this.slices.set(geomId, slice);
        return slice;
    }

    private append(mesh: MeshData): GeometrySlice {
        const verts = mesh.positions.length / 3;
        const baseVertex = this.vertexCount;
        const firstIndex = this.indexCount;

        this.growVertices(this.vertexCount + verts);
        this.growIndices(this.indexCount + mesh.indices.length);

        //Interleave. Missing uv/colour default to zero, which the shader ignores unless the
        //VertexColors flag is set.
        const v = this.vertexData;
        for (let i = 0; i < verts; ++i) {
            const at = (baseVertex + i) * VERTEX_FLOATS;
            v[at] = mesh.positions[i * 3];
            v[at + 1] = mesh.positions[i * 3 + 1];
            v[at + 2] = mesh.positions[i * 3 + 2];
            v[at + 3] = mesh.normals[i * 3];
            v[at + 4] = mesh.normals[i * 3 + 1];
            v[at + 5] = mesh.normals[i * 3 + 2];
            v[at + 6] = mesh.uvs ? mesh.uvs[i * 2] : 0;
            v[at + 7] = mesh.uvs ? mesh.uvs[i * 2 + 1] : 0;
            v[at + 8] = mesh.colors ? mesh.colors[i * 3] : 0;
            v[at + 9] = mesh.colors ? mesh.colors[i * 3 + 1] : 0;
            v[at + 10] = mesh.colors ? mesh.colors[i * 3 + 2] : 0;
        }
        //Indices stay geometry-local; baseVertex offsets them at draw time.
        this.indexData.set(mesh.indices, firstIndex);

        this.vertexCount += verts;
        this.indexCount += mesh.indices.length;
        this.dirty = true;

        return { firstIndex, indexCount: mesh.indices.length, baseVertex };
    }

    private dirty = false;

    private growVertices(needed: number) {
        if (needed * VERTEX_FLOATS <= this.vertexData.length) return;
        const next = new Float32Array(Math.max(needed * VERTEX_FLOATS, this.vertexData.length * 2 || 4096));
        next.set(this.vertexData);
        this.vertexData = next;
    }

    private growIndices(needed: number) {
        if (needed <= this.indexData.length) return;
        const next = new Uint32Array(Math.max(needed, this.indexData.length * 2 || 4096));
        next.set(this.indexData);
        this.indexData = next;
    }

    /** Uploads anything appended since the last call. */
    flush() {
        if (!this.dirty) return;
        this.dirty = false;

        const vBytes = this.vertexCount * VERTEX_BYTES;
        const iBytes = align4(this.indexCount * 4);

        if (!this.vertexBuffer || this.vertexCapacity < vBytes) {
            this.vertexBuffer?.destroy();
            this.vertexCapacity = Math.max(vBytes, this.vertexCapacity * 2, 64 * 1024);
            this.vertexBuffer = this.device.createBuffer({
                label: 'geometry-vertices',
                size: align4(this.vertexCapacity),
                //STORAGE as well as VERTEX: the GI injection pass walks these triangles in compute.
                usage: GPUBufferUsage.VERTEX | GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });
        }
        if (!this.indexBuffer || this.indexCapacity < iBytes) {
            this.indexBuffer?.destroy();
            this.indexCapacity = Math.max(iBytes, this.indexCapacity * 2, 64 * 1024);
            this.indexBuffer = this.device.createBuffer({
                label: 'geometry-indices',
                size: align4(this.indexCapacity),
                usage: GPUBufferUsage.INDEX | GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST,
            });
        }

        this.device.queue.writeBuffer(this.vertexBuffer, 0, this.vertexData.buffer, 0, vBytes);
        this.device.queue.writeBuffer(this.indexBuffer, 0, this.indexData.buffer, 0, iBytes);
    }

    destroy() {
        this.vertexBuffer?.destroy();
        this.indexBuffer?.destroy();
        this.slices.clear();
        this.maxTriArea.clear();
    }
}

function align4(n: number) {
    return (n + 3) & ~3;
}
