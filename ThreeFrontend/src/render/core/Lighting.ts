//The viewport's lighting model. Renderer-neutral; no three.
//
//Tier 0 must remain byte-identical to the historical WebGL look, so everything added here is
//expressed as terms that vanish at Tier 0 rather than as a different shading path.

import { RGB, fromHex } from './Color';
import { Vec3 } from './Vec3';
import { solarPosition, timeOfTimestep } from '../../helpers/Sun';
import { VisualMappingOptions } from './PlantColors';

/**
 * Normal-offset shadow bias, in shadow texels.
 *
 * Leaves are single thin quads: a leaf that shadows itself is comparing a surface against
 * ITSELF in the shadow map, and no depth bias can separate a triangle from itself. Offsetting
 * the lookup along the surface normal is what breaks that, and expressing the offset in
 * TEXELS rather than metres keeps it correct if the map resolution or the field size changes.
 *
 * Kept small: the offset is also how light leaks between genuinely separate leaves, and at
 * ~3.8 mm per texel four texels is about 15 mm, well under a leaf's own size.
 *
 * Four is also what the Three path's previous hard-coded `shadow.bias` of -0.0005 worked out
 * to over this field, so raising the default here does not make WebGL worse than it was.
 */
export const SHADOW_NORMAL_OFFSET_TEXELS = 4;
/** Extra texels at grazing incidence, where one texel covers more of the surface. */
export const SHADOW_GRAZING_OFFSET_TEXELS = 4;

/**
 * What the viewport adds on top of the hemisphere ambient, which is always on.
 *
 * Independent switches rather than a ladder. The ladder was a convenient way to build these
 * up, but the terms are genuinely independent: occlusion darkens the AMBIENT, so it is
 * meaningful with no sun at all, and so is indirect light. Forcing a user to accept shadows to
 * get occlusion described the implementation order, not the lighting.
 *
 * All false is the parity look, byte-identical to the WebGL renderer, and that is still what
 * every term vanishing at once has to produce.
 */
export type LightingOptions = {
    /**
     * Physical daylight instead of the historical flat ambient.
     *
     * The legacy hemisphere runs at intensity 3.175 with a white sky, which makes the ambient
     * term the entire scene: a lit surface gets ~3.2 from the sky and ~1.6 from the sun, so
     * anything that modulates the ambient - occlusion, indirect light - is adjusting a term
     * that already dominates, and the change is invisible. Daylight moves the energy where it
     * physically belongs: a modest sky, and a sun that carries the rest. Exposure on a lit
     * surface is about the same; what changes is that SHADOW is now dark, which is where
     * occlusion and bounce have something to say.
     */
    daylight: boolean;
    /** A directional sun plus its shadow map. */
    shadows: boolean;
    /** Screen-space ambient occlusion, for near-field contact shading. */
    occlusion: boolean;
    /** Voxel cone-traced indirect light: canopy self-shading and colour bleed. */
    indirect: boolean;
    /** A second bounce. Only meaningful together with `indirect`. */
    secondBounce: boolean;
    /**
     * Normal-offset shadow bias, in shadow texels. See SHADOW_NORMAL_OFFSET_TEXELS.
     *
     * Exposed rather than fixed because the right value depends on the canopy: too little and
     * a leaf shadows itself, too much and light leaks between leaves that genuinely touch.
     * Both failures are obvious on screen and neither is predictable from the scene alone.
     */
    shadowBiasTexels: number;
};

export const LIGHTING_OFF: LightingOptions = {
    daylight: false, shadows: false, occlusion: false, indirect: false, secondBounce: false,
    shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS,
};

/**
 * Daylight sky, and the sun that goes with it.
 *
 * Chosen so a fully lit, up-facing surface lands at roughly the same exposure as the legacy
 * model - about 4.8 against 4.775 - while a shadowed one drops from 4.775 to 0.8. That ratio
 * is the whole point: it is what makes a shadow read as a shadow, and what gives occlusion
 * and indirect light room to matter.
 */
export const DAY_SKY = fromHex('#bfd4ff');
export const DAY_GROUND = fromHex('#6b5a44');
export const DAY_SKY_INTENSITY = 0.85;
export const DAY_SUN_INTENSITY = 4.0;

/** Legacy saved sessions stored a single number; this is what those numbers meant. */
export function lightingFromLegacyTier(tier: number): LightingOptions {
    return {
        shadows: tier >= 1,
        occlusion: tier >= 2,
        indirect: tier >= 3,
        secondBounce: tier >= 4,
        //Legacy sessions predate the daylight model and expect the flat ambient.
        daylight: false,
        shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS,
    };
}

/** The hemisphere light the scene has always had (`HemisphereLight(0xffffff, 0x707070, 3.175)`). */
export const HEMI_SKY = fromHex('#ffffff');
export const HEMI_GROUND = fromHex('#707070');
export const HEMI_INTENSITY = 3.175;
/** The light sits at (0, 10000, 0), so its direction is straight up. */
export const HEMI_WORLD_DIR: Vec3 = { x: 0, y: 1, z: 0 };

/**
 * Sun brightness.
 *
 * An artistic choice, not a physical one: the sun is ADDED to the existing hemisphere rather
 * than replacing part of it, so that Tier 0 stays exactly as it was. Kept modest to avoid
 * blowing out lit surfaces, since the two terms sum.
 */
export const SUN_INTENSITY = 1.6;

/**
 * Shadow map resolution, shared by both backends so they blur alike.
 *
 * One cascade at 4096 covers the whole 10x10 m field at roughly 2.5 mm per texel. That the
 * domain is small and known is exactly why no cascade machinery is needed here.
 */
export const SHADOW_SIZE = 4096;


export const SUN_COLOR = fromHex('#fff4e0');

