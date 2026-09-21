//The sky dome, turned into pixels: one cube map that both backends sample.
//
//SkyDome.ts is the ported physics and is monochrome - the solver only ever needs broadband
//energy, so `skyRadiance` returns a single W/m^2/sr. This file supplies the two things a
//background needs on top of that, kept clearly separate from the port so the physics stays
//auditable: a HUE, and an EXPOSURE that lands the result in 8 bits.
//
//Why the CPU builds the texels rather than each backend evaluating the formula in its own
//shader: writing the CIE model twice, once in GLSL and once in WGSL, is two things to keep in
//step, and this repo treats WebGL/WebGPU parity as a contract. Six CPU-filled faces make the
//drift structurally impossible - both backends read the same bytes, and the only difference
//left is texture filtering.
//
//Renderer-neutral: nothing in this file may import three.

import { RGB, linearToSrgb } from './Color';
import { Vec3 } from './Vec3';
import { VisualMappingOptions } from './PlantColors';
import { DAY_GROUND } from './Lighting';
import { SkyDome, airMass, fastAcos, sampleSkyDome, sampleTickDome, skyRadiance } from './SkyDome';
import { DEFAULT_SOLAR_SITE, SolarSite, snapHoursPerTick, timeOfTimestep } from '../../helpers/Sun';

/**
 * Texels per cube face.
 *
 * The CIE shape is low-frequency and the solar DISC is not in it at all - the beam is carried
 * separately as `directNormal` - so there is no fine detail to resolve. What sets the floor is
 * the horizon: the dome steps from sky to ground radiance at y = 0, and bilinear magnification
 * smears that edge over exactly one texel, so the face size is the thickness of the horizon
 * line. At 128 that is ~0.7 degrees, which reads as an edge; at 64 it is ~1.4 degrees, which
 * starts to read as a bug.
 */
export const SKY_FACE_SIZE = 128;

/** Cube face order, matching both GL and WebGPU: +X, -X, +Y, -Y, +Z, -Z. */
export const SKY_FACE_COUNT = 6;

export type SkyCube = {
    size: number;
    /**
     * Six RGBA8 faces in cube order, already sRGB-ENCODED, stored in three's env-map space
     * (world X mirrored - see skyFaceDirection).
     */
    faces: Uint8Array[];
    /**
     * Bumped on every refill. Snapshot it as a NUMBER; do not hold the cube and compare later.
     *
     * resolveSky returns the SAME SkyCube object every time, with its face buffers rewritten in
     * place - allocating 400 KB per timestep during playback is worse than reusing them. So
     * neither the cube nor its `faces` arrays ever change identity, and a backend that decides
     * whether to re-upload by comparing references will upload once and then go stale forever.
     * This counter is the only signal that anything changed.
     */
    revision: number;
    /** The dome the faces were rendered from. For diagnostics and the visual harness. */
    dome: SkyDome;
};

//--- colour ---------------------------------------------------------------------------------

/**
 * Rayleigh optical depth at the zenith, per channel.
 *
 * 0.0977 at 550 nm at sea level, scaled by the lambda^-4 law to 680 / 550 / 440 nm. This is
 * the one piece of physics the C# has no counterpart for, because a broadband energy solver
 * has no use for colour.
 */
const RAYLEIGH_TAU: RGB = { r: 0.04181, g: 0.0977, b: 0.23853 };

/**
 * How far every tint is pulled back towards white, before cloud is accounted for.
 *
 * ARTISTIC, not physical. So are MAX_SUN_AIR_MASS, REDDENING_AWAY and SKY_ZENITH_TARGET below;
 * each says so at its own declaration, and those four are the whole set of dials in this file
 * that are not derived from something citable. Single-scattering Rayleigh has no Mie term and
 * no multiple scattering, and without this a low sun goes pure red rather than orange.
 */
const AEROSOL_NEUTRAL = 0.1;

/**
 * Air mass at which extra reddening of the beam stops growing. A numerical guard: at the
 * horizon the relative air mass reaches ~38, and exp(-0.24 * 38) is 1e-4, which would take the
 * blue channel to black rather than to orange.
 */
const MAX_SUN_AIR_MASS = 8;

