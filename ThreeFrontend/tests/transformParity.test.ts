//Parity gate for the primitive -> model matrix decode.
//
//This is the highest-risk piece of math in the renderer port: the half-length recentring for
//cylinders and roots reads the matrix AFTER scaling, and getting that wrong offsets every
//organ by half its length in a way that still looks plausible on screen. These tests pin the
//neutral implementation against the exact THREE.Matrix4 chain that ThreeSceneFn.tsx used.

import * as THREE from 'three';
import { Organs, Primitives, Cylinder, Box, Rectangle } from '../src/helpers/Primitives';
import { createMat4, fromRowMajor3x4, normalMatrix3, perspectiveZeroToOne } from '../src/render/core/Mat4';
import { decodeCylinderish, decodePlanar } from '../src/render/core/PrimitiveTransform';

/** The exact expression from ThreeSceneFn.tsx buildObject3D(), kept as the reference. */
function threeReferenceCylinderish(p: { affineTransform: Float32Array; radius: number; length: number }) {
    const matrix = new THREE.Matrix4().fromArray([...Array.from(p.affineTransform), 0, 0, 0, 1]).transpose();
    return matrix
        .scale(new THREE.Vector3(p.radius, p.length, p.radius))
        .setPosition(new THREE.Vector3(
            p.affineTransform[3] + matrix.elements[4] * 0.5,
            p.affineTransform[7] + matrix.elements[5] * 0.5,
            p.affineTransform[11] + matrix.elements[6] * 0.5));
}

function threeReferencePlanar(p: { affineTransform: Float32Array }) {
    return new THREE.Matrix4().fromArray([...Array.from(p.affineTransform), 0, 0, 0, 1]).transpose();
}

//A spread of transforms: axis-aligned, rotated, non-uniformly scaled, translated far from
//the origin, and one left-handed case (V7 laminae have a negative determinant).
const TRANSFORMS: number[][] = [
    [1, 0, 0, 0,   0, 1, 0, 0,   0, 0, 1, 0],
    [1, 0, 0, 2.5, 0, 1, 0, 0.75, 0, 0, 1, -3.25],
    [0, -1, 0, 1,  1, 0, 0, 2,    0, 0, 1, 3],
    [0.7071, -0.7071, 0, 0.1,  0.7071, 0.7071, 0, 0.2,  0, 0, 1, 0.3],
    [0.36, 0.48, -0.8, 5,  -0.8, 0.6, 0, -2,  0.48, 0.64, 0.6, 11],
    //left-handed: negated middle row, as ExportV7 builds for leaves
    [1, 0, 0, 0.5,  0, -1, 0, 1.5,  0, 0, 1, 2.5],
];

function cyl(t: number[], radius: number, length: number): Cylinder {
    return {
        type: Primitives.Cylinder,
        organ: Organs.Stem,
        affineTransform: new Float32Array(t),
        length, radius,
        color: '#123456',
        stats: new Float32Array(9),
    };
}

function box(t: number[], radius: number, length: number): Box {
    return {
        type: Primitives.Box,
        organ: Organs.Root,
        affineTransform: new Float32Array(t),
        length, radius,
        stats: new Float32Array(9),
    };
}

function rect(t: number[]): Rectangle {
    return {
        type: Primitives.Rectangle,
        organ: Organs.Leaf,
        affineTransform: new Float32Array(t),
        color: '#00ff00',
        stats: new Float32Array(9),
    };
}

function expectMatrix(actual: Float32Array, expected: THREE.Matrix4, label: string) {
    for (let i = 0; i < 16; ++i)
        expect([label, i, actual[i]]).toEqual([label, i, expect.closeTo(expected.elements[i], 5) as any]);
}

