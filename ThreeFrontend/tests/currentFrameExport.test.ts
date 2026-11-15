import appstate, { captureFrameSource } from '../src/appstate';
import { exportCurrentFrame, exportRootsAvailable } from '../src/render/export/CurrentFrameExport';
import { registerFrameCapabilities } from '../src/helpers/Scene';
import type { PlantModel } from '../src/helpers/Scene';
import { Organs, Primitives } from '../src/helpers/Primitives';
import { BoxTerrainItem, MeshTerrainItem } from '../src/helpers/Terrain';
import { Obstacle } from '../src/helpers/Obstacle';
import { registerGeometry, getGeometry } from '../src/render/core/GeometryStore';
import { createMat4, multiply } from '../src/render/core/Mat4';
import { buildPlants } from '../src/render/core/BuildScene';
import { InstanceList } from '../src/render/core/InstanceList';
import { Geom } from '../src/render/core/GeometryIds';
import { VisualMappingOptions } from '../src/render/core/PlantColors';
import { deformLeaf } from '../src/render/core/ContactDeformation';

jest.mock('../src/appstate', () => {
    const { signal } = require('@preact/signals');
    return { __esModule: true, captureFrameSource: jest.fn(), default: {
        playPointer: signal(1), species: signal([]), obstacles: signal([]), terrainList: [],
        fieldSizeX: signal(10), fieldSizeD: signal(4), fieldSizeZ: signal(10), needsRender: signal(false),
    } };
});

const chosen = { roots: true, soilAndTrays: true, obstacles: true, metadata: false };
const matrix = () => new Float32Array([1, 0, 0, 5, 0, 1, 0, 6, 0, 0, 1, 7]);
const stats = () => new Float32Array([.25, .5, 3, 0, 40, 50, 60, .75, 1]);
const triangle = (name: string, offset = 0) => ({ name,
    positions: new Float32Array([offset, 0, 0, offset + 1, 0, 0, offset, 1, 0]),
    normals: new Float32Array([0, 0, 1, 0, 0, 1, 0, 0, 1]),
    uvs: new Float32Array([0, 0, 1, 0, 0, 1]), indices: new Uint32Array([0, 1, 2]),
});
let scene: PlantModel[], source: any;

function namedImport(names: string[]) {
    const vertices = names.flatMap((_, i) => [i * 10, 0, 0, i * 10 + 1, 0, 0, i * 10, 1, 0]);
    const obstacle = new Obstacle('mesh', 2, 3, 4, 0, 0, 0, 0, 0, new Float32Array(vertices), vertices.map((_, i) => i).slice(0, names.length * 3));
    obstacle.groups = names.map((name, i) => ({ name, vertexFirst: i * 3, vertexCount: 3, indexFirst: i * 3, indexCount: 3 }));
    obstacle.meshData!.colors!.fill(1); //viewport measurement/selection must not reach natural export
    return obstacle;
}

function accessor(doc: any, index: number): number[] {
    const a = doc.accessors[index], v = doc.bufferViews[a.bufferView];
    const bytes = Buffer.from(doc.buffers[0].uri.split(',')[1], 'base64');
    const offset = bytes.byteOffset + (v.byteOffset || 0) + (a.byteOffset || 0);
    const count = a.count * ({ VEC3: 3, VEC2: 2, SCALAR: 1 } as any)[a.type];
    return Array.from(a.componentType === 5125 ? new Uint32Array(bytes.buffer, offset, count) : new Float32Array(bytes.buffer, offset, count));
}

function worldTriangles(doc: any, root: number): number[][] {
    const points: number[][] = [];
    const visit = (index: number, parent: Float32Array) => {
        const node = doc.nodes[index], world = node.matrix ? multiply(parent, new Float32Array(node.matrix), createMat4()) : parent;
        if (node.mesh !== undefined) for (const primitive of doc.meshes[node.mesh].primitives) {
            const positions = accessor(doc, primitive.attributes.POSITION), indices = accessor(doc, primitive.indices);
            for (const vertex of indices) {
                const [x, y, z] = positions.slice(vertex * 3, vertex * 3 + 3);
                points.push([world[0]*x + world[4]*y + world[8]*z + world[12],
                    world[1]*x + world[5]*y + world[9]*z + world[13], world[2]*x + world[6]*y + world[10]*z + world[14]]);
            }
        }
        for (const child of node.children ?? []) visit(child, world);
    };
    visit(root, createMat4()); return points;
}