/**
 * How much of the sun-path reddening a direction receives, as a function of its cosine to the
 * sun: none of it is ARTISTIC in form, but the numbers are.
 *
 * Single scattering has one spectral path - the sun's - and applying it uniformly is what
 * makes a naive sky go wrong at dawn: the whole dome loses its blue and the zenith comes out
 * GREEN, which is both unphysical and instantly readable as a bug. The physical reason it is
 * wrong is that a twilight zenith is lit by MULTIPLY-scattered light, which never travelled
 * the sun's long path and has not been reddened by it, while the aureole around the sun is
 * dominated by forward single scattering, which has.
 *
 * So the reddening is weighted towards the sun: full strength looking straight at it, a
 * quarter of it looking away. That keeps sunrise warm where a sunrise is warm - around the sun
 * and along the horizon, where the view path is long as well - and leaves the low-sun zenith
 * blue.
 */
const REDDENING_AWAY = 0.25;

/** Largest index of the beam-extinction table; see SkyCubeBuilder.beamTable. */
const BEAM_TABLE_LAST = 256;

/**
 * exp(CLEAR_D * gamma) from the CIE clear indicatrix, tabulated against the scattering angle.
 *
 * This was the single most expensive thing in a refill: one Math.exp for every texel above the
 * horizon, ~49k of them, and it does not depend on anything that changes between frames. The
 * table is uniform in GAMMA rather than in its cosine on purpose - in the cosine the function's
 * derivative is unbounded at gamma = 0, which is exactly where the sun is.
 *
 * At 4096 entries the step is 7.7e-4 rad, so the worst relative error is ~0.23%, which reaches
 * the indicatrix multiplied by 10 and the output through a Reinhard roll-off: well under one
 * byte. tests/skyTexture.test.ts checks it against the analytic form.
 */
const INDICATRIX_LAST = 4095;
const INDICATRIX_LUT = (() => {
    const lut = new Float32Array(INDICATRIX_LAST + 1);
    for (let i = 0; i <= INDICATRIX_LAST; ++i)
        lut[i] = Math.exp(-3 * (Math.PI * i / INDICATRIX_LAST));
    return lut;
})();

const INDICATRIX_SCALE = INDICATRIX_LAST / Math.PI;

/**
 * The radiance the exposure is anchored to, in W/m^2/sr.
 *
 * MEASURED, not chosen: the zenith radiance of a clear sky at the default site on 21 June at
 * solar noon. tests/skyTexture.test.ts recomputes it from the dome and asserts this constant
 * against it, so a change to the dome maths fails a test instead of silently re-exposing the
 * sky.
 */
export const SKY_REFERENCE_RADIANCE = 60.7;

/**
 * Where that anchor lands on the Reinhard curve. ARTISTIC - this is the brightness dial.
 *
 * It is a separate constant from the anchor on purpose: one is what the physics says, the
 * other is how bright we choose to draw it, and conflating them would make a re-measurement
 * look like a restyling. Kept well below 1 because per-channel Reinhard desaturates as it
 * compresses - anchoring the zenith at 1 washes a deep blue sky out to pale grey-blue, which
 * is the single thing most likely to make this look wrong.
 */
export const SKY_ZENITH_TARGET = 0.3;

export const SKY_EXPOSURE = SKY_ZENITH_TARGET / SKY_REFERENCE_RADIANCE;

/** Rec.709 luminance, used only to strip brightness out of a tint. */
const luma = (r: number, g: number, b: number) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

/**
 * Soil colour for the lower hemisphere, normalised to luminance 1 so it carries hue only.
 *
 * DAY_GROUND is reused rather than the terrain's own #573600: that one has a blue channel of
 * exactly zero, and normalising it to luminance 1 produces a fully saturated orange lower
 * hemisphere. DAY_GROUND is already this codebase's answer to "what colour is the ground, seen
 * by a sky model" - it is the hemisphere light's ground term - so it is the honest constant to
 * reuse, and it keeps the two in step if either is retuned.
 */
const GROUND_TINT = (() => {
    const l = luma(DAY_GROUND.r, DAY_GROUND.g, DAY_GROUND.b);
    return { r: DAY_GROUND.r / l, g: DAY_GROUND.g / l, b: DAY_GROUND.b / l };
})();

