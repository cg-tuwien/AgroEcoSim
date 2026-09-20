//Tests for the lighting model's invariants.
//
//The important one is not "does it look nice" but "does Tier 0 still vanish": every term the
//sun adds must be exactly zero at the parity tier, because that is what keeps the WebGL
//viewport byte-identical to the pre-refactor renderer.

import { HEMI_GROUND, HEMI_INTENSITY, HEMI_SKY, LEAF_TRANSLUCENCY, LIGHTING_OFF, LightingOptions, SHADOW_NORMAL_OFFSET_TEXELS, lightingFromLegacyTier, resolveLighting, shadowBounds, SUN_INTENSITY } from '../src/render/core/Lighting';
import { VisualMappingOptions } from '../src/render/core/PlantColors';

const NATURAL = VisualMappingOptions.Natural;
const opts = (o: Partial<LightingOptions>): LightingOptions => ({ ...LIGHTING_OFF, ...o });
const ALL: LightingOptions = { ...LIGHTING_OFF, shadows: true, occlusion: true, indirect: true, secondBounce: true };
const legacyHemi = {
    hemiSky: { r: HEMI_SKY.r * HEMI_INTENSITY, g: HEMI_SKY.g * HEMI_INTENSITY, b: HEMI_SKY.b * HEMI_INTENSITY },
    hemiGround: { r: HEMI_GROUND.r * HEMI_INTENSITY, g: HEMI_GROUND.g * HEMI_INTENSITY, b: HEMI_GROUND.b * HEMI_INTENSITY },
};
//Timestep 3 at 4 hours from a summer midnight is midday: a high sun.
const midday = (o: LightingOptions, mapping = NATURAL) =>
    resolveLighting(o, mapping, '2026-06-21', 3, 4);

describe('Tier 0 contributes nothing', () => {
    test('the sun colour is exactly black and shadows are off', () => {
        const l = midday(LIGHTING_OFF);
        expect(l.sunColor).toEqual({ r: 0, g: 0, b: 0 });
        expect(l.shadows).toBe(false);
        expect(l.shadows).toBe(false);
    });
});

describe('data mappings force Tier 0', () => {
    //Those mappings paint quantitative values into emissive. Lighting them would make the
    //same number read as a different colour depending on which way a leaf faces, which
    //silently corrupts a scientific readout - so the renderer enforces it, not the UI.
    test.each([
        VisualMappingOptions.Water,
        VisualMappingOptions.Energy,
        VisualMappingOptions.Irradiance,
        VisualMappingOptions.Resource,
        VisualMappingOptions.Production,
        VisualMappingOptions.Auxins,
        VisualMappingOptions.Cytokinins,
    ])('%s is never lit', (mapping) => {
        const l = midday(opts({ shadows: true }), mapping);
        expect(l.shadows).toBe(false);
        expect(l.sunColor).toEqual({ r: 0, g: 0, b: 0 });
        expect(l.shadows).toBe(false);
    });

    test('but Natural at Tier 1 is lit', () => {
        const l = midday(opts({ shadows: true }));
        expect(l.shadows).toBe(true);
        expect(l.sunColor.r).toBeGreaterThan(0);
        expect(l.shadows).toBe(true);
    });
});

describe('the sun follows the simulated clock', () => {
    test('night contributes no light and casts no shadow', () => {
        //Timestep 0 is local midnight.
        const night = resolveLighting(opts({ shadows: true }), NATURAL, '2026-06-21', 0, 4);
        expect(night.sunColor).toEqual({ r: 0, g: 0, b: 0 });
        expect(night.shadows).toBe(false);
    });

    test('midday is bright and above the horizon', () => {
        const l = midday(opts({ shadows: true }));
        expect(l.sunDirection.y).toBeGreaterThan(0.5);
        expect(Math.hypot(l.sunDirection.x, l.sunDirection.y, l.sunDirection.z)).toBeCloseTo(1, 6);
    });

    test('the sun moves across the day', () => {
        const morning = resolveLighting(opts({ shadows: true }), NATURAL, '2026-06-21', 2, 4);
        const afternoon = resolveLighting(opts({ shadows: true }), NATURAL, '2026-06-21', 4, 4);
        expect(morning.sunDirection.x).not.toBeCloseTo(afternoon.sunDirection.x, 3);
    });

    test('brightness never exceeds the configured intensity', () => {
        for (let step = 0; step < 12; ++step) {
            const l = resolveLighting(opts({ shadows: true }), NATURAL, '2026-06-21', step, 2);
            //The horizon fade only ever scales down.
            expect(l.sunColor.r).toBeLessThanOrEqual(SUN_INTENSITY + 1e-6);
            expect(l.sunColor.r).toBeGreaterThanOrEqual(0);
        }
    });

    test('the winter default date still produces daylight at midday', () => {
        //2026-02-01 is the backend's fallback date; the sun is low but not absent.
        const l = resolveLighting(opts({ shadows: true }), NATURAL, '2026-02-01', 3, 4);
        expect(l.sunColor.r).toBeGreaterThan(0);
        expect(l.sunDirection.y).toBeGreaterThan(0);
        expect(l.sunDirection.y).toBeLessThan(0.5);   // noticeably lower than midsummer
    });
});

describe('shadowBounds', () => {
    test('covers the whole field', () => {
        const { center, radius } = shadowBounds(10, 10);
        expect(center.x).toBeCloseTo(5, 6);
        expect(center.z).toBeCloseTo(5, 6);
        //Must reach the far corner of the field from its centre.
        expect(radius).toBeGreaterThanOrEqual(Math.hypot(5, 5));
    });

    test('depends only on the field, so it cannot swim as plants grow', () => {
        //A frustum fitted to the geometry would resize every timestep and the shadow map
        //would visibly shift between frames.
        const a = shadowBounds(10, 10);
        const b = shadowBounds(10, 10);
        expect(a).toEqual(b);
        //But it does follow the field size.
        expect(shadowBounds(20, 20).radius).toBeGreaterThan(a.radius);
    });
});

describe('ambient occlusion is gated by tier, not by the sun', () => {
    test('only the AO tier enables it', () => {
        expect(midday(LIGHTING_OFF).ambientOcclusion).toBe(false);
        expect(midday(opts({ shadows: true })).ambientOcclusion).toBe(false);
        expect(midday(opts({ shadows: true, occlusion: true })).ambientOcclusion).toBe(true);
    });

    test('a data mapping switches it off along with everything else', () => {
        //Same reasoning as the sun: occlusion would darken an irradiance readout by canopy
        //position, so the same number would print as two different colours.
        const l = resolveLighting(opts({ shadows: true, occlusion: true }), VisualMappingOptions.Irradiance, '2026-06-21', 3, 4);
        expect(l.ambientOcclusion).toBe(false);
        expect(l.shadows).toBe(false);
    });

    test('it survives the night, when the sun does not', () => {
        //Occlusion attenuates the hemisphere term, which is the only light left after dark.
        //Turning it off at night would make surfaces jump brighter as the sun set.
        const night = resolveLighting(opts({ shadows: true, occlusion: true }), NATURAL, '2026-12-21', 0, 4);
        expect(night.sunColor).toEqual({ r: 0, g: 0, b: 0 });
        expect(night.shadows).toBe(false);
        expect(night.ambientOcclusion).toBe(true);
    });
});

describe('the voxel GI tier', () => {
    test('only the GI tier enables it, and it implies the tiers below', () => {
        expect(midday(opts({ shadows: true, occlusion: true })).voxelGI).toBe(false);
        const gi = midday(opts({ shadows: true, occlusion: true, indirect: true }));
        expect(gi.voxelGI).toBe(true);
        //GI attenuates the hemisphere and adds a bounce; it does not replace the sun, so
        //everything the tiers below contribute must still be on.
        expect(gi.ambientOcclusion).toBe(true);
        expect(gi.shadows).toBe(true);
    });

    test('a data mapping switches it off along with everything else', () => {
        const l = resolveLighting(opts({ shadows: true, occlusion: true, indirect: true }), VisualMappingOptions.Energy, '2026-06-21', 3, 4);
        expect(l.voxelGI).toBe(false);
        expect(l.shadows).toBe(false);
    });

    test('it survives the night, like occlusion', () => {
        //Indirect light is mostly the sky term redistributed, which is all there is after dark.
        const night = resolveLighting(opts({ shadows: true, occlusion: true, indirect: true }), NATURAL, '2026-12-21', 0, 4);
        expect(night.voxelGI).toBe(true);
        expect(night.sunColor).toEqual({ r: 0, g: 0, b: 0 });
    });
});

describe('the second bounce', () => {
    test('only the top tier asks for two, and it implies everything below', () => {
        expect(midday(opts({ shadows: true, occlusion: true, indirect: true })).bounces).toBe(1);
        const two = midday(ALL);
        expect(two.bounces).toBe(2);
        expect(two.voxelGI).toBe(true);
        expect(two.ambientOcclusion).toBe(true);
        expect(two.shadows).toBe(true);
    });

    test('tiers below GI never ask for a second bounce', () => {
        for (const o of [LIGHTING_OFF, opts({ shadows: true }), opts({ shadows: true, occlusion: true })])
            expect(midday(o).bounces).toBe(1);
    });

    test('a data mapping drops it along with the rest', () => {
        const l = resolveLighting(ALL, VisualMappingOptions.Water, '2026-06-21', 3, 4);
        expect(l.bounces).toBe(1);
        expect(l.voxelGI).toBe(false);
        expect(l.shadows).toBe(false);
    });

    test('adding a switch only ever adds', () => {
        //The switches are independent, but they still have to compose monotonically: turning
        //one on may never turn another term off. That is what makes "switch it off if it is
        //slow" a safe instruction.
        const ladder = [LIGHTING_OFF, opts({ shadows: true }), opts({ shadows: true, occlusion: true }),
                        opts({ shadows: true, occlusion: true, indirect: true }), ALL];
        let prev = midday(ladder[0]);
        for (const o of ladder.slice(1)) {
            const cur = midday(o);
            expect(cur.shadows || !prev.shadows).toBe(true);
            expect(cur.ambientOcclusion || !prev.ambientOcclusion).toBe(true);
            expect(cur.voxelGI || !prev.voxelGI).toBe(true);
            expect(cur.bounces).toBeGreaterThanOrEqual(prev.bounces);
            prev = cur;
        }
    });
});

describe('the switches are independent', () => {
    //The point of replacing the ladder: occlusion and indirect light attenuate the AMBIENT,
    //so both are meaningful with no sun at all. Requiring shadows first described the order
    //the features were built in, not the lighting.
    test('occlusion works without shadows', () => {
        const l = midday(opts({ occlusion: true }));
        expect(l.ambientOcclusion).toBe(true);
        expect(l.shadows).toBe(false);
        expect(l.sunColor).toEqual({ r: 0, g: 0, b: 0 });
    });

    test('indirect works without shadows or occlusion', () => {
        const l = midday(opts({ indirect: true }));
        expect(l.voxelGI).toBe(true);
        expect(l.ambientOcclusion).toBe(false);
        expect(l.shadows).toBe(false);
    });

    test('a second bounce without indirect light is ignored', () => {
        expect(midday(opts({ secondBounce: true })).bounces).toBe(1);
        expect(midday(opts({ indirect: true, secondBounce: true })).bounces).toBe(2);
    });

    test('everything off is the parity look', () => {
        const l = midday(LIGHTING_OFF);
        //Exact shape on purpose: this is what "the historical WebGL look" means, and a new
        //term appearing here without being neutral at rest is exactly what must not slip by.
        //shadowBiasTexels is carried through but unused while shadows are off.
        expect(l).toEqual({
            shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS,
            ...legacyHemi,
            ambientOcclusion: false, voxelGI: false, bounces: 1, leafTranslucency: 0,
            sunDirection: { x: 0, y: 0, z: 0 }, sunColor: { r: 0, g: 0, b: 0 }, shadows: false,
        });
    });
});

describe('legacy saved sessions', () => {
    //Sessions saved while lighting was a single number must not silently lose their setting.
    test.each([
        [0, { daylight: false, shadows: false, occlusion: false, indirect: false, secondBounce: false, leafTranslucency: false, shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS }],
        [1, { daylight: false, shadows: true, occlusion: false, indirect: false, secondBounce: false, leafTranslucency: false, shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS }],
        [2, { daylight: false, shadows: true, occlusion: true, indirect: false, secondBounce: false, leafTranslucency: false, shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS }],
        [3, { daylight: false, shadows: true, occlusion: true, indirect: true, secondBounce: false, leafTranslucency: false, shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS }],
        [4, { daylight: false, shadows: true, occlusion: true, indirect: true, secondBounce: true, leafTranslucency: false, shadowBiasTexels: SHADOW_NORMAL_OFFSET_TEXELS }],
    ])('tier %i maps to the switches it used to mean', (tier, expected) => {
        expect(lightingFromLegacyTier(tier as number)).toEqual(expected);
    });
});

