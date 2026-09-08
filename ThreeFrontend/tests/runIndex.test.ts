//The run index is the eager half of "keep `t` on the client, stream `s`": eight bytes per stored
//frame instead of the frame itself, which is what makes a reload able to rebuild a 1,440-step
//timeline for about 11 KB.
//
//Two things are pinned here that nothing else pins. The FORMAT, because it is written by C#
//(`RunStore.WriteFrame`) and read by TypeScript, and nothing type-checks across that seam - a
//change to either side would otherwise show up as frames that fetch the wrong step. And the
//TORN-READ behaviour, because the file is appended to while the run computes, so a reader really
//does catch it mid-entry.

import { decodeFrameIndex, measureHistoryBytes } from '../src/appstate';

/** Builds an index exactly as RunStore does: little-endian (step, storedBytes) pairs. */
function buildIndex(entries: [number, number][]): Uint8Array {
    const buf = new Uint8Array(entries.length * 8);
    const view = new DataView(buf.buffer);
    entries.forEach(([step, size], i) => {
        view.setUint32(i * 8, step, true);
        view.setUint32(i * 8 + 4, size, true);
    });
    return buf;
}

describe('run frame index', () => {
    it('decodes the steps a run stored, in order', () => {
        expect(decodeFrameIndex(buildIndex([[0, 443], [1, 512], [2, 6001]]))).toEqual([0, 1, 2]);
    });

    it('reads the step from the FIRST four bytes, not the size', () => {
        //A byte-order or field-order slip is invisible in the 0,1,2 case above because the
        //numbers are small and ascending. Make the size large and unmistakable.
        expect(decodeFrameIndex(buildIndex([[7, 0x11223344]]))).toEqual([7]);
    });

    it('survives a run long enough to matter', () => {
        const entries: [number, number][] = [];
        for (let i = 0; i < 1440; ++i) entries.push([i, 100000 + i]);
        const steps = decodeFrameIndex(buildIndex(entries));
        expect(steps.length).toBe(1440);
        expect(steps[1439]).toBe(1439);
    });

    it('drops a trailing part-entry rather than decoding half a step', () => {
        //The store appends eight bytes per frame while HTTP requests read the same file. A read
        //that lands mid-append returns a length that is not a multiple of eight. Decoding the
        //remainder would produce a plausible-looking wrong step, and fetching THAT returns some
        //other frame which decodes perfectly - a silent wrong answer, the worst kind.
        const full = buildIndex([[0, 443], [1, 512]]);
        for (let torn = 1; torn < 8; ++torn) {
            const partial = full.slice(0, full.byteLength - torn);
            expect(decodeFrameIndex(partial)).toEqual([0]);
        }
    });

    it('is empty for an empty or sub-entry index', () => {
        expect(decodeFrameIndex(new Uint8Array(0))).toEqual([]);
        expect(decodeFrameIndex(new Uint8Array(7))).toEqual([]);
    });

    it('respects the byteOffset of a view into a larger buffer', () => {
        //Uint8Array over a slice of a bigger ArrayBuffer is what fetch + subarray produce. A
        //DataView built without passing byteOffset reads from the wrong place - the same trap
        //BinaryReader already documents.
        const backing = buildIndex([[99, 1], [100, 2]]);
        const shifted = new Uint8Array(backing.buffer, 8, 8);
        expect(decodeFrameIndex(shifted)).toEqual([100]);
    });
});

