//Sun and sky radiance for one instant, in broadband shortwave units
//(irradiances in W/m^2, radiances in W/m^2/sr).
//
//A port of Agro/Irradiance/SkyDome.cs - the dome CpuLightSolver integrates to get plant
//irradiance. It is here so the viewport can show the sky the simulation is actually using
//rather than a decorative one, in the same spirit as helpers/Sun.ts mirroring the server's
//solar position. Every constant below has a counterpart in the C#; the structure and the
//order are kept line-comparable on purpose, so the two can be diffed when either changes.
//
//The chain, deliberately assembled from small individually citable steps:
//  1. solar position from the NOAA algorithm (helpers/Sun.ts, which mirrors the server's
//     SolarCalculator package),
//  2. clear-sky beam transmittance after Meinel & Meinel (1976), tau_b = 0.7^(m^0.678),
//     with the Kasten & Young (1989) relative optical air mass,
//  3. clear-sky diffuse transmittance after Liu & Jordan, tau_d = 0.271 - 0.294 tau_b,
//  4. cloud attenuation of the global horizontal irradiance after Kasten & Czeplak (1980),
//     GHI = GHI_clear (1 - 0.75 c^3.4), driven by the sky coverage the server simulates,
//  5. the beam/diffuse split of that GHI from the clearness index after Erbs et al. (1982),
//  6. the angular distribution of the diffuse part from the CIE general sky formula, blended
//     between the standard clear (type 12) and standard overcast (type 1) gradation and
//     indicatrix pairs by the same cloud cover.
//
//Only the relative SHAPE comes from the CIE model: it is normalised so that its
//cosine-weighted hemispherical integral reproduces the diffuse horizontal irradiance from
//step 5. The absolute result therefore never depends on the shape constants - they only
//redistribute a known total over the hemisphere. tests/skyDome.test.ts checks exactly that.
//
//Cross-references name the C# MEMBER rather than a line number: the two files are edited
//independently and a line number is wrong the first time either one gains a comment, which is
//worse than no pointer at all because it trains the reader to distrust the whole mechanism.
//
//Renderer-neutral: nothing in this file may import three.
//
//One deliberate difference from the C#: the struct carries `sunAirMass`, which the colour
//model in SkyTexture.ts needs. Recomputing it there would be a second place to get it wrong.
//
//Precision: the C# is float throughout and this is double. The differences land around the
//seventh digit and are invisible once the result is an 8-bit background colour, so no attempt
//is made to emulate float rounding.

import { Vec3 } from './Vec3';
import { LATITUDE, LONGITUDE, directionFrom, solarPosition } from '../../helpers/Sun';

const DEG = Math.PI / 180;

/**
 * Broadband shortwave albedo of the soil. Bare and vegetated ground both sit close to 0.2 in
 * the shortwave; see e.g. Oke, Boundary Layer Climates. Mirrors SkyDome.GroundAlbedo.
 */
export const GROUND_ALBEDO = 0.2;

//CIE general sky, standard clear (type 12): gradation a = -1, b = -0.32;
//indicatrix c = 10, d = -3, e = 0.45.
const CLEAR_B = -0.32;
const CLEAR_C = 10;
const CLEAR_D = -3;
const CLEAR_E = 0.45;
//CIE general sky, standard overcast (type 1): gradation a = 4, b = -0.7; isotropic indicatrix.
const OVERCAST_A = 4;
const OVERCAST_B = -0.7;

const CLEAR_INDICATRIX_OFFSET = Math.exp(CLEAR_D * Math.PI * 0.5);

