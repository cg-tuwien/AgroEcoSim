//Maps simulation history onto output video frames, and decodes scenes on demand.
//Renderer-neutral; no three.

import { FrameMap, FrameMapEntry, TimingMode } from './types';
import { simulationClock, DEFAULT_TIME_ZONE, DEFAULT_INITIAL_DATE } from '../../helpers/Sun';

export type HistoryEntry = { t: number; s: unknown };

/** One output frame per history entry in the range. */
export function mapUniform(history: HistoryEntry[], from: number, to: number, step: number, hoursPerTick: number): FrameMap {
    const map: FrameMap = [];
    const last = Math.min(to, history.length - 1);
    for (let i = Math.max(0, from); i <= last; i += Math.max(1, step))
        map.push({ historyIndex: i, simHours: hoursPerTick * history[i].t });
    return map;
}

/**
 * Maps simulated hours onto real seconds, holding the most recent frame when the simulation
 * has nothing new. Use when the gaps between stored timesteps are uneven and `uniform` would
 * therefore play at a lurching pace.
 */
export class FramePlan implements Iterable<FrameMapEntry> {
    readonly length: number;
    private readonly read: (index: number) => FrameMapEntry;
    constructor(length: number, read: (index: number) => FrameMapEntry) { this.length = length; this.read = read; }
    at(index: number): FrameMapEntry {
        if (!Number.isSafeInteger(index) || index < 0 || index >= this.length) throw new RangeError('Frame index out of range.');
        return this.read(index);
    }
    *[Symbol.iterator](): Iterator<FrameMapEntry> { for (let i = 0; i < this.length; ++i) yield this.at(i); }
}
const planFrom = (entries: FrameMap) => new FramePlan(entries.length, i => entries[i]);

function validRange(from: number, to: number, hoursPerTick: number): void {
    if (!Number.isSafeInteger(from) || !Number.isSafeInteger(to) || !Number.isFinite(hoursPerTick) || hoursPerTick <= 0)
        throw new Error('Frame range and timestep must be finite positive values.');
}

export function mapSimTime(
    history: HistoryEntry[], from: number, to: number,
    fps: number, hoursPerSecond: number, hoursPerTick: number,
): FramePlan {
    validRange(from, to, hoursPerTick);
    if (!Number.isFinite(fps) || fps <= 0 || !Number.isFinite(hoursPerSecond) || hoursPerSecond <= 0)
        throw new Error('Frame rate and simulated hours per second must be finite and greater than zero.');
    const last = Math.min(to, history.length - 1), first = Math.max(0, from);
    if (last < first) return planFrom([]);
    for (let i = first; i <= last; ++i)
        if (!Number.isFinite(history[i].t * hoursPerTick)) throw new Error('Invalid history timestamp.');
    if (!isMonotonic(history, first, last)) return planFrom(mapUniform(history, first, last, 1, hoursPerTick));
    const startHours = hoursPerTick * history[first].t, endHours = hoursPerTick * history[last].t;
    const count = Math.ceil((endHours - startHours) / hoursPerSecond * fps) + 1;
    if (!Number.isSafeInteger(count) || count < 1) throw new Error('The requested animation has too many frames.');
    return new FramePlan(count, f => {
        const wantHours = f === count - 1 ? endHours : Math.min(endHours, startHours + f / fps * hoursPerSecond);
        // Indexed access also supports prefetch and out-of-order inspection without mutable state.
        let low = first, high = last;
        while (low < high) {
            const mid = Math.ceil((low + high) / 2);
            if (hoursPerTick * history[mid].t <= wantHours) low = mid;
            else high = mid - 1;
        }
        return { historyIndex: low, simHours: wantHours };
    });
}

export function isMonotonic(history: HistoryEntry[], from: number, to: number): boolean {
    for (let i = from + 1; i <= to; ++i)
        if (history[i].t <= history[i - 1].t) return false;
    return true;
}

