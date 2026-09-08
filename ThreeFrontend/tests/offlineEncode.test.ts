//Tests for the offline encoders and frame mapping.
//
//These are the parts that can be wrong in ways a screenshot cannot reveal: a malformed ZIP
//central directory or a bad EBML size prefix produces a file that simply will not open.

import { buildFrameMap, mapUniform, mapSimTime, isMonotonic, FrameSource } from '../src/render/offline/FrameSource';
import { ZipStoreWriter, crc32 } from '../src/render/offline/encode/ZipStoreWriter';
import { bitrateFor, evenDimension } from '../src/render/offline/encode/codecs';

const hist = (ts: number[]) => ts.map(t => ({ t, s: `frame${t}` }));

describe('mapUniform', () => {
    test('one output frame per history entry', () => {
        const m = mapUniform(hist([0, 1, 2, 3]), 0, 3, 1, 4);
        expect(m.map(x => x.historyIndex)).toEqual([0, 1, 2, 3]);
        //simHours is the timestep scaled by hoursPerTick.
        expect(m.map(x => x.simHours)).toEqual([0, 4, 8, 12]);
    });

    test('honours the step and clamps to the end of history', () => {
        const m = mapUniform(hist([0, 1, 2, 3, 4, 5]), 0, 99, 2, 1);
        expect(m.map(x => x.historyIndex)).toEqual([0, 2, 4]);
    });

    test('a step of zero does not hang', () => {
        const m = mapUniform(hist([0, 1, 2]), 0, 2, 0, 1);
        expect(m.length).toBe(3);
    });
});

describe('mapSimTime', () => {
    test('always ends on the last history entry', () => {
        //Otherwise the final simulation state - the mature plant - never appears in the video.
        const m = mapSimTime(hist([0, 10]), 0, 1, 2, 1, 1);
        expect(m[m.length - 1].historyIndex).toBe(1);
    });

    test('holds the previous entry while the simulation has nothing new', () => {
        //Timesteps 0 and 10 only: at 1 hour per tick and 1 hour per second, 10 seconds of
        //video must come from two entries, holding the first for most of it.
        const m = mapSimTime(hist([0, 10]), 0, 1, 2 /*fps*/, 1 /*h per s*/, 1);
        expect(m.length).toBeGreaterThan(2);
        expect(m[0].historyIndex).toBe(0);
        expect(m[m.length - 1].historyIndex).toBe(1);
        //Monotonic and never skipping backwards.
        for (let i = 1; i < m.length; ++i)
            expect(m[i].historyIndex).toBeGreaterThanOrEqual(m[i - 1].historyIndex);
    });

    test('falls back to uniform when timestamps are not monotonic', () => {
        //The preview handler pushes unconditionally while result guards on t, so duplicates
        //are genuinely possible; a stuttering video is worse than an even one.
        const messy = hist([0, 5, 5, 3, 9]);
        expect(isMonotonic(messy, 0, 4)).toBe(false);
        const m = mapSimTime(messy, 0, 4, 25, 100, 1);
        expect(m.map(x => x.historyIndex)).toEqual([0, 1, 2, 3, 4]);
    });

    test('detects strictly increasing series', () => {
        expect(isMonotonic(hist([0, 1, 2]), 0, 2)).toBe(true);
        expect(isMonotonic(hist([0, 0, 1]), 0, 2)).toBe(false);
    });
});

describe('buildFrameMap', () => {
    test('dispatches on the timing mode', () => {
        const h = hist([0, 1, 2, 3]);
        const uniform = buildFrameMap(h, { mode: 'uniform', from: 0, to: 3, step: 1, fps: 25, hoursPerSecond: 10, hoursPerTick: 1 });
        expect(uniform.length).toBe(4);

        const sim = buildFrameMap(h, { mode: 'simTime', from: 0, to: 3, step: 1, fps: 10, hoursPerSecond: 1, hoursPerTick: 1 });
        //3 simulated hours at 1 hour/second and 10 fps is about 30 frames.
        expect(sim.length).toBeGreaterThan(20);
    });
});

describe('FrameSource', () => {
    test('decodes once per distinct index and reports reuse', async () => {
        let decodes = 0;
        const src = new FrameSource<string>(i => { decodes++; return `scene${i}`; });

        expect(await src.at(0)).toEqual({ scene: 'scene0', reused: false });
        expect(await src.at(0)).toEqual({ scene: 'scene0', reused: true });
        expect(decodes).toBe(1);

        expect((await src.at(1)).reused).toBe(false);
        expect(decodes).toBe(2);

        //Only the latest frame is remembered - going back re-decodes rather than growing
        //an unbounded cache.
        expect((await src.at(0)).reused).toBe(false);
        expect(decodes).toBe(3);
    });

    test('an asynchronous decode is awaited, not handed back as a promise', async () => {
        //Frames outside the hot window are compressed, so the decode returns a promise. The
        //export loop writes appstate.scene straight from `scene` and depends on the rebuild
        //effects running synchronously off that write, so the await has to happen in here.
        const src = new FrameSource<string>(async i => `scene${i}`);
        const { scene, reused } = await src.at(3);
        expect(scene).toBe('scene3');
        expect(reused).toBe(false);
        expect(await src.at(3)).toEqual({ scene: 'scene3', reused: true });
    });
});