export type SkyDome = {
    /**
     * Unit vector from the scene towards the sun. Y is up; north is -Z and east is +X, the
     * convention the Mitsuba adapter uses, so shadows keep pointing the same way.
     */
    sunDirection: Vec3;
    /** Beam irradiance on a plane normal to sunDirection, in W/m^2. */
    directNormal: number;
    /** Diffuse irradiance on a horizontal plane, in W/m^2. */
    diffuseHorizontal: number;
    /** Global (beam + diffuse) irradiance on a horizontal plane, in W/m^2. */
    globalHorizontal: number;
    /**
     * Radiance of the ground seen by a ray that leaves the scene downwards, in W/m^2/sr.
     * Used only where no terrain triangle was hit.
     */
    groundRadiance: number;
    //Absolute multipliers of the two normalised CIE shapes; their sum integrates to
    //diffuseHorizontal.
    clearScale: number;
    overcastScale: number;
    /**
     * Kasten & Young relative optical air mass at the sun, or 0 when the sun is down.
     *
     * Not in the C# struct. The Rayleigh tint in SkyTexture.ts needs it to redden a low sun,
     * and deriving it a second time there would be a second place to get it wrong.
     */
    sunAirMass: number;
};

/** True while there is enough light to bother tracing. Mirrors SkyDome.IsLit. */
export function isLit(dome: SkyDome): boolean {
    return dome.globalHorizontal > 0.5;
}

/**
 * Radiance arriving from direction `d` (a unit vector pointing away from the scene).
 *
 * Directions below the horizon return the ground radiance, which is what a ray that escaped
 * without hitting any terrain triangle should see.
 */
export function skyRadiance(dome: SkyDome, d: Vec3): number {
    if (d.y <= 0) return dome.groundRadiance;
    const cosGamma = clamp(d.x * dome.sunDirection.x + d.y * dome.sunDirection.y
        + d.z * dome.sunDirection.z, -1, 1);
    return dome.clearScale * clearShape(d.y, cosGamma) + dome.overcastScale * overcastShape(d.y);
}

/**
 * Irradiance on a plane with unit normal `n`, assuming an unobstructed sky: the classic
 * isotropic (Liu & Jordan) transposition of beam, diffuse and ground-reflected components.
 */
export function planeIrradiance(dome: SkyDome, n: Vec3): number {
    const cosIncidence = n.x * dome.sunDirection.x + n.y * dome.sunDirection.y
        + n.z * dome.sunDirection.z;
    const beam = cosIncidence > 0 ? dome.directNormal * cosIncidence : 0;
    //n is a unit vector, so this is cos of the tilt from horizontal.
    const cosTilt = n.y;
    return beam
        + dome.diffuseHorizontal * 0.5 * (1 + cosTilt)
        + dome.globalHorizontal * GROUND_ALBEDO * 0.5 * (1 - cosTilt);
}

/**
 * Kasten & Young (1989) relative optical air mass, from the solar elevation in DEGREES.
 *
 * Split out of the sampling function because the colour model evaluates it per view ray, and
 * because the station-pressure correction below belongs in exactly ONE place: here, and in its
 * counterpart in the C#, changed together. Applying it a second time anywhere else - in the
 * per-view-ray path in SkyTexture, say - would square the exponential and quietly brighten a
 * high site out of all proportion.
 */
export function airMass(elevationDegrees: number, altitudeM = 0): number {
    const sinElevation = Math.sin(elevationDegrees * DEG);
    const relative = 1 / (sinElevation + 0.50572 * Math.pow(elevationDegrees + 6.07995, -1.6364));
    //Kasten & Young give the RELATIVE air mass, which assumes sea level. The absolute one
    //scales it by the station pressure, and the standard-atmosphere pressure ratio is
    //exp(-altitude / 8435 m). This is what makes a mountain site's beam stronger and its sky
    //deeper; at the default 188 m it is a 2% correction.
    return altitudeM === 0 ? relative : relative * Math.exp(-altitudeM / PRESSURE_SCALE_HEIGHT_M);
}

/** Scale height of the standard atmosphere's pressure profile, in metres. */
export const PRESSURE_SCALE_HEIGHT_M = 8435;

function clearShape(cosZenith: number, cosGamma: number): number {
    const gradation = 1 - Math.exp(CLEAR_B / Math.max(cosZenith, 1e-4));
    const gamma = fastAcos(cosGamma);
    const indicatrix = 1 + CLEAR_C * (Math.exp(CLEAR_D * gamma) - CLEAR_INDICATRIX_OFFSET)
        + CLEAR_E * cosGamma * cosGamma;
    return gradation * indicatrix;
}

function overcastShape(cosZenith: number): number {
    return 1 + OVERCAST_A * Math.exp(OVERCAST_B / Math.max(cosZenith, 1e-4));
}