beforeEach(() => {
    const mesh = triangle('recorded sliced leaf');
    const id = registerGeometry('current-frame-fixture', mesh);
    scene = [{ species: 'fixture species', geometry: { id: 'recorded', profile: 1, leaves: [id], petal: id }, primitives: [
        { type: Primitives.Rectangle, organ: Organs.Leaf, affineTransform: matrix(), color: '#22aa33', laminaSlice: 0, stats: stats() },
        { type: Primitives.Cylinder, organ: Organs.Petiole, affineTransform: matrix(), color: '#336622', length: 2, radius: .01, stats: stats() },
        { type: Primitives.Box, organ: Organs.Root, affineTransform: matrix(), length: .5, radius: .01, stats: stats() },
    ] }];
    registerFrameCapabilities(scene, { version: 8, rootsDownloaded: true, analyticsAvailable: true, hasRootGeometry: true, virtualRoots: false, organColors: false });
    source = { step: 17, hoursPerTick: 4, runId: 'recorded-run', read: jest.fn(async () => scene), verify: jest.fn() };
    (captureFrameSource as jest.Mock).mockReturnValue(source);
    appstate.terrainList = [new BoxTerrainItem('P001_Y026_T001_S001', new Float32Array([1, 2, 3, 4, 5, 6, 0, 0, 0, 1]), 0)];
    appstate.obstacles.value = [namedImport(['P001_Y026_T001', 'P001_Y026_T001_D001', 'P001_Y026_T001_CAP_END', 'P001_Y026_T0010', 'BACK_WALL'])];
    appstate.species.value = [];
    appstate.playPointer.value = 1;
});

test('separates all five categories, preserves names and matching plant identities', async () => {
    const { json, step } = await exportCurrentFrame(chosen), doc = JSON.parse(json);
    expect(step).toBe(17);
    expect(doc.scenes[0].nodes.map((i: number) => doc.nodes[i].name))
        .toEqual(['Plants (above ground)', 'Roots', 'Soil', 'Trays', 'Other obstacles']);
    const groups = Object.fromEntries(doc.scenes[0].nodes.map((i: number) => [doc.nodes[i].name, doc.nodes[i]]));
    expect(doc.nodes[groups.Roots.children[0]].name).toBe(doc.nodes[groups['Plants (above ground)'].children[0]].name);
    expect(groups.Trays.children.map((i: number) => doc.nodes[i].name)).toEqual(['P001_Y026_T001', 'P001_Y026_T001_D001', 'P001_Y026_T001_CAP_END']);
    expect(groups['Other obstacles'].children.map((i: number) => doc.nodes[i].name)).toEqual(['P001_Y026_T0010', 'BACK_WALL']);
    for (const i of [...groups.Trays.children, ...groups['Other obstacles'].children]) {
        const primitive = doc.meshes[doc.nodes[i].mesh].primitives[0];
        expect(doc.accessors[primitive.attributes.POSITION].count).toBe(3);
        expect(accessor(doc, primitive.indices)).toEqual([0, 1, 2]);
    }
    expect(Array.from(appstate.obstacles.value[0].meshData!.colors!)).toEqual(new Array(45).fill(1));
    expect(json).not.toContain('extras');
    expect(json).not.toContain('COLOR_0');
    expect(appstate.playPointer.peek()).toBe(1);
});

test('keeps multiple plants distinct while sharing repeated rigid organ and soil geometry', async () => {
    scene.push({ ...scene[0], primitives: [...scene[0].primitives] });
    appstate.terrainList.push(new BoxTerrainItem('second soil', new Float32Array([10, 2, 3, 4, 5, 6, 0, 0, 0, 1]), 1));
    const doc = JSON.parse((await exportCurrentFrame(chosen)).json);
    const plants = doc.nodes.find((n: any) => n.name === 'Plants (above ground)');
    expect(plants.children.map((i: number) => doc.nodes[i].name)).toEqual(['plant:0 fixture species', 'plant:1 fixture species']);
    const leaves = doc.nodes.filter((n: any) => n.name === 'Leaf:0');
    expect(doc.nodes[leaves[0].children[0]].mesh).toBe(doc.nodes[leaves[1].children[0]].mesh);
    const soil = doc.nodes.find((n: any) => n.name === 'Soil');
    expect(doc.nodes[soil.children[0]].mesh).toBe(doc.nodes[soil.children[1]].mesh);
});