export function buildFrameMap(
    history: HistoryEntry[],
    opts: { mode: TimingMode; from: number; to: number; step: number; fps: number; hoursPerSecond: number; hoursPerTick: number; initialDate?: string; timeZone?: string },
): FramePlan {
    validRange(opts.from, opts.to, opts.hoursPerTick);
    if (!Number.isFinite(opts.fps) || opts.fps <= 0 || !Number.isSafeInteger(opts.step) || opts.step < 1)
        throw new Error('Frame rate and frame stride must be positive.');
    if (opts.mode === 'noon') return planFrom(mapNoon(history, opts.from, opts.to, opts.hoursPerTick, opts.initialDate, opts.timeZone));
    return opts.mode === 'simTime'
        ? mapSimTime(history, opts.from, opts.to, opts.fps, opts.hoursPerSecond, opts.hoursPerTick)
        : planFrom(mapUniform(history, opts.from, opts.to, opts.step, opts.hoursPerTick));
}

/**
 * One frame per simulated day: whichever timestep falls closest to local noon.
 *
 * A growth animation stepped every few hours flickers between day and night, and the plant's
 * shape is buried under the lighting changing faster than the plant does. Sampling the same
 * time each day holds the sun still so that what moves is the growth - which is the thing the
 * animation is of.
 *
 * "Closest to noon" rather than "the first step after noon": with an hours-per-tick that does
 * not divide 24, the offset from noon drifts through the run, and taking the nearest keeps it
 * bounded at half a tick instead of letting it wander a whole one.
 */
export function mapNoon(history: HistoryEntry[], from: number, to: number, hoursPerTick: number,
                        initialDate = DEFAULT_INITIAL_DATE, timeZone = DEFAULT_TIME_ZONE): FrameMap {
    validRange(from, to, hoursPerTick);
    const clock = simulationClock(initialDate, timeZone);
    const last = Math.min(to, history.length - 1);
    const start = Math.max(0, from);
    if (last < start) return [];

    //Best candidate per day, keyed by day number.
    const best = new Map<number, { index: number; distance: number; simHours: number }>();
    for (let i = start; i <= last; ++i) {
        const simHours = hoursPerTick * history[i].t;
        if (!Number.isFinite(simHours)) throw new Error('Invalid history timestamp.');
        const local = clock(simHours);
        const day = Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate());
        const distance = Math.abs((local.getTime() - day) / 3600_000 - 12);
        const current = best.get(day);
        //Ties go to the earlier step, so the choice does not depend on iteration order.
        if (!current || distance < current.distance || (distance === current.distance && simHours < current.simHours)) best.set(day, { index: i, distance, simHours });
    }

    return Array.from(best.keys()).sort((a, b) => a - b)
        .map(day => { const b = best.get(day)!; return { historyIndex: b.index, simHours: b.simHours }; });
}

/**
 * Decodes frames on demand, remembering only the most recent one.
 *
 * Decoding is deliberately not prefetched. A decoded frame is thousands of objects each
 * holding typed arrays, and a mature field's history runs to tens of megabytes of base64;
 * buffering even a few frames ahead risks hundreds of megabytes while saving nothing, since
 * decode (~5-30 ms) is far cheaper than the 4K render and readback it feeds.
 *
 * The single-entry memo does matter though: under `simTime`, consecutive output frames often
 * resolve to the same history entry, and `reused` lets the caller skip both the decode and
 * the render entirely.
 */
export class FrameSource<T> {
    private lastIndex = -1;
    private lastScene: T | undefined;
    private decode: (index: number) => T | Promise<T>;

    constructor(decode: (index: number) => T | Promise<T>) {
        this.decode = decode;
    }

    /**
     * Awaited because a frame outside the hot window has to be decompressed first. The await is
     * deliberately here and not around the `appstate.scene` write that follows it: that write
     * runs the rebuild effects synchronously, and the export loop depends on the renderer being
     * up to date by the time it calls renderToPixels.
     */
    async at(historyIndex: number): Promise<{ scene: T; reused: boolean }> {
        if (historyIndex === this.lastIndex && this.lastScene !== undefined)
            return { scene: this.lastScene, reused: true };

        this.lastScene = await this.decode(historyIndex);
        this.lastIndex = historyIndex;
        return { scene: this.lastScene, reused: false };
    }

    reset() {
        this.lastIndex = -1;
        this.lastScene = undefined;
    }
}