/**
 * Nvidia's classic polynomial arc cosine.
 *
 * Kept rather than replaced with Math.acos so the port stays term-by-term comparable with
 * SkyDome.FastAcos - and it is one of only two transcendentals in the loop that
 * fills a sky cube, so it earns its place here too.
 *
 * The C# calls it "~1e-5 absolute error", which is the typical figure rather than the bound:
 * measured over the whole domain the worst case is 6.8e-5, at x = 0. That is ~0.004 degrees on
 * the scattering angle and it feeds an indicatrix that varies over tens of degrees, so it is
 * far below anything the sky can show - but the test asserts the measured bound, not the
 * comment's.
 */
export function fastAcos(x: number): number {
    const negate = x < 0;
    const a = Math.abs(x);
    let ret = -0.0187293 * a + 0.0742610;
    ret = ret * a - 0.2121144;
    ret = ret * a + 1.5707288;
    ret *= Math.sqrt(Math.max(0, 1 - a));
    return negate ? Math.PI - ret : ret;
}

/**
 * Van der Corput radical inverse in base 2. Mirrors RaySampling.RadicalInverse2.
 *
 * Every shift is unsigned: JS bit operations work on SIGNED int32, so a plain `>>` would drag
 * the sign bit through the swap and a plain result would come back negative.
 */
