import { MeshData } from '../src/render/core/Geometry';
import { obstacleMappingGeometry } from '../src/render/core/ObstacleMappingGeometry';
import { toBufferGeometry } from '../src/render/three/ThreeGeometry';

const source = (): MeshData => ({
    positions: new Float32Array(12), normals: new Float32Array(12), colors: new Float32Array(12),
    indices: new Uint32Array([0, 1, 2, 0, 2, 3, 3, 2, 1, 3, 1, 0]), name: 'facade',
});
const groups = [
    { name: 'tray', indexFirst: 0, indexCount: 3 },
    { name: 'wall', indexFirst: 3, indexCount: 3 },
    { name: 'tray-2', indexFirst: 6, indexCount: 3 },
    //The last triangle is unclaimed context.
];

test('partitions every triangle once, retaining source topology and shared vertex storage', () => {
    const mesh = source();
    const before = mesh.indices.slice();
    const result = obstacleMappingGeometry(mesh, groups, new Map([['tray', 0], ['tray-2', 1]]));
    expect(result.contextCount).toBe(6);
    expect(Array.from(result.mesh.indices)).toEqual([0, 2, 3, 3, 1, 0, 0, 1, 2, 3, 2, 1]);
    expect(mesh.indices).toEqual(before);
    expect(result.mesh.positions).toBe(mesh.positions);
    expect(result.mesh.normals).toBe(mesh.normals);
    expect(result.mesh.colors).toBe(mesh.colors);
    const base = toBufferGeometry(mesh);
    const mapped = toBufferGeometry(result.mesh, mesh);
    for (const name of ['position', 'normal', 'color']) expect(mapped.getAttribute(name)).toBe(base.getAttribute(name));
    expect(mapped.index).not.toBe(base.index);
});

test('new values with the same membership do not reorder or allocate; changed membership does', () => {
    const mesh = source();
    const result = obstacleMappingGeometry(mesh, groups, new Map([['tray', 0]]));
    const indices = result.mesh.indices;
    const version = result.version;
    expect(obstacleMappingGeometry(mesh, groups, new Map([['tray', 12]]))).toBe(result);
    expect(result.version).toBe(version);
    obstacleMappingGeometry(mesh, groups, new Map([['wall', 0]]));
    expect(result.version).toBe(version + 1);
    expect(result.mesh.indices).toBe(indices);
    expect(Array.from(indices.slice(9))).toEqual([0, 2, 3]);
    obstacleMappingGeometry(mesh, groups, new Map());
    expect(result.contextCount).toBe(mesh.indices.length);
    expect(result.mesh.indices).toEqual(mesh.indices);
});

test('a fully measured mesh is a single measured range', () => {
    const mesh = source();
    const all = [{ name: 'all', indexFirst: 0, indexCount: mesh.indices.length }];
    const result = obstacleMappingGeometry(mesh, all, new Map([['all', 0]]));
    expect(result.contextCount).toBe(0);
    expect(result.mesh.indices).toEqual(mesh.indices);
});
