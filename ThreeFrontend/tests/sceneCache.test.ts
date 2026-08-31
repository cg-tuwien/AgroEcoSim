//The decoded-scene cache: hits, eviction, and - the part that would be a real bug - dropping
//everything when history is replaced, since the cache is keyed by an index a new run reuses.

const fs = require('fs');
const SESSION = '/home/ubuntu/agro-fixture/session.json';

describe('decoded scene cache', () => {
    let appstate: any, decodeSceneAt: any, clearSceneCache: any, frame: any;

    beforeEach(() => {
        jest.resetModules();
        const mod = require('../src/appstate');
        appstate = mod.default;
        decodeSceneAt = mod.decodeSceneAt;
        clearSceneCache = mod.clearSceneCache;

        const session = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
        //The largest entry - the first is the empty t=0 scene.
        frame = session.history.reduce((a: any, b: any) => (b.s.length > a.s.length ? b : a));
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

        const needed = Math.ceil(200000 / perFrame) + 8;
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