test('scheduled plants retain request identities separately from compact scene indices in GLTF', async () => {
    scene[0].sourcePlantIndex = 3;
    registerFrameCapabilities(scene, { version: 9, rootsDownloaded: true, analyticsAvailable: true, hasRootGeometry: true, virtualRoots: false, organColors: false });
    const first = JSON.parse((await exportCurrentFrame({ ...chosen, metadata: true })).json);
    const identity = (doc: any) => doc.nodes.filter((n: any) => n.name === 'plant:3 fixture species');
    expect(identity(first)).toHaveLength(2); // above ground and roots
    expect(first.scenes[0].extras.greenFdt.definitions.identity).toContain('sourcePlantIndex, when recorded, is the stable zero-based plant order in the run request across frames');
    expect(first.scenes[0].extras.greenFdt.definitions.identity).toContain('older recordings omit it');
    for (const node of identity(first)) expect(node.extras.greenFdt).toMatchObject({ plantIndex: 0, sourcePlantIndex: 3 });
    scene.unshift({ ...scene[0], sourcePlantIndex: 0 });
    const next = JSON.parse((await exportCurrentFrame({ ...chosen, metadata: true })).json);
    for (const node of identity(next)) expect(node.extras.greenFdt).toMatchObject({ plantIndex: 1, sourcePlantIndex: 3 });
});

test.each(Array.from({ length: 8 }, (_, bits) => bits))('independent inclusion combination %i keeps only selected geometry', async bits => {
    const { json } = await exportCurrentFrame({ ...chosen, roots: !!(bits & 1), soilAndTrays: !!(bits & 2), obstacles: !!(bits & 4) });
    const doc = JSON.parse(json), names = doc.scenes[0].nodes.map((i: number) => doc.nodes[i].name);
    expect(names.includes('Roots')).toBe(!!(bits & 1));
    expect(names.includes('Soil')).toBe(!!(bits & 2)); expect(names.includes('Trays')).toBe(!!(bits & 2));
    expect(names.includes('Other obstacles')).toBe(!!(bits & 4));
    expect(names).toContain('Plants (above ground)');
    for (const a of doc.accessors.filter((a: any) => a.type === 'VEC3')) expect(a.count).toBeLessThanOrEqual(52);
    if (!(bits & 4)) expect(doc.nodes.map((n: any) => n.name)).not.toContain('BACK_WALL');
});

test('exports truthful organ analytics and scene definitions only when requested', async () => {
    const doc = JSON.parse((await exportCurrentFrame({ ...chosen, metadata: true })).json);
    expect(doc.scenes[0].extras.greenFdt).toMatchObject({ schemaVersion: 1, timestep: 17, hoursPerTick: 4, elapsedHours: 68, sourceFormatVersion: 8 });
    const organs = doc.nodes.filter((n: any) => n.extras?.greenFdt.primitiveIndex !== undefined);
    const leaf = organs.find((n: any) => n.extras.greenFdt.organ === 'Leaf').extras.greenFdt.statistics;
    const stem = organs.find((n: any) => n.extras.greenFdt.organ === 'Petiole').extras.greenFdt.statistics;
    const root = organs.find((n: any) => n.extras.greenFdt.organ === 'Root').extras.greenFdt.statistics;
    expect(leaf).toMatchObject({ waterFillRatio: .25, energyFillRatio: .5, irradianceWhPerM2: 40, auxins: 3 });
    expect(leaf.woodRatio).toBeUndefined(); expect(stem.woodRatio).toBe(40); expect(stem.irradianceWhPerM2).toBeUndefined();
    expect(root.auxins).toBeUndefined(); expect(root.cytokinins).toBeUndefined();
    expect(doc.scenes[0].extras.greenFdt.definitions.previousDayProduction.roots).toContain('g/m²');
    expect(JSON.stringify(doc.scenes[0].extras)).not.toContain('Joules');
});

test('omits fabricated or nonfinite statistics, keeping legitimate measured zeros', async () => {
    scene[0].primitives[0].stats![2] = NaN;
    scene[0].primitives[0].stats![5] = 0;
    const doc = JSON.parse((await exportCurrentFrame({ ...chosen, metadata: true })).json);
    const leaf = doc.nodes.find((n: any) => n.extras?.greenFdt.organ === 'Leaf').extras.greenFdt.statistics;
    expect(leaf.auxins).toBeUndefined(); expect(leaf.previousDayResources).toBe(0);
    registerFrameCapabilities(scene, { version: 7, rootsDownloaded: true, analyticsAvailable: false, hasRootGeometry: true, virtualRoots: false, organColors: false });
    const noAnalytics = JSON.parse((await exportCurrentFrame({ ...chosen, metadata: true })).json);
    expect(noAnalytics.nodes.some((n: any) => n.extras?.greenFdt.statistics)).toBe(false);
});

test('does not infer roots availability from mutable request settings or virtual roots', async () => {
    expect(exportRootsAvailable(scene)).toBe(true);
    registerFrameCapabilities(scene, { version: 7, rootsDownloaded: true, analyticsAvailable: false, hasRootGeometry: false, virtualRoots: true, organColors: false });
    expect(exportRootsAvailable(scene)).toBe(false);
    await expect(exportCurrentFrame(chosen)).rejects.toThrow('no downloaded root geometry');
});

