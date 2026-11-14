import * as THREE from 'three';
import { readFileSync } from 'fs';
import { dirname, resolve } from 'path';
import { exportGeometry, ExportNode } from '../src/render/core/GeometryExport';
import { MeshData, computeVertexNormals } from '../src/render/core/Geometry';
import { createMat4 } from '../src/render/core/Mat4';
import { deformLeaf, foldScale } from '../src/render/core/ContactDeformation';

function triangle(): MeshData {
    const positions = new Float32Array([-1, -1, 0, 1, -1, 0, 1, 1, 0]);
    const indices = new Uint32Array([0, 1, 2]);
    return { name: 'blade', positions, indices, normals: computeVertexNormals(positions, indices),
        uvs: new Float32Array([0, 0, 0, 1, 0, 1]) };
}

function readAccessor(gltf: any, index: number): number[] {
    const accessor = gltf.accessors[index], view = gltf.bufferViews[accessor.bufferView];
    const bytes = Buffer.from(gltf.buffers[view.buffer].uri.split(',')[1], 'base64');
    const data = new DataView(bytes.buffer, bytes.byteOffset + view.byteOffset + (accessor.byteOffset ?? 0), view.byteLength);
    const size = { SCALAR: 1, VEC2: 2, VEC3: 3 }[accessor.type] as number;
    return Array.from({ length: accessor.count * size }, (_, at) => accessor.componentType === 5125
        ? data.getUint32(at * 4, true) : data.getFloat32(at * 4, true));
}

function primitive(gltf: any, name: string) {
    const node = gltf.nodes.find((item: any) => item.name === name);
    return gltf.meshes[node.mesh].primitives[0];
}

function expectArrayClose(actual: ArrayLike<number>, expected: ArrayLike<number>, precision = 6) {
    expect(actual.length).toBe(expected.length);
    for (let i = 0; i < expected.length; ++i) expect(actual[i]).toBeCloseTo(expected[i], precision);
}

test('keeps category hierarchy, shares rigid geometry and materials, and preserves optional attributes', () => {
    const mesh = triangle();
    mesh.colors = new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]);
    const offset = createMat4(); offset[12] = 4;
    const roots: ExportNode[] = [{ name: 'Plants (above ground)', children: [
        { name: 'first', mesh, doubleSided: true },
        { name: 'second', mesh, model: offset, doubleSided: true },
        { name: 'third', mesh, color: { r: 0, g: 1, b: 0 }, doubleSided: true },
    ] }];
    const gltf = JSON.parse(exportGeometry(roots));
    expect(gltf.scenes[0].nodes).toEqual([0]);
    expect(gltf.nodes[0]).toEqual({ name: 'Plants (above ground)', children: [1, 2, 3] });
    expect(gltf.nodes[1].mesh).toBe(gltf.nodes[2].mesh);
    expect(gltf.nodes[2].matrix).toEqual(Array.from(offset));
    expect(gltf.meshes).toHaveLength(2);
    expect(gltf.materials).toHaveLength(2);
    expect(primitive(gltf, 'third').attributes).toEqual(primitive(gltf, 'first').attributes);
    expect(readAccessor(gltf, primitive(gltf, 'first').attributes.POSITION)).toEqual(Array.from(mesh.positions));
    expect(readAccessor(gltf, primitive(gltf, 'first').attributes.NORMAL)).toEqual(Array.from(mesh.normals));
    expect(readAccessor(gltf, primitive(gltf, 'first').attributes.TEXCOORD_0)).toEqual(Array.from(mesh.uvs!));
    expect(readAccessor(gltf, primitive(gltf, 'first').attributes.COLOR_0)).toEqual(Array.from(mesh.colors));
    expect(gltf.materials[0].pbrMetallicRoughness.baseColorFactor).toEqual([.5, .5, .5, 1]);
    expect(gltf.materials[0].doubleSided).toBe(true);
    expect(gltf.bufferViews.every((view: any) => view.byteOffset % 4 === 0)).toBe(true);
});