export type FrameLighting = {
    /** Normal-offset shadow bias in texels, passed through from the options. */
    shadowBiasTexels: number;
    /**
     * Hemisphere sky and ground, already multiplied by their intensity.
     *
     * Carried per frame rather than read from constants so the daylight switch can change
     * them. With daylight off these are exactly the historical values, which is what keeps
     * the default byte-identical to the WebGL renderer.
     */
    hemiSky: RGB;
    hemiGround: RGB;
    /** Whether to run the ambient-occlusion passes. */
    ambientOcclusion: boolean;
    /**
     * Whether to build the density field and cone-trace it.
     *
     * The cone trace multiplies the SAME hemisphere term Tier 0 uses by a traced visibility
     * and adds bounced radiance, rather than replacing the ambient with its own estimate.
     * That is what makes the invariant exact: with an empty grid nothing is occluded and
     * nothing bounces, so visibility is 1, bounce is 0, and the result is byte-identical to
     * the tier below.
     */
    voxelGI: boolean;
    /**
     * How many bounces the field carries: 1 or 2.
     *
     * The second is strictly additive - a cell can only gain radiance from it - so it can
     * never darken anything, and turning it off can only ever return to the tier below.
     */
    bounces: number;
    /** Direction TO the sun, world space, normalised. Zero-length when the sun is down. */
    sunDirection: Vec3;
    /** Already multiplied by intensity. Black when the tier or the sun altitude says so. */
    sunColor: RGB;
    shadows: boolean;
};

const BLACK: RGB = { r: 0, g: 0, b: 0 };

const scaled = (c: RGB, k: number): RGB => ({ r: c.r * k, g: c.g * k, b: c.b * k });

/**
 * Resolves the lighting for a frame.
 *
 * Two rules are enforced here rather than in the UI, so no renderer can bypass them:
 *
 *  - A non-Natural visual mapping forces Tier 0. Those mappings paint quantitative data into
 *    `emissive`; adding directional light on top would make the same numeric value read as a
 *    different colour depending on which way a leaf happens to face, quietly corrupting a
 *    scientific readout.
 *  - A sun below the horizon contributes nothing, so night-time steps do not get lit from
 *    underneath.
 */
export function resolveLighting(
    options: LightingOptions,
    mapping: VisualMappingOptions,
    initialDate: string,
    timestep: number,
    hoursPerTick: number,
): FrameLighting {
    //Data ramps are quantitative readouts painted into emissive; lighting them would make one
    //number read as two colours. That includes the daylight sky, so this drops to the exact
    //historical ambient rather than merely turning the sun off.
    const dataMapping = mapping !== VisualMappingOptions.Natural;
    const anything = options.daylight || options.shadows || options.occlusion || options.indirect;

    const legacySky: RGB = scaled(HEMI_SKY, HEMI_INTENSITY);
    const legacyGround: RGB = scaled(HEMI_GROUND, HEMI_INTENSITY);

    if (!anything || dataMapping)
        return {
            shadowBiasTexels: options.shadowBiasTexels,
            hemiSky: legacySky, hemiGround: legacyGround,
            ambientOcclusion: false, voxelGI: false, bounces: 1,
            sunDirection: { x: 0, y: 0, z: 0 }, sunColor: BLACK, shadows: false,
        };

    const ambientOcclusion = options.occlusion;
    const voxelGI = options.indirect;
    const bounces = options.indirect && options.secondBounce ? 2 : 1;

    //Daylight moves the energy out of the ambient and into the sun; see LightingOptions.
    const hemiSky = options.daylight ? scaled(DAY_SKY, DAY_SKY_INTENSITY) : legacySky;
    const hemiGround = options.daylight ? scaled(DAY_GROUND, DAY_SKY_INTENSITY) : legacyGround;
    const sunPeak = options.daylight ? DAY_SUN_INTENSITY : SUN_INTENSITY;

    const base = {
        shadowBiasTexels: options.shadowBiasTexels,
        hemiSky, hemiGround, ambientOcclusion, voxelGI, bounces,
    };

    //Occlusion and indirect light attenuate the ambient, so they apply with no sun at all -
    //and in daylight the sky alone is a legitimate, if dim, look.
    if (!options.shadows)
        return { ...base, sunDirection: { x: 0, y: 1, z: 0 }, sunColor: BLACK, shadows: false };

    const when = timeOfTimestep(initialDate, timestep, hoursPerTick);
    const sun = solarPosition(when);

    //Occlusion still applies at night: it darkens the ambient term, which is the only light
    //there is once the sun is down.
    if (sun.altitude <= 0)
        return { ...base, sunDirection: { x: 0, y: 1, z: 0 }, sunColor: BLACK, shadows: false };

    //Fade the last few degrees so a shadow does not snap on and off between timesteps.
    const horizonFade = Math.min(1, sun.altitude / 3);
    const scale = sunPeak * horizonFade;

    return {
        ...base,
        sunDirection: sun.direction,
        sunColor: { r: SUN_COLOR.r * scale, g: SUN_COLOR.g * scale, b: SUN_COLOR.b * scale },
        shadows: true,
    };
}

/**
 * A bounding sphere for the shadow camera to cover.
 *
 * Deliberately derived from the FIELD, not from the plants: a frustum fitted to the actual
 * geometry would resize every time a plant grew, and the shadow map would visibly swim from
 * one timestep to the next.
 */
export function shadowBounds(fieldSizeX: number, fieldSizeZ: number, headroom = 6) {
    const center: Vec3 = { x: fieldSizeX * 0.5, y: headroom * 0.5, z: fieldSizeZ * 0.5 };
    const radius = Math.hypot(fieldSizeX * 0.5, headroom * 0.5, fieldSizeZ * 0.5);
    return { center, radius };
}