/**
 * sRGB encode as a lookup table.
 *
 * Three Math.pow per texel would dominate a six-face refill.
 *
 * Measured against the analytic function over 4096 entries sampled at the bucket CENTRE: worst
 * case 0.84 of a byte, mean 0.25. Half of that worst case is unavoidable - the table stores
 * integers, so it is there before any tabulation error - and the rest is half a table step,
 * which near zero is 0.4 of a byte because the transfer curve's slope is 12.92 there. Indexing
 * by the bucket's LEFT endpoint instead costs a full step and takes the worst case to 1.23, so
 * the rounding is doing real work. The test pins both numbers.
 *
 * Parity is untouched either way: both backends read whatever bytes this produces.
 */
const SRGB_LUT = (() => {
    const lut = new Uint8Array(4096);
    for (let i = 0; i < 4096; ++i)
        lut[i] = Math.round(255 * linearToSrgb(i / 4095));
    return lut;
})();

/**
 * Linear value -> sRGB byte, through the table. Exported so the test can measure the table's
 * own error against the analytic transfer function rather than against another rounded byte.
 *
 * `+ 0.5` is the bucket centre; `| 0` alone floors, which is a full-step one-sided bias.
 */
export const encodeSrgbByte = (v: number) =>
    SRGB_LUT[v <= 0 ? 0 : v >= 1 ? 4095 : (v * 4095 + 0.5) | 0];
const encode = encodeSrgbByte;

/**
 * The linear RGB a direction should show, before sRGB encoding.
 *
 * Exported for the tests; the cube builder inlines the same maths over precomputed tables.
 */
export function skyColor(dome: SkyDome, skyCoverage: number, dir: Vec3, out: RGB): RGB {
    if (dir.y <= 0) {
        const v = dome.groundRadiance * SKY_EXPOSURE;
        const k = v / (1 + v);
        out.r = k * GROUND_TINT.r; out.g = k * GROUND_TINT.g; out.b = k * GROUND_TINT.b;
        return out;
    }

    const radiance = skyRadiance(dome, dir);
    const mView = airMass(Math.asin(Math.min(1, dir.y)) * (180 / Math.PI));
    const cosGamma = dir.x * dome.sunDirection.x + dir.y * dome.sunDirection.y
        + dir.z * dome.sunDirection.z;
    const mBeam = beamAirMass(dome.sunAirMass, cosGamma);
    const neutral = AEROSOL_NEUTRAL + skyCoverage * (1 - AEROSOL_NEUTRAL);

    //Scattered into the eye along the view path, times the reddening of the beam before it
    //scattered. The first factor saturates to 1 in all three channels at large mView, which is
    //why the horizon whitens on its own - physics, not a fudge.
    const ir = (1 - Math.exp(-RAYLEIGH_TAU.r * mView)) * Math.exp(-RAYLEIGH_TAU.r * mBeam);
    const ig = (1 - Math.exp(-RAYLEIGH_TAU.g * mView)) * Math.exp(-RAYLEIGH_TAU.g * mBeam);
    const ib = (1 - Math.exp(-RAYLEIGH_TAU.b * mView)) * Math.exp(-RAYLEIGH_TAU.b * mBeam);

    return exposeInto(radiance, ir, ig, ib, neutral, out);
}

/** The sun-path air mass a direction is reddened by. See REDDENING_AWAY. */
function beamAirMass(sunAirMass: number, cosGamma: number): number {
    const towards = cosGamma > 0 ? cosGamma : 0;
    return Math.min(sunAirMass, MAX_SUN_AIR_MASS)
        * (REDDENING_AWAY + (1 - REDDENING_AWAY) * towards);
}

/**
 * Tint, normalise, expose and roll off.
 *
 * Reinhard is applied PER CHANNEL, not on luminance: the tint alone pushes blue past 1, and
 * per-channel roll-off desaturates the bright aureole around the sun towards white, which is
 * what a bright sky actually does.
 */
function exposeInto(radiance: number, ir: number, ig: number, ib: number,
                    neutral: number, out: RGB): RGB {
    const l = luma(ir, ig, ib);
    let tr = 1, tg = 1, tb = 1;
    if (l > 1e-9) {
        const inv = 1 / l;
        const nr = ir * inv, ng = ig * inv, nb = ib * inv;
        tr = nr + (1 - nr) * neutral;
        tg = ng + (1 - ng) * neutral;
        tb = nb + (1 - nb) * neutral;
    }
    const v = radiance * SKY_EXPOSURE;
    const xr = v * tr, xg = v * tg, xb = v * tb;
    out.r = xr / (1 + xr); out.g = xg / (1 + xg); out.b = xb / (1 + xb);
    return out;
}

