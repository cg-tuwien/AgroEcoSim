//Tests for the sky dome ported from Agro/Irradiance/SkyDome.cs.
//
//Like tests/sun.test.ts, the numbers are checked against published physics rather than
//against the implementation, so a transcription slip out of the C# shows up as a wrong
//irradiance instead of a self-consistent one.

import {
    GROUND_ALBEDO, airMass, fastAcos, isLit, planeIrradiance, radicalInverse2,
    sampleSkyDome, sampleTickDome, skyRadiance, tickSunInstant,
} from '../src/render/core/SkyDome';
import { LATITUDE, LONGITUDE } from '../src/helpers/Sun';

//Solar noon at Vienna is about 10:55 UTC all year: 12:00 less 16.3725/15 h of longitude, give
//or take the equation of time. The elevation is stationary at its maximum, so a few minutes
//either way moves these numbers by far less than the tolerances below.
const summerNoon = new Date(Date.UTC(2026, 5, 21, 10, 55));
const winterNoon = new Date(Date.UTC(2026, 11, 21, 10, 55));
const midnight = new Date(Date.UTC(2026, 5, 21, 23, 0));

describe('radicalInverse2', () => {
    //JS bit operations are signed; a plain >> would drag the sign bit through the swap.
    test('reverses the bits of the first few integers', () => {
        expect(radicalInverse2(0)).toBeCloseTo(0, 12);
        expect(radicalInverse2(1)).toBeCloseTo(0.5, 12);
        expect(radicalInverse2(2)).toBeCloseTo(0.25, 12);
        expect(radicalInverse2(3)).toBeCloseTo(0.75, 12);
        expect(radicalInverse2(4)).toBeCloseTo(0.125, 12);
    });

    test('stays inside [0, 1) and never goes negative', () => {
        for (let i = 0; i < 4096; ++i) {
            const v = radicalInverse2(i);
            expect(v).toBeGreaterThanOrEqual(0);
            expect(v).toBeLessThan(1);
        }
    });
});

describe('fastAcos', () => {
    //Measured worst case over the whole domain is 6.8e-5, at x = 0 - the C# comment's "~1e-5"
    //is the typical error, not the bound. Pinned a little above the measured figure so a
    //mistyped coefficient fails while the approximation itself does not.
    test('tracks Math.acos to within its measured bound', () => {
        let worst = 0;
        for (let i = 0; i <= 20000; ++i)
            worst = Math.max(worst, Math.abs(fastAcos(-1 + 2 * i / 20000) - Math.acos(-1 + 2 * i / 20000)));
        expect(worst).toBeLessThan(1e-4);
        //...and is a real approximation, not accidentally exact: if someone swaps in Math.acos
        //the port has silently stopped mirroring the C#.
        expect(worst).toBeGreaterThan(1e-5);
    });
});

describe('Kasten & Young air mass', () => {
    //Published: ~1 at the zenith, ~2 at 30 degrees, ~5.6 at 10 degrees, ~38 at the horizon.
    test('matches the published values', () => {
        expect(airMass(90)).toBeCloseTo(1, 2);
        expect(airMass(30)).toBeGreaterThan(1.9);
        expect(airMass(30)).toBeLessThan(2.1);
        expect(airMass(10)).toBeGreaterThan(5.4);
        expect(airMass(10)).toBeLessThan(5.8);
        expect(airMass(0)).toBeGreaterThan(30);
        expect(airMass(0)).toBeLessThan(40);
    });
});

