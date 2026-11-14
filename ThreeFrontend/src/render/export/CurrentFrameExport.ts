//A geometry download reads one recorded frame, independently of either viewport backend.
import appstate, { captureFrameSource } from '../../appstate';
import { frameCapabilities, type PlantModel } from '../../helpers/Scene';
import { Organs, Primitives, type Primitive } from '../../helpers/Primitives';
import { MeshTerrainItem, type ITerrainItem } from '../../helpers/Terrain';
import type { Obstacle, ObstacleGroup } from '../../helpers/Obstacle';
import { buildPlants } from '../core/BuildScene';
import { fromHex } from '../core/Color';
import { exportGeometry, type ExportNode } from '../core/GeometryExport';
import type { MeshData } from '../core/Geometry';
import { Geom } from '../core/GeometryIds';
import { getGeometry, registerGeometry } from '../core/GeometryStore';
import { InstanceList, IFlags } from '../core/InstanceList';
import { createMat4, scaleInPlace, setPosition } from '../core/Mat4';
import { VisualMappingOptions } from '../core/PlantColors';
import '../core/StaticGeometry';

export interface FrameExportOptions {
    roots: boolean;
    soilAndTrays: boolean;
    obstacles: boolean;
    metadata: boolean;
}

export function exportRootsAvailable(scene: PlantModel[]): boolean {
    const flags = frameCapabilities(scene);
    return !!(flags?.rootsDownloaded && flags.hasRootGeometry);
}

const soilColor = fromHex('#573600');
const obstacleColor = fromHex('#999');
const naturalMeshes = new WeakMap<MeshData, MeshData>();
type ExportGroup = ExportNode & { children: ExportNode[] };

/** The recorded quantities are deliberately named separately from absolute water and energy. */
const definitions = {
    identity: 'plantIndex and primitiveIndex identify objects within this frame only. sourcePlantIndex, when recorded, is the stable zero-based plant order in the run request across frames; older recordings omit it.',
    coordinates: 'Metres; right-handed, Y up, east +X and north -Z.',
    waterFillRatio: 'Above-ground water / efficient water capacity; root water / storage capacity. Clamped to [0, 1]; dimensionless.',
    energyFillRatio: 'Energy / energy capacity, clamped to [0, 1]; dimensionless.',
    irradianceWhPerM2: 'Lamina irradiance accumulated over the current tick, Wh/m²; not radiance.',
    woodRatio: 'Stem or root lignification fraction; dimensionless.',
    auxins: 'Above-ground auxin signal in custom model units.',
    previousDayResources: {
        aboveGround: 'Previous-day light exposure (Wh/m²), or its inherited/aggregated axis resource value.',
        roots: 'Sampled soil-water availability accumulated in grams.',
    },
    previousDayProduction: {
        aboveGround: 'Previous-day model production per m²; energy accounting depends on the recorded species model. No physical conversion is inferred.',
        roots: 'Previous-day absorbed water, g/m².',
    },
    relativeResources: 'Previous-day resource value divided by the maximum within this plant and its above-ground/root compartment.',
    relativeProduction: 'Previous-day production divided by the maximum within this plant and its above-ground/root compartment.',
};

function organExtras(primitive: Primitive, index: number, analytics: boolean): Record<string, unknown> {
    const organ = 'organ' in primitive ? primitive.organ : Organs.Unspecified;
    const data: Record<string, unknown> = { primitiveIndex: index, organ: Organs[organ], organCode: organ };
    const recorded = primitive.stats;
    if (analytics && recorded) {
        const stats: Record<string, number> = {};
        const add = (name: string, slot: number) => {
            const value = recorded[slot];
            if (Number.isFinite(value)) stats[name] = value;
        };
        add('waterFillRatio', 0); add('energyFillRatio', 1);
        if (organ !== Organs.Root) add('auxins', 2);
        // Fruits and their dry wings are shadow geometry, not light sensors or wood.
        if (organ !== Organs.Fruit && organ !== Organs.FruitWing)
            add(primitive.type === Primitives.Rectangle ? 'irradianceWhPerM2' : 'woodRatio', 4);
        add('previousDayResources', 5); add('previousDayProduction', 6);
        add('relativeResources', 7); add('relativeProduction', 8);
        data.statistics = stats;
    }
    return { greenFdt: data };
}

/** Drop viewport data-mapping colours without touching the source arrays. */
function naturalMesh(mesh: MeshData): MeshData {
    let natural = naturalMeshes.get(mesh);
    if (!natural) {
        natural = { name: mesh.name, positions: mesh.positions, normals: mesh.normals, uvs: mesh.uvs, indices: mesh.indices };
        naturalMeshes.set(mesh, natural);
    }
    return natural;
}

