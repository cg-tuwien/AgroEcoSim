import { ConsoleLogger } from "@microsoft/signalr/dist/esm/Utils";
import { Obstacle } from "./Obstacle";
import { Organs, Primitive, Primitives } from "./Primitives";
import { BoxTerrainItem, ITerrainItem, MeshTerrainItem } from "./Terrain";
import type { ObstacleGroup } from "./Obstacle";
import { PlantModel, RecordedGeometry } from "./Scene";
import { registerGeometry } from '../render/core/GeometryStore';
import { computeVertexNormals } from '../render/core/Geometry';
const recordedGeometry = new Map<string, RecordedGeometry>();

const decoder = new TextDecoder("utf-8");

/** "00".."ff", so the per-byte hex conversion is an index rather than toString plus a pad. */
const HEX2: string[] = Array.from({ length: 256 }, (_, i) => i.toString(16).padStart(2, '0'));

//Interned colour strings, keyed by packed rgb. Bounded only by the number of distinct colours
//the simulation actually emits, which is a palette, not a continuum.
const hexCache = new Map<number, string>();

export default class BinaryReader {
    source: Uint8Array;
    pos: number;
    /**
     * A view over the same bytes, used for every multi-byte read.
     *
     * readFloat32 used to be `new Float32Array(source.slice(pos, pos + 4).buffer)`, which
     * allocates a copy of the four bytes AND a typed array to reinterpret them - two objects
     * per float, so roughly fifty per primitive once the affine transform and the analytics
     * block are counted. At tens of thousands of primitives per timestep, redecoded on every
     * frame of playback, that was the single largest source of garbage in the app.
     *
     * The byteOffset/byteLength must be passed through: `src` is frequently a view into a
     * larger buffer (Node's Buffer pools its allocations), and a DataView over `src.buffer`
     * alone would silently read from the wrong place.
     */
    private view: DataView;

    constructor(src: Uint8Array) {
        this.source = src;
        this.pos = 0;
        this.view = new DataView(src.buffer, src.byteOffset, src.byteLength);
    }

    readUInt8() {
        return this.source[this.pos++];
    }

    readInt8() {
        const result = this.readUInt8();
        return result >= 128 ? 256 - result : result;
    }

    readUInt16() {
        const result = this.view.getUint16(this.pos, true);
        this.pos += 2;
        return result;
    }

    readInt16() {
        const result = this.readUInt16();
        return result >= 32768 ? result - 65536 : result;
    }

    readUInt32() {
        const result = this.view.getUint32(this.pos, true);
        this.pos += 4;
        return result;
    }

    readInt32() {
        const result = this.view.getInt32(this.pos, true);
        this.pos += 4;
        return result;
    }

    readFloat32() {
        const result = this.view.getFloat32(this.pos, true);
        this.pos += 4;
        return result;
    }

    readFloat32Vector(n: number) {
        const result = new Float32Array(n);
        //Read straight into the destination rather than through readFloat32, so the whole
        //vector costs one bounds check per element and no call overhead.
        let p = this.pos;
        for (let i = 0; i < n; ++i, p += 4)
            result[i] = this.view.getFloat32(p, true);
        this.pos = p;
        return result;
    }

    readInt32Array(n: number) {
        const result : number[] = new Array(n);
        let p = this.pos;
        for (let i = 0; i < n; ++i, p += 4)
            result[i] = this.view.getInt32(p, true);
        this.pos = p;
        return result;
    }

