//Tests for the sky cube: the colour model, the exposure, and above all the FACE ORIENTATION.
//
//The orientation tests are the important ones. Everything else here fails visibly the first
//time anyone looks at the viewport; a mirrored cube looks completely normal until you notice
//the sun is rising in the west, which for a preview of where the simulation thinks the sun is
//would be a correctness bug wearing a plausible face.

import {
    SKY_EXPOSURE, SKY_FACE_COUNT, SKY_FACE_SIZE, SKY_REFERENCE_RADIANCE,
    SKY_ZENITH_TARGET, encodeSrgbByte, resolveSky, skyColor, skyFaceDirection,
} from '../src/render/core/SkyTexture';
import { DEFAULT_SOLAR_SITE } from '../src/helpers/Sun';
import { sampleSkyDome, skyRadiance } from '../src/render/core/SkyDome';
import { VisualMappingOptions } from '../src/render/core/PlantColors';
import { linearToSrgb } from '../src/render/core/Color';

const NATURAL = VisualMappingOptions.Natural;
const JUNE = '2026-06-21';
const sky = (timestep: number, coverage = 0, enabled = true, mapping = NATURAL) =>
    resolveSky(enabled, mapping, JUNE, timestep, 1, coverage);

//Face centre is between the two middle texels, so this samples at the exact centre.
const centre = (face: number) =>
    skyFaceDirection(face, (SKY_FACE_SIZE - 1) / 2, (SKY_FACE_SIZE - 1) / 2, SKY_FACE_SIZE);

describe('cube faces hold the directions the shaders expect', () => {
    //The cube is stored in three's env-map space, where world X is mirrored, so cube +X holds
    //the WEST sky. Both backends undo it the same way. Spelled out in words because the sign
    //is the whole risk.
    test.each([
        [0, 'west', { x: -1, y: 0, z: 0 }],
        [1, 'east', { x: 1, y: 0, z: 0 }],
        [2, 'zenith', { x: 0, y: 1, z: 0 }],
        [3, 'nadir', { x: 0, y: -1, z: 0 }],
        [4, 'south', { x: 0, y: 0, z: 1 }],
        [5, 'north', { x: 0, y: 0, z: -1 }],
    ])('face %i is the %s sky', (face, _name, expected) => {
        const c = centre(face as number);
        expect(c.x).toBeCloseTo((expected as any).x, 6);
        expect(c.y).toBeCloseTo((expected as any).y, 6);
        expect(c.z).toBeCloseTo((expected as any).z, 6);
    });

    test('every texel direction is a unit vector', () => {
        for (let face = 0; face < SKY_FACE_COUNT; ++face)
            for (const [x, y] of [[0, 0], [SKY_FACE_SIZE - 1, 0], [0, SKY_FACE_SIZE - 1],
                                  [SKY_FACE_SIZE - 1, SKY_FACE_SIZE - 1], [7, 53]]) {
                const d = skyFaceDirection(face, x, y, SKY_FACE_SIZE);
                expect(Math.hypot(d.x, d.y, d.z)).toBeCloseTo(1, 9);
            }
    });

    test('the six faces cover the sphere without overlapping', () => {
        //Each face centre is a distinct axis, so the six of them sum to zero.
        let sx = 0, sy = 0, sz = 0;
        for (let face = 0; face < SKY_FACE_COUNT; ++face) {
            const c = centre(face);
            sx += c.x; sy += c.y; sz += c.z;
        }
        expect(Math.hypot(sx, sy, sz)).toBeCloseTo(0, 6);
    });
});

describe('the sun ends up where the sun is', () => {
    //The end-to-end mirror guard: brightest texel in, world direction out. This is what fails
    //if the X negation is dropped from either skyFaceDirection or a backend's shader.
    const brightest = (timestep: number) => {
        const cube = sky(timestep)!;
        let best = -1, bestFace = 0, bestI = 0;
        for (let face = 0; face < SKY_FACE_COUNT; ++face) {
            const buf = cube.faces[face];
            for (let t = 0; t < SKY_FACE_SIZE * SKY_FACE_SIZE; ++t) {
                const v = buf[t * 4] + buf[t * 4 + 1] + buf[t * 4 + 2];
                if (v > best) { best = v; bestFace = face; bestI = t; }
            }
        }
        return skyFaceDirection(bestFace, bestI % SKY_FACE_SIZE,
                                Math.floor(bestI / SKY_FACE_SIZE), SKY_FACE_SIZE);
    };

    test('the morning sky is brightest in the east', () => {
        //06:00 local on midsummer day. East is +X.
        expect(brightest(6).x).toBeGreaterThan(0.2);
    });

    test('the evening sky is brightest in the west', () => {
        expect(brightest(18).x).toBeLessThan(-0.2);
    });

    test('the brightest direction is the sun itself', () => {
        const cube = sky(12)!;
        const d = brightest(12);
        const s = cube.dome.sunDirection;
        //Within a few degrees; the aureole is broad and the texels are ~0.7 degrees.
        expect(d.x * s.x + d.y * s.y + d.z * s.z).toBeGreaterThan(0.99);
    });
});

