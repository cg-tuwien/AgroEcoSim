//The decoded-scene cache: hits, eviction, and - the part that would be a real bug - dropping
//everything when history is replaced, since the cache is keyed by an index a new run reuses.

const fs = require('fs');
const SESSION = '/home/ubuntu/agro-fixture/session.json';

describe('decoded scene cache', () => {
    let appstate: any, decodeSceneAt: any, sceneAt: any, clearSceneCache: any, frame: any;

    beforeEach(() => {
        jest.resetModules();
        const mod = require('../src/appstate');
        appstate = mod.default;
        //The synchronous path: a cache hit, or a frame still in the uncompressed hot window.
        //That is what the playback machine takes, and what these tests are about.
        decodeSceneAt = mod.sceneAtSync;
        sceneAt = mod.sceneAt;
        clearSceneCache = mod.clearSceneCache;

        const session = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
        //The largest entry - the first is the empty t=0 scene.
        const saved = session.history.reduce((a: any, b: any) => (b.s.length > a.s.length ? b : a));
        //`history` holds BYTES in memory; base64 is only the on-disk session format. Feeding the
        //base64 straight in would test a shape the app never actually has.
        const bytes = Buffer.from(saved.s, 'base64');
        frame = { t: saved.t, s: new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength), plain: true };
        appstate.history = [frame];
    });

    test('a repeated index returns the very same object, not a re-decode', () => {
        const a = decodeSceneAt(0);
        const b = decodeSceneAt(0);
        expect(a).toBe(b);
        expect(a.length).toBeGreaterThan(0);
    });

    test('clearing forces a fresh decode with equal content', () => {
        const a = decodeSceneAt(0);
        clearSceneCache();
        const b = decodeSceneAt(0);
        expect(b).not.toBe(a);
        expect(b.length).toBe(a.length);
        expect(b[0].primitives.length).toBe(a[0].primitives.length);
        expect(Array.from(b[0].primitives[0].affineTransform))
            .toEqual(Array.from(a[0].primitives[0].affineTransform));
    });

    test('an out-of-range index clamps to the last frame and still caches', () => {
        expect(decodeSceneAt(99)).toBe(decodeSceneAt(0));
    });

    test('an empty history decodes to nothing without throwing', () => {
        appstate.history = [];
        expect(decodeSceneAt(0)).toEqual([]);
    });

    //The budget is in primitives, so filling it takes a lot of small frames. That is the point
    //of the design and the only way to exercise the eviction path honestly.
    test('the cache evicts once the primitive budget is exceeded', () => {
        const perFrame = decodeSceneAt(0)[0].primitives.length;
        expect(perFrame).toBeGreaterThan(0);

        //Read the real budget rather than restating it: the constant was raised once already,
        //because at 200,000 it sat below a single production frame and the cache never stored
        //anything at all.
        const needed = Math.ceil(require('../src/appstate').SCENE_CACHE_PRIMITIVES / perFrame) + 8;
        appstate.history = new Array(needed).fill(frame);
        clearSceneCache();

        const first = decodeSceneAt(0);
        for (let i = 1; i < needed; ++i) decodeSceneAt(i);

        //Index 0 is the oldest, so it must be gone; the most recent must still be resident.
        expect(decodeSceneAt(0)).not.toBe(first);
        const last = decodeSceneAt(needed - 1);
        expect(decodeSceneAt(needed - 1)).toBe(last);
    });
});

//Compressed frames are the whole reason retrieval became asynchronous.
describe('compressed frames', () => {
    const { deflateBytes } = require('../src/helpers/deflate');
    const fs2 = require('fs');

    let appstate: any, sceneAtSync: any, sceneAt: any, raw: Uint8Array;

    beforeEach(async () => {
        jest.resetModules();
        const mod = require('../src/appstate');
        appstate = mod.default;
        sceneAtSync = mod.sceneAtSync;
        sceneAt = mod.sceneAt;

        const session = JSON.parse(fs2.readFileSync('/home/ubuntu/agro-fixture/session.json', 'utf8'));
        const saved = session.history.reduce((a: any, b: any) => (b.s.length > a.s.length ? b : a));
        raw = new Uint8Array(Buffer.from(saved.s, 'base64'));
    });

    test('the synchronous path declines a compressed frame rather than decoding garbage', async () => {
        appstate.history = [{ t: 0, s: await deflateBytes(raw), plain: false }];
        //undefined, not [] - the caller must know to go asynchronous, not think it is empty.
        expect(sceneAtSync(0)).toBeUndefined();
    });

    test('the asynchronous path inflates and decodes to the same scene', async () => {
        const packed = await deflateBytes(raw);
        expect(packed.byteLength).toBeLessThan(raw.byteLength);

        appstate.history = [{ t: 0, s: raw, plain: true }];
        const fromPlain = sceneAtSync(0);

        jest.resetModules();
        const mod = require('../src/appstate');
        mod.default.history = [{ t: 0, s: packed, plain: false }];
        const fromPacked = await mod.sceneAt(0);

        expect(fromPacked.length).toBe(fromPlain.length);
        expect(fromPacked[0].primitives.length).toBe(fromPlain[0].primitives.length);
        expect(Array.from(fromPacked[0].primitives[0].affineTransform))
            .toEqual(Array.from(fromPlain[0].primitives[0].affineTransform));
    });

    test('a decompressed frame is cached, so the second read is synchronous', async () => {
        appstate.history = [{ t: 0, s: await deflateBytes(raw), plain: false }];
        expect(sceneAtSync(0)).toBeUndefined();
        const first = await sceneAt(0);
        //Now in the decoded LRU, so the playback machine never awaits it again.
        expect(sceneAtSync(0)).toBe(first);
    });

    test('an empty history is empty on both paths', async () => {
        appstate.history = [];
        expect(sceneAtSync(0)).toEqual([]);
        expect(await sceneAt(0)).toEqual([]);
    });
});
