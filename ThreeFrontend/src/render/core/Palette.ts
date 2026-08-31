//The leaf colour palette, made deterministic.
//
//helpers/Plant.ts built this table at module load and then shuffled it with Math.random().
//That meant leaf colours differed on every page reload, so the WebGL and WebGPU renderers
//could never be screenshot-compared, and a saved session never re-rendered the same way
//twice. Seeding the shuffle is a deliberate, user-visible change: the palette itself is
//unchanged, but the ordering is now stable across reloads and across renderers.

import { RGB, fromHex } from './Color';

/** Any constant works; changing it reshuffles which leaf gets which green. */
export const GREEN_PALETTE_SEED = 0x5eed6ee7;

/**
 * Mulberry32 - a small, fast, well-distributed 32-bit PRNG.
 * Chosen over a hand-rolled LCG because it passes gjrand and needs no tuning; the exact
 * algorithm does not matter here, only that it is deterministic and dependency-free.
 */
export function mulberry32(seed: number): () => number {
    let a = seed >>> 0;
    return function () {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/**
 * Fisher-Yates, structured exactly like the shuffle() that used to sit in helpers/Plant.ts
 * so that the only behavioural difference is where the random numbers come from.
 */
export function shuffled<T>(src: T[], rnd: () => number): T[] {
    const array = src.slice();
    let currentIndex = array.length;
    while (currentIndex > 0) {
        const randomIndex = Math.floor(rnd() * currentIndex);
        currentIndex--;
        [array[currentIndex], array[randomIndex]] = [array[randomIndex], array[currentIndex]];
    }
    return array;
}

/**
 * 800 near-greens: green channel 120..159, red channel 0..19, blue fixed at 0.
 * Transcribed from helpers/Plant.ts, including the hex round-trip - the values are authored
 * as sRGB hex and decoded to linear, so going straight to linear floats would shift them.
 */
function buildGreenRamp(): RGB[] {
    const out: RGB[] = [];
    for (let g = 120; g < 160; ++g)
        for (let r = 0; r < 20; ++r)
            out.push(fromHex(`#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}00`));
    return out;
}

export const greenColors: RGB[] = shuffled(buildGreenRamp(), mulberry32(GREEN_PALETTE_SEED));
