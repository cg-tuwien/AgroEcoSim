//Types for the offline animation renderer. Renderer-neutral; no three.

import type { CameraPose, PixelReadback } from '../IViewportRenderer';

export type { CameraPose, PixelReadback };

export type OfflineQuality = {
    /** Output width in pixels, independent of the on-screen canvas and devicePixelRatio. */
    width: number;
    height: number;
    /** Supersampling factor: render at width*ssaa x height*ssaa, then box-average down. */
    ssaa: number;
    /** Multisampling. Mutually exclusive with ssaa > 1 by default - see the VRAM note. */
    msaa: 1 | 4;
};

/**
 * How simulation timesteps map onto video frames.
 *
 * `uniform` emits one frame per history entry; `simTime` maps simulated hours onto real
 * seconds so a run with irregular timestep gaps still plays at an even pace.
 */
export type TimingMode = 'uniform' | 'simTime' | 'noon';

export type FrameMapEntry = { historyIndex: number; simHours: number };
export type FrameMap = FrameMapEntry[];

export type OfflineProgress =
    | { state: 'idle' }
    | { state: 'running'; done: number; total: number; msPerFrame: number; phase: 'render' | 'encode' | 'mux' }
    | { state: 'done'; bytes: number }
    | { state: 'error'; message: string }
    | { state: 'cancelled' };

/** Estimated VRAM for the offscreen targets, in bytes. Colour + depth, times sample count. */
export function estimateVram(q: OfflineQuality): number {
    const w = q.width * q.ssaa;
    const h = q.height * q.ssaa;
    const bytesPerSample = 4 /* rgba8 */ + 3 /* depth24 */;
    return w * h * bytesPerSample * q.msaa;
}

export class OfflineSizeError extends Error {
    constructor(w: number, h: number, max: number) {
        super(`Offscreen target ${w}x${h} exceeds this GPU's maximum texture size of ${max}. `
            + `Reduce the resolution or the supersampling factor.`);
        this.name = 'OfflineSizeError';
    }
}