describe('decodeCylinderish matches the THREE.Matrix4 chain', () => {
    const sizes: [number, number][] = [[1, 1], [0.004, 0.12], [0.25, 3.5], [0, 0.5]];

    test('cylinders', () => {
        TRANSFORMS.forEach((t, ti) =>
            sizes.forEach(([r, l], si) => {
                const out = decodeCylinderish(cyl(t, r, l), createMat4());
                expectMatrix(out, threeReferenceCylinderish(cyl(t, r, l)), `cyl t${ti} s${si}`);
            }));
    });

    test('roots (boxes) take the same path', () => {
        TRANSFORMS.forEach((t, ti) =>
            sizes.forEach(([r, l], si) => {
                const out = decodeCylinderish(box(t, r, l), createMat4());
                expectMatrix(out, threeReferenceCylinderish(box(t, r, l)), `box t${ti} s${si}`);
            }));
    });

    test('the recentring offset really is post-scale', () => {
        //Guards the specific bug this extraction exists to prevent: reading the Y basis
        //vector before scaling would drop the length factor, so a long organ would be
        //visibly misplaced. Compare against a deliberately wrong implementation.
        const p = cyl([0, -1, 0, 1, 1, 0, 0, 2, 0, 0, 1, 3], 0.05, 4);
        const correct = decodeCylinderish(p, createMat4());

        const wrong = fromRowMajor3x4(p.affineTransform, createMat4());
        const preScaleY = [wrong[4], wrong[5], wrong[6]];
        expect(correct[12]).not.toBeCloseTo(p.affineTransform[3] + preScaleY[0] * 0.5, 6);
    });
});

describe('decodePlanar matches the THREE.Matrix4 chain', () => {
    test('leaves use the wire transform unchanged', () => {
        TRANSFORMS.forEach((t, ti) => {
            const out = decodePlanar(rect(t), createMat4());
            expectMatrix(out, threeReferencePlanar(rect(t)), `rect t${ti}`);
        });
    });
});

describe('normalMatrix3 matches THREE.Matrix3.getNormalMatrix up to sign and scale', () => {
    test('transformed normals point the same way', () => {
        for (const t of TRANSFORMS) {
            const m = fromRowMajor3x4(t, createMat4());
            const mine = normalMatrix3(m, new Float32Array(9));

            const three = new THREE.Matrix3().getNormalMatrix(new THREE.Matrix4().fromArray(Array.from(m)));

            //normalMatrix3 returns the cofactor matrix, which is det * inverse-transpose.
            //After normalization the |det| cancels, so compare normalized directions. The
            //sign flips for left-handed transforms, which the shader's front_facing test
            //absorbs, so compare absolute alignment.
            for (const n of [[1, 0, 0], [0, 1, 0], [0, 0, 1], [0.577, 0.577, 0.577]]) {
                const a = applyMat3(mine, n);
                const b = new THREE.Vector3(n[0], n[1], n[2]).applyMatrix3(three).normalize();
                normalize(a);
                const dot = Math.abs(a[0] * b.x + a[1] * b.y + a[2] * b.z);
                expect(dot).toBeCloseTo(1, 5);
            }
        }
    });
});

describe('perspectiveZeroToOne', () => {
    test('maps near to 0 and far to 1, unlike the OpenGL convention', () => {
        const near = 0.025, far = 5000;
        const m = perspectiveZeroToOne((50 * Math.PI) / 180, 16 / 9, near, far, createMat4());

        const projectZ = (z: number) => {
            //view-space point on the -Z axis
            const clipZ = m[10] * -z + m[14];
            const clipW = -(-z) * 1 + 0;   // out[11] = -1
            return clipZ / (clipW === 0 ? 1e-9 : clipW);
        };

        expect(projectZ(near)).toBeCloseTo(0, 5);
        expect(projectZ(far)).toBeCloseTo(1, 4);

        //three's equivalent maps near to -1; confirm we are deliberately different.
        const cam = new THREE.PerspectiveCamera(50, 16 / 9, near, far);
        cam.updateProjectionMatrix();
        const e = cam.projectionMatrix.elements;
        const threeNear = (e[10] * -near + e[14]) / near;
        expect(threeNear).toBeCloseTo(-1, 5);
    });
});

function applyMat3(m: Float32Array, v: number[]): number[] {
    return [
        m[0] * v[0] + m[3] * v[1] + m[6] * v[2],
        m[1] * v[0] + m[4] * v[1] + m[7] * v[2],
        m[2] * v[0] + m[5] * v[1] + m[8] * v[2],
    ];
}

function normalize(v: number[]) {
    const l = Math.hypot(v[0], v[1], v[2]) || 1;
    v[0] /= l; v[1] /= l; v[2] /= l;
}
