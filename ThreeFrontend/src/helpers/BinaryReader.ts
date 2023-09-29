import { Primitive, Primitives } from "./Primitives";


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

    isEnd() {
        return this.pos == this.source.length;
    }

    readAgroScene()
    {
        const version = this.readUInt8();
        switch (version)
        {
            case 3: return this.readAgroSceneV3();
            case 5: return this.readAgroSceneV5or6(false);
            case 6: return this.readAgroSceneV5or6(true);
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
                    case 1: entity.push({ type: Primitives.Disk, affineTransform: this.readFloat32Vector(12), stats: undefined }); break; //disk
                    case 2: { //cylinder / stem
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        entity.push({ type: Primitives.Cylinder, affineTransform: transform, length: length, radius: radius, stats: undefined });
                    }
                    break;
                    case 4: { //sphere / bud
                        const center = this.readFloat32Vector(3);
                        const radius = this.readFloat32();
                        entity.push({ type: Primitives.Sphere, center: center, radius: radius, stats: undefined });
                    }
                    break;
                    case 8: entity.push({ type: Primitives.Rectangle, affineTransform: this.readFloat32Vector(12), stats: undefined }); break; //plane / leaf
                }
                const isSensor = this.readUInt8();
            }
            result.push(entity);
        }
        return result;
    }

    readAgroSceneV5or6(roots: boolean)
    {
        const result : Primitive[][] = [];
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
                switch(this.readUInt8())
                {
                    case 1: { //leaf
                        const transform = this.readFloat32Vector(12);
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const lastIrradiance = this.readFloat32();
                        const dailyResource = this.readFloat32();
                        const dailyProduction = this.readFloat32();
                        const auxins = this.readFloat32();
                        const cytokianins = this.readFloat32();
                        entity.push({ type: Primitives.Rectangle, affineTransform: transform, stats: new Float32Array([waterRatio, energyRatio, lastIrradiance, dailyResource, dailyProduction, 0, 0, auxins, cytokianins]) });
                        maxDailyProductionShoots = Math.max(dailyProduction, maxDailyProductionShoots);
                        maxDailyResourceShoots = Math.max(dailyResource, maxDailyResourceShoots);
                    }
                    break;
                    case 2: { //stem
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const woodRatio = this.readFloat32();
                        const auxins = this.readFloat32();
                        const cytokianins = this.readFloat32();
                        entity.push({ type: Primitives.Cylinder, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, woodRatio, auxins, cytokianins]) });
                    }
                    break;
                    case 3: { //bud
                        const center = this.readFloat32Vector(3);
                        const radius = this.readFloat32();
                        const waterRatio = this.readFloat32();
                        const energyRatio = this.readFloat32();
                        const auxins = this.readFloat32();
                        const cytokianins = this.readFloat32();
                        entity.push({ type: Primitives.Sphere, center: center, radius: radius, stats: new Float32Array([waterRatio, energyRatio, auxins, cytokianins]) });
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
                            //const auxins = this.readFloat32();
                            //const cytokianins = this.readFloat32();
                            entity.push({ type: Primitives.Box, affineTransform: transform, length: length, radius: radius, stats: new Float32Array([waterRatio, energyRatio, woodRatio, dailyResource, dailyProduction, 0, 0]) });
                            maxDailyProductionRoots = Math.max(dailyProduction, maxDailyProductionRoots);
                            maxDailyResourceRoots = Math.max(dailyResource, maxDailyResourceRoots);
                        }
                    }
                }
            }

            for(let j = 0; j < entity.length; ++j)
                switch (entity[j].type)
                {
                    case Primitives.Rectangle:
                    {
                        entity[j].stats[5] = entity[j].stats[3] / maxDailyResourceShoots;
                        entity[j].stats[6] = entity[j].stats[4] / maxDailyProductionShoots;
                    }
                    break;
                    case Primitives.Box:
                    {
                        entity[j].stats[5] = entity[j].stats[3] / maxDailyResourceRoots;
                        entity[j].stats[6] = entity[j].stats[4] / maxDailyProductionRoots;
                    }
                    break;
                }

            result.push(entity);
        }

        return result;
    }
}