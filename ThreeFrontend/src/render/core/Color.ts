//Renderer-neutral colour math. Nothing in this file may import three.
//
//Every function here is a deliberate, verified port of three r152's math/Color.js and
//math/ColorManagement.js. The Three renderer and the WebGPU renderer must agree bit-for-bit
//on colour or the parity screenshots diverge, so where three does something surprising
//(the 0.41666 exponent, HSL in linear space, hue lerped without wraparound) this file
//reproduces the surprise rather than the textbook version.

/** Linear-sRGB. Every RGB value that crosses a module boundary in src/render is linear. */
export type RGB = { r: number; g: number; b: number };
export type HSL = { h: number; s: number; l: number };

export const BLACK: RGB = Object.freeze({ r: 0, g: 0, b: 0 });

export function rgb(r: number, g: number, b: number): RGB {
    return { r, g, b };
}

//--- transfer functions -------------------------------------------------------------------
//Ported from ColorManagement.js. Note the 0.41666 exponent: it is NOT 1/2.4 (0.4166666...),
//and the WGSL shader must use the same truncated constant or gradients drift in the 8th bit.

export function srgbToLinear(c: number): number {
    return c < 0.04045 ? c * 0.0773993808 : Math.pow(c * 0.9478672986 + 0.0521327014, 2.4);
}

export function linearToSrgb(c: number): number {
    return c < 0.0031308 ? c * 12.92 : 1.055 * Math.pow(c, 0.41666) - 0.055;
}

export function srgbToLinearRGB(c: RGB): RGB {
    return { r: srgbToLinear(c.r), g: srgbToLinear(c.g), b: srgbToLinear(c.b) };
}

export function linearToSrgbRGB(c: RGB): RGB {
    return { r: linearToSrgb(c.r), g: linearToSrgb(c.g), b: linearToSrgb(c.b) };
}

//--- parsing ------------------------------------------------------------------------------

/**
 * "#rgb" or "#rrggbb" -> linear RGB. Equivalent to `new THREE.Color(hex)` under r152's
 * default ColorManagement, which treats a hex literal as sRGB and converts to the linear
 * working space.
 */
export function fromHex(hex: string): RGB {
    const s = hex.trim().replace(/^#/, '');
    let r: number, g: number, b: number;

    if (s.length === 3) {
        //three expands #abc to #aabbcc
        r = parseInt(s[0] + s[0], 16);
        g = parseInt(s[1] + s[1], 16);
        b = parseInt(s[2] + s[2], 16);
    } else if (s.length === 6) {
        r = parseInt(s.slice(0, 2), 16);
        g = parseInt(s.slice(2, 4), 16);
        b = parseInt(s.slice(4, 6), 16);
    } else {
        console.error(`Color.fromHex: unsupported hex "${hex}"`);
        return { ...BLACK };
    }

    return { r: srgbToLinear(r / 255), g: srgbToLinear(g / 255), b: srgbToLinear(b / 255) };
}

/**
 * Parses a computed CSS colour into linear RGB.
 *
 * getComputedStyle returns "rgb(68, 68, 68)" for `color`, and for the `background`
 * shorthand a longer string that merely STARTS with the colour
 * ("rgb(250, 250, 250) none repeat scroll ..."), which is why the regex is anchored at the
 * start and not at the end - the same reason THREE.Color.setStyle gets away with it.
 */
export function fromCss(value: string): RGB {
    const s = value.trim();

    const fn = /^(rgba?)\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*[\d.]+\s*)?\)/.exec(s);
    if (fn)
        return {
            r: srgbToLinear(Math.min(255, parseInt(fn[2], 10)) / 255),
            g: srgbToLinear(Math.min(255, parseInt(fn[3], 10)) / 255),
            b: srgbToLinear(Math.min(255, parseInt(fn[4], 10)) / 255),
        };

    if (s.startsWith('#')) return fromHex(s.split(/\s/)[0]);

    console.error(`Color.fromCss: could not parse "${value}", falling back to black`);
    return { ...BLACK };
}

/** Linear RGB -> "#rrggbb" (sRGB-encoded). Diagnostics and the glTF/debug paths only. */
export function toHex(c: RGB): string {
    const ch = (v: number) => {
        const n = Math.round(Math.max(0, Math.min(1, linearToSrgb(v))) * 255);
        return n.toString(16).padStart(2, '0');
    };
    return `#${ch(c.r)}${ch(c.g)}${ch(c.b)}`;
}

//--- HSL ----------------------------------------------------------------------------------
//three's getHSL/setHSL default to the WORKING colour space, which is Linear-sRGB, so
//fromWorkingColorSpace/toWorkingColorSpace are both no-ops and the HSL conversion runs on
//LINEAR values. That is unintuitive but it is what lerpHSL does throughout helpers/Plant.ts,
//so reproducing it is mandatory.

function euclideanModulo(n: number, m: number): number {
    return ((n % m) + m) % m;
}

function clamp(v: number, lo: number, hi: number): number {
    return Math.max(lo, Math.min(hi, v));
}

function hue2rgb(p: number, q: number, t: number): number {
    if (t < 0) t += 1;
    if (t > 1) t -= 1;
    if (t < 1 / 6) return p + (q - p) * 6 * t;
    if (t < 1 / 2) return q;
    if (t < 2 / 3) return p + (q - p) * 6 * (2 / 3 - t);
    return p;
}

export function rgbToHsl(c: RGB): HSL {
    const { r, g, b } = c;
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);

    let hue = 0, saturation = 0;
    const lightness = (min + max) / 2.0;

    if (min !== max) {
        const delta = max - min;
        saturation = lightness <= 0.5 ? delta / (max + min) : delta / (2 - max - min);

        switch (max) {
            case r: hue = (g - b) / delta + (g < b ? 6 : 0); break;
            case g: hue = (b - r) / delta + 2; break;
            case b: hue = (r - g) / delta + 4; break;
        }
        hue /= 6;
    }

    return { h: hue, s: saturation, l: lightness };
}

export function hslToRgb(h: number, s: number, l: number): RGB {
    h = euclideanModulo(h, 1);
    s = clamp(s, 0, 1);
    l = clamp(l, 0, 1);

    if (s === 0) return { r: l, g: l, b: l };

    const p = l <= 0.5 ? l * (1 + s) : l + s - l * s;
    const q = 2 * l - p;

    //three names the hue2rgb parameters (q, p, t) at the call site and (p, q, t) in the
    //function, i.e. they are swapped. Keep the call shape identical rather than "fixing" it.
    return {
        r: hue2rgb(q, p, h + 1 / 3),
        g: hue2rgb(q, p, h),
        b: hue2rgb(q, p, h - 1 / 3),
    };
}

//--- interpolation ------------------------------------------------------------------------

export function lerp(a: RGB, b: RGB, t: number): RGB {
    return {
        r: a.r + (b.r - a.r) * t,
        g: a.g + (b.g - a.g) * t,
        b: a.b + (b.b - a.b) * t,
    };
}

/**
 * three's Color.lerpHSL.
 *
 * Hue is interpolated with a plain lerp, NOT along the shortest arc, so blending across the
 * 1.0/0.0 seam sweeps the long way round the wheel. That is visible in the wood ramps and is
 * relied upon by the current look, so it is preserved deliberately.
 */
export function lerpHSL(a: RGB, b: RGB, t: number): RGB {
    const ha = rgbToHsl(a);
    const hb = rgbToHsl(b);
    return hslToRgb(
        ha.h + (hb.h - ha.h) * t,
        ha.s + (hb.s - ha.s) * t,
        ha.l + (hb.l - ha.l) * t,
    );
}
