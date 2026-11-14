import { computeVertexNormals, type MeshData } from './Geometry';
import type { RGB } from './Color';
import { deformLeaf, type Fold } from './ContactDeformation';
import { createMat4, multiply, normalMatrix3, type Mat4 } from './Mat4';

/** A clean export description. Picking references and renderer objects never enter the file. */
export type ExportNode = {
    name: string;
    children?: ExportNode[];
    mesh?: MeshData;
    model?: Mat4;
    color?: RGB;
    doubleSided?: boolean;
    flatShaded?: boolean;
    fold?: Fold;
    extras?: Record<string, unknown>;
};

type Attributes = { POSITION: number; NORMAL?: number; TEXCOORD_0?: number; COLOR_0?: number };
type GeometryAccessors = { attributes: Attributes; indices: number };
type GltfNode = { name: string; children?: number[]; matrix?: number[]; mesh?: number; extras?: Record<string, unknown> };
type AccessorType = 'SCALAR' | 'VEC2' | 'VEC3';
const COMPONENTS: Record<AccessorType, number> = { SCALAR: 1, VEC2: 2, VEC3: 3 };
const DEFAULT_COLOR: RGB = { r: .5, g: .5, b: .5 };

/**
 * Export a renderer-neutral snapshot as self-contained glTF 2.0. Rigid instances share their
 * vertex data and keep their matrices; only deformation and transforms glTF cannot express
 * as TRS need fresh vertices. This keeps a mature canopy proportional to its organ count.
 */