    read7BitEncodedInt() {
        let result = 0, shift = 0;
        let b;
        do {
            b = this.source[this.pos++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while (b & 0x80);
        return result;
    }

    readString() {
        //assuming a C# 7bit encoded length first
        const byteLength = this.read7BitEncodedInt();
        const slice = this.source.subarray(this.pos, this.pos + byteLength);
        this.pos += byteLength;
        return decoder.decode(slice);
    }

    readHexRGB() {
        const r = this.source[this.pos++];
        const g = this.source[this.pos++];
        const b = this.source[this.pos++];

        //One string per primitive adds up, and a scene draws from a small fixed palette - the
        //shuffled green ramp plus a handful of organ colours - so the same few dozen strings
        //are rebuilt tens of thousands of times per timestep. Interning by the packed 24-bit
        //value makes the repeat case a map lookup and hands every caller the same instance.
        const key = (r << 16) | (g << 8) | b;
        const hit = hexCache.get(key);
        if (hit !== undefined) return hit;

        const made = `#${HEX2[r]}${HEX2[g]}${HEX2[b]}`;
        hexCache.set(key, made);
        return made;
    }

    isEnd() {
        return this.pos == this.source.length;
    }

    readAgroScene()
    {
        const version = this.readUInt8();
        if (version < 7)
            console.error("Deprecated scene format version:", version);
        else
        switch (version)
        {
            case 7: return this.readAgroSceneV7();
            case 8: return this.readAgroSceneV7(true);
            default: console.error("Unsupported scene format version:", version);
        }
    }

    readAgroSceneV3()
    {
        const result : Primitive[][] = [];
        const entitesCount = this.readUInt32();
        for(let i = 0; i < entitesCount; ++i)
        {
            const entity : Primitive[] = [];
            const primitivesCount = this.readUInt32();
            for(let j = 0; j < primitivesCount; ++j)
            {
                switch(this.readUInt8())
                {
                    case 1: entity.push({ type: Primitives.Disk, organ: undefined, affineTransform: this.readFloat32Vector(12), stats: undefined }); break; //disk
                    case 2: { //cylinder / stem
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        entity.push({ type: Primitives.Cylinder, organ: undefined, affineTransform: transform, length: length, radius: radius, stats: undefined, color: undefined });
                    }
                    break;
                    //buds disabled
                    // case 4: { //sphere / bud
                    //     const center = this.readFloat32Vector(3);
                    //     const radius = this.readFloat32();
                    //     entity.push({ type: Primitives.Sphere, center: center, radius: radius, stats: undefined });
                    // }
                    // break;
                    case 8: entity.push({ type: Primitives.Rectangle, organ: undefined, affineTransform: this.readFloat32Vector(12), stats: undefined, color: undefined, laminaSlice: 0 }); break; //plane / leaf
                }
                const isSensor = this.readUInt8();
            }
            result.push(entity);
        }
        return result;
    }

    readAgroSceneV5or6(roots: boolean)
    {
        const primitives : Primitive[][] = [];
        const entitesCount = this.readUInt32();
        for(let i = 0; i < entitesCount; ++i)
        {
            const entity : Primitive[] = [];
            const primitivesCount = this.readUInt32();
            let maxDailyResourceShoots = 0;
            let maxDailyProductionShoots = 0;
            let maxDailyResourceRoots = 0;
            let maxDailyProductionRoots = 0;
            for(let j = 0; j < primitivesCount; ++j)
            {
                const parentIndex = this.readInt32();
                const organ = this.readUInt8();
                switch(organ)
                {
                    case 5: case 11: { //leaf //flower_petal
                        const transform = this.readFloat32Vector(12);
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const lastIrradiance = this.readFloat32();
                        const dailyResource = this.readFloat32();
                        const dailyProduction = this.readFloat32();
                        const color = this.readHexRGB();
                        const auxins = this.readFloat32();
                        const dailyEfficiency = this.readFloat32();
                        entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, lastIrradiance, dailyResource, dailyProduction, 0, 0]), color: color, laminaSlice: 0 });
                        maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                        maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                    }
                    break;
                    case 4: case 6: case 8: case 9: case 10: case 12: case 13: { //stem //petiole //meristem //flower_stem //flower_meristem //flower_petiole //flower_bud
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const woodRatio = this.readFloat32();
                        const dailyResource = this.readFloat32();
                        const dailyProduction = this.readFloat32();
                        const color = this.readHexRGB();
                        const auxins = this.readFloat32();
                        const dailyEfficiency = this.readFloat32();
                        entity.push({ type: Primitives.Cylinder, organ: organ, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, woodRatio, dailyResource, dailyProduction, 0, 0]), color: color });
                        maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                        maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                    }
                    break;
                    case 2: { //bud
                        const center = this.readFloat32Vector(3);
                        const radius = this.readFloat32();
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const auxins = this.readFloat32();
                        const dailyEfficiency = this.readFloat32();
                        // buds disabled
                        //entity.push({ type: Primitives.Sphere, organ: organ, center: center, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, cytokinins]) });
                    }
                    break;
                }
            }

            if (roots)
            {
                const rootsCount =  this.readUInt32();
                for(let j = 0; j < rootsCount; ++j)
                {
                    const parentIndex = this.readInt32();
                    switch(this.readUInt8())
                    {
                        case 1: { //root
                            const length = this.readFloat32();
                            const radius = this.readFloat32();
                            const transform = this.readFloat32Vector(12);
                            const waterRatio = this.readFloat32();
                            const energyRatio = this.readFloat32();
                            const woodRatio = this.readFloat32();
                            const dailyResource = this.readFloat32();
                            const dailyProduction = this.readFloat32();
                            const auxins = 0;//this.readFloat32();
                            const cytokinins = 0;//this.readFloat32();
                            entity.push({ type: Primitives.Box, organ: Organs.Root, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, cytokinins, woodRatio, dailyResource, dailyProduction, 0, 0]) });
                            maxDailyProductionRoots = Math.max(dailyProduction, maxDailyProductionRoots);
                            maxDailyResourceRoots = Math.max(dailyResource, maxDailyResourceRoots);
                        }
                    }
                }
            }

            for(let j = 0; j < entity.length; ++j)
            {
                const ent = entity[j];
                switch (ent.type)
                {
                    case Primitives.Rectangle:
                    case Primitives.Cylinder:
                    {
                        ent.stats[7] = ent.stats[5] / maxDailyResourceShoots;
                        ent.stats[8] = ent.stats[6] / maxDailyProductionShoots;
                    }
                    break;
                    case Primitives.Box:
                    {
                        ent.stats[7] = ent.stats[5] / maxDailyResourceRoots;
                        ent.stats[8] = ent.stats[6] / maxDailyProductionRoots;
                    }
                    break;
                }
            }

            primitives.push(entity);
        }

        return primitives;
    }

    readAgroSceneV7(v8 = false)
    {
        const result : PlantModel[] = [];
        const flags = this.readInt8();
        if (v8) this.readRecordedGeometry();
        const doRoots = (flags & 1) > 0;
        const doAnalytics = (flags & 2) >0;
        const entitesCount = this.readUInt32();
        for(let i = 0; i < entitesCount; ++i)
        {
            const entity : Primitive[] = [];
            const species = this.readString();
            const geometry = v8 ? recordedGeometry.get(this.readString()) : undefined;
            if (v8 && !geometry) throw new Error('V8 scene references a missing immutable mesh asset.');
            const primitivesCount = this.readUInt32();
            let maxDailyResourceShoots = 0;
            let maxDailyProductionShoots = 0;
            let maxDailyResourceRoots = 0;
            let maxDailyProductionRoots = 0;
            //A long blade is simulated as a chain of leaf organs, and each of them needs to be
            //drawn with its own slice of the species outline rather than the whole of it. The
            //position within the chain is what says which slice; it is not on the wire, but the
            //parent link is, and a parent always precedes its child in the record order.
            //Indexed by ORGAN index, which is not the primitive index - buds are decoded and
            //then dropped, so the two run apart as soon as one appears.
            const leafOrgan = new Uint8Array(primitivesCount);
            const chainPosition = new Int32Array(primitivesCount);
            for(let j = 0; j < primitivesCount; ++j)
            {
                const parentIndex = this.readInt32();
                const organ = this.readUInt8();
                const kind = v8 ? this.readUInt8() : 0;
                if (kind > 2 || (kind === 1 && organ !== Organs.Leaf) || (kind === 2 && organ !== Organs.Petiole))
                    throw new Error('Invalid V8 deformation record.');
                const deformation = kind ? this.readFloat32Vector(4) : undefined;
                if (deformation && (geometry?.profile !== 1 || deformation.some(x => !Number.isFinite(x))
                    || (kind === 1 && deformation.some((x, axis) => Math.abs(x) > (axis < 2 ? 25 : 75) * Math.PI / 180 + 1e-6))
                    // Species may opt into a larger whole-petiole contact budget.
                    // Coordinates retain the V8 three-span interpretation; older frames
                    // need no conversion. Match the maximum explicit backend setting.
                    || (kind === 2 && Math.hypot(deformation[0],deformation[1]) + Math.hypot(deformation[2],deformation[3]) > Math.PI / 2 + 1e-6)))
                    throw new Error('Invalid V8 deformation coordinates.');
                if (organ === Organs.Leaf)
                {
                    leafOrgan[j] = 1;
                    if (parentIndex >= 0 && parentIndex < j && leafOrgan[parentIndex])
                        chainPosition[j] = chainPosition[parentIndex] + 1;
                }
                switch(organ)
                {
                    case Organs.Leaf: case Organs.FlowerPadel: { //lamina record: 12-float affine transform, no length/radius
                        const slice = organ === Organs.Leaf ? chainPosition[j] : 0;
                        const transform = this.readFloat32Vector(12);
                        if (doAnalytics)
                        {
                            const waterRatio = this.readFloat32();
                            const energyRatio = this.readFloat32();
                            const lastIrradiance = this.readFloat32();
                            const dailyResource = this.readFloat32();
                            const dailyProduction = this.readFloat32();
                            const color = this.readHexRGB();
                            const auxins = this.readFloat32();
                            const dailyEfficiency = this.readFloat32();
                            entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, lastIrradiance, dailyResource, dailyProduction, 0, 0]), color: color, laminaSlice: slice, deformation });
                            maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                            maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                        }
                        else
                        {
                            const color = this.readHexRGB();
                            entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([0, 0, 0, 0, 0, 0, 0, 0, 0]), color: color, laminaSlice: slice, deformation });
                        }
                    }
                    break;
                    case Organs.Stem: case Organs.Petiole: case Organs.Meristem: case Organs.RizomeMeristem:
                    case Organs.FlowerBud: case Organs.FlowerStem: case Organs.FlowerPedicel: case Organs.FlowerMeristem: { //cylinder record: length + radius + 12-float affine transform
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        if (doAnalytics)
                        {
                            const waterRatio = this.readFloat32();
                            const energyRatio = this.readFloat32();
                            const woodRatio = this.readFloat32();
                            const dailyResource = this.readFloat32();
                            const dailyProduction = this.readFloat32();
                            const color = this.readHexRGB();
                            const auxins = this.readFloat32();
                            const dailyEfficiency = this.readFloat32();
                            entity.push({ type: Primitives.Cylinder, organ: organ, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, woodRatio, dailyResource, dailyProduction, 0, 0]), color: color, deformation });
                            maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                            maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                        }
                        else
                        {
                            const color = this.readHexRGB();
                            entity.push({ type: Primitives.Cylinder, organ: organ, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([0, 0, 0, 0, 0, 0, 0, 0, 0]), color: color, deformation });
                        }
                    }
                    break;
                    case Organs.Bud: { //bud
                        const center = this.readFloat32Vector(3);
                        const radius = this.readFloat32();
                        if (doAnalytics)
                        {
                            const waterRatio = this.readFloat32();
                            const energyRatio = this.readFloat32();
                            const auxins = this.readFloat32();
                            const dailyEfficiency = this.readFloat32();
                        }
                        // buds disabled
                        //entity.push({ type: Primitives.Sphere, organ: organ, center: center, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, cytokinins]) });
                    }
                    break;
                    default:
                        //Every record is preceded by its organ code and its length depends on that code.
                        //An unknown code cannot be skipped, so everything after it would be decoded from
                        //a wrong offset. Abort loudly instead of returning garbage geometry.
                        console.error(`Unknown organ code ${organ} in the scene stream (entity ${i}, primitive ${j}). Organs in Primitives.ts is out of sync with OrganTypes in Agro/Organs.cs.`);
                        return result;
                }
            }

            if (doRoots)
            {
                //ExportV7 writes the VirtualRoots flag first; virtual root systems carry only their
                //bounding size instead of a list of root segments.
                const virtualRoots = this.readUInt8() != 0;
                if (virtualRoots)
                    this.readFloat32Vector(3); //bounding size of the virtual root system
                const rootsCount = virtualRoots ? 0 : this.readUInt32();
                for(let j = 0; j < rootsCount; ++j)
                {
                    const parentIndex = this.readInt32();
                    switch(this.readUInt8())
                    {
                        case 1: { //root, the roots section uses a local code, not an OrganTypes value
                            const length = this.readFloat32();
                            const radius = this.readFloat32();
                            const transform = this.readFloat32Vector(12);
                            const waterRatio = doAnalytics ? this.readFloat32() : 0;
                            const energyRatio = doAnalytics ? this.readFloat32() : 0;
                            const woodRatio = doAnalytics ? this.readFloat32() : 0;
                            const dailyResource = doAnalytics ? this.readFloat32() : 0;
                            const dailyProduction = doAnalytics ? this.readFloat32() : 0;
                            const auxins = 0;//this.readFloat32();
                            const cytokinins = 0;//this.readFloat32();
                            entity.push({ type: Primitives.Box, organ: Organs.Root, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, cytokinins, woodRatio, dailyResource, dailyProduction, 0, 0]) });
                            maxDailyProductionRoots = Math.max(dailyProduction, maxDailyProductionRoots);
                            maxDailyResourceRoots = Math.max(dailyResource, maxDailyResourceRoots);
                        }
                    }
                }
            }

            for(let j = 0; j < entity.length; ++j)
            {
                const ent = entity[j];
                switch (ent.type)
                {
                    case Primitives.Rectangle:
                    case Primitives.Cylinder:
                    {
                        ent.stats[7] = ent.stats[5] / maxDailyResourceShoots;
                        ent.stats[8] = ent.stats[6] / maxDailyProductionShoots;
                    }
                    break;
                    case Primitives.Box:
                    {
                        ent.stats[7] = ent.stats[5] / maxDailyResourceRoots;
                        ent.stats[8] = ent.stats[6] / maxDailyProductionRoots;
                    }
                    break;
                }
            }

            result.push({ primitives: entity, species: species, geometry });
        }

        return result;
    }

    private readRecordedGeometry() {
        const count = this.readUInt32();
        if (count > 4096) throw new Error('Invalid V8 asset count.');
        for (let i = 0; i < count; ++i) {
            const id = this.readString(), bytes = this.readUInt32(), end = this.pos + bytes;
            if (!/^[0-9a-f]{64}$/.test(id) || end > this.source.length) throw new Error('Invalid V8 asset.');
            if (recordedGeometry.has(id)) { this.pos = end; continue; }
            const profile = this.readUInt8(), version = this.readUInt8();
            if (profile > 1 || version !== 1) throw new Error(`Unsupported deformation profile ${profile}/${version}.`);
            const slices = this.readUInt32();
            if (!slices || slices > 256) throw new Error('Invalid V8 blade slices.');
            const readMesh = (key: string) => {
                const vertices = this.readUInt32();
                if (vertices > 1000000 || this.pos + vertices * 12 > end) throw new Error('Invalid V8 vertices.');
                const positions = this.readFloat32Vector(vertices * 3), size = this.readUInt32();
                if (size % 3 || size > 6000000 || this.pos + size * 4 + vertices * 4 > end) throw new Error('Invalid V8 triangles.');
                const indices = new Uint32Array(this.readInt32Array(size));
                if (indices.some(x => x >= vertices)) throw new Error('Invalid V8 vertex index.');
                const uvs = new Float32Array(vertices * 2);
                for (let v = 0; v < vertices; ++v) uvs[v * 2] = this.readFloat32();
                return registerGeometry(key, { positions, indices, uvs, normals: computeVertexNormals(positions, indices), name: key });
            };
            const leaves: number[] = [];
            for (let slice = 0; slice < slices; ++slice) leaves.push(readMesh(`recorded:${id}:leaf:${slice}`));
            const petal = readMesh(`recorded:${id}:petal`);
            if (this.pos !== end) throw new Error('Invalid V8 asset length.');
            recordedGeometry.set(id, { id, profile, leaves, petal });
        }
    }

    readTerrain() {
        const terrains : ITerrainItem[] = [];
        const version = this.readUInt8();
        const entitiesCount = this.readInt32();
        for(let i = 0; i < entitiesCount; ++i) {
            const type = this.readUInt8();
            if (type == 0)
            {
                const data = this.readFloat32Vector(3 + 3 + 4);
                const id = this.readString();
                terrains.push(new BoxTerrainItem(id, data, i));
            }
            else if (type == 1)
            {
                const id = this.readString();
                const position = this.readFloat32Vector(3);
                const pointsCount = this.readInt32();
                const points = this.readFloat32Vector(pointsCount * 3);
                const trianglesCount = this.readInt32();
                const triangles = this.readInt32Array(trianglesCount * 3);
                terrains.push(new MeshTerrainItem(id, position[0], position[1], position[2], points, triangles, i));
            }
        }

        const obstacles : Obstacle[] = [];
        const obstaclesCount = this.readInt32();
        for(let i = 0; i < obstaclesCount; ++i) {
            const verticesCount = this.readInt32();
            const vertices = this.readFloat32Vector(verticesCount * 3);
            const trianglesCount = this.readInt32();
            const faces = this.readInt32Array(trianglesCount);
            obstacles.push(new Obstacle('mesh', 0, 0, 0, 0, 0, 0, 0, 0, vertices, faces));
        }

        //Version 1 appends the group table: the imported obstacles arrive merged into one mesh so
        //the viewport keeps a single draw call, and this says which slice of it each named object
        //owns. Without it an obstacle cannot be coloured by its own measured value.
        if (version >= 1) {
            const groupCount = this.readInt32();
            const groups: ObstacleGroup[] = [];
            for (let i = 0; i < groupCount; ++i) {
                const name = this.readString();
                const vertexFirst = this.readInt32();
                const vertexCount = this.readInt32();
                const indexFirst = this.readInt32();
                const indexCount = this.readInt32();
                if (vertexCount > 0)
                    groups.push({ name, vertexFirst, vertexCount, indexFirst, indexCount });
            }
            //Every group indexes the one merged mesh, which is the only imported obstacle there is.
            if (groups.length > 0 && obstacles.length > 0)
                obstacles[0].groups = groups;
        }

        return {terrains: terrains, obstacles: obstacles};
    }
}
