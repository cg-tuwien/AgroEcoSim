import BinaryReader from '../src/helpers/BinaryReader';
import { Organs, Primitives } from '../src/helpers/Primitives';
import { buildPlants } from '../src/render/core/BuildScene';
import { InstanceList, IFlags } from '../src/render/core/InstanceList';
import { VisualMappingOptions, leafColor } from '../src/render/core/PlantColors';
import { neutral } from '../src/render/core/Theme';
import { Geom } from '../src/render/core/GeometryIds';
import { getGeometry } from '../src/render/core/GeometryStore';
import { sceneStats } from '../src/render/core/SceneStats';
import { exportGeometry } from '../src/render/core/GeometryExport';
import { toBufferGeometry } from '../src/render/three/ThreeGeometry';
import { captureFrameSource } from '../src/appstate';
import { exportCurrentFrame } from '../src/render/export/CurrentFrameExport';
import '../src/render/core/StaticGeometry';

jest.mock('../src/appstate', () => {
    const { signal } = require('@preact/signals');
    return { __esModule: true, captureFrameSource: jest.fn(), default: {
        playPointer: signal(0), species: signal([]), obstacles: signal([]), terrainList: [],
        fieldSizeX: signal(1), fieldSizeD: signal(1), fieldSizeZ: signal(1), needsRender: signal(false),
    } };
});

class Writer {
    data: number[] = [];
    u8(n: number) { this.data.push(n & 255); return this; }
    u32(n: number) { const b = new Uint8Array(4); new DataView(b.buffer).setUint32(0, n, true); this.data.push(...b); return this; }
    f32(n: number) { const b = new Uint8Array(4); new DataView(b.buffer).setFloat32(0, n, true); this.data.push(...b); return this; }
    str(s: string) { const b = new TextEncoder().encode(s); this.u8(b.length); this.data.push(...b); return this; }
    floats(a: number[]) { a.forEach(n => this.f32(n)); return this; }
    mesh(p: number[], ix: number[]) {
        this.u32(p.length / 3).floats(p).u32(ix.length); ix.forEach(n => this.u32(n));
        for (let i = 0; i < p.length / 3; ++i) this.f32(0);
        return this;
    }
    bytes() { return new Uint8Array(this.data); }
}

const triangle = [-1, 0, 0, 1, 0, 0, 0, 1, 0];
// A closed octahedron fixture makes the three independent body semiaxes measurable.
const body = [1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1];
const bodyIndices = [0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5];
const bodyTransform = [.02, 0, 0, 1.02, 0, 0, .01, 2, 0, -.01, 0, 3];
const wingTransform = [.03, 0, 0, 1.07, 0, 0, .001, 2, 0, -.005, 0, 3];

function frame(version: 1 | 2, analytics = false, wings = true, bad = false): Uint8Array {
    const id = (version === 1 ? 'a' : bad ? 'b' : analytics ? 'd' : wings ? 'e' : 'f').repeat(64);
    const asset = new Writer().u8(0).u8(version).u32(1)
        .mesh(triangle, [0, 1, 2]).mesh(triangle, [0, 1, 2]);
    if (version === 2) {
        asset.mesh(body, bodyIndices).u8(wings ? 1 : 0);
        if (wings) asset.mesh(triangle, [0, 1, 2]);
    }
    const w = new Writer().u8(8).u8(analytics ? 2 : 0).u32(1).str(id).u32(asset.data.length);
    w.data.push(...asset.data);
    w.u32(1).str('fruit fixture').str(id).u32(version === 1 ? 1 : wings || bad ? 3 : 1);
    const organ = (kind: Organs, matrix: number[]) => {
        w.u32(0xffffffff).u8(kind).u8(0).floats(matrix);
        if (analytics) w.floats([.25, .5, 0, .2, .1]);
        w.u8(128).u8(64).u8(32);
        if (analytics) w.floats([0, 0]);
    };
    if (version === 1) organ(Organs.Leaf, wingTransform);
    else {
        organ(Organs.Fruit, bodyTransform);
        if (wings || bad) { organ(Organs.FruitWing, wingTransform); organ(Organs.FruitWing, wingTransform); }
    }
    return w.bytes();
}

function instances(bytes: Uint8Array) {
    const scene = new BinaryReader(bytes).readAgroScene();
    const list = new InstanceList();
    buildPlants(list, { scene, visualMapping: VisualMappingOptions.Natural, debugBoxes: false,
        showLeaves: false, showRoots: false, leafGeom: () => Geom.Plane, petalGeom: () => Geom.Plane });
    return { scene, list };
}