//--- cube geometry --------------------------------------------------------------------------

/**
 * The WORLD direction whose radiance belongs in texel (x, y) of cube face `face`.
 *
 * `x` is the column and `y` the row FROM THE TOP; cube faces are never Y-flipped (CubeTexture
 * sets flipY = false in its constructor and WebGLTextures feeds that straight to
 * UNPACK_FLIP_Y_WEBGL).
 *
 * X is negated relative to the GL cube-face table because the cube is stored in three's
 * env-map space: WebGLBackground samples a non-render-target CubeTexture with flipEnvMap = -1,
 * and backgroundCube.glsl.js reads vec3(flipEnvMap * dir.x, dir.yz). The WebGPU sky shader
 * negates x for the same reason, so both backends read the same texel for the same world
 * direction.
 *
 * Getting this wrong mirrors east and west. In a sun-position preview that is a correctness
 * bug, not a cosmetic one: the sun would rise in the west. The faces therefore hold, in cube
 * order, the WEST, EAST, ZENITH, NADIR, SOUTH and NORTH sky - and tests/skyTexture.test.ts
 * says so in those words.
 */
export function skyFaceDirection(face: number, x: number, y: number, size: number): Vec3 {
    const u = 2 * ((x + 0.5) / size) - 1;
    const v = 2 * ((y + 0.5) / size) - 1;

    let dx: number, dy: number, dz: number;
    switch (face) {
        case 0: dx = 1; dy = -v; dz = -u; break;    //+X
        case 1: dx = -1; dy = -v; dz = u; break;    //-X
        case 2: dx = u; dy = 1; dz = v; break;      //+Y
        case 3: dx = u; dy = -1; dz = -v; break;    //-Y
        case 4: dx = u; dy = -v; dz = 1; break;     //+Z
        default: dx = -u; dy = -v; dz = -1; break;  //-Z
    }

    //Into three's env-map space, then normalise.
    const inv = 1 / Math.sqrt(dx * dx + dy * dy + dz * dz);
    return { x: -dx * inv, y: dy * inv, z: dz * inv };
}

//--- the builder ----------------------------------------------------------------------------

/**
 * Fills the six faces, reusing its buffers and its per-texel tables.
 *
 * The tables are the whole performance story. A texel's direction never changes, so the
 * gradation, the overcast shape and the view-path scattering term can all be computed once and
 * reused for every timestep. What is left per refill is one dot product, one fastAcos - whose
 * cost is a sqrt - and table lookups; there is no Math.exp in the loop at all, which is what
 * INDICATRIX_LUT bought. They are allocated lazily so the default - sky off - pays nothing, and
 * so tests that merely import this module stay cheap.
 */
class SkyCubeBuilder {
    private cube: SkyCube | undefined;
    private key = '';

    private dirs: Float32Array | undefined;       //3 per texel
    private gradClear: Float32Array | undefined;  //1 per texel
    private shapeOver: Float32Array | undefined;  //1 per texel
    private scatter: Float32Array | undefined;    //3 per texel, the view-path Rayleigh term
    private beam: Float32Array | undefined;       //3 per cosine-to-sun bucket, rebuilt per fill

    update(when: Date, hoursPerTick: number, site: SolarSite, skyCoverage: number,
           key: string): SkyCube {
        this.ensureTables();
        if (this.cube && this.key === key) return this.cube;

        //sampleTickDome, not sampleSkyDome: the solver never integrates the instant a tick
        //BEGINS, and at a 24-hour tick that instant is local midnight. See its doc comment.
        const dome = sampleTickDome(when, hoursPerTick, site.latitude, site.longitude,
                                    skyCoverage, site.azimuthDeg, site.altitudeM);
        this.fill(dome, skyCoverage);
        this.key = key;
        this.cube!.dome = dome;
        this.cube!.revision++;
        return this.cube!;
    }