test('writes only explicit extras and represents flat shading by omitting normals', () => {
    const mesh = triangle();
    const plain = exportGeometry([{ name: 'leaf', mesh }]);
    expect(plain).not.toContain('extras');
    const gltf = JSON.parse(exportGeometry([{ name: 'leaf', mesh, flatShaded: true,
        extras: { species: 'Geranium', waterRatio: .75 } }], { timestep: 12, hoursPerTick: 4 }));
    expect(gltf.scenes[0].extras).toEqual({ timestep: 12, hoursPerTick: 4 });
    expect(gltf.nodes[0].extras).toEqual({ species: 'Geranium', waterRatio: .75 });
    expect(primitive(gltf, 'leaf').attributes.NORMAL).toBeUndefined();
    expect(JSON.parse(exportGeometry([{ name: 'Soil' }]))).not.toHaveProperty('buffers');
    expect(JSON.parse(exportGeometry([]))).toEqual({ asset: { version: '2.0', generator: 'GreenFDT recorded geometry' }, scene: 0, scenes: [{}] });
});

test('keeps reflected, nonuniform rigid transforms and shared indices intact', () => {
    const mesh = triangle(), reflected = createMat4();
    reflected[0] = -2; reflected[5] = 3; reflected[10] = .5; reflected[13] = 7;
    const gltf = JSON.parse(exportGeometry([{ name: 'reflected', mesh, model: reflected }, { name: 'plain', mesh }]));
    expect(gltf.nodes[0].matrix).toEqual(Array.from(reflected));
    expect(gltf.nodes[0].mesh).toBe(gltf.nodes[1].mesh);
    expect(readAccessor(gltf, primitive(gltf, 'reflected').indices)).toEqual([0, 1, 2]);
});

test('bakes the same folded leaf coordinates as the renderer without changing source arrays', () => {
    const mesh = triangle(), before = mesh.positions.slice(), model = createMat4();
    model[0] = -.04; model[5] = .02; model[10] = .015; model[12] = 2;
    const fold = { angles: new Float32Array([.3, -.2, .4, .1]), scale: foldScale(model) };
    const gltf = JSON.parse(exportGeometry([{ name: 'folded', mesh, model, fold }, { name: 'rigid', mesh, model }]));
    const points = readAccessor(gltf, primitive(gltf, 'folded').attributes.POSITION);
    const expected: number[] = [];
    for (let at = 0; at < mesh.positions.length; at += 3) {
        const p = { x: 0, y: 0, z: 0 };
        deformLeaf(p, mesh.positions[at], mesh.positions[at + 1], mesh.positions[at + 2], mesh.uvs![at / 3 * 2], fold.angles, fold.scale);
        expected.push(p.x, p.y, p.z);
    }
    expectArrayClose(points, expected);
    expect(gltf.nodes[0].matrix).toEqual(Array.from(model));
    expect(gltf.nodes[0].mesh).not.toBe(gltf.nodes[1].mesh);
    expect(mesh.positions).toEqual(before);
    const noUvs = { ...mesh, uvs: undefined };
    expect(primitive(JSON.parse(exportGeometry([{ name: 'no-uvs', mesh: noUvs, fold }])), 'no-uvs').attributes.TEXCOORD_0).toBeUndefined();
});

test('bakes a sheared parent into descendants with correct reflected winding and inverse-transpose normals', () => {
    const mesh = triangle(), shear = createMat4(), child = createMat4();
    shear[2] = .4; shear[12] = 3;
    child[0] = -2; child[5] = 3; child[13] = 2;
    const gltf = JSON.parse(exportGeometry([{ name: 'shear', model: shear,
        children: [{ name: 'blade', model: child, mesh }] }]));
    expect(gltf.nodes.every((node: any) => node.matrix === undefined)).toBe(true);
    const matrix = new THREE.Matrix4().fromArray(shear).multiply(new THREE.Matrix4().fromArray(child));
    const expected: number[] = [];
    for (let at = 0; at < mesh.positions.length; at += 3)
        expected.push(...new THREE.Vector3().fromArray(mesh.positions, at).applyMatrix4(matrix).toArray());
    const out = primitive(gltf, 'blade');
    expectArrayClose(readAccessor(gltf, out.attributes.POSITION), expected);
    expect(readAccessor(gltf, out.indices)).toEqual([0, 2, 1]);
    const expectedNormal = new THREE.Vector3(0, 0, 1).applyNormalMatrix(new THREE.Matrix3().getNormalMatrix(matrix));
    expectArrayClose(readAccessor(gltf, out.attributes.NORMAL), [...expectedNormal.toArray(), ...expectedNormal.toArray(), ...expectedNormal.toArray()]);
    expect(mesh.indices).toEqual(new Uint32Array([0, 1, 2]));
});