test('handles legacy panel/tray names without swallowing panel walls', async () => {
    appstate.terrainList[0].id = 'P1_Y00_S01';
    appstate.obstacles.value = [namedImport(['P1_Y00_T01', 'P1_Y00_T01_D01', 'P1_Y00_T02_CAP_START', 'P1_Y00_WALL'])];
    const doc = JSON.parse((await exportCurrentFrame(chosen)).json);
    const trays = doc.nodes.find((n: any) => n.name === 'Trays');
    expect(trays.children.map((i: number) => doc.nodes[i].name)).toEqual(['P1_Y00_T01', 'P1_Y00_T01_D01', 'P1_Y00_T02_CAP_START']);
    const other = doc.nodes.find((n: any) => n.name === 'Other obstacles');
    expect(other.children.map((i: number) => doc.nodes[i].name)).toEqual(['P1_Y00_WALL']);
});

test('keeps inseparable legacy mesh under Other obstacles and explains the limitation', async () => {
    appstate.obstacles.value[0].groups = undefined;
    const included = await exportCurrentFrame(chosen);
    expect(included.warnings[0]).toContain('cannot be separated');
    const doc = JSON.parse(included.json);
    expect(doc.nodes.some((n: any) => n.name === 'Trays')).toBe(false);
    expect(doc.nodes.some((n: any) => n.name === 'Unclassified imported mesh')).toBe(true);
    const excluded = JSON.parse((await exportCurrentFrame({ ...chosen, obstacles: false })).json);
    expect(excluded.nodes.some((n: any) => n.name === 'Unclassified imported mesh')).toBe(false);
});

test('snapshots static transforms and export options before fetching a cold frame', async () => {
    let deliver!: (value: PlantModel[]) => void;
    source.read.mockImplementation(() => new Promise<PlantModel[]>(resolve => deliver = resolve));
    const options = { ...chosen }, job = exportCurrentFrame(options);
    options.soilAndTrays = false;
    appstate.obstacles.value[0].px.value = 500;
    appstate.terrainList = [];
    deliver(scene);
    const doc = JSON.parse((await job).json), tray = doc.nodes.find((n: any) => n.name === 'P001_Y026_T001');
    expect(tray.matrix.slice(12, 15)).toEqual([2, 3, 4]);
    expect(doc.nodes.some((n: any) => n.name === 'Soil')).toBe(true);
    expect(captureFrameSource).toHaveBeenLastCalledWith(1);
});

test('matches translated soil triangles and actual folded and bent organ geometry', async () => {
    const soil = triangle('translated soil');
    appstate.terrainList = [new MeshTerrainItem('field', 10, 20, 30, soil.positions, Array.from(soil.indices), 0)];
    scene[0].primitives[0].deformation = new Float32Array([.1, -.2, .3, -.1]);
    scene[0].primitives[1].deformation = new Float32Array([.1, .2, -.1, .1]);
    const doc = JSON.parse((await exportCurrentFrame(chosen)).json);
    const soilIndex = doc.nodes.findIndex((n: any) => n.name === 'Soil');
    expect(worldTriangles(doc, soilIndex)).toEqual([[10,20,30], [11,20,30], [10,21,30]]);
    const list = new InstanceList();
    buildPlants(list, { scene, visualMapping: VisualMappingOptions.Natural, debugBoxes: false, showLeaves: true, showRoots: false,
        leafGeom: () => Geom.Plane, petalGeom: () => Geom.Plane });
    const expected: number[][] = [];
    for (let i = 0; i < list.count; ++i) {
        const inst = list.items[i];
        if (inst.ref?.type !== 'plant' || inst.ref.index.primitive === 2) continue;
        const mesh = getGeometry(inst.geom)!, m = inst.model;
        for (const index of mesh.indices) {
            const point = { x: mesh.positions[index*3], y: mesh.positions[index*3+1], z: mesh.positions[index*3+2] };
            if (inst.fold) deformLeaf(point, point.x, point.y, point.z, mesh.uvs?.[index*2] ?? 0, inst.fold.angles, inst.fold.scale);
            expected.push([m[0]*point.x + m[4]*point.y + m[8]*point.z + m[12],
                m[1]*point.x + m[5]*point.y + m[9]*point.z + m[13], m[2]*point.x + m[6]*point.y + m[10]*point.z + m[14]]);
        }
    }
    const actual = worldTriangles(doc, doc.nodes.findIndex((n: any) => n.name === 'Plants (above ground)'));
    expect(actual.length).toBe(expected.length);
    actual.forEach((point, i) => point.forEach((value, axis) => expect(value).toBeCloseTo(expected[i][axis], 5)));
});