describe('crc32', () => {
    test('matches the known value for "123456789"', () => {
        //The standard CRC-32 check value.
        expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xCBF43926);
    });

    test('empty input is zero', () => {
        expect(crc32(new Uint8Array(0))).toBe(0);
    });
});

describe('ZipStoreWriter', () => {
    test('produces a structurally valid stored zip', async () => {
        const zip = new ZipStoreWriter();
        const a = new TextEncoder().encode('hello');
        const b = new TextEncoder().encode('world!!');
        zip.add('frame_00000.png', a);
        zip.add('frame_00001.png', b);
        expect(zip.fileCount).toBe(2);

        const bytes = zip.toBytes();
        const view = new DataView(bytes.buffer);

        expect(view.getUint32(0, true)).toBe(0x04034b50);          // first local header

        //End-of-central-directory sits in the last 22 bytes when there is no comment.
        const eocd = bytes.length - 22;
        expect(view.getUint32(eocd, true)).toBe(0x06054b50);
        expect(view.getUint16(eocd + 8, true)).toBe(2);             // entries on this disk
        expect(view.getUint16(eocd + 10, true)).toBe(2);            // entries total

        const centralOffset = view.getUint32(eocd + 16, true);
        expect(view.getUint32(centralOffset, true)).toBe(0x02014b50);
        //The central directory size must actually reach the EOCD record.
        expect(centralOffset + view.getUint32(eocd + 12, true)).toBe(eocd);

        //Stored, not deflated, so the payload appears verbatim.
        expect(view.getUint16(8, true)).toBe(0);
        expect(view.getUint32(18, true)).toBe(a.length);
    });
});

describe('codec helpers', () => {
    test('bitrate scales with pixels and frame rate', () => {
        const a = bitrateFor(1920, 1080, 25, 'medium');
        const b = bitrateFor(3840, 2160, 25, 'medium');
        expect(b).toBe(a * 4);
        expect(bitrateFor(1920, 1080, 50, 'medium')).toBe(a * 2);
        expect(bitrateFor(1920, 1080, 25, 'high')).toBeGreaterThan(a);
    });

    test('dimensions are rounded up to even', () => {
        //4:2:0 chroma subsampling cannot represent an odd dimension.
        expect(evenDimension(1919)).toBe(1920);
        expect(evenDimension(1920)).toBe(1920);
    });
});

describe('WebMWriter', () => {
    //The muxer is hand-rolled, so its byte layout is worth pinning: a wrong EBML size prefix
    //produces a file that no player will open, with no earlier symptom.
    const { WebMWriter } = require('../src/render/offline/encode/WebMWriter');

    function bytesOf(writer: any): Uint8Array {
        //jsdom's Blob exposes no arrayBuffer(), so reach for the chunks the writer buffered.
        const chunks: Uint8Array[] = writer.chunks;
        const total = chunks.reduce((a, c) => a + c.length, 0);
        const out = new Uint8Array(total);
        let at = 0;
        for (const c of chunks) { out.set(c, at); at += c.length; }
        return out;
    }

    test('starts with the EBML magic and declares webm', () => {
        const w = new WebMWriter({ codecId: 'V_VP9', width: 640, height: 480 });
        w.addFrame(new Uint8Array([1, 2, 3]), 0, true);
        w.finish();

        const b = bytesOf(w);
        expect(Array.from(b.slice(0, 4))).toEqual([0x1A, 0x45, 0xDF, 0xA3]);
        const text = new TextDecoder().decode(b);
        expect(text.includes('webm')).toBe(true);
        expect(text.includes('V_VP9')).toBe(true);
    });

    test('writes a real duration rather than leaving it zero', () => {
        const w = new WebMWriter({ codecId: 'V_VP9', width: 320, height: 240 });
        w.setFrameInterval(40);              // 25 fps
        w.addFrame(new Uint8Array([1]), 0, true);
        w.addFrame(new Uint8Array([2]), 40, false);
        w.addFrame(new Uint8Array([3]), 80, false);
        w.finish();

        //Duration is the last 8 bytes of the Info element, a big-endian float64 in
        //milliseconds. Left at zero, players report an infinite duration and cannot scrub.
        const info: Uint8Array = (w as any).infoBytes;
        const duration = new DataView(info.buffer, info.byteOffset + info.length - 8, 8).getFloat64(0, false);
        expect(duration).toBeCloseTo(120, 6);   // last timestamp 80 + one 40 ms interval
    });

    test('starts a new cluster rather than overflowing a relative timestamp', () => {
        const w = new WebMWriter({ codecId: 'V_VP8', width: 64, height: 64 });
        w.addFrame(new Uint8Array([0]), 0, true);
        //Well past the 2 s cluster limit, and a keyframe, so a new cluster must start -
        //otherwise the offset would overflow SimpleBlock's signed 16 bits.
        w.addFrame(new Uint8Array([0]), 5000, true);
        w.finish();

        expect((w as any).clusterTimestamp).toBe(5000);
    });

    test('refuses frames after finalising', () => {
        const w = new WebMWriter({ codecId: 'V_VP9', width: 64, height: 64 });
        w.addFrame(new Uint8Array([0]), 0, true);
        w.finish();
        expect(() => w.addFrame(new Uint8Array([0]), 40, false)).toThrow(/finalis/i);
    });
});