describe('clear-sky irradiance at Vienna', () => {
    //Meinel & Meinel through Liu & Jordan gives ~900 W/m2 at midsummer noon and ~265 at
    //midwinter noon for this latitude; both are in line with published clear-sky GHI for the
    //site. These are the numbers that catch a mis-transcribed coefficient.
    test('midsummer noon lands near 900 W/m2', () => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        expect(dome.globalHorizontal).toBeGreaterThan(810);
        expect(dome.globalHorizontal).toBeLessThan(990);
        expect(isLit(dome)).toBe(true);
    });

    test('midwinter noon is roughly a third of that', () => {
        const dome = sampleSkyDome(winterNoon, LATITUDE, LONGITUDE, 0);
        expect(dome.globalHorizontal).toBeGreaterThan(225);
        expect(dome.globalHorizontal).toBeLessThan(305);
    });

    test('the diffuse fraction of a clear summer sky is modest', () => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        const fraction = dome.diffuseHorizontal / dome.globalHorizontal;
        expect(fraction).toBeGreaterThan(0.12);
        expect(fraction).toBeLessThan(0.28);
    });

    test('the sun is high in June and low in December', () => {
        //A direct read of the sun elevation through the direction's Y component.
        expect(sampleSkyDome(summerNoon).sunDirection.y).toBeGreaterThan(0.88);
        const winter = sampleSkyDome(winterNoon).sunDirection.y;
        expect(winter).toBeGreaterThan(0.25);
        expect(winter).toBeLessThan(0.36);
    });

    test('the ground radiance is the albedo times GHI over pi', () => {
        const dome = sampleSkyDome(summerNoon);
        expect(dome.groundRadiance).toBeCloseTo(GROUND_ALBEDO * dome.globalHorizontal / Math.PI, 6);
    });
});

describe('night', () => {
    test('a sun below the horizon zeroes the whole dome', () => {
        const dome = sampleSkyDome(midnight, LATITUDE, LONGITUDE, 0);
        expect(dome.globalHorizontal).toBe(0);
        expect(dome.directNormal).toBe(0);
        expect(dome.diffuseHorizontal).toBe(0);
        expect(dome.groundRadiance).toBe(0);
        expect(isLit(dome)).toBe(false);
        //Every direction is black, including below the horizon.
        expect(skyRadiance(dome, { x: 0, y: 1, z: 0 })).toBe(0);
        expect(skyRadiance(dome, { x: 0, y: -1, z: 0 })).toBe(0);
    });
});

describe('cloud cover', () => {
    test('Kasten & Czeplak: full overcast is exactly a quarter of the clear GHI', () => {
        const clear = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        const dull = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 1);
        expect(dull.globalHorizontal).toBeCloseTo(0.25 * clear.globalHorizontal, 6);
    });

    test('more cloud is monotonically less light', () => {
        let previous = Infinity;
        for (let c = 0; c <= 1.0001; c += 0.1) {
            const g = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, c).globalHorizontal;
            expect(g).toBeLessThanOrEqual(previous);
            previous = g;
        }
    });

    test('a fully overcast dome has no sun-angle dependence left', () => {
        //Coverage 1 zeroes the clear scale, leaving the isotropic-indicatrix overcast shape,
        //which depends only on the zenith angle. Two directions at the same height, one
        //towards the sun and one away from it, must therefore match exactly.
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 1);
        expect(dome.clearScale).toBe(0);
        const h = Math.SQRT1_2;
        expect(skyRadiance(dome, { x: h, y: h, z: 0 }))
            .toBeCloseTo(skyRadiance(dome, { x: -h, y: h, z: 0 }), 9);
    });

    test('a clear dome is brightest towards the sun at a given height', () => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        const s = dome.sunDirection;
        //Two directions at the sun's own height: one at the sun, one diametrically opposite.
        const away = { x: -s.x, y: s.y, z: -s.z };
        expect(skyRadiance(dome, s)).toBeGreaterThan(skyRadiance(dome, away));
    });
});

describe('the CIE shape carries the right total', () => {
    //The model's own claim: the shape is normalised so its cosine-weighted hemispherical
    //integral reproduces diffuseHorizontal, which means the shape constants only redistribute
    //a known total and can never corrupt it. Integrated here with an INDEPENDENT sample set -
    //a plain LCG, not the 128-point Hammersley set the code normalises with - so this is a
    //real check and not a restatement.
    const integrate = (dome: ReturnType<typeof sampleSkyDome>, samples = 400000) => {
        let state = 123456789;
        const next = () => {
            state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
            return state / 4294967296;
        };
        let sum = 0;
        for (let i = 0; i < samples; ++i) {
            //Cosine-weighted hemisphere sampling: pdf = cos/pi, so the estimate is pi*mean(L).
            const u1 = next(), u2 = next();
            const r = Math.sqrt(u1), phi = 2 * Math.PI * u2;
            sum += skyRadiance(dome, {
                x: r * Math.cos(phi),
                y: Math.sqrt(Math.max(0, 1 - u1)),
                z: r * Math.sin(phi),
            });
        }
        return Math.PI * (sum / samples);
    };

    test.each([0, 0.5, 1])('at coverage %p', coverage => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, coverage);
        expect(integrate(dome)).toBeCloseTo(dome.diffuseHorizontal, -0.5);
    });
});

describe('planeIrradiance', () => {
    test('a horizontal plane sees the global horizontal irradiance', () => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        //Beam*cos + full diffuse + no ground view: that IS the definition of GHI.
        expect(planeIrradiance(dome, { x: 0, y: 1, z: 0 }))
            .toBeCloseTo(dome.globalHorizontal, 4);
    });

    test('a downward-facing plane sees only reflected ground', () => {
        const dome = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        expect(planeIrradiance(dome, { x: 0, y: -1, z: 0 }))
            .toBeCloseTo(dome.globalHorizontal * GROUND_ALBEDO, 4);
    });
});

describe('world azimuth rotates the sun, and only the sun', () => {
    //Convention: azimuth 0 means the scene's +X (its right vector) points east, and an
    //increasing angle turns it clockwise seen from above. Subtracting it from the solar
    //azimuth is the whole of the maths - these tests pin the sign, which is the part that is
    //invisible until shadows point the wrong way.
    const base = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, 0).sunDirection;

    test('a quarter turn maps +X to where +Z was', () => {
        const turned = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, 90).sunDirection;
        expect(turned.x).toBeCloseTo(base.z, 9);
        expect(turned.z).toBeCloseTo(-base.x, 9);
        expect(turned.y).toBeCloseTo(base.y, 12);
    });

    test('an arbitrary turn is a rotation about the up axis', () => {
        const theta = 37;
        const c = Math.cos(theta * Math.PI / 180), s = Math.sin(theta * Math.PI / 180);
        const turned = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, theta).sunDirection;
        expect(turned.x).toBeCloseTo(c * base.x + s * base.z, 9);
        expect(turned.z).toBeCloseTo(-s * base.x + c * base.z, 9);
    });

    test('a full turn comes back to where it started', () => {
        const turned = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, 360).sunDirection;
        expect(turned.x).toBeCloseTo(base.x, 9);
        expect(turned.y).toBeCloseTo(base.y, 9);
        expect(turned.z).toBeCloseTo(base.z, 9);
    });

    test('rotating the world does not change how much light arrives', () => {
        const a = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, 0);
        const b = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0, 123);
        expect(b.globalHorizontal).toBeCloseTo(a.globalHorizontal, 9);
        expect(b.diffuseHorizontal).toBeCloseTo(a.diffuseHorizontal, 9);
        expect(b.directNormal).toBeCloseTo(a.directNormal, 9);
    });
});

describe('a tick is previewed by the dome the solver integrates', () => {
    //Mirrors Agro/Irradiance/CpuLightSolver.cs BuildDomes. The rule is not "the instant the
    //tick begins" - the solver never samples that - so these pin the two halves of the real
    //rule: which instant's GEOMETRY is used, and what its MAGNITUDES are scaled to.
    const midnight = new Date(Date.UTC(2026, 5, 20, 22, 0));   //local midnight, 21 June Vienna

    test('a one-hour tick is the half-hour mark, not the boundary', () => {
        const tick = sampleTickDome(summerNoon, 1);
        const half = sampleSkyDome(new Date(summerNoon.getTime() + 1800_000));
        expect(tick.sunDirection.x).toBeCloseTo(half.sunDirection.x, 9);
        expect(tick.sunDirection.y).toBeCloseTo(half.sunDirection.y, 9);
        //One sub-step, so the mean IS the brightest and no scaling happens.
        expect(tick.globalHorizontal).toBeCloseTo(half.globalHorizontal, 6);
    });

    test('a day-long tick starting at midnight is a DAYLIT dome', () => {
        //The case that makes this worth doing: the tick boundary is local midnight, so
        //previewing the boundary would show a black sky over a step the simulation ran in sun.
        expect(sampleSkyDome(midnight).globalHorizontal).toBe(0);
        const tick = sampleTickDome(midnight, 24);
        expect(tick.globalHorizontal).toBeGreaterThan(0);
        expect(tick.sunDirection.y).toBeGreaterThan(0.5);
    });

    test('the brightest geometry carries the tick MEAN, not the brightest magnitude', () => {
        //A 24-hour tick spans night, so the mean must come out well below the peak while the
        //direction stays the peak's.
        const tick = sampleTickDome(midnight, 24);
        const peak = tickSunInstant(midnight, 24);
        const peakDome = sampleSkyDome(peak);
        expect(tick.sunDirection).toEqual(peakDome.sunDirection);
        expect(tick.globalHorizontal).toBeLessThan(peakDome.globalHorizontal);
        //Every magnitude moves by the same factor, and the ground radiance follows GHI.
        const factor = tick.globalHorizontal / peakDome.globalHorizontal;
        expect(tick.directNormal).toBeCloseTo(peakDome.directNormal * factor, 6);
        expect(tick.diffuseHorizontal).toBeCloseTo(peakDome.diffuseHorizontal * factor, 6);
        expect(tick.groundRadiance)
            .toBeCloseTo(GROUND_ALBEDO * tick.globalHorizontal / Math.PI, 9);
    });

    test('the shape still integrates to the SCALED diffuse total', () => {
        //The normalisation invariant has to survive the scaling, or a long tick would show a
        //sky whose brightness and whose stated diffuse irradiance disagree.
        const tick = sampleTickDome(midnight, 24);
        let state = 987654321, sum = 0;
        const n = 200000;
        for (let i = 0; i < n; ++i) {
            state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
            const u1 = state / 4294967296;
            state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
            const u2 = state / 4294967296;
            const r = Math.sqrt(u1), phi = 2 * Math.PI * u2;
            sum += skyRadiance(tick, { x: r * Math.cos(phi), y: Math.sqrt(Math.max(0, 1 - u1)),
                                       z: r * Math.sin(phi) });
        }
        expect(Math.PI * (sum / n)).toBeCloseTo(tick.diffuseHorizontal, -0.5);
    });

    test('cloud cover cannot move which sub-step is brightest', () => {
        //Coverage scales every sub-step of a tick by the same factor, which is exactly why the
        //lighting path may call tickSunInstant without knowing the weather.
        for (const c of [0, 0.4, 1])
            expect(sampleTickDome(midnight, 24, LATITUDE, LONGITUDE, c).sunDirection)
                .toEqual(sampleSkyDome(tickSunInstant(midnight, 24)).sunDirection);
    });

    test('a night-long tick stays dark rather than dividing by zero', () => {
        const winterNight = new Date(Date.UTC(2026, 11, 21, 20, 0));
        const tick = sampleTickDome(winterNight, 4);
        expect(tick.globalHorizontal).toBe(0);
        expect(Number.isFinite(tick.groundRadiance)).toBe(true);
    });
});

describe('the site is a parameter, not a constant', () => {
    test('the southern hemisphere puts the midday sun in the north', () => {
        //Sydney, -33.87. Solar noon there is around 02:00 UTC.
        const noon = new Date(Date.UTC(2026, 11, 21, 1, 0));
        const dome = sampleSkyDome(noon, -33.87, 151.21, 0);
        //North is -Z, so a northern sun has a negative Z component.
        expect(dome.sunDirection.y).toBeGreaterThan(0.5);
        expect(dome.sunDirection.z).toBeLessThan(0);
        //December is high summer there.
        expect(dome.globalHorizontal).toBeGreaterThan(900);
    });

    test('Vienna remains the default', () => {
        const explicit = sampleSkyDome(summerNoon, LATITUDE, LONGITUDE, 0);
        const implicit = sampleSkyDome(summerNoon);
        expect(implicit.globalHorizontal).toBe(explicit.globalHorizontal);
        expect(implicit.sunDirection).toEqual(explicit.sunDirection);
    });
});