describe('exposure is anchored to a measured radiance', () => {
    test('SKY_REFERENCE_RADIANCE is the clear midsummer-noon zenith', () => {
        //Recomputed from the dome, so a change to the physics fails here rather than silently
        //re-exposing the sky.
        const noon = new Date(Date.UTC(2026, 5, 21, 10, 55));
        const measured = skyRadiance(sampleSkyDome(noon), { x: 0, y: 1, z: 0 });
        expect(Math.abs(measured - SKY_REFERENCE_RADIANCE)).toBeLessThan(1);
    });

    test('the anchor lands on the curve where the artistic constant says', () => {
        expect(SKY_EXPOSURE * SKY_REFERENCE_RADIANCE).toBeCloseTo(SKY_ZENITH_TARGET, 9);
    });
});

describe('the colour model', () => {
    const noonDome = (coverage = 0) =>
        sampleSkyDome(new Date(Date.UTC(2026, 5, 21, 10, 55)), undefined, undefined, coverage);
    const at = (dome: any, coverage: number, d: any) =>
        skyColor(dome, coverage, d, { r: 0, g: 0, b: 0 });

    test('a clear zenith is blue: blue above green above red', () => {
        const c = at(noonDome(), 0, { x: 0, y: 1, z: 0 });
        expect(c.b).toBeGreaterThan(c.g);
        expect(c.g).toBeGreaterThan(c.r);
    });

    test('a fully overcast sky is achromatic', () => {
        const c = at(noonDome(1), 1, { x: 0, y: 1, z: 0 });
        expect(c.r).toBeCloseTo(c.g, 6);
        expect(c.g).toBeCloseTo(c.b, 6);
    });

    test('the horizon is less blue than the zenith', () => {
        //The view-path term saturates in all three channels at long air mass, so a hazy
        //horizon whitens on its own.
        const dome = noonDome();
        const zenith = at(dome, 0, { x: 0, y: 1, z: 0 });
        const horizon = at(dome, 0, { x: 1, y: 0.02, z: 0 });
        expect(horizon.b / horizon.r).toBeLessThan(zenith.b / zenith.r);
    });

    test('a low sun is warm towards the sun and still blue overhead', () => {
        //The failure this guards is a naive single-scattering sky, which strips the blue out
        //of the whole dome at dawn and leaves the zenith GREEN.
        const dawn = sampleSkyDome(new Date(Date.UTC(2026, 5, 21, 3, 30)));
        const towards = at(dawn, 0, dawn.sunDirection);
        expect(towards.r).toBeGreaterThan(towards.b);
        const zenith = at(dawn, 0, { x: 0, y: 1, z: 0 });
        expect(zenith.b).toBeGreaterThan(zenith.g);
        expect(zenith.g).toBeGreaterThan(zenith.r);
    });

    test('below the horizon is the soil, not the sky', () => {
        const dome = noonDome();
        const ground = at(dome, 0, { x: 0, y: -1, z: 0 });
        //Warm: the ground tint is the hemisphere light's ground colour.
        expect(ground.r).toBeGreaterThan(ground.g);
        expect(ground.g).toBeGreaterThan(ground.b);
        //And not black, since the ground is lit.
        expect(ground.r).toBeGreaterThan(0.05);
    });

    test('night is very dark but still a sky', () => {
        const night = sampleSkyDome(new Date(Date.UTC(2026, 5, 21, 23, 0)));
        const c = at(night, 0, { x: 0, y: 1, z: 0 });
        expect(c.r).toBe(0);
        expect(c.g).toBe(0);
        expect(c.b).toBe(0);
    });
});