    private ensureTables() {
        if (this.dirs) return;
        const size = SKY_FACE_SIZE;
        const texels = SKY_FACE_COUNT * size * size;

        this.dirs = new Float32Array(texels * 3);
        this.gradClear = new Float32Array(texels);
        this.shapeOver = new Float32Array(texels);
        this.scatter = new Float32Array(texels * 3);

        let i = 0;
        for (let face = 0; face < SKY_FACE_COUNT; ++face)
            for (let y = 0; y < size; ++y)
                for (let x = 0; x < size; ++x, ++i) {
                    const d = skyFaceDirection(face, x, y, size);
                    this.dirs[i * 3] = d.x; this.dirs[i * 3 + 1] = d.y; this.dirs[i * 3 + 2] = d.z;

                    if (d.y > 0) {
                        //Duplicated from SkyDome's private shape functions rather than exported
                        //from there: these are the per-direction halves, hoisted out of the
                        //inner loop, and exporting them would invite calling them per frame.
                        this.gradClear[i] = 1 - Math.exp(-0.32 / Math.max(d.y, 1e-4));
                        this.shapeOver[i] = 1 + 4 * Math.exp(-0.7 / Math.max(d.y, 1e-4));
                        const m = airMass(Math.asin(Math.min(1, d.y)) * (180 / Math.PI));
                        this.scatter[i * 3] = 1 - Math.exp(-RAYLEIGH_TAU.r * m);
                        this.scatter[i * 3 + 1] = 1 - Math.exp(-RAYLEIGH_TAU.g * m);
                        this.scatter[i * 3 + 2] = 1 - Math.exp(-RAYLEIGH_TAU.b * m);
                    }
                }

        this.cube = {
            size,
            faces: Array.from({ length: SKY_FACE_COUNT }, () => new Uint8Array(size * size * 4)),
            revision: 0,
            dome: sampleSkyDome(new Date(0)),
        };
    }

    /** Per-channel beam extinction against the cosine to the sun; see the call site. */
    private beamTable(sunAirMass: number): Float32Array {
        const table = this.beam ??= new Float32Array((BEAM_TABLE_LAST + 1) * 3);
        for (let i = 0; i <= BEAM_TABLE_LAST; ++i) {
            const m = beamAirMass(sunAirMass, i / BEAM_TABLE_LAST);
            table[i * 3] = Math.exp(-RAYLEIGH_TAU.r * m);
            table[i * 3 + 1] = Math.exp(-RAYLEIGH_TAU.g * m);
            table[i * 3 + 2] = Math.exp(-RAYLEIGH_TAU.b * m);
        }
        return table;
    }

    private fill(dome: SkyDome, skyCoverage: number) {
        const size = SKY_FACE_SIZE;
        const perFace = size * size;
        const dirs = this.dirs!, gradClear = this.gradClear!, shapeOver = this.shapeOver!;
        const scatter = this.scatter!;
        const { x: sx, y: sy, z: sz } = dome.sunDirection;
        const clearScale = dome.clearScale, overcastScale = dome.overcastScale;
        const neutral = AEROSOL_NEUTRAL + skyCoverage * (1 - AEROSOL_NEUTRAL);

        //Beam extinction now varies across the sky, because the reddening is weighted towards
        //the sun - so it is tabulated against the cosine to the sun rather than hoisted. The
        //function is smooth and monotone in that cosine, so 257 entries is far finer than an
        //8-bit output can show, and it keeps three exp() out of a 98k-texel inner loop.
        const beam = this.beamTable(dome.sunAirMass);

        //The ground half is a single colour; compute it once.
        const gv = dome.groundRadiance * SKY_EXPOSURE;
        const gk = gv / (1 + gv);
        const groundR = encode(gk * GROUND_TINT.r);
        const groundG = encode(gk * GROUND_TINT.g);
        const groundB = encode(gk * GROUND_TINT.b);

        for (let face = 0; face < SKY_FACE_COUNT; ++face) {
            const buf = this.cube!.faces[face];
            const base = face * perFace;
            for (let t = 0; t < perFace; ++t) {
                const i = base + t;
                const o = t * 4;
                const dy = dirs[i * 3 + 1];

                if (dy <= 0) {
                    buf[o] = groundR; buf[o + 1] = groundG; buf[o + 2] = groundB; buf[o + 3] = 255;
                    continue;
                }

                const dx = dirs[i * 3], dz = dirs[i * 3 + 2];
                let cosGamma = dx * sx + dy * sy + dz * sz;
                cosGamma = cosGamma < -1 ? -1 : cosGamma > 1 ? 1 : cosGamma;
                const gamma = fastAcos(cosGamma);
                const indicatrix = 1
                    + 10 * (INDICATRIX_LUT[(gamma * INDICATRIX_SCALE) | 0] - CLEAR_INDICATRIX_OFFSET)
                    + 0.45 * cosGamma * cosGamma;
                const radiance = clearScale * gradClear[i] * indicatrix
                    + overcastScale * shapeOver[i];

                //exposeInto and encode are inlined from here down. They exist as named
                //functions because skyColor is the readable reference the tests check against,
                //but this loop runs ~49k times per refill and the object they pass a colour
                //through is pure overhead at that count.
                const b = (cosGamma > 0 ? (cosGamma * BEAM_TABLE_LAST) | 0 : 0) * 3;
                const ir = scatter[i * 3] * beam[b];
                const ig = scatter[i * 3 + 1] * beam[b + 1];
                const ib = scatter[i * 3 + 2] * beam[b + 2];

                const l = 0.2126 * ir + 0.7152 * ig + 0.0722 * ib;
                let tr = 1, tg = 1, tb = 1;
                if (l > 1e-9) {
                    const inv = 1 / l;
                    const nr = ir * inv, ng = ig * inv, nb = ib * inv;
                    tr = nr + (1 - nr) * neutral;
                    tg = ng + (1 - ng) * neutral;
                    tb = nb + (1 - nb) * neutral;
                }
                const v = radiance * SKY_EXPOSURE;
                const xr = v * tr, xg = v * tg, xb = v * tb;
                const cr = xr / (1 + xr), cg = xg / (1 + xg), cb = xb / (1 + xb);

                buf[o] = SRGB_LUT[cr >= 1 ? 4095 : (cr * 4095 + 0.5) | 0];
                buf[o + 1] = SRGB_LUT[cg >= 1 ? 4095 : (cg * 4095 + 0.5) | 0];
                buf[o + 2] = SRGB_LUT[cb >= 1 ? 4095 : (cb * 4095 + 0.5) | 0];
                buf[o + 3] = 255;
            }
        }
    }
}