/** Copy only one object's vertices, rebasing its indices out of the merged import. */
function compactGroup(mesh: MeshData, group: ObstacleGroup): MeshData {
    const { vertexFirst, vertexCount, indexFirst, indexCount } = group;
    if (![vertexFirst, vertexCount, indexFirst, indexCount].every(x => Number.isInteger(x) && x >= 0)
        || vertexFirst + vertexCount > mesh.positions.length / 3
        || indexFirst + indexCount > mesh.indices.length || indexCount % 3 !== 0)
        throw new Error(`Invalid geometry ranges for imported object ${group.name}.`);
    const indices = new Uint32Array(indexCount);
    for (let i = 0; i < indexCount; ++i) {
        const index = mesh.indices[indexFirst + i] - vertexFirst;
        if (index < 0 || index >= vertexCount)
            throw new Error(`Invalid vertex reference in imported object ${group.name}.`);
        indices[i] = index;
    }
    return {
        name: group.name,
        positions: mesh.positions.slice(vertexFirst * 3, (vertexFirst + vertexCount) * 3),
        normals: mesh.normals.slice(vertexFirst * 3, (vertexFirst + vertexCount) * 3),
        uvs: mesh.uvs?.slice(vertexFirst * 2, (vertexFirst + vertexCount) * 2),
        indices,
    };
}

function trayMatcher(terrain: readonly ITerrainItem[]): (name: string) => boolean {
    const bases = new Set<string>(), oldPanels = new Set<string>();
    for (const soil of terrain) {
        const base = soil.id?.replace(/_S\d[^_]*$/, '');
        if (!base || base === soil.id) continue;
        //The old P1_Y00_S01 scheme names the panel, not an individual tray.
        if (/^P\d+_Y\d+$/.test(base)) oldPanels.add(base);
        else bases.add(base);
    }
    return name => {
        //Walk underscore boundaries, never letting T001 match T0010. This also includes caps
        //and dividers without comparing every object against every tray.
        let prefix = name;
        while (prefix) {
            if (bases.has(prefix)) return true;
            const cut = prefix.lastIndexOf('_');
            if (cut < 0) break;
            if (/^T\d+$/.test(prefix.slice(cut + 1)) && oldPanels.has(prefix.slice(0, cut))) return true;
            prefix = prefix.slice(0, cut);
        }
        return false;
    };
}

/** Static matrices and inclusion are fixed before waiting for a cold recorded frame. */
function staticSnapshot(options: FrameExportOptions): { nodes: ExportNode[]; warnings: string[] } {
    const soils: ExportNode[] = [], trays: ExportNode[] = [], others: ExportNode[] = [], warnings: string[] = [];
    const terrain = appstate.terrainList;
    const isTray = trayMatcher(terrain);
    if (options.soilAndTrays) {
        for (const soil of terrain) {
            const mesh = soil instanceof MeshTerrainItem ? soil.meshData : getGeometry(soil.geomId());
            if (!mesh) throw new Error(`Soil geometry is unavailable for ${soil.id}.`);
            soils.push({ name: soil.id, mesh: naturalMesh(mesh), model: soil.writeModel(createMat4()),
                color: soilColor, doubleSided: true, flatShaded: true });
        }
        if (terrain.length === 0) {
            const model = createMat4();
            scaleInPlace(model, appstate.fieldSizeX.peek(), appstate.fieldSizeD.peek(), appstate.fieldSizeZ.peek());
            setPosition(model, 0, -appstate.fieldSizeD.peek(), 0);
            soils.push({ name: 'field', mesh: getGeometry(Geom.BoxCorner), model,
                color: soilColor, doubleSided: true, flatShaded: true });
        }
    }
    if (options.soilAndTrays || options.obstacles) for (const obstacle of appstate.obstacles.peek() as Obstacle[]) {
        const model = obstacle.writeModel(createMat4());
        if (obstacle.meshData) {
            if (obstacle.groups?.length) {
                for (const group of obstacle.groups) {
                    if (group.indexCount === 0) continue;
                    const tray = isTray(group.name);
                    if (!(tray ? options.soilAndTrays : options.obstacles)) continue;
                    (tray ? trays : others).push({ name: group.name, mesh: compactGroup(obstacle.meshData, group),
                        model, color: obstacleColor, doubleSided: true, flatShaded: true });
                }
            } else {
                if (!warnings.length) warnings.push('This legacy imported mesh has no object names. Its tray geometry cannot be separated and is included only with Other obstacles.');
                if (options.obstacles) others.push({ name: 'Unclassified imported mesh', mesh: naturalMesh(obstacle.meshData),
                    model, color: obstacleColor, doubleSided: true, flatShaded: true });
            }
        } else if (options.obstacles) {
            const mesh = getGeometry(obstacle.geomId());
            if (!mesh) throw new Error('Obstacle geometry is unavailable.');
            others.push({ name: `${obstacle.type.peek()}:${others.length}`, mesh: naturalMesh(mesh),
                model, color: obstacleColor, doubleSided: true, flatShaded: true });
        }
    }
    const nodes: ExportNode[] = [];
    if (soils.length) nodes.push({ name: 'Soil', children: soils });
    if (trays.length) nodes.push({ name: 'Trays', children: trays });
    if (others.length) nodes.push({ name: 'Other obstacles', children: others });
    return { nodes, warnings };
}

