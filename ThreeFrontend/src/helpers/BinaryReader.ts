import { ConsoleLogger } from "@microsoft/signalr/dist/esm/Utils";
import { Obstacle } from "./Obstacle";
import { Organs, Primitive, Primitives } from "./Primitives";
import { BoxTerrainItem, ITerrainItem, MeshTerrainItem } from "./Terrain";
import { PlantModel } from "./Scene";

const decoder = new TextDecoder("utf-8");

export default class BinaryReader {
    source: Uint8Array;
    pos: 0;

    constructor(src: Uint8Array) {
        this.source = src;
        this.pos = 0;
    }

    readUInt8() {
        return this.source[this.pos++];
    }

    readInt8() {
        const result = this.readUInt8();
        return result >= 128 ? 256 - result : result;
    }

    readUInt16() {
        const result = (this.source[this.pos + 1] << 8) | this.source[this.pos];
        this.pos += 2;
        return result;
    }

    readInt16() {
        const result = this.readUInt16();
        return result >= 32768 ? result - 65536 : result;
    }

    readUInt32() {
        const result = (this.source[this.pos + 3] << 24) | (this.source[this.pos + 2] << 16) | (this.source[this.pos + 1] << 8) | this.source[this.pos];
        this.pos += 4;
        return result;
    }

    readInt32() {
        const result = this.readUInt32();
        return result >= 2147483648 ? result - 4294967296 : result;
    }

    readFloat32() {
        //const result = new Float32Array([this.source[this.pos + 3], this.source[this.pos + 2], this.source[this.pos + 1], this.source[this.pos]]);
        const result = new Float32Array(this.source.slice(this.pos, this.pos + 4).buffer);
        this.pos += 4;
        return result[0];
    }

    readFloat32Vector(n: number) {
        const result = new Float32Array(n);
        for(let i = 0; i < n; ++i)
            result[i] = this.readFloat32();
        return result;
    }

    readInt32Array(n: number) {
        const result : number[] = [];
        for(let i = 0; i < n; ++i)
            result[i] = this.readInt32();
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
        const toHex2 = (n: number) => {
            const s = n.toString(16);
            return s.length === 1 ? "0" + s : s;
        };
        const r = this.source[this.pos++];
        const g = this.source[this.pos++];
        const b = this.source[this.pos++];
        return `#${toHex2(r)}${toHex2(g)}${toHex2(b)}`;
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
                    case 8: entity.push({ type: Primitives.Rectangle, organ: undefined, affineTransform: this.readFloat32Vector(12), stats: undefined, color: undefined }); break; //plane / leaf
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
                        entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, lastIrradiance, dailyResource, dailyProduction, 0, 0]), color: color });
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

    readAgroSceneV7()
    {
        const result : PlantModel[] = [];
        const flags = this.readInt8();
        const doRoots = (flags & 1) > 0;
        const doAnalytics = (flags & 2) >0;
        const entitesCount = this.readUInt32();
        for(let i = 0; i < entitesCount; ++i)
        {
            const entity : Primitive[] = [];
            const species = this.readString();
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
                            entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, lastIrradiance, dailyResource, dailyProduction, 0, 0]), color: color });
                            maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                            maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                        }
                        else
                        {
                            const color = this.readHexRGB();
                            entity.push({ type: Primitives.Rectangle, organ: organ, affineTransform: transform, stats: new Float32Array([0, 0, 0, 0, 0, 0, 0, 0, 0]), color: color });
                        }
                    }
                    break;
                    case 4: case 6: case 8: case 9: case 10: case 12: case 13: { //stem //petiole //meristem //flower_stem //flower_meristem //flower_petiole //flower_bud
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
                            entity.push({ type: Primitives.Cylinder, organ: organ, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, 0, woodRatio, dailyResource, dailyProduction, 0, 0]), color: color });
                            maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                            maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                        }
                        else
                        {
                            const color = this.readHexRGB();
                            entity.push({ type: Primitives.Cylinder, organ: organ, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([0, 0, 0, 0, 0, 0, 0, 0, 0]), color: color });
                        }
                    }
                    break;
                    case 2: { //bud
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
                }
            }

            if (doRoots)
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

            result.push({ primitives: entity, species: species });
        }

        return result;
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

        return {terrains: terrains, obstacles: obstacles};
    }
}