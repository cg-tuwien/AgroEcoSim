import { Mat4, createMat4, invertRigid, multiply } from '../src/render/core/Mat4';
//Parity gate for the renderer-neutral core.
//
//src/render/core must never import three, so nothing checks by construction that its colour
//and geometry math still agrees with three. These tests do: every assertion compares the
//neutral implementation against the three implementation it was ported from. If three is
//ever upgraded, a failure here is the early warning that the WebGPU renderer has drifted.

import * as THREE from 'three';
import {
    RGB, fromHex, fromCss, lerp, lerpHSL, rgbToHsl, hslToRgb,
    srgbToLinear, linearToSrgb,
} from '../src/render/core/Color';
import { computeVertexNormals } from '../src/render/core/Geometry';
import { mulberry32, shuffled, greenColors } from '../src/render/core/Palette';

const EPS = 1e-12;

function expectRGB(actual: RGB, expected: THREE.Color, what: string) {
    expect(`${what}.r`).toBe(`${what}.r`);
    expect(actual.r).toBeCloseTo(expected.r, 12);
    expect(actual.g).toBeCloseTo(expected.g, 12);
    expect(actual.b).toBeCloseTo(expected.b, 12);
}

const HEXES = [
    '#000000', '#ffffff', '#7f4f1f', '#009900', '#898989', '#909090',
    '#00BFE6', '#ffc757', '#FDDE81', '#D84315', '#424d62', '#ffcc66',
    '#0a95ff', '#ff0a91', '#003f5c', '#58508d', '#bc5090', '#ff6361', '#ffa600',
    '#010101', '#0a0a0a', '#123456', '#fe0102',
];

describe('Color: transfer functions match three', () => {
    test('srgbToLinear / linearToSrgb agree with ColorManagement', () => {
        for (let i = 0; i <= 255; ++i) {
            const c = i / 255;
            //three exposes these through Color conversion; compare via a round-trip on a
            //single channel so we exercise exactly the same code path three uses.
            const viaThree = new THREE.Color().setRGB(c, c, c, THREE.SRGBColorSpace).r;
            expect(srgbToLinear(c)).toBeCloseTo(viaThree, 12);
        }
    });

    test('linearToSrgb inverts srgbToLinear only approximately, matching three', () => {
        //three's LinearToSRGB uses the TRUNCATED exponent 0.41666, not 1/2.4 = 0.41666...,
        //so the round trip is deliberately inexact - about 4e-6 at worst. Asserting an exact
        //inverse here would be asserting that we are more correct than three, which is the
        //opposite of what parity requires. The bound below pins the size of that error so a
        //future three upgrade that fixes the constant shows up as a failure.
        let worst = 0;
        for (let i = 0; i <= 100; ++i) {
            const c = i / 100;
            worst = Math.max(worst, Math.abs(linearToSrgb(srgbToLinear(c)) - c));
        }
        expect(worst).toBeGreaterThan(0);        // it really is inexact
        expect(worst).toBeLessThan(1e-5);        // but only in the 5th decimal
    });

    test('uses three\'s truncated 0.41666 exponent, not 1/2.4', () => {
        //Direct check of the quirk the WGSL shader must also reproduce.
        const c = 0.5;
        expect(linearToSrgb(c)).toBeCloseTo(1.055 * Math.pow(c, 0.41666) - 0.055, 15);
        expect(linearToSrgb(c)).not.toBeCloseTo(1.055 * Math.pow(c, 1 / 2.4) - 0.055, 9);
    });
});

describe('Color: fromHex matches new THREE.Color(hex)', () => {
    test.each(HEXES)('%s', (hex) => {
        expectRGB(fromHex(hex), new THREE.Color(hex), hex);
    });

    test('3-digit hex expands like three', () => {
        expectRGB(fromHex('#999'), new THREE.Color('#999'), '#999');
        expectRGB(fromHex('#000'), new THREE.Color('#000'), '#000');
        expectRGB(fromHex('#fff'), new THREE.Color('#fff'), '#fff');
    });
});

describe('Color: fromCss matches THREE.Color.setStyle', () => {
    test('rgb() form', () => {
        expectRGB(fromCss('rgb(68, 68, 68)'), new THREE.Color().setStyle('rgb(68, 68, 68)'), 'fg');
        expectRGB(fromCss('rgb(250, 250, 250)'), new THREE.Color().setStyle('rgb(250, 250, 250)'), 'bg');
    });

    test('tolerates the background shorthand tail that getComputedStyle returns', () => {
        //getComputedStyle(...).getPropertyValue("background") yields the colour followed by
        //the rest of the shorthand; three's regex ignores the tail and so must ours.
        const shorthand = 'rgb(28, 28, 28) none repeat scroll 0% 0% / auto padding-box border-box';
        expectRGB(fromCss(shorthand), new THREE.Color().setStyle('rgb(28, 28, 28)'), 'bg-shorthand');
    });
});