test('handles zero scale and absent normals with finite attributes', () => {
    const mesh = { ...triangle(), normals: undefined } as unknown as MeshData;
    const model = createMat4(); model[10] = 0;
    const gltf = JSON.parse(exportGeometry([{ name: 'flat', mesh, model }]));
    expect(gltf.nodes[0].matrix).toBeUndefined();
    expect(readAccessor(gltf, primitive(gltf, 'flat').attributes.NORMAL)).toEqual([0, 0, 1, 0, 0, 1, 0, 0, 1]);
    model[0] = 0;
    const collapsed = JSON.parse(exportGeometry([{ name: 'collapsed', mesh, model }]));
    expect(collapsed.nodes[0].mesh).toBeUndefined();
    expect(collapsed.buffers).toBeUndefined();
});

test('encodes multiple base64 blocks without corrupting boundaries or typed-array offsets', () => {
    const count = 3000, storage = new Float32Array(count * 3 + 7), positions = storage.subarray(3, count * 3 + 3);
    for (let at = 0; at < positions.length; ++at) positions[at] = at * .125;
    const mesh: MeshData = { name: 'large', positions, indices: Uint32Array.from({ length: count }, (_, at) => at),
        normals: new Float32Array(positions.length) };
    const gltf = JSON.parse(exportGeometry([{ name: 'large', mesh }]));
    const bytes = Buffer.from(gltf.buffers[0].uri.split(',')[1], 'base64');
    expect(bytes.length).toBe(gltf.buffers[0].byteLength);
    expect(readAccessor(gltf, primitive(gltf, 'large').attributes.POSITION)).toEqual(Array.from(positions));
    expect(readAccessor(gltf, primitive(gltf, 'large').indices)).toEqual(Array.from(mesh.indices));
});

test('the installed glTF loader reads hierarchy, reflected transforms, flat surfaces, and extras', async () => {
    const reflected = createMat4(); reflected[0] = -2; reflected[12] = 5;
    const input = exportGeometry([{ name: 'Plants (above ground)', children: [
        { name: 'leaf', mesh: triangle(), model: reflected, doubleSided: true, extras: { waterRatio: .8 } },
        { name: 'flat', mesh: triangle(), flatShaded: true },
    ] }], { timestep: 42 });
    // Jest excludes node_modules from its ESM transform. Compile the installed loader and
    // its utility dependency in memory, using the same Three runtime as these assertions.
    const loadExample = (path: string): any => {
        const compiled = require('@babel/core').transformSync(readFileSync(path, 'utf8'), {
            babelrc: false, configFile: false, plugins: ['@babel/plugin-transform-modules-commonjs'],
        });
        const module = { exports: {} };
        const localRequire = (name: string) => name === 'three' ? THREE : loadExample(resolve(dirname(path), name));
        new Function('require', 'module', 'exports', compiled.code)(localRequire, module, module.exports);
        return module.exports;
    };
    const { GLTFLoader } = loadExample(require.resolve('three/examples/jsm/loaders/GLTFLoader.js'));
    // jsdom's global fetch is stubbed for the application. Supply the actual embedded buffer
    // to FileLoader without replacing any of the GLTF parser or geometry construction.
    const transport = jest.spyOn(THREE.FileLoader.prototype, 'load').mockImplementation((url, onLoad) => {
        const bytes = Buffer.from(String(url).split(',')[1], 'base64');
        Promise.resolve().then(() => onLoad!(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)));
        return undefined;
    });
    try {
        const result = await new GLTFLoader().parseAsync(input, '');
        result.scene.updateMatrixWorld(true);
        const leaf = result.scene.getObjectByName('leaf'), flat = result.scene.getObjectByName('flat');
        const point = new THREE.Vector3().fromBufferAttribute(leaf.geometry.attributes.position, 0).applyMatrix4(leaf.matrixWorld);
        expect(point.toArray()).toEqual([7, -1, 0]);
        expect(leaf.matrixWorld.determinant()).toBe(-2);
        expect(leaf.parent.name).toBe('Plants_(above_ground)');
        expect(leaf.userData.waterRatio).toBe(.8);
        expect(result.scene.userData.timestep).toBe(42);
        expect(flat.material.flatShading).toBe(true);
    } finally { transport.mockRestore(); }
});
