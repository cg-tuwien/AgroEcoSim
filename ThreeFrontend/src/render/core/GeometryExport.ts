import { InstanceList, IFlags } from './InstanceList';
import { getGeometry } from './GeometryStore';
import { computeVertexNormals } from './Geometry';
import { deformLeaf } from './ContactDeformation';

/** Bake precisely the displayed scene into a self-contained glTF, for either renderer. */
export function exportGeometry(lists: InstanceList[]): string {
    const chunks: Uint8Array[] = [], views: any[] = [], accessors: any[] = [], meshes: any[] = [], nodes: any[] = [], materials: any[] = [];
    let byteLength = 0;
    const append = (data: Float32Array | Uint32Array, type: string, componentType: number, min?: number[], max?: number[]) => {
        const view = views.length; views.push({ buffer: 0, byteOffset: byteLength, byteLength: data.byteLength });
        chunks.push(new Uint8Array(data.buffer, data.byteOffset, data.byteLength)); byteLength += data.byteLength;
        const index = accessors.length;
        accessors.push({ bufferView: view, componentType, count: data.length / (type === 'VEC3' ? 3 : 1), type, min, max });
        return index;
    };
    for (const list of lists) for (let i = 0; i < list.count; ++i) {
        const inst = list.items[i]; if (inst.flags & IFlags.Hidden) continue;
        const mesh = getGeometry(inst.geom); if (!mesh) continue;
        const positions = new Float32Array(mesh.positions.length), point = { x: 0, y: 0, z: 0 }, m = inst.model;
        const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
        for (let at = 0; at < positions.length; at += 3) {
            point.x = mesh.positions[at]; point.y = mesh.positions[at + 1]; point.z = mesh.positions[at + 2];
            if (inst.fold) deformLeaf(point, point.x, point.y, point.z, mesh.uvs?.[at / 3 * 2] ?? 0, inst.fold.angles, inst.fold.scale);
            for (let k = 0; k < 3; ++k) {
                const value = m[k] * point.x + m[k + 4] * point.y + m[k + 8] * point.z + m[k + 12];
                positions[at + k] = value; min[k] = Math.min(min[k], value); max[k] = Math.max(max[k], value);
            }
        }
        const indices = inst.indexRange ? mesh.indices.subarray(inst.indexRange.first, inst.indexRange.first + inst.indexRange.count) : mesh.indices;
        const attributes = { POSITION: append(positions, 'VEC3', 5126, min, max), NORMAL: append(computeVertexNormals(positions, indices), 'VEC3', 5126) };
        const index = append(indices, 'SCALAR', 5125), material = materials.length;
        materials.push({ pbrMetallicRoughness: { baseColorFactor: [inst.color.r, inst.color.g, inst.color.b, 1], metallicFactor: 0, roughnessFactor: 1 },
            emissiveFactor: [inst.emissive.r, inst.emissive.g, inst.emissive.b], doubleSided: !!(inst.flags & IFlags.DoubleSided) });
        nodes.push({ mesh: meshes.length, name: inst.ref?.type === 'plant' ? `plant:${inst.ref.index.entity}:organ:${inst.ref.index.primitive}` : mesh.name });
        meshes.push({ primitives: [{ attributes, indices: index, material }] });
    }
    const bytes = new Uint8Array(byteLength); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    const text: string[] = [];
    for (let i = 0; i < bytes.length; i += 8192) text.push(String.fromCharCode(...bytes.subarray(i, i + 8192)));
    return JSON.stringify({ asset: { version: '2.0', generator: 'GreenFDT recorded geometry' }, scene: 0,
        scenes: [{ nodes: nodes.map((_, i) => i) }], nodes, meshes, materials, accessors, bufferViews: views,
        buffers: [{ byteLength, uri: `data:application/octet-stream;base64,${btoa(text.join(''))}` }] });
}