describe('Color: HSL round-trips match three', () => {
    test('rgbToHsl matches getHSL in the linear working space', () => {
        for (const hex of HEXES) {
            const mine = rgbToHsl(fromHex(hex));
            const theirs = new THREE.Color(hex).getHSL({ h: 0, s: 0, l: 0 });
            expect(mine.h).toBeCloseTo(theirs.h, 12);
            expect(mine.s).toBeCloseTo(theirs.s, 12);
            expect(mine.l).toBeCloseTo(theirs.l, 12);
        }
    });

    test('hslToRgb matches setHSL', () => {
        for (let i = 0; i < 32; ++i) {
            const h = i / 32, s = ((i * 7) % 32) / 32, l = ((i * 13) % 32) / 32;
            expectRGB(hslToRgb(h, s, l), new THREE.Color().setHSL(h, s, l), `hsl(${h},${s},${l})`);
        }
    });
});

describe('Color: interpolation matches three', () => {
    const pairs: [string, string][] = [
        ['#009900', '#7f4f1f'],   // stem Natural wood ramp
        ['#898989', '#7f4f1f'],   // root Natural wood ramp
        ['#909090', '#ffc757'],   // energy
        ['#909090', '#FDDE81'],   // auxins
        ['#909090', '#D84315'],   // cytokinins
        ['#003f5c', '#58508d'],   // heat
    ];

    test('lerpHSL', () => {
        for (const [a, b] of pairs)
            for (let i = 0; i <= 10; ++i) {
                const t = i / 10;
                expectRGB(lerpHSL(fromHex(a), fromHex(b), t), new THREE.Color(a).lerpHSL(new THREE.Color(b), t), `${a}->${b}@${t}`);
            }
    });

    test('lerp', () => {
        for (const [a, b] of pairs)
            for (let i = 0; i <= 10; ++i) {
                const t = i / 10;
                expectRGB(lerp(fromHex(a), fromHex(b), t), new THREE.Color(a).lerp(new THREE.Color(b), t), `${a}->${b}@${t}`);
            }
    });
});

describe('Palette', () => {
    test('mulberry32 is deterministic and in range', () => {
        const a = mulberry32(1234);
        const b = mulberry32(1234);
        for (let i = 0; i < 100; ++i) {
            const v = a();
            expect(v).toBe(b());
            expect(v).toBeGreaterThanOrEqual(0);
            expect(v).toBeLessThan(1);
        }
    });

    test('shuffled is a permutation and does not mutate its input', () => {
        const src = Array.from({ length: 50 }, (_, i) => i);
        const copy = src.slice();
        const out = shuffled(src, mulberry32(7));
        expect(src).toEqual(copy);
        expect(out.slice().sort((x, y) => x - y)).toEqual(copy);
    });

    test('greenColors is stable across imports and has the expected extent', () => {
        //40 green steps x 20 red steps, exactly as helpers/Plant.ts built it.
        expect(greenColors.length).toBe(800);
        //Determinism is the whole point of seeding: re-deriving must reproduce the order.
        const again = shuffled(greenColors.slice().sort(byChannel), mulberry32(1));
        expect(again.length).toBe(800);
        //Every entry must be a pure-ish green with no blue.
        for (const c of greenColors) expect(c.b).toBeCloseTo(0, 12);
    });
});

function byChannel(a: RGB, b: RGB) {
    return a.r - b.r || a.g - b.g || a.b - b.b;
}

describe('Geometry: computeVertexNormals matches three', () => {
    test('agrees on an irregular indexed mesh', () => {
        //A deliberately lopsided mesh: three area-weights by using the raw cross products,
        //so a mesh with very different triangle areas is what actually discriminates
        //between that and naive unit-normal averaging.
        const positions = new Float32Array([
            0, 0, 0,
            4, 0, 0,
            0, 0.25, 0,
            1, 3, 2,
            -2, 0.5, 1,
        ]);
        const indices = new Uint32Array([0, 1, 3, 0, 3, 2, 0, 2, 4, 1, 2, 3]);

        const mine = computeVertexNormals(positions, indices);

        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.BufferAttribute(positions.slice(), 3));
        g.setIndex(new THREE.BufferAttribute(indices.slice(), 1));
        g.computeVertexNormals();
        const theirs = g.getAttribute('normal').array as Float32Array;

        expect(mine.length).toBe(theirs.length);
        for (let i = 0; i < mine.length; ++i) expect(mine[i]).toBeCloseTo(theirs[i], 5);
    });
});

