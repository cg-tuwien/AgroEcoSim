//A minimal ZIP writer, stored (uncompressed) only.
//
//No dependency, and no deflate: PNG and JPEG are already compressed, so storing them costs
//nothing but a few bytes of header each and avoids pulling in a compressor. This is the
//universal fallback for every browser or configuration where WebCodecs cannot encode video,
//and the output feeds straight into ffmpeg.

const LOCAL_HEADER = 0x04034b50;
const CENTRAL_HEADER = 0x02014b50;
const END_OF_CENTRAL = 0x06054b50;

const crcTable = (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; ++n) {
        let c = n;
        for (let k = 0; k < 8; ++k) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c >>> 0;
    }
    return table;
})();

export function crc32(bytes: Uint8Array): number {
    let c = 0xFFFFFFFF;
    for (let i = 0; i < bytes.length; ++i) c = crcTable[(c ^ bytes[i]) & 0xFF] ^ (c >>> 8);
    return (c ^ 0xFFFFFFFF) >>> 0;
}

type Entry = { name: Uint8Array; data: Uint8Array; crc: number; offset: number };

export class ZipStoreWriter {
    private entries: Entry[] = [];
    private chunks: Uint8Array[] = [];
    private offset = 0;

    get bytesWritten() { return this.offset; }
    get fileCount() { return this.entries.length; }

    add(name: string, data: Uint8Array) {
        const encodedName = new TextEncoder().encode(name);
        const crc = crc32(data);
        const entry: Entry = { name: encodedName, data, crc, offset: this.offset };

        const header = new DataView(new ArrayBuffer(30));
        header.setUint32(0, LOCAL_HEADER, true);
        header.setUint16(4, 20, true);           // version needed
        header.setUint16(6, 0x0800, true);       // flags: UTF-8 names
        header.setUint16(8, 0, true);            // method 0 = stored
        header.setUint16(10, 0, true);           // mod time
        header.setUint16(12, 0, true);           // mod date
        header.setUint32(14, crc, true);
        header.setUint32(18, data.length, true); // compressed size
        header.setUint32(22, data.length, true); // uncompressed size
        header.setUint16(26, encodedName.length, true);
        header.setUint16(28, 0, true);           // extra field length

        this.push(new Uint8Array(header.buffer));
        this.push(encodedName);
        this.push(data);
        this.entries.push(entry);
    }

    finish(): Blob {
        return new Blob([this.toBytes() as BlobPart], { type: 'application/zip' });
    }

    /**
     * The finished archive as bytes.
     *
     * Separate from finish() because jsdom's Blob has no arrayBuffer(), so the structure
     * cannot otherwise be asserted in a unit test - and a malformed central directory
     * produces a file that simply refuses to open, with no earlier symptom.
     */
    toBytes(): Uint8Array {
        const centralStart = this.offset;

        for (const e of this.entries) {
            const h = new DataView(new ArrayBuffer(46));
            h.setUint32(0, CENTRAL_HEADER, true);
            h.setUint16(4, 20, true);            // version made by
            h.setUint16(6, 20, true);            // version needed
            h.setUint16(8, 0x0800, true);        // flags: UTF-8 names
            h.setUint16(10, 0, true);            // method 0 = stored
            h.setUint16(12, 0, true);
            h.setUint16(14, 0, true);
            h.setUint32(16, e.crc, true);
            h.setUint32(20, e.data.length, true);
            h.setUint32(24, e.data.length, true);
            h.setUint16(28, e.name.length, true);
            h.setUint16(30, 0, true);            // extra
            h.setUint16(32, 0, true);            // comment
            h.setUint16(34, 0, true);            // disk number
            h.setUint16(36, 0, true);            // internal attrs
            h.setUint32(38, 0, true);            // external attrs
            h.setUint32(42, e.offset, true);     // offset of local header

            this.push(new Uint8Array(h.buffer));
            this.push(e.name);
        }

        const end = new DataView(new ArrayBuffer(22));
        end.setUint32(0, END_OF_CENTRAL, true);
        end.setUint16(4, 0, true);               // disk number
        end.setUint16(6, 0, true);               // disk with central directory
        end.setUint16(8, this.entries.length, true);
        end.setUint16(10, this.entries.length, true);
        end.setUint32(12, this.offset - centralStart, true);
        end.setUint32(16, centralStart, true);
        end.setUint16(20, 0, true);              // comment length
        this.push(new Uint8Array(end.buffer));

        return concatChunks(this.chunks);
    }

    private push(bytes: Uint8Array) {
        this.chunks.push(bytes);
        this.offset += bytes.length;
    }
}


function concatChunks(chunks: Uint8Array[]): Uint8Array {
    const total = chunks.reduce((a, c) => a + c.length, 0);
    const out = new Uint8Array(total);
    let at = 0;
    for (const c of chunks) { out.set(c, at); at += c.length; }
    return out;
}