export function exportGeometry(roots: ExportNode[], sceneExtras?: Record<string, unknown>): string {
    const chunks: Uint8Array[] = [];
    const bufferViews: { buffer: number; byteOffset: number; byteLength: number; target: number }[] = [];
    const accessors: { bufferView: number; componentType: number; count: number; type: AccessorType; min?: number[]; max?: number[] }[] = [];
    const meshes: { primitives: { attributes: Attributes; indices: number; material: number }[] }[] = [];
    const materials: { pbrMetallicRoughness: { baseColorFactor: number[]; metallicFactor: number; roughnessFactor: number }; doubleSided: boolean }[] = [];
    const nodes: GltfNode[] = [];
    const geometryCache = new Map<MeshData, GeometryAccessors>();
    const meshCache = new Map<GeometryAccessors, Map<string, number>>();
    const materialCache = new Map<string, number>();
    let byteLength = 0;

    const append = (data: Float32Array | Uint32Array, type: AccessorType, indices = false, min?: number[], max?: number[]) => {
        const view = bufferViews.length;
        bufferViews.push({ buffer: 0, byteOffset: byteLength, byteLength: data.byteLength, target: indices ? 34963 : 34962 });
        chunks.push(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
        byteLength += data.byteLength; // Every attribute uses 32-bit components, hence remains aligned.
        const index = accessors.length;
        accessors.push({ bufferView: view, componentType: indices ? 5125 : 5126,
            count: data.length / COMPONENTS[type], type, min, max });
        return index;
    };

    const geometry = (mesh: MeshData, smooth: boolean): GeometryAccessors => {
        const cached = geometryCache.get(mesh);
        if (cached) {
            if (smooth && cached.attributes.NORMAL === undefined)
                cached.attributes.NORMAL = append(unitNormals(mesh.normals?.length === mesh.positions.length
                    ? mesh.normals : computeVertexNormals(mesh.positions, mesh.indices)), 'VEC3');
            return cached;
        }
        if (mesh.positions.length % 3 || mesh.indices.length % 3)
            throw new Error(`Invalid triangle geometry: ${mesh.name}`);
        const count = mesh.positions.length / 3;
        if (mesh.indices.some(index => index >= count)) throw new Error(`Invalid vertex index: ${mesh.name}`);
        const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
        for (let at = 0; at < mesh.positions.length; ++at) {
            const value = mesh.positions[at], axis = at % 3;
            if (!Number.isFinite(value)) throw new Error(`Non-finite vertex: ${mesh.name}`);
            min[axis] = Math.min(min[axis], value); max[axis] = Math.max(max[axis], value);
        }
        const attributes: Attributes = { POSITION: append(mesh.positions, 'VEC3', false, min, max) };
        if (smooth) attributes.NORMAL = append(unitNormals(mesh.normals?.length === mesh.positions.length
            ? mesh.normals : computeVertexNormals(mesh.positions, mesh.indices)), 'VEC3');
        if (mesh.uvs) {
            checkAttribute(mesh.uvs, count * 2, mesh.name);
            attributes.TEXCOORD_0 = append(mesh.uvs, 'VEC2');
        }
        if (mesh.colors) {
            checkAttribute(mesh.colors, count * 3, mesh.name);
            attributes.COLOR_0 = append(mesh.colors, 'VEC3');
        }
        const result = { attributes, indices: append(mesh.indices, 'SCALAR', true) };
        geometryCache.set(mesh, result);
        return result;
    };

    const visit = (source: ExportNode, inheritedBake?: Mat4): number => {
        let model = source.model;
        if (inheritedBake) model = model ? multiply(inheritedBake, model, createMat4()) : inheritedBake;
        if (model) checkMatrix(model, source.name);
        const bake = model && !isTrs(model) ? model : undefined;
        const node: GltfNode = { name: source.name };
        const index = nodes.length;
        nodes.push(node);
        if (model && !bake) node.matrix = Array.from(model);
        if (source.extras !== undefined) node.extras = source.extras;

        if (source.mesh?.positions.length && source.mesh.indices.length) {
            const folded = source.fold?.angles.some(value => value !== 0) ? source.fold : undefined;
            const mesh = folded || bake ? bakeGeometry(source.mesh, folded, bake) : source.mesh;
            if (mesh.indices.length) {
                const data = geometry(mesh, !source.flatShaded), color = source.color ?? DEFAULT_COLOR;
                if (![color.r, color.g, color.b].every(value => Number.isFinite(value) && value >= 0 && value <= 1))
                    throw new Error(`Invalid material color: ${source.name}`);
                const materialKey = `${color.r},${color.g},${color.b},${!!source.doubleSided}`;
                let material = materialCache.get(materialKey);
                if (material === undefined) {
                    material = materials.length;
                    materialCache.set(materialKey, material);
                    materials.push({ pbrMetallicRoughness: { baseColorFactor: [color.r, color.g, color.b, 1],
                        metallicFactor: 0, roughnessFactor: 1 }, doubleSided: !!source.doubleSided });
                }
                let variants = meshCache.get(data);
                if (!variants) { variants = new Map(); meshCache.set(data, variants); }
                const key = `${material}:${!!source.flatShaded}`;
                let meshIndex = variants.get(key);
                if (meshIndex === undefined) {
                    meshIndex = meshes.length;
                    const attributes = { ...data.attributes };
                    // glTF defines missing normals as flat shading. Do not smooth a folded blade
                    // or a faceted tray merely because they share indexed vertices.
                    if (source.flatShaded) delete attributes.NORMAL;
                    meshes.push({ primitives: [{ attributes, indices: data.indices, material }] });
                    variants.set(key, meshIndex);
                }
                node.mesh = meshIndex;
            }
        }
        // If a group had a shear, its transform was removed from the glTF hierarchy. Carry it
        // into its children as well as baking its own mesh, so the whole subtree stays put.
        if (source.children?.length) node.children = source.children.map(child => visit(child, bake));
        return index;
    };

    const scene: { nodes?: number[]; extras?: Record<string, unknown> } = {};
    if (roots.length) scene.nodes = roots.map(root => visit(root));
    if (sceneExtras !== undefined) scene.extras = sceneExtras;
    const bytes = new Uint8Array(byteLength);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    const base64: string[] = [];
    // Multiples of three let chunks be encoded independently, without intermediate padding
    // or a second full-sized binary string beside the output's base64 string.
    for (let at = 0; at < bytes.length; at += 24576)
        base64.push(btoa(String.fromCharCode(...bytes.subarray(at, at + 24576))));
    return JSON.stringify({ asset: { version: '2.0', generator: 'GreenFDT recorded geometry' }, scene: 0,
        scenes: [scene], ...(nodes.length ? { nodes } : {}),
        ...(byteLength ? { meshes, materials, accessors, bufferViews,
            buffers: [{ byteLength, uri: `data:application/octet-stream;base64,${base64.join('')}` }] } : {}) });
}

function checkAttribute(values: Float32Array, length: number, name: string) {
    if (values.length !== length || values.some(value => !Number.isFinite(value)))
        throw new Error(`Invalid vertex attribute: ${name}`);
}

function checkMatrix(model: Mat4, name: string) {
    if (model.length !== 16 || model.some(value => !Number.isFinite(value))
        || model[3] !== 0 || model[7] !== 0 || model[11] !== 0 || model[15] !== 1)
        throw new Error(`Invalid affine transform: ${name}`);
}

/** Reflections are valid glTF matrices; shear and zero-length basis vectors are not. */
function isTrs(m: Mat4): boolean {
    const x = Math.hypot(m[0], m[1], m[2]), y = Math.hypot(m[4], m[5], m[6]), z = Math.hypot(m[8], m[9], m[10]);
    return x > 0 && y > 0 && z > 0
        && Math.abs((m[0] * m[4] + m[1] * m[5] + m[2] * m[6]) / x / y) < 1e-5
        && Math.abs((m[0] * m[8] + m[1] * m[9] + m[2] * m[10]) / x / z) < 1e-5
        && Math.abs((m[4] * m[8] + m[5] * m[9] + m[6] * m[10]) / y / z) < 1e-5;
}

function determinant(m: Mat4): number {
    return m[0] * (m[5] * m[10] - m[6] * m[9])
        + m[4] * (m[2] * m[9] - m[1] * m[10]) + m[8] * (m[1] * m[6] - m[2] * m[5]);
}

/** Normalize only when necessary, keeping authored unit normals and their sharing intact. */
function unitNormals(source: Float32Array): Float32Array {
    let result = source;
    for (let at = 0; at < source.length; at += 3) {
        const length = Math.hypot(source[at], source[at + 1], source[at + 2]);
        if (Number.isFinite(length) && Math.abs(length - 1) < 1e-5) continue;
        if (result === source) result = source.slice();
        if (length > 0 && Number.isFinite(length)) {
            result[at] /= length; result[at + 1] /= length; result[at + 2] /= length;
        } else {
            // Unreferenced vertices and collapsed triangles can have no normal. Keep their
            // attribute finite and unit length; they contribute no rendered surface.
            result[at] = result[at + 1] = 0; result[at + 2] = 1;
        }
    }
    return result;
}

function bakeGeometry(mesh: MeshData, fold?: Fold, model?: Mat4): MeshData {
    const positions = new Float32Array(mesh.positions.length), point = { x: 0, y: 0, z: 0 };
    for (let at = 0; at < positions.length; at += 3) {
        point.x = mesh.positions[at]; point.y = mesh.positions[at + 1]; point.z = mesh.positions[at + 2];
        if (fold) deformLeaf(point, point.x, point.y, point.z, mesh.uvs?.[at / 3 * 2] ?? 0, fold.angles, fold.scale);
        positions[at] = point.x; positions[at + 1] = point.y; positions[at + 2] = point.z;
    }
    let normals = fold || mesh.normals?.length !== positions.length
        ? computeVertexNormals(positions, mesh.indices) : mesh.normals;
    let indices = mesh.indices;
    if (model) {
        const det = determinant(model), normalMatrix = normalMatrix3(model, new Float32Array(9));
        const transformedNormals = new Float32Array(normals.length), sign = det < 0 ? -1 : 1;
        for (let at = 0; at < positions.length; at += 3) {
            const x = positions[at], y = positions[at + 1], z = positions[at + 2];
            const nx = normals[at], ny = normals[at + 1], nz = normals[at + 2];
            for (let axis = 0; axis < 3; ++axis) {
                positions[at + axis] = model[axis] * x + model[axis + 4] * y + model[axis + 8] * z + model[axis + 12];
                transformedNormals[at + axis] = sign * (normalMatrix[axis] * nx + normalMatrix[axis + 3] * ny + normalMatrix[axis + 6] * nz);
            }
        }
        if (det < 0) {
            indices = indices.slice();
            for (let at = 0; at < indices.length; at += 3) [indices[at + 1], indices[at + 2]] = [indices[at + 2], indices[at + 1]];
        }
        if (det === 0) {
            const kept: number[] = [];
            for (let at = 0; at < indices.length; at += 3) {
                const a = indices[at] * 3, b = indices[at + 1] * 3, c = indices[at + 2] * 3;
                const ux = positions[b] - positions[a], uy = positions[b + 1] - positions[a + 1], uz = positions[b + 2] - positions[a + 2];
                const vx = positions[c] - positions[a], vy = positions[c + 1] - positions[a + 1], vz = positions[c + 2] - positions[a + 2];
                if (Math.hypot(uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx) > 0)
                    kept.push(indices[at], indices[at + 1], indices[at + 2]);
            }
            indices = Uint32Array.from(kept);
        }
        normals = det === 0 ? computeVertexNormals(positions, indices) : unitNormals(transformedNormals);
    }
    return { ...mesh, positions, normals, indices };
}