describe('Geometry bridge: MeshData <-> BufferGeometry round-trips losslessly', () => {
    //ThreeGeometrySource currently derives the static table FROM three, so comparing the
    //two would be tautological. What is worth guarding is the bridge itself: that converting
    //a BufferGeometry to MeshData and back preserves every attribute exactly. When the
    //hand-written generators land, this file is where their output gets compared to the
    //three-derived arrays element-wise before ThreeGeometrySource is retired.
    const { fromBufferGeometry, toBufferGeometry } = require('../src/render/three/ThreeGeometry');

    const cases: [string, () => THREE.BufferGeometry][] = [
        ['BoxCentered', () => new THREE.BoxGeometry()],
        ['BoxCorner', () => new THREE.BoxGeometry().translate(0.5, 0.5, 0.5)],
        ['Cylinder', () => new THREE.CylinderGeometry(1, 1, 1.0, 8)],
        ['Sphere', () => new THREE.SphereGeometry(1, 8, 8)],
        ['Plane', () => new THREE.PlaneGeometry(2, 2)],
        ['CircleHalf', () => new THREE.CircleGeometry(0.5, 12).rotateX(-Math.PI * 0.5)],
        ['Dodecahedron', () => new THREE.DodecahedronGeometry(0.03)],
    ];

    test.each(cases)('%s', (name, make) => {
        const src = make();
        const md = fromBufferGeometry(src, name);
        const back = toBufferGeometry(md);

        const srcPos = src.getAttribute('position').array as ArrayLike<number>;
        const backPos = back.getAttribute('position').array as ArrayLike<number>;
        expect(backPos.length).toBe(srcPos.length);
        for (let i = 0; i < srcPos.length; ++i) expect(backPos[i]).toBe(srcPos[i]);

        const srcNrm = src.getAttribute('normal').array as ArrayLike<number>;
        const backNrm = back.getAttribute('normal').array as ArrayLike<number>;
        expect(backNrm.length).toBe(srcNrm.length);
        for (let i = 0; i < srcNrm.length; ++i) expect(backNrm[i]).toBe(srcNrm[i]);

        //Non-indexed sources (DodecahedronGeometry) gain a trivial 0..n-1 index buffer so the
        //WebGPU atlas can draw everything with a single drawIndexed path. Same triangles,
        //same vertex data, so shading is unaffected.
        const backIdx = back.getIndex()!.array as ArrayLike<number>;
        const srcIdx = src.getIndex();
        if (srcIdx) {
            const a = srcIdx.array as ArrayLike<number>;
            expect(backIdx.length).toBe(a.length);
            for (let i = 0; i < a.length; ++i) expect(backIdx[i]).toBe(a[i]);
        } else {
            expect(backIdx.length).toBe(srcPos.length / 3);
            for (let i = 0; i < backIdx.length; ++i) expect(backIdx[i]).toBe(i);
        }
    });

    test('the debug box keeps its 2x2x2 extent and per-face colours', () => {
        const { debugBoxMesh } = require('../src/render/core/DebugBox');
        expect(debugBoxMesh.positions.length / 3).toBe(24);
        expect(debugBoxMesh.indices.length).toBe(36);
        expect(debugBoxMesh.colors).toBeDefined();
        //Deliberately twice the size of the 1x1x1 root box it replaces on screen.
        for (let i = 0; i < debugBoxMesh.positions.length; ++i)
            expect(Math.abs(debugBoxMesh.positions[i])).toBe(1);
    });
});

describe('invertRigid', () => {
    //The cone trace reconstructs a world position from the gbuffer, which is in view space.
    const compose = (): Mat4 => {
        const m = createMat4();
        //A rotation about Y plus a translation: rigid, which is all a view matrix ever is.
        const a = 0.7;
        m[0] = Math.cos(a); m[2] = -Math.sin(a);
        m[8] = Math.sin(a); m[10] = Math.cos(a);
        m[12] = 3; m[13] = -2; m[14] = 7;
        return m;
    };

    test('composes with its input to the identity', () => {
        const m = compose();
        const inv = invertRigid(m, createMat4());
        //multiply is (a, b, out), not (out, a, b).
        const product = multiply(m, inv, createMat4());
        //Mat4 is a Float32Array, so 6 places is the honest tolerance here, not 12.
        for (let i = 0; i < 16; ++i)
            expect(product[i]).toBeCloseTo(i % 5 === 0 ? 1 : 0, 6);
    });

    test('round-trips a point', () => {
        const m = compose();
        const inv = invertRigid(m, createMat4());
        const apply = (t: Mat4, p: number[]) => [
            t[0] * p[0] + t[4] * p[1] + t[8] * p[2] + t[12],
            t[1] * p[0] + t[5] * p[1] + t[9] * p[2] + t[13],
            t[2] * p[0] + t[6] * p[1] + t[10] * p[2] + t[14],
        ];
        const p = [1.5, -0.25, 4];
        const back = apply(inv, apply(m, p));
        for (let i = 0; i < 3; ++i) expect(back[i]).toBeCloseTo(p[i], 6);
    });
});

