import { Primitive } from "./Primitives";

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
        return this.readUInt8() - 128;
    }

    readUInt16() {
        const result = (this.source[this.pos + 1] << 8) | this.source[this.pos];
        this.pos += 2;
        return result;
    }

    readInt16() {
        return this.readUInt16() - 32768;
    }

    readUInt32() {
        const result = (this.source[this.pos + 3] << 24) | (this.source[this.pos + 2] << 16) | (this.source[this.pos + 1] << 8) | this.source[this.pos];
        this.pos += 4;
        return result;
    }

    readInt32() {
        return this.readUInt16() - 32768;
    }

    readFloat32() {
        //const result = new Float32Array([this.source[this.pos + 3], this.source[this.pos + 2], this.source[this.pos + 1], this.source[this.pos]]);
        const result = new Float32Array(this.source.slice(this.pos, this.pos + 4).buffer);
        this.pos += 4;
        console.log(result[0])
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
                    case 1: entity.push({ type: 1, affineTransform: this.readFloat32Vector(12) }); break; //disk
                    case 2: {
                        const length = this.readFloat32();
                        const radius = this.readFloat32();
                        const transform = this.readFloat32Vector(12);
                        entity.push({ type: 2, affineTransform: transform, length: length, radius: radius });
                    }
                    break;
                    case 4: {
                        const center = this.readFloat32Vector(3);
                        const radius = this.readFloat32();
                        entity.push({ type: 4, center: center, radius: radius });
                    }
                    break;
                    case 8: entity.push({ type: 8, affineTransform: this.readFloat32Vector(12) }); break; //disk
                }
                const isSensor = this.readUInt8();
            }
            result.push(entity);
        }
        return result;
    }
}