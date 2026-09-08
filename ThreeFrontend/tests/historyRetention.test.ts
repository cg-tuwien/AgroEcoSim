//History retention: the shape held in memory, the format written to disk, and the ceiling.
//
//A 4,134-plant green-facade run killed the tab around simulated month 2. The cause was not a
//leak but an absence of any ceiling: `history` accumulated one entry per timestep and was
//emptied only when a new run started. These tests pin the three things that changed.

const fs = require('fs');
const SESSION = '/home/ubuntu/agro-fixture/session.json';

describe('history is held as bytes, not base64', () => {
    const { arrayBufferToBase64 } = require('../src/appstate');

    test('base64 round-trips exactly, across the chunk boundary', () => {
        //The encoder walks the buffer in 8192-byte chunks because String.fromCharCode.apply
        //throws RangeError past the argument limit. Chunking is precisely the part that is easy
        //to get wrong, so the sizes here straddle several multiples of it, and 3-byte alignment
        //matters too since base64 pads every 3 bytes.
        for (const n of [0, 1, 2, 3, 8191, 8192, 8193, 24576, 24577, 100000]) {
            const bytes = new Uint8Array(n);
            for (let i = 0; i < n; ++i) bytes[i] = (i * 7 + (i >> 5)) & 0xff;

            const b64 = arrayBufferToBase64(bytes);
            const back = Buffer.from(b64, 'base64');
            expect(back.length).toBe(n);
            expect(Buffer.compare(back, Buffer.from(bytes))).toBe(0);
        }
    });

    test('every byte value survives, including 0 and 255', () => {
        const bytes = new Uint8Array(256);
        for (let i = 0; i < 256; ++i) bytes[i] = i;
        const back = Buffer.from(arrayBufferToBase64(bytes), 'base64');
        expect(Buffer.compare(back, Buffer.from(bytes))).toBe(0);
    });

    test('a real scene round-trips', () => {
        const session = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
        const saved = session.history.reduce((a: any, b: any) => (b.s.length > a.s.length ? b : a));
        const bytes = new Uint8Array(Buffer.from(saved.s, 'base64'));
        expect(arrayBufferToBase64(bytes)).toBe(saved.s);
    });
});

describe('saved sessions rehydrate to bytes', () => {
    const { decodeSavedHistory } = require('../src/appstate');

    test('base64 entries become Uint8Array', () => {
        const out = decodeSavedHistory([{ t: 0, s: Buffer.from([1, 2, 3]).toString('base64') }]);
        expect(out).toHaveLength(1);
        expect(out[0].t).toBe(0);
        expect(out[0].s).toBeInstanceOf(Uint8Array);
        expect(Array.from(out[0].s)).toEqual([1, 2, 3]);
    });

    test('entries that are already bytes pass through untouched', () => {
        //Guards the in-memory path being fed back through load() without a re-encode.
        const bytes = new Uint8Array([9, 8, 7]);
        const out = decodeSavedHistory([{ t: 4, s: bytes }]);
        expect(out[0].s).toBe(bytes);
    });

    test('a missing or malformed history loads as empty rather than throwing', () => {
        expect(decodeSavedHistory(undefined)).toEqual([]);
        expect(decodeSavedHistory(null)).toEqual([]);
        expect(decodeSavedHistory({} as any)).toEqual([]);
    });

    test('the source array is released as it is consumed', () => {
        //A large session would otherwise hold both representations at once, which is the very
        //thing that makes loading one of these files fail.
        const saved: any[] = [{ t: 0, s: 'AAAA' }, { t: 1, s: 'AAAA' }];
        decodeSavedHistory(saved);
        expect(saved[0]).toBeUndefined();
        expect(saved[1]).toBeUndefined();
    });
});

describe('the memory ceiling stops the run instead of the tab', () => {
    let appstate: any;

    beforeEach(() => {
        jest.resetModules();
        appstate = require('../src/appstate').default;
    });

    test('a budget is published for the HUD to measure against', () => {
        expect(appstate.historyBudget.value).toBeGreaterThan(0);
        //jsdom has no performance.memory, so the fallback must be what is used.
        expect(appstate.historyBudget.value).toBe(1_200_000_000);
    });

    test('stopForMemory latches, so the abort is sent once', () => {
        const warn = jest.spyOn(console, 'warn').mockImplementation(() => { /* silenced */ });
        try {
            expect(appstate.historyLimitHit.value).toBe(false);
            appstate.stopForMemory();
            appstate.stopForMemory();
            appstate.stopForMemory();
            expect(appstate.historyLimitHit.value).toBe(true);
            //Once, not once per remaining frame.
            expect(warn).toHaveBeenCalledTimes(1);
        } finally { warn.mockRestore(); }
    });

    test('the warning says the computed frames survive', () => {
        const warn = jest.spyOn(console, 'warn').mockImplementation(() => { /* silenced */ });
        try {
            appstate.stopForMemory();
            const text = String(warn.mock.calls[0][0]);
            expect(text).toMatch(/stopped/i);
            //The whole point of aborting rather than crashing.
            expect(text).toMatch(/saved|exported/i);
        } finally { warn.mockRestore(); }
    });
});

//Eviction is what actually removes the ceiling: once the server holds the frames, the client
//keeps `t` and lets the payloads go.
describe('payload eviction, once a run store is behind it', () => {
    let appstate: any, sceneAtSync: any, sceneAt: any, realFetch: any;

    beforeEach(() => {
        jest.resetModules();
        //appstate fetches the species list at module load, so any stand-in for fetch has to be
        //a plausible Response - a bare {ok, arrayBuffer} takes the module down on import.
        realFetch = (globalThis as any).fetch;
        (globalThis as any).fetch = jest.fn(async () => ({
            ok: true, json: async () => [], text: async () => '',
            arrayBuffer: async () => new ArrayBuffer(0),
        }));

        const mod = require('../src/appstate');
        appstate = mod.default;
        sceneAtSync = mod.sceneAtSync;
        sceneAt = mod.sceneAt;
    });

    afterEach(() => { (globalThis as any).fetch = realFetch; });

    test('without a run id the run is stopped rather than data thrown away', () => {
        //Nothing on the server means an evicted frame is gone for good, so stopping is the only
        //honest option - and it leaves a session that can still be saved.
        const warn = jest.spyOn(console, 'warn').mockImplementation(() => { /* silenced */ });
        try {
            expect(appstate.runId.value).toBe('');
            appstate.stopForMemory();
            expect(appstate.historyLimitHit.value).toBe(true);
        } finally { warn.mockRestore(); }
    });

    test('an evicted frame declines the synchronous path', async () => {
        appstate.runId.value = 'abc';
        appstate.history = [{ t: 0, s: undefined, plain: true }];
        //undefined, not [] - it has to go and fetch, not render an empty scene.
        expect(sceneAtSync(0)).toBeUndefined();
    });

    test('an evicted frame is fetched back from the run store', async () => {
        const session = JSON.parse(require('fs').readFileSync(SESSION, 'utf8'));
        const saved = session.history.reduce((a: any, b: any) => (b.s.length > a.s.length ? b : a));
        const raw = new Uint8Array(Buffer.from(saved.s, 'base64'));

        appstate.runId.value = 'run123';
        appstate.history = [{ t: 7, s: undefined, plain: true }];

        const calls: string[] = [];
        (globalThis as any).fetch = jest.fn(async (url: string) => {
            calls.push(url);
            return { ok: true, json: async () => [],
                     arrayBuffer: async () => raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength) };
        });

        const scene = await sceneAt(0);
        expect(scene.length).toBeGreaterThan(0);
        //Addressed by `t`, the server's step number - NOT by the client's array index, which
        //differs whenever the stream dropped a preview.
        expect(calls[0]).toContain('/run/run123/frame/7');
    });

    test('a fetch failure yields an empty scene rather than throwing into the render loop', async () => {
        appstate.runId.value = 'run123';
        appstate.history = [{ t: 0, s: undefined, plain: true }];
        (globalThis as any).fetch = jest.fn(async () => ({ ok: false, json: async () => [] }));
        await expect(sceneAt(0)).resolves.toEqual([]);
    });

    test('a frame with no run id and no payload does not attempt a fetch', async () => {
        appstate.runId.value = '';
        appstate.history = [{ t: 0, s: undefined, plain: true }];
        const spy = jest.fn(async () => ({ ok: true, json: async () => [], arrayBuffer: async () => new ArrayBuffer(0) }));
        (globalThis as any).fetch = spy;
        await expect(sceneAt(0)).resolves.toEqual([]);
        expect(spy).not.toHaveBeenCalled();
    });
});

//The regression that mattered: eviction was a fixed window of 64 frames while the budget was in
//bytes, so with frames larger than budget/64 it dropped nothing at all - and because a runId was
//present, the stopForMemory fallback was skipped too. The tab died with both safety nets idle.
describe('eviction copes with frames larger than budget/window', () => {
    let appstate: any, evictOldestPayloads: any, realFetch: any, warn: any;

    beforeEach(() => {
        jest.resetModules();
        realFetch = (globalThis as any).fetch;
        (globalThis as any).fetch = jest.fn(async () => ({
            ok: true, json: async () => [], arrayBuffer: async () => new ArrayBuffer(0) }));
        const mod = require('../src/appstate');
        appstate = mod.default;
        evictOldestPayloads = mod.evictOldestPayloads;
        warn = jest.spyOn(console, 'warn').mockImplementation(() => { /* silenced */ });
    });
    afterEach(() => { (globalThis as any).fetch = realFetch; warn.mockRestore(); });

    /** Fills history the way the preview handler does, accounting included. */
    const fill = (frames: number, bytesEach: number) => {
        appstate.history = [];
        let total = 0;
        for (let i = 0; i < frames; ++i) {
            appstate.history.push({ t: i, s: new Uint8Array(bytesEach), plain: true });
            total += bytesEach + 136;
        }
        appstate.historySize.value = total;
        return total;
    };

    test('a dozen huge frames are evicted, not silently ignored', () => {
        appstate.runId.value = 'run1';
        //Twelve frames of 200 KB is 2.4 MB. The budget leaves room for the protected set (the
        //newest frame plus the playhead window) but not for all twelve - so real eviction has to
        //happen. Twelve is also FEWER than the old 64-frame window, which is exactly the shape
        //where the old code found nothing to drop and said nothing.
        appstate.historyBudget.value = 2_000_000;
        appstate.playPointer.value = 0;
        const before = fill(12, 200_000);
        expect(before).toBeGreaterThan(appstate.historyBudget.value);

        evictOldestPayloads();

        expect(appstate.evictedFrames.value).toBeGreaterThan(0);
        expect(appstate.historySize.value).toBeLessThanOrEqual(appstate.historyBudget.value);
        expect(appstate.history.filter((e: any) => !e.s).length).toBeGreaterThan(0);
        //It got there by dropping, not by giving up.
        expect(appstate.historyLimitHit.value).toBe(false);
    });

    test('the newest frame and the playhead window are never dropped', () => {
        appstate.runId.value = 'run1';
        appstate.historyBudget.value = 1000;   //impossible to satisfy, so it drops all it may
        fill(40, 100_000);
        appstate.playPointer.value = 20;
        evictOldestPayloads();

        //The frame on screen and the ones a scrub is about to reach must survive.
        expect(appstate.history[39].s).toBeDefined();
        for (let i = 16; i <= 24; ++i) expect(appstate.history[i].s).toBeDefined();
    });

    test('a history that cannot be shrunk enough stops the run', () => {
        appstate.runId.value = 'run1';
        //Two frames, both protected (newest + playhead), together over budget. Nothing can be
        //dropped, so the old code returned quietly and the tab went on to die.
        appstate.historyBudget.value = 1000;
        fill(2, 100_000);
        appstate.playPointer.value = 0;
        evictOldestPayloads();
        expect(appstate.historyLimitHit.value).toBe(true);
    });

    test('accounting stays consistent and never goes negative', () => {
        appstate.runId.value = 'run1';
        appstate.historyBudget.value = 500_000;
        fill(30, 100_000);
        evictOldestPayloads();

        //Whatever is left must equal the sum of what is still held.
        const held = appstate.history.reduce((n: number, e: any) => n + (e.s ? e.s.byteLength + 136 : 0), 0);
        expect(appstate.historySize.value).toBe(held);
        expect(appstate.historySize.value).toBeGreaterThanOrEqual(0);
    });
});