export function radicalInverse2(i: number): number {
    let bits = i >>> 0;
    bits = ((bits << 16) | (bits >>> 16)) >>> 0;
    bits = (((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >>> 1)) >>> 0;
    bits = (((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >>> 2)) >>> 0;
    bits = (((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >>> 4)) >>> 0;
    bits = (((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >>> 8)) >>> 0;
    return bits * 2.3283064365386963e-10;
}

/**
 * Fixed cosine-distributed directions over the upper hemisphere, used to normalise the two CIE
 * shapes. A deterministic Hammersley set keeps the normalisation, and hence the whole result,
 * reproducible. Mirrors SkyDome.NormalizationDirections.
 */
const NORMALIZATION_DIRECTIONS = buildNormalizationDirections(128);

function buildNormalizationDirections(count: number): Float64Array {
    const result = new Float64Array(count * 3);
    for (let i = 0; i < count; ++i) {
        const u1 = (i + 0.5) / count;
        const u2 = radicalInverse2(i);
        const r = Math.sqrt(u1);
        const phi = 2 * Math.PI * u2;
        result[i * 3] = r * Math.cos(phi);
        result[i * 3 + 1] = Math.sqrt(Math.max(0, 1 - u1));
        result[i * 3 + 2] = r * Math.sin(phi);
    }
    return result;
}

const DARK: SkyDome = {
    sunDirection: { x: 0, y: 1, z: 0 },
    directNormal: 0, diffuseHorizontal: 0, globalHorizontal: 0, groundRadiance: 0,
    clearScale: 0, overcastScale: 0, sunAirMass: 0,
};

/**
 * Builds the dome for one instant. Mirrors SkyDome.Sample.
 *
 * @param utc Instant in UTC.
 * @param latitude Degrees north.
 * @param longitude Degrees east.
 * @param skyCoverage Cloud cover, 0 = clear .. 1 = fully overcast.
 * @param worldAzimuthDeg Rotation of the scene about the up axis, in degrees: 0 means the
 *   scene's +X (its right vector) points east, and increasing values turn it clockwise seen
 *   from above. Only the sun DIRECTION depends on it - it says how the scene's axes are
 *   oriented on the compass, and rotates nothing in the scene graph. Subtracting it from the
 *   solar azimuth is the whole of the maths; see tests/skyDome.test.ts for the derivation's
 *   sanity check.
 * @param altitudeM Height of the site above sea level, which thins the atmosphere the beam
 *   crosses. Mirrors Agro/AgroWorld.cs Altitude, which the C# dome reads the same way.
 */
export function sampleSkyDome(utc: Date, latitude = LATITUDE, longitude = LONGITUDE,
                              skyCoverage = 0, worldAzimuthDeg = 0, altitudeM = 0): SkyDome {
    const solar = solarPosition(utc, latitude, longitude);
    const sinElevation = Math.sin(solar.altitude * DEG);
    //Below (or level with) the horizon: no beam and no sky brightness worth tracing.
    if (!(sinElevation > 0.001)) return DARK;

    const sunDirection = directionFrom(solar.altitude, solar.azimuth - worldAzimuthDeg);

    //Extraterrestrial normal irradiance including the orbital eccentricity factor.
    const dayAngle = 2 * Math.PI * (dayOfYearUtc(utc) - 1) / 365;
    const extraterrestrial = 1361 * (1 + 0.033 * Math.cos(dayAngle));

    const m = airMass(solar.altitude, altitudeM);

    const beamTransmittance = Math.pow(0.7, Math.pow(m, 0.678));
    const diffuseTransmittance = Math.max(0, 0.271 - 0.294 * beamTransmittance);
    const clearSkyGlobal = extraterrestrial * sinElevation
        * (beamTransmittance + diffuseTransmittance);

    const coverage = clamp(skyCoverage, 0, 1);
    const global = clearSkyGlobal * (1 - 0.75 * Math.pow(coverage, 3.4));
    if (!(global > 0.5)) return { ...DARK, sunDirection, sunAirMass: m };

    //Erbs et al. diffuse fraction from the clearness index.
    const clearness = clamp(global / (extraterrestrial * sinElevation), 0, 1);
    let diffuseFraction: number;
    if (clearness <= 0.22)
        diffuseFraction = 1 - 0.09 * clearness;
    else if (clearness <= 0.8) {
        const k2 = clearness * clearness;
        diffuseFraction = 0.9511 - 0.1604 * clearness + 4.388 * k2
            - 16.638 * k2 * clearness + 12.336 * k2 * k2;
    }
    else
        diffuseFraction = 0.165;
    diffuseFraction = clamp(diffuseFraction, 0.05, 1);

    const diffuseHorizontal = global * diffuseFraction;
    const directNormal = Math.max(0, (global - diffuseHorizontal) / sinElevation);

    //Normalise both CIE shapes so that each integrates to one unit of diffuse horizontal
    //irradiance: integral(shape * cos) dOmega = pi * mean(shape) over cosine-distributed
    //directions.
    const dirs = NORMALIZATION_DIRECTIONS;
    const count = dirs.length / 3;
    let clearSum = 0, overcastSum = 0;
    for (let i = 0; i < count; ++i) {
        const dx = dirs[i * 3], dy = dirs[i * 3 + 1], dz = dirs[i * 3 + 2];
        const cosGamma = clamp(dx * sunDirection.x + dy * sunDirection.y + dz * sunDirection.z,
                               -1, 1);
        clearSum += clearShape(dy, cosGamma);
        overcastSum += overcastShape(dy);
    }
    const clearIntegral = Math.PI * (clearSum / count);
    const overcastIntegral = Math.PI * (overcastSum / count);

    const clearScale = clearIntegral > 1e-6
        ? (1 - coverage) * diffuseHorizontal / clearIntegral : 0;
    const overcastScale = overcastIntegral > 1e-6
        ? coverage * diffuseHorizontal / overcastIntegral : 0;

    return {
        sunDirection,
        directNormal,
        diffuseHorizontal,
        globalHorizontal: global,
        groundRadiance: GROUND_ALBEDO * global / Math.PI,
        clearScale,
        overcastScale,
        sunAirMass: m,
    };
}

/**
 * Representative instantaneous dome for the TIMESTEP preview, not the CPU light integral.
 *
 * The CPU now integrates six solar positions per hour, including the diffuse sky. This
 * preview retains one sun for interactive rendering and must not be used for plant energy.
 * Its historical representative-dome approximation remains useful because the
 * obvious thing is wrong: sampling only the instant a tick BEGINS can show a dark sky. It takes
 * `clamp(HoursPerTick, 1, 6)` domes at the midpoints of equal sub-steps, keeps the geometry of
 * the BRIGHTEST one, and scales its magnitudes so they carry the mean irradiance over the tick.
 *
 * At the default one-hour tick that is half an hour of sun - about 7 degrees - away from the
 * tick boundary. At a 24-hour tick, which is a legal snapped value, the boundary is local
 * midnight while the solver is integrating a daylit dome: previewing the boundary would show a
 * black sky over a step the simulation ran in full sun.
 *
 * @param start The instant the tick begins, from timeOfTimestep.
 * @param hoursPerTick ALREADY SNAPPED - the value the simulation runs with, not the requested
 *   one. See snapHoursPerTick.
 */
export function sampleTickDome(start: Date, hoursPerTick: number,
                               latitude = LATITUDE, longitude = LONGITUDE, skyCoverage = 0,
                               worldAzimuthDeg = 0, altitudeM = 0): SkyDome {
    const subSteps = subStepsOf(hoursPerTick);
    const span = hoursPerTick / subSteps;

    let brightest: SkyDome | undefined;
    let meanGlobal = 0;
    for (let s = 0; s < subSteps; ++s) {
        const dome = sampleSkyDome(subStepInstant(start, s, span), latitude, longitude,
                                   skyCoverage, worldAzimuthDeg, altitudeM);
        meanGlobal += dome.globalHorizontal;
        if (!brightest || dome.globalHorizontal > brightest.globalHorizontal) brightest = dome;
    }
    meanGlobal /= subSteps;

    //Scale the representative preview's brightness to the tick mean. CPU gathering uses
    //the full angular time integral instead. A dark tick is untouched - there is nothing to
    //scale and the ratio would be 0/0.
    return isLit(brightest!) ? scaledDome(brightest!, meanGlobal / brightest!.globalHorizontal)
                             : brightest!;
}

/**
 * The instant whose SUN the tick is represented by - the brightest sub-step's midpoint.
 *
 * Split out for the lighting path, which needs the direction and the altitude but not the
 * irradiances. Cloud cover is deliberately not a parameter: it scales every sub-step of a tick
 * by the same factor, so it cannot move which one is brightest.
 */
export function tickSunInstant(start: Date, hoursPerTick: number,
                               latitude = LATITUDE, longitude = LONGITUDE): Date {
    const subSteps = subStepsOf(hoursPerTick);
    const span = hoursPerTick / subSteps;

    let best = subStepInstant(start, 0, span);
    let bestGlobal = -1;
    for (let s = 0; s < subSteps; ++s) {
        const at = subStepInstant(start, s, span);
        const global = sampleSkyDome(at, latitude, longitude, 0).globalHorizontal;
        if (global > bestGlobal) { bestGlobal = global; best = at; }
    }
    return best;
}

/** Preview-only sampling budget. CPU integration uses six positions per hour. */
function subStepsOf(hoursPerTick: number): number {
    const whole = Math.trunc(hoursPerTick);
    return whole < 1 ? 1 : whole > 6 ? 6 : whole;
}

function subStepInstant(start: Date, s: number, spanHours: number): Date {
    return new Date(start.getTime() + (s + 0.5) * spanHours * 3600_000);
}

/** Mirrors SkyDome.Scaled: every magnitude moves, the geometry does not. */
function scaledDome(dome: SkyDome, factor: number): SkyDome {
    const globalHorizontal = dome.globalHorizontal * factor;
    return {
        ...dome,
        directNormal: dome.directNormal * factor,
        diffuseHorizontal: dome.diffuseHorizontal * factor,
        globalHorizontal,
        //Recomputed rather than scaled, exactly as the C# constructor does it.
        groundRadiance: GROUND_ALBEDO * globalHorizontal / Math.PI,
        clearScale: dome.clearScale * factor,
        overcastScale: dome.overcastScale * factor,
    };
}

/** Matches DateTime.DayOfYear on a UTC DateTime, which is what the server passes. */
function dayOfYearUtc(when: Date): number {
    const start = Date.UTC(when.getUTCFullYear(), 0, 1);
    const day = Date.UTC(when.getUTCFullYear(), when.getUTCMonth(), when.getUTCDate());
    return Math.round((day - start) / 86400000) + 1;
}

function clamp(value: number, lo: number, hi: number): number {
    return value < lo ? lo : value > hi ? hi : value;
}