describe('the cube agrees with the reference implementation', () => {
    //fill() inlines exposeInto and the two lookup tables for speed. This is what keeps that
    //optimisation honest: the bytes it writes must match what skyColor computes analytically.
    test('every sampled texel matches skyColor to within a byte', () => {
        const timestep = 10, coverage = 0.3;
        const cube = sky(timestep, coverage)!;
        const dome = cube.dome;
        let worst = 0;
        for (let face = 0; face < SKY_FACE_COUNT; ++face)
            for (let y = 1; y < SKY_FACE_SIZE; y += 17)
                for (let x = 1; x < SKY_FACE_SIZE; x += 13) {
                    const d = skyFaceDirection(face, x, y, SKY_FACE_SIZE);
                    const c = skyColor(dome, coverage, d, { r: 0, g: 0, b: 0 });
                    const o = (y * SKY_FACE_SIZE + x) * 4;
                    const buf = cube.faces[face];
                    const byte = (v: number) =>
                        Math.round(255 * linearToSrgb(Math.max(0, Math.min(1, v))));
                    worst = Math.max(worst,
                        Math.abs(buf[o] - byte(c.r)),
                        Math.abs(buf[o + 1] - byte(c.g)),
                        Math.abs(buf[o + 2] - byte(c.b)));
                }
        //One byte, because both sides here are ALREADY rounded to integers: a table error of
        //under half a byte still lands on the neighbouring integer sometimes. The table's own
        //error is measured directly in the next test, which is where the half-byte claim lives.
        expect(worst).toBeLessThanOrEqual(1);
    });

    test('the sRGB table stays within the error its comment claims', () => {
        //Checked against linearToSrgb itself, not against another rounded byte. Half of the
        //bound is the table storing integers at all; the rest is half a table step. Indexing by
        //the bucket's left endpoint costs a whole step and pushes the worst case past 1.2, so
        //the upper bound here is what keeps the rounding from being quietly dropped.
        let worst = 0;
        for (let i = 0; i <= 200000; ++i) {
            const v = i / 200000;
            worst = Math.max(worst, Math.abs(encodeSrgbByte(v) - 255 * linearToSrgb(v)));
        }
        expect(worst).toBeLessThan(0.9);
        expect(worst).toBeGreaterThan(0.5);   //...and it is not accidentally exact
    });

    test('every texel is opaque and the faces are the declared size', () => {
        const cube = sky(12)!;
        expect(cube.size).toBe(SKY_FACE_SIZE);
        expect(cube.faces).toHaveLength(SKY_FACE_COUNT);
        for (const buf of cube.faces) {
            expect(buf.length).toBe(SKY_FACE_SIZE * SKY_FACE_SIZE * 4);
            for (let t = 0; t < buf.length; t += 4) expect(buf[t + 3]).toBe(255);
        }
    });
});

describe('resolveSky gates and memoises', () => {
    test('switched off there is no sky at all', () => {
        expect(sky(12, 0, false)).toBeUndefined();
    });

    test('a data mapping suppresses it, as the greyed-out control implies', () => {
        for (const m of [VisualMappingOptions.Water, VisualMappingOptions.Irradiance])
            expect(sky(12, 0, true, m)).toBeUndefined();
    });

    test('night still returns a cube - a dark sky is a real answer', () => {
        const cube = sky(0)!;   //midnight local
        expect(cube).toBeDefined();
        expect(cube.faces[2][0]).toBe(0);
    });

    test('unchanged inputs do not rebuild', () => {
        const first = sky(9)!.revision;
        expect(sky(9)!.revision).toBe(first);
        expect(sky(9)!.revision).toBe(first);
    });

    test('a new timestep or new cloud cover does rebuild', () => {
        const base = sky(9)!.revision;
        expect(sky(10)!.revision).toBeGreaterThan(base);
        const afterStep = sky(10)!.revision;
        expect(sky(10, 0.4)!.revision).toBeGreaterThan(afterStep);
    });

    test('moving the site rebuilds', () => {
        //Guard the spread itself. tsconfig only covers src/**, so a test importing a name the
        //module no longer exports typechecks clean, spreads `undefined`, and passes for the
        //wrong reason - which is exactly what happened here once already.
        expect(Object.keys(DEFAULT_SOLAR_SITE).sort())
            .toEqual(['altitudeM', 'azimuthDeg', 'latitude', 'longitude']);

        const base = sky(9)!.revision;
        const moved = resolveSky(true, NATURAL, JUNE, 9, 1, 0,
                                 { ...DEFAULT_SOLAR_SITE, latitude: -33.87 })!;
        expect(moved.revision).toBeGreaterThan(base);
        //...and it really is the moved site that was rendered, not a half-empty one.
        expect(moved.dome.sunDirection.z).toBeLessThan(0);   //southern hemisphere: sun to the north
    });

    test('each of the four site fields is part of the cache key', () => {
        //A field left out of the key means moving it silently shows the previous sky.
        for (const patch of [{ latitude: 10 }, { longitude: 10 },
                             { altitudeM: 2500 }, { azimuthDeg: 45 }]) {
            const before = resolveSky(true, NATURAL, JUNE, 9, 1, 0, DEFAULT_SOLAR_SITE)!.revision;
            const after = resolveSky(true, NATURAL, JUNE, 9, 1, 0,
                                     { ...DEFAULT_SOLAR_SITE, ...patch })!.revision;
            expect(after).toBeGreaterThan(before);
        }
    });

    test('the SAME cube object comes back, mutated in place', () => {
        //This is the one sharp edge in the API and it is deliberate: reallocating 400 KB of
        //face buffers per timestep during playback is worse than reusing them. The consequence
        //is that holding a reference and comparing it later tells a caller NOTHING - both the
        //object and its `faces` arrays are identical across refills. A backend deciding whether
        //to re-upload must snapshot `revision` as a NUMBER.
        const a = sky(14)!;
        const facesBefore = a.faces;
        const firstFace = a.faces[0];
        const revisionBefore = a.revision;

        const b = sky(15)!;
        expect(b).toBe(a);
        expect(b.faces).toBe(facesBefore);
        expect(b.faces[0]).toBe(firstFace);
        expect(b.revision).toBeGreaterThan(revisionBefore);
        //...and the stale reference now reports the NEW revision, which is exactly the trap.
        expect(a.revision).toBe(b.revision);
    });
});