test.each([false, true])('V8 fruit assets decode with analytics=%s and preserve exact body/wing matrices', analytics => {
    const { scene, list } = instances(frame(2, analytics));
    expect(scene[0].primitives.map(p => p.type)).toEqual([Primitives.Ellipsoid, Primitives.Rectangle, Primitives.Rectangle]);
    expect(list.count).toBe(3);
    const fruit = list.items[0];
    expect(fruit.model[12]).toBeCloseTo(1.02, 6);
    expect(Math.hypot(fruit.model[0], fruit.model[1], fruit.model[2]) * 2).toBeCloseTo(.04, 7);
    expect(Math.hypot(fruit.model[4], fruit.model[5], fruit.model[6]) * 2).toBeCloseTo(.02, 7);
    expect(Math.hypot(fruit.model[8], fruit.model[9], fruit.model[10]) * 2).toBeCloseTo(.02, 7);
    expect(fruit.flags & IFlags.DoubleSided).toBe(0);
    for (let i = 0; i < list.count; ++i) {
        expect(list.items[i].flags & (IFlags.Foliage | IFlags.Hidden)).toBe(0);
        expect(list.items[i].ref).toEqual({ type: 'plant', index: { entity: 0, primitive: i } });
    }
    expect(list.items[1].flags & IFlags.DoubleSided).not.toBe(0);
    if (analytics) for (const p of scene[0].primitives) {
        expect(p.stats![7]).toBeCloseTo(1);
        expect(p.stats![8]).toBeCloseTo(1);
    }
    expect(sceneStats(scene)).toEqual({ objects: 3, triangles: 10 });
});

test('fruits and wings do not report a false zero-irradiance measurement', () => {
    const { scene } = instances(frame(2, true));
    for (const p of scene[0].primitives)
        expect(leafColor(p, 0, VisualMappingOptions.Irradiance).color).toEqual(neutral);
});

test('version one remains readable and a version two asset can omit wings', () => {
    expect(instances(frame(1)).scene[0].geometry?.fruit).toBeUndefined();
    expect(instances(frame(2, false, false)).list.count).toBe(1);
});

test('a fruit wing without its recorded geometry fails instead of drawing the species leaf', () => {
    expect(() => instances(frame(2, false, false, true))).toThrow('missing its immutable mesh');
});

test('both renderer geometry and GLTF retain the same fruit body and paired wing meshes', () => {
    const { list } = instances(frame(2));
    const nodes = list.items.slice(0, list.count).map((inst, index) => {
        const mesh = getGeometry(inst.geom)!;
        const three = toBufferGeometry(mesh);
        expect(Array.from(three.getAttribute('position').array)).toEqual(Array.from(mesh.positions));
        expect(Array.from(three.index!.array)).toEqual(Array.from(mesh.indices));
        three.dispose();
        return { name: `${index}`, mesh, model: inst.model, color: inst.color,
            doubleSided: !!(inst.flags & IFlags.DoubleSided) };
    });
    const doc = JSON.parse(exportGeometry(nodes));
    expect(doc.nodes).toHaveLength(3);
    expect(doc.meshes).toHaveLength(2); // both wings share the immutable mesh
    expect(doc.nodes[0].matrix).toEqual(Array.from(list.items[0].model));
    const material = doc.materials[doc.meshes[doc.nodes[0].mesh].primitives[0].material];
    expect(material.doubleSided).toBe(false);
    expect(material.pbrMetallicRoughness.baseColorFactor.slice(0, 3)).toEqual(Object.values(list.items[0].color));
});

test('current-frame GLTF includes fruit analytics without claiming irradiance or wood', async () => {
    const { scene } = instances(frame(2, true));
    (captureFrameSource as jest.Mock).mockReturnValue({ step: 3, hoursPerTick: 4, runId: 'fruit',
        read: async () => scene, verify: jest.fn() });
    const doc = JSON.parse((await exportCurrentFrame({ roots: false, soilAndTrays: false, obstacles: false, metadata: true })).json);
    const organs = doc.nodes.filter((n: any) => n.extras?.greenFdt.primitiveIndex !== undefined)
        .map((n: any) => n.extras.greenFdt);
    expect(organs.map((o: any) => o.organ)).toEqual(['Fruit', 'FruitWing', 'FruitWing']);
    for (const organ of organs) {
        expect(organ.statistics.waterFillRatio).toBe(.25);
        expect(organ.statistics.relativeResources).toBe(1);
        expect(organ.statistics.irradianceWhPerM2).toBeUndefined();
        expect(organ.statistics.woodRatio).toBeUndefined();
    }
});