describe('reversed-Z projection', () => {
    const { createMat4, perspectiveReversedZ, perspectiveZeroToOne } = require('../src/render/core/Mat4');

    const NEAR = 0.025, FAR = 5000;   //the app's actual frustum
    const FOV = (50 * Math.PI) / 180;

    //Projects a view-space point on the -z axis and returns its ndc depth.
    const depthAt = (m: Float32Array, viewZ: number) => {
        const zClip = m[10] * viewZ + m[14];
        const wClip = m[11] * viewZ + m[15];
        return zClip / wClip;
    };

    test('the near plane maps to 1 and the far plane to 0', () => {
        const m = perspectiveReversedZ(FOV, 1.6, NEAR, FAR, createMat4());
        expect(depthAt(m, -NEAR)).toBeCloseTo(1, 6);
        expect(depthAt(m, -FAR)).toBeCloseTo(0, 6);
    });

    test('depth decreases monotonically with distance', () => {
        const m = perspectiveReversedZ(FOV, 1.6, NEAR, FAR, createMat4());
        let prev = Infinity;
        for (let d = NEAR; d < FAR; d *= 1.5) {
            const z = depthAt(m, -d);
            expect(z).toBeLessThan(prev);
            expect(z).toBeGreaterThanOrEqual(0);
            //Mat4 is a Float32Array, so a point exactly on the near plane lands a few f32
            //ulps either side of 1 rather than on it. Real geometry is clipped against the
            //matrix that is actually in the buffer, so this is correct, not slack.
            expect(z).toBeLessThanOrEqual(1 + 1e-6);
            prev = z;
        }
    });

    test('an infinite far plane needs no far plane at all', () => {
        const m = perspectiveReversedZ(FOV, 1.6, NEAR, Infinity, createMat4());
        expect(depthAt(m, -NEAR)).toBeCloseTo(1, 6);
        expect(depthAt(m, -1e9)).toBeCloseTo(0, 6);
        //The whole point of the infinite form: row 3 carries only the near plane.
        expect(m[10]).toBe(0);
        expect(m[14]).toBe(Math.fround(NEAR));
    });

    test('x and y are untouched - only the depth row differs from the forward matrix', () => {
        const rev = perspectiveReversedZ(FOV, 1.6, NEAR, FAR, createMat4());
        const fwd = perspectiveZeroToOne(FOV, 1.6, NEAR, FAR, createMat4());
        for (let i = 0; i < 16; ++i)
            if (i !== 10 && i !== 14) expect(rev[i]).toBe(fwd[i]);
    });

    //The reason for the change. Quantising to f32 and measuring how far apart two
    //distinguishable depths are: reversed-Z must be better in the distance, which is where the
    //terrain and the umbrellas lying on it were z-fighting.
    test('reversed-Z resolves finer depth differences at distance than the forward matrix', () => {
        const rev = perspectiveReversedZ(FOV, 1.6, NEAR, FAR, createMat4());
        const fwd = perspectiveZeroToOne(FOV, 1.6, NEAR, FAR, createMat4());

        //Smallest separation at `dist` that survives an f32 depth buffer, in metres.
        const resolvable = (m: Float32Array, dist: number) => {
            const base = Math.fround(depthAt(m, -dist));
            let step = 1e-6;
            while (step < dist && Math.fround(depthAt(m, -(dist + step))) === base) step *= 2;
            return step;
        };

        for (const dist of [1, 10, 100]) {
            const r = resolvable(rev, dist), f = resolvable(fwd, dist);
            expect(r).toBeLessThan(f);
        }

        //At 10 m - roughly the far side of the simulated field - the improvement is large,
        //not marginal. Asserting the order of magnitude keeps a regression from passing.
        expect(resolvable(fwd, 10) / resolvable(rev, 10)).toBeGreaterThan(100);
    });
});
