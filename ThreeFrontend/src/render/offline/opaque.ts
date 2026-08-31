//Enforces the one thing PixelReadback promises about alpha.
//
//Both backends render opaque - the clear value has alpha 1 and every shader writes 1 - so a
//readback should already be opaque. It is enforced anyway because the failure is silent and
//total: a PNG whose RGB is right but whose alpha is zero opens as a blank, transparent image,
//and nothing in the pipeline flags it. There is no use for alpha in these exports, so the
//cheapest correct thing is to guarantee it rather than trust it.
//
//Returns the count of samples it had to correct, so a caller can say whether it fired.
export function forceOpaque(data: Uint8ClampedArray): number {
    let fixed = 0;
    for (let i = 3; i < data.length; i += 4) {
        if (data[i] !== 255) { data[i] = 255; ++fixed; }
    }
    return fixed;
}

/**
 * Says so, once, when a readback arrived non-opaque.
 *
 * Silent correction would hide the very thing worth knowing: an export coming out blank and
 * transparent has a cause, and whether alpha was the cause is the first question to settle.
 */
let reported = false;
export function reportOpaque(fixed: number, samples: number, backend: string) {
    if (fixed === 0 || reported) return;
    reported = true;
    console.warn(`Offline readback (${backend}) arrived with ${fixed} of ${samples} samples ` +
                 'non-opaque and was corrected. The image is fine; please report this.');
}

/**
 * Warns when an exported image is a single flat colour.
 *
 * A blank export has a signature worth reading rather than guessing at: all-zero means the
 * readback never received the render, while the clear colour means the pass ran and drew
 * nothing - a missing bind group or an empty draw list. Those are different bugs with
 * different fixes, and the difference is one line of console output.
 *
 * Sampled, not exhaustive: a 4K frame is 8 million pixels and this runs on the main thread
 * right before the encode.
 */
export function warnIfFlat(data: Uint8ClampedArray, width: number, height: number, backend: string) {
    if (data.length < 16) return;
    const step = Math.max(4, Math.floor(data.length / 4 / 4096) * 4);
    let minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
    for (let i = 0; i < data.length; i += step) {
        const r = data[i], g = data[i + 1], b = data[i + 2];
        if (r < minR) minR = r; if (r > maxR) maxR = r;
        if (g < minG) minG = g; if (g > maxG) maxG = g;
        if (b < minB) minB = b; if (b > maxB) maxB = b;
    }
    if (maxR - minR > 1 || maxG - minG > 1 || maxB - minB > 1) return;

    const what = maxR === 0 && maxG === 0 && maxB === 0
        ? 'all black - the readback never received the render'
        : `the flat colour rgb(${maxR},${maxG},${maxB}) - the pass ran but drew nothing`;
    console.error(`Offline export (${backend}) produced a ${width}x${height} image that is ${what}. ` +
                  'Please report this line.');
}
