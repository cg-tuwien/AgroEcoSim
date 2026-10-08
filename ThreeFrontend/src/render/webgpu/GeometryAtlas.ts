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

/**
 * Where one geometry lives in the atlas.
 *
 * `vertexCount` is carried so that a write into the slice can be bounded. It used not to be, and
 * nothing else records it: the registry REPLACES the mesh stored under an existing id
 * (GeometryRegistry.register), while the atlas keeps the slice it appended from the old one. Any
 * write sized from the current mesh could then run past the end of its slice and into whatever
 * geometry was appended next - which shows up as unrelated objects wearing this one's colours.
 */
export type GeometrySlice = { firstIndex: number; indexCount: number; baseVertex: number; vertexCount: number };

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
    triangle(slice: GeometrySlice, tri: number, out: Float32Array, hinges?: Float32Array): boolean {
        const i0 = slice.firstIndex + tri * 3;
        if (i0 + 2 >= this.indexCount) return false;
        for (let k = 0; k < 3; ++k) {
            const v = (slice.baseVertex + this.indexData[i0 + k]) * VERTEX_FLOATS;
            out[k * 3] = this.vertexData[v];
            out[k * 3 + 1] = this.vertexData[v + 1];
            out[k * 3 + 2] = this.vertexData[v + 2];
            if (hinges) hinges[k] = this.vertexData[v + 6];
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

    /**
     * Re-reads a geometry's vertex colours into the atlas in place.
     *
     * The obstacle mapping repaints a million-vertex facade mesh on every step of playback. The
     * atlas never reclaims space, so re-registering the mesh would grow the buffer without bound;
     * the colours are the only thing that changed and the vertex count did not, so they can be
     * overwritten where they already are. flush() re-uploads the whole buffer, hence just the
     * dirty flag.
     */
    refreshVertexColors(geomId: number): boolean {
        const slice = this.slices.get(geomId);
        const mesh = getGeometry(geomId);
        if (!slice || !mesh?.colors) return false;

        //Bounded by the SLICE, not by the mesh. The two are the same geometry only as long as
        //nothing re-registered this id with a different mesh; if something did, sizing the write
        //from the mesh would silently repaint the geometry stored after it, and the symptom would
        //appear on an unrelated object rather than here.
        const meshVerts = mesh.positions.length / 3;
        if (meshVerts !== slice.vertexCount) {
            console.warn(`Geometry ${geomId} was re-registered with ${meshVerts} vertices but the atlas `
                + `holds a ${slice.vertexCount}-vertex slice for it. Refreshing only what fits; the atlas `
                + `entry is stale and the geometry should be re-appended.`);
        }
        const verts = Math.min(meshVerts, slice.vertexCount);
        const v = this.vertexData;
        for (let i = 0; i < verts; ++i) {
            const at = (slice.baseVertex + i) * VERTEX_FLOATS;
            v[at + 8] = mesh.colors[i * 3];
            v[at + 9] = mesh.colors[i * 3 + 1];
            v[at + 10] = mesh.colors[i * 3 + 2];
        }
        this.dirty = true;
        return true;
    }

    /** Replace only an existing index slice when obstacle measurement membership changes. */
    refreshIndices(geomId: number): boolean {
        const slice = this.slices.get(geomId);
        const mesh = getGeometry(geomId);
        if (!slice || !mesh) return false;
        if (mesh.indices.length !== slice.indexCount)
            throw new Error(`Geometry ${geomId} index count changed inside an existing atlas slice`);
        this.indexData.set(mesh.indices, slice.firstIndex);
        if (this.indexBuffer && !this.dirty) {
            this.device.queue.writeBuffer(this.indexBuffer, slice.firstIndex * 4,
                mesh.indices.buffer, mesh.indices.byteOffset, mesh.indices.byteLength);
            ++this.uploads;
            this.uploadedBytes += mesh.indices.byteLength;
        } else this.dirty = true;
        return true;
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

        return { firstIndex, indexCount: mesh.indices.length, baseVertex, vertexCount: verts };
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

    /**
     * How much this atlas has re-uploaded, and how often.
     *
     * A diagnostic rather than a metric. The documented cause of obstacle flicker is a full vertex
     * re-upload once per mouse-move event - 42 MB on an imported facade - and the guards against it
     * live in three places (the paint signature, the resident-slice check, the dirty flag). If the
     * viewport flickers again, the first question is whether this counter is climbing while nothing
     * but the camera moves; if it is not, the cause is in the draw and not in the upload.
     */
    uploads = 0;
    uploadedBytes = 0;

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
        ++this.uploads;
        this.uploadedBytes += vBytes + iBytes;
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