//Matches SkyDome's ClearIndicatrixOffset; the indicatrix is inlined in the fill loop.
const CLEAR_INDICATRIX_OFFSET = Math.exp(-3 * Math.PI * 0.5);

const builder = new SkyCubeBuilder();

/**
 * Resolves the sky background for a frame. Mirrors resolveLighting's signature on purpose -
 * the host's two effects are twins.
 *
 * Two rules are enforced here rather than in the UI, so no renderer can bypass them:
 *
 *  - A non-Natural visual mapping returns nothing. Those mappings paint quantitative data into
 *    emissive and the control is greyed out for them; a restored session with the sky switched
 *    on and a data ramp showing must not display a state its own checkbox denies.
 *  - A DARK dome still returns a cube. Night is a real result, and falling back to the
 *    light-grey theme colour after sunset would be the exact opposite of the point.
 */
export function resolveSky(
    enabled: boolean,
    mapping: VisualMappingOptions,
    initialDate: string,
    timestep: number,
    hoursPerTick: number,
    skyCoverage: number,
    site: SolarSite = DEFAULT_SOLAR_SITE,
): SkyCube | undefined {
    if (!enabled || mapping !== VisualMappingOptions.Natural) return undefined;

    const when = timeOfTimestep(initialDate, timestep, hoursPerTick, site.timeZone);
    const coverage = skyCoverage < 0 ? 0 : skyCoverage > 1 ? 1 : skyCoverage;

    //The instant is quantised to two minutes for the cache key only - the dome itself is built
    //from the exact time. The sun moves 0.25 degrees a minute and half a texel is ~0.35
    //degrees at this face size, so the quantisation is below what the texture can show, and it
    //makes scrubbing within an hour and multi-frame exports of one instant free. It does NOT
    //help hour-per-tick playback, where the sun genuinely moves 15 degrees a frame and a
    //refill is the correct answer.
    //hoursPerTick is in the key because it selects WHICH instant inside the tick the dome is
    //built at, not just where the tick starts.
    const key = `${Math.round(when.getTime() / 120000)}|${Math.round(coverage * 1000)}`
        + `|${snapHoursPerTick(hoursPerTick)}`
        + `|${site.latitude}|${site.longitude}|${site.altitudeM}|${site.azimuthDeg}`;

    return builder.update(when, snapHoursPerTick(hoursPerTick), site, coverage, key);
}