describe('daylight moves energy from the ambient to the sun', () => {
    //The point of the switch. With the legacy hemisphere at intensity 3.175 a lit surface gets
    //~3.2 from the sky and ~1.6 from the sun, so occlusion and indirect light are modulating a
    //term that already dominates and neither is visible. Daylight keeps a lit surface at
    //roughly the same exposure while making a shadowed one dark.
    const lum = (c: { r: number; g: number; b: number }) => (c.r + c.g + c.b) / 3;

    test('the sky drops and the sun rises', () => {
        const legacy = midday(opts({ shadows: true }));
        const day = midday(opts({ daylight: true, shadows: true }));
        expect(lum(day.hemiSky)).toBeLessThan(lum(legacy.hemiSky) * 0.5);
        expect(lum(day.sunColor)).toBeGreaterThan(lum(legacy.sunColor) * 1.5);
    });

    test('a lit surface keeps roughly its exposure, a shadowed one does not', () => {
        const legacy = midday(opts({ shadows: true }));
        const day = midday(opts({ daylight: true, shadows: true }));
        //An up-facing surface sees the sky term in full, plus the sun.
        const litLegacy = lum(legacy.hemiSky) + lum(legacy.sunColor);
        const litDay = lum(day.hemiSky) + lum(day.sunColor);
        expect(litDay).toBeGreaterThan(litLegacy * 0.75);
        expect(litDay).toBeLessThan(litLegacy * 1.35);
        //In shadow only the sky remains, and that is where the difference has to show up.
        expect(lum(day.hemiSky)).toBeLessThan(lum(legacy.hemiSky) * 0.4);
    });

    test('daylight alone is a valid look, with no sun and no shadows', () => {
        const l = midday(opts({ daylight: true }));
        expect(l.shadows).toBe(false);
        expect(lum(l.hemiSky)).toBeLessThan(lum(midday(LIGHTING_OFF).hemiSky));
    });

    test('a data mapping falls back to the exact historical ambient', () => {
        //Not merely "sun off": the ramps are calibrated against the legacy hemisphere.
        const l = resolveLighting(ALL, VisualMappingOptions.Water, '2026-06-21', 3, 4);
        expect(l.hemiSky).toEqual(legacyHemi.hemiSky);
        expect(l.hemiGround).toEqual(legacyHemi.hemiGround);
    });

    test('everything off is still exactly the historical hemisphere', () => {
        const l = midday(LIGHTING_OFF);
        expect(l.hemiSky).toEqual(legacyHemi.hemiSky);
        expect(l.hemiGround).toEqual(legacyHemi.hemiGround);
    });
});

describe('leaf translucency', () => {
    //The term that stops a canopy going dark when it is seen from below. It is the only
    //lighting switch that is live with no sun at all - it modulates the hemisphere ambient,
    //which is the only light the viewport has until something else is ticked - so "off is
    //exactly zero" matters more here than for any of the others.
    test('off is exactly zero, including alongside every other switch', () => {
        expect(midday(LIGHTING_OFF).leafTranslucency).toBe(0);
        expect(midday(ALL).leafTranslucency).toBe(0);
        expect(midday(opts({ daylight: true, shadows: true })).leafTranslucency).toBe(0);
    });

    test('on, it is the shared constant both backends read', () => {
        expect(midday(opts({ leafTranslucency: true })).leafTranslucency).toBe(LEAF_TRANSLUCENCY);
    });

    test('it counts as a lighting term on its own, with no sun and no other switch', () => {
        //Not part of the ladder: ticking it alone must resolve to something, or the checkbox
        //would appear dead. It still adds no sun.
        const l = midday(opts({ leafTranslucency: true }));
        expect(l.leafTranslucency).toBe(LEAF_TRANSLUCENCY);
        expect(l.sunColor).toEqual({ r: 0, g: 0, b: 0 });
        expect(l.shadows).toBe(false);
        //and the ambient is still the historical one, since daylight is off
        expect(l.hemiSky).toEqual(legacyHemi.hemiSky);
        expect(l.hemiGround).toEqual(legacyHemi.hemiGround);
    });

    test('a data mapping forces it off', () => {
        //Those mappings paint the value into emissive with a black albedo, so the term could
        //not change a pixel - but it is forced here rather than left to that coincidence, the
        //same way every other term is.
        for (const mapping of [VisualMappingOptions.Irradiance, VisualMappingOptions.Water])
            expect(midday(opts({ leafTranslucency: true, shadows: true }), mapping).leafTranslucency).toBe(0);
    });

    test('the legacy tiers never enable it', () => {
        for (let tier = 0; tier <= 4; ++tier)
            expect(lightingFromLegacyTier(tier).leafTranslucency).toBe(false);
    });
});