export async function exportCurrentFrame(options: FrameExportOptions): Promise<{ json: string; step: number; warnings: string[] }> {
    const chosen = { ...options };
    const source = captureFrameSource(appstate.playPointer.peek());
    const statics = staticSnapshot(chosen);
    //Keep the exact loaded species meshes even if the settings change while a frame downloads.
    const species = new Map(appstate.species.peek().map(s => [s.name.peek(), {
        leaves: s.leafMeshSlices.slice(), leaf: s.leafMesh, petal: s.petalMesh,
    }]));
    const scene = await source.read();
    source.verify();
    if (chosen.roots && !exportRootsAvailable(scene))
        throw new Error('This frame has no downloaded root geometry. Uncheck Roots and export again.');
    const capabilities = frameCapabilities(scene);
    const list = new InstanceList();
    buildPlants(list, {
        scene, visualMapping: VisualMappingOptions.Natural, debugBoxes: false, showLeaves: true, showRoots: chosen.roots,
        leafGeom: (name, slice) => {
            const s = species.get(name);
            const mesh = s?.leaves.length ? s.leaves[Math.max(0, Math.min(slice, s.leaves.length - 1))] : s?.leaf;
            return mesh ? registerGeometry(`export:leaf:${name}:${slice}`, mesh) : Geom.Plane;
        },
        petalGeom: name => {
            const mesh = species.get(name)?.petal;
            return mesh ? registerGeometry(`export:petal:${name}`, mesh) : Geom.Plane;
        },
    });
    const above: ExportNode[] = [], roots: ExportNode[] = [];
    const plantNodes = new Map<string, ExportGroup>(), organNodes = new Map<string, ExportGroup>();
    for (let i = 0; i < list.count; ++i) {
        const inst = list.items[i];
        if ((inst.flags & IFlags.Hidden) || inst.ref?.type !== 'plant') continue;
        const { entity, primitive: index } = inst.ref.index;
        const plant = scene[entity], primitive = plant.primitives[index];
        const isRoot = 'organ' in primitive && primitive.organ === Organs.Root;
        const key = `${isRoot ? 'root' : 'plant'}:${entity}`;
        let plantNode = plantNodes.get(key);
        if (!plantNode) {
            plantNode = { name: `plant:${plant.sourcePlantIndex ?? entity} ${plant.species}`, children: [],
                extras: chosen.metadata ? { greenFdt: {
                    plantIndex: entity, sourcePlantIndex: plant.sourcePlantIndex, species: plant.species,
                } } : undefined };
            plantNodes.set(key, plantNode); (isRoot ? roots : above).push(plantNode);
        }
        const organKey = `${key}:${index}`;
        let organNode = organNodes.get(organKey);
        if (!organNode) {
            const organ = 'organ' in primitive ? Organs[primitive.organ] : 'Unspecified';
            organNode = { name: `${organ}:${index}`, children: [],
                extras: chosen.metadata ? organExtras(primitive, index, !!capabilities?.analyticsAvailable) : undefined };
            organNodes.set(organKey, organNode); plantNode.children.push(organNode);
        }
        const mesh = getGeometry(inst.geom);
        if (!mesh) throw new Error(`Recorded geometry is unavailable for ${plant.species}.`);
        organNode.children.push({ name: `part:${organNode.children.length}`, mesh, model: inst.model.slice(),
            color: inst.color, doubleSided: !!(inst.flags & IFlags.DoubleSided),
            flatShaded: !!(inst.flags & IFlags.FlatShaded), fold: inst.fold });
    }
    const nodes: ExportNode[] = [];
    if (above.length) nodes.push({ name: 'Plants (above ground)', children: above });
    if (roots.length) nodes.push({ name: 'Roots', children: roots });
    nodes.push(...statics.nodes);
    if (!nodes.length) throw new Error('There is no geometry to export with these options.');
    const extras = chosen.metadata ? { greenFdt: {
        schemaVersion: 1, timestep: source.step, hoursPerTick: source.hoursPerTick,
        elapsedHours: source.step * source.hoursPerTick, runId: source.runId || undefined,
        sourceFormatVersion: capabilities?.version, analyticsAvailable: !!capabilities?.analyticsAvailable, definitions,
    } } : undefined;
    return { json: exportGeometry(nodes, extras), step: source.step, warnings: statics.warnings };
}
