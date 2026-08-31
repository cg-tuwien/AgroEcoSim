//Renderer-neutral geometry. Nothing in this file may import three.
//Both the Three.js adapter (src/render/three) and the WebGPU renderer (src/render/webgpu)
//consume MeshData, so a geometry only ever has to be defined once.

export type MeshData = {
    /** Interleaved xyz, 3 floats per vertex. */
    positions: Float32Array;
    /** Interleaved xyz, 3 floats per vertex. Always present; generated if the source lacked it. */
    normals: Float32Array;
    /** Interleaved uv, 2 floats per vertex. Absent when the source geometry carried no uvs. */
    uvs?: Float32Array;
    /** Interleaved linear rgb, 3 floats per vertex. Only the debug box uses this. */
    colors?: Float32Array;
    indices: Uint32Array;
    /** Diagnostic only - shows up in buffer labels and error messages. */
    name: string;
};

export function vertexCount(m: MeshData) {
    return m.positions.length / 3;
}

export function triangleCount(m: MeshData) {
    return m.indices.length / 3;
}

/**
 * Area-weighted smooth vertex normals, matching THREE.BufferGeometry.computeVertexNormals().
 * three accumulates the raw (unnormalized) face cross products, which weights each face by
 * twice its area, and only normalizes at the end - reproduce that exactly rather than
 * averaging unit face normals, or shading diverges on the leaf meshes.
 */
export function computeVertexNormals(positions: Float32Array, indices: Uint32Array): Float32Array {
    const normals = new Float32Array(positions.length);

    for (let i = 0; i < indices.length; i += 3) {
        const ia = indices[i] * 3, ib = indices[i + 1] * 3, ic = indices[i + 2] * 3;

        const ax = positions[ia], ay = positions[ia + 1], az = positions[ia + 2];
        const bx = positions[ib], by = positions[ib + 1], bz = positions[ib + 2];
        const cx = positions[ic], cy = positions[ic + 1], cz = positions[ic + 2];

        //edges b-a and c-a
        const e1x = bx - ax, e1y = by - ay, e1z = bz - az;
        const e2x = cx - ax, e2y = cy - ay, e2z = cz - az;

        const nx = e1y * e2z - e1z * e2y;
        const ny = e1z * e2x - e1x * e2z;
        const nz = e1x * e2y - e1y * e2x;

        normals[ia] += nx; normals[ia + 1] += ny; normals[ia + 2] += nz;
        normals[ib] += nx; normals[ib + 1] += ny; normals[ib + 2] += nz;
        normals[ic] += nx; normals[ic + 1] += ny; normals[ic + 2] += nz;
    }

    normalizeInPlace(normals);
    return normals;
}

export function normalizeInPlace(v: Float32Array) {
    for (let i = 0; i < v.length; i += 3) {
        const x = v[i], y = v[i + 1], z = v[i + 2];
        const len = Math.sqrt(x * x + y * y + z * z);
        if (len > 0) {
            const inv = 1 / len;
            v[i] = x * inv; v[i + 1] = y * inv; v[i + 2] = z * inv;
        }
    }
}

/** Axis-aligned bounds, used for the GI voxel domain and for offline camera framing. */
export function computeBounds(m: MeshData) {
    const min = [Infinity, Infinity, Infinity];
    const max = [-Infinity, -Infinity, -Infinity];
    const p = m.positions;
    for (let i = 0; i < p.length; i += 3)
        for (let c = 0; c < 3; ++c) {
            if (p[i + c] < min[c]) min[c] = p[i + c];
            if (p[i + c] > max[c]) max[c] = p[i + c];
        }
    return { min, max };
}