describe('PNG encoder', () => {
    //The canvas encoder could hand back a blank image without failing, on a machine under GPU
    //pressure, with the readback verified good and the video export of the same pixels fine.
    //Encoding here is checked structurally instead: a real PNG, the right size, no alpha.
    const { encodePngBytes, canEncodePng } = require('../src/render/offline/encode/PngEncoder');

    const rgba = (w: number, h: number) => {
        const d = new Uint8ClampedArray(w * h * 4);
        for (let i = 0; i < w * h; ++i) {
            d[i * 4] = i % 256; d[i * 4 + 1] = (i * 7) % 256; d[i * 4 + 2] = (i * 13) % 256; d[i * 4 + 3] = 255;
        }
        return d;
    };

    const parse = (bytes: Uint8Array) => {
        const chunks: { type: string; length: number }[] = [];
        let at = 8;
        while (at < bytes.length) {
            const view = new DataView(bytes.buffer, bytes.byteOffset);
            const len = view.getUint32(at);
            chunks.push({ type: String.fromCharCode(...bytes.subarray(at + 4, at + 8)), length: len });
            at += 12 + len;
        }
        return chunks;
    };

    (canEncodePng() ? test : test.skip)('produces a structurally valid, alpha-free PNG', async () => {
        const bytes: Uint8Array = await encodePngBytes(rgba(37, 11), 37, 11);

        expect(Array.from(bytes.subarray(0, 8))).toEqual([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        const chunks = parse(bytes);
        expect(chunks.map(c => c.type)).toEqual(['IHDR', 'IDAT', 'IEND']);

        const view = new DataView(bytes.buffer, bytes.byteOffset);
        expect(view.getUint32(16)).toBe(37);       // IHDR width
        expect(view.getUint32(20)).toBe(11);       // IHDR height
        expect(bytes[24]).toBe(8);                 // bit depth
        //Colour type 2 is RGB. Alpha is deliberately not stored: the renderer is opaque, and
        //an alpha channel is only somewhere for a bug to hide.
        expect(bytes[25]).toBe(2);
    });

    (canEncodePng() ? test : test.skip)('the image data round-trips through deflate', async () => {
        const zlib = require('zlib');
        const w = 5, h = 3;
        const src = rgba(w, h);
        const bytes: Uint8Array = await encodePngBytes(src, w, h);

        let at = 8, idat: Uint8Array | undefined;
        const view = new DataView(bytes.buffer, bytes.byteOffset);
        while (at < bytes.length) {
            const len = view.getUint32(at);
            if (String.fromCharCode(...bytes.subarray(at + 4, at + 8)) === 'IDAT')
                idat = bytes.subarray(at + 8, at + 8 + len);
            at += 12 + len;
        }
        const raw = new Uint8Array(zlib.inflateSync(Buffer.from(idat!)));

        //Every scanline is filter byte 0 followed by RGB triples.
        expect(raw.length).toBe(h * (w * 3 + 1));
        for (let y = 0; y < h; ++y) {
            expect(raw[y * (w * 3 + 1)]).toBe(0);
            for (let x = 0; x < w; ++x) {
                const got = raw.subarray(y * (w * 3 + 1) + 1 + x * 3, y * (w * 3 + 1) + 4 + x * 3);
                const from = (y * w + x) * 4;
                expect(Array.from(got)).toEqual([src[from], src[from + 1], src[from + 2]]);
            }
        }
    });
});