describe('history byte accounting', () => {
    const entry = (n: number | undefined) => ({ s: n === undefined ? undefined : new Uint8Array(n) });

    it('counts only the payloads actually resident', () => {
        //An evicted frame is on the server and costs this tab nothing but its `t`. Counting it
        //would make the budget unreachable; not counting a resident one makes it unenforceable.
        const bytesOfOne = measureHistoryBytes([entry(1000)]);
        expect(bytesOfOne).toBeGreaterThan(1000);
        expect(measureHistoryBytes([entry(1000), entry(undefined), entry(undefined)])).toBe(bytesOfOne);
    });

    it('is zero for a timeline rebuilt from the index', () => {
        //Every frame present, no payload resident. This is the state a reload lands in, and it is
        //why the transport bar cannot ask `historySize` whether there is anything to play.
        const rebuilt = new Array(600).fill(0).map(() => entry(undefined));
        expect(measureHistoryBytes(rebuilt)).toBe(0);
        expect(rebuilt.length).toBe(600);
    });

    it('adds up across many frames', () => {
        const one = measureHistoryBytes([entry(500)]);
        expect(measureHistoryBytes([entry(500), entry(500), entry(500)])).toBe(3 * one);
    });
});

//The timeline is filled in from the run index WHILE a run computes, so by the time a preview for
//step 57 arrives the history may already carry step 57 with no payload. Appending it a second
//time leaves a duplicate `t` and a non-monotonic history - at which point FrameSource's
//isMonotonic gives up and the whole animation silently falls back to uniform timing. This is the
//seam between the two ways a frame can reach the client, and nothing else covers it.
describe('filing a received frame into a synced timeline', () => {
    let appstate: any, upsert: any, isMonotonic: any;

    beforeEach(() => {
        jest.resetModules();
        const mod = require('../src/appstate');
        appstate = mod.default;
        upsert = mod.upsertHistoryFrame;
        isMonotonic = require('../src/render/offline/FrameSource').isMonotonic;
        appstate.history.length = 0;
        appstate.historySize.value = 0;
    });

    const bytes = (n: number) => new Uint8Array(n);
    const steps = () => appstate.history.map((e: any) => e.t);

    it('fills a payload into a step the index already announced', () => {
        appstate.history.push({ t: 0, s: undefined, plain: true },
                              { t: 1, s: undefined, plain: true },
                              { t: 2, s: undefined, plain: true });
        const at = upsert(1, bytes(500));

        expect(at).toBe(1);
        expect(steps()).toEqual([0, 1, 2]);          // no duplicate step
        expect(appstate.history[1].s.byteLength).toBe(500);
        expect(appstate.historySize.value).toBeGreaterThan(500);
    });

    it('counts the payload once, however often the frame arrives', () => {
        appstate.history.push({ t: 7, s: undefined, plain: true });
        upsert(7, bytes(500));
        const afterFirst = appstate.historySize.value;
        //A reconnect can deliver the same step again. Counting it twice is what makes the memory
        //budget drift away from what is actually held.
        upsert(7, bytes(500));
        expect(appstate.historySize.value).toBe(afterFirst);
        expect(appstate.history.length).toBe(1);
    });

    it('appends a step the timeline has not heard of', () => {
        appstate.history.push({ t: 0, s: bytes(10), plain: true });
        expect(upsert(1, bytes(10))).toBe(1);
        expect(steps()).toEqual([0, 1]);
    });

    it('keeps the history sorted when a frame arrives out of order', () => {
        appstate.history.push({ t: 0, s: bytes(10), plain: true },
                              { t: 5, s: bytes(10), plain: true });
        expect(upsert(3, bytes(10))).toBe(1);
        expect(steps()).toEqual([0, 3, 5]);
    });

    it('leaves the history monotonic, which is what the animation depends on', () => {
        //A run index announcing every step, previews arriving for a scattered few - the real
        //shape of a live run at a scale where the browser cannot keep up.
        for (let t = 0; t < 60; ++t) appstate.history.push({ t, s: undefined, plain: true });
        for (const t of [0, 7, 8, 23, 24, 25, 59]) upsert(t, bytes(100));

        expect(appstate.history.length).toBe(60);
        expect(isMonotonic(appstate.history, 0, appstate.history.length - 1)).toBe(true);
        expect(appstate.history.filter((e: any) => e.s).length).toBe(7);
    });
});
