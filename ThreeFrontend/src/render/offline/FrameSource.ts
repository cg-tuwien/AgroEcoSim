//Maps simulation history onto output video frames, and decodes scenes on demand.
//Renderer-neutral; no three.

import { FrameMap, TimingMode } from './types';

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
export function mapSimTime(
    history: HistoryEntry[], from: number, to: number,
    fps: number, hoursPerSecond: number, hoursPerTick: number,
): FrameMap {
    const last = Math.min(to, history.length - 1);
    const first = Math.max(0, from);
    if (last < first) return [];

    //`t` is only trustworthy if it increases. The preview handler pushes to history
    //unconditionally while the result handler guards on t, so duplicates and regressions are
    //both possible; a non-monotonic series would produce a stuttering, arbitrary mapping, so
    //fall back rather than emit something subtly wrong.
    if (!isMonotonic(history, first, last))
        return mapUniform(history, first, last, 1, hoursPerTick);

    const startHours = hoursPerTick * history[first].t;
    const endHours = hoursPerTick * history[last].t;
    const durationSeconds = Math.max(1 / fps, (endHours - startHours) / Math.max(1e-9, hoursPerSecond));
    //The +1 makes the range inclusive of its end. Without it the final frame lands just
    //short of endHours and the last history entry never appears - which would silently drop
    //the mature plant, i.e. the whole point of the run, from the end of every video.
    const frameCount = Math.max(1, Math.round(durationSeconds * fps) + 1);

    const map: FrameMap = [];
    let cursor = first;
    for (let f = 0; f < frameCount; ++f) {
        const wantHours = startHours + (f / fps) * hoursPerSecond;
        //History is sorted, so a forward-only scan is enough.
        while (cursor + 1 <= last && hoursPerTick * history[cursor + 1].t <= wantHours) cursor++;
        map.push({ historyIndex: cursor, simHours: wantHours });
    }
    return map;
}

export function isMonotonic(history: HistoryEntry[], from: number, to: number): boolean {
    for (let i = from + 1; i <= to; ++i)
        if (history[i].t <= history[i - 1].t) return false;
    return true;
}

export function buildFrameMap(
    history: HistoryEntry[],
    opts: { mode: TimingMode; from: number; to: number; step: number; fps: number; hoursPerSecond: number; hoursPerTick: number },
): FrameMap {
    if (opts.mode === 'noon') return mapNoon(history, opts.from, opts.to, opts.hoursPerTick);
    return opts.mode === 'simTime'
        ? mapSimTime(history, opts.from, opts.to, opts.fps, opts.hoursPerSecond, opts.hoursPerTick)
        : mapUniform(history, opts.from, opts.to, opts.step, opts.hoursPerTick);
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
export function mapNoon(history: HistoryEntry[], from: number, to: number, hoursPerTick: number): FrameMap {
    const last = Math.min(to, history.length - 1);
    const start = Math.max(0, from);
    if (last < start) return [];

    //Best candidate per day, keyed by day number.
    const best = new Map<number, { index: number; distance: number; simHours: number }>();
    for (let i = start; i <= last; ++i) {
        const simHours = hoursPerTick * history[i].t;
        const day = Math.floor(simHours / 24);
        const distance = Math.abs((simHours - day * 24) - 12);
        const current = best.get(day);
        //Ties go to the earlier step, so the choice does not depend on iteration order.
        if (!current || distance < current.distance) best.set(day, { index: i, distance, simHours });
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
