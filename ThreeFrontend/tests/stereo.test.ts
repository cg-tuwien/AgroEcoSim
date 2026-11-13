//The stereo interleave and the off-axis eye frusta. Mirrors GodotFrontend/Tests/StereoTests.cs, so
//both viewers can be checked against the same statements.

import { convergenceDistance, eyeAt, eyeFrustum, loadStereoOptions, MONO, saveStereoOptions, stereoActive } from '../src/render/core/Stereo';

describe('checkerboard interleave', () => {
    test('even rows start with the left eye, odd rows with the right', () => {
        expect([0, 1, 2, 3].map(x => eyeAt(x, 0, false))).toEqual(['left', 'right', 'left', 'right']);
        expect([0, 1, 2, 3].map(x => eyeAt(x, 1, false))).toEqual(['right', 'left', 'right', 'left']);
        expect(eyeAt(2559, 1535, false)).toBe('left');
    });

    test('swapping exchanges every pixel', () => {
        for (let y = 0; y < 3; ++y)
            for (let x = 0; x < 3; ++x)
                expect(eyeAt(x, y, true)).not.toBe(eyeAt(x, y, false));
    });
});

describe('off-axis eye frusta', () => {
    const sep = 0.06, conv = 2.5, near = 0.05, fov = 50, aspect = 1.6;
    const tanY = Math.tan((fov * Math.PI) / 360);
    //Where a point at lateral position px and depth d lands in an eye's NDC.
    const ndc = (eye: 'left' | 'right', px: number, depth: number) => {
        const f = eyeFrustum(eye, sep, conv, near, fov, aspect);
        return ((px - f.lateralOffset) * near / depth - f.frustumShift) / (near * tanY * aspect);
    };

    test('both eyes see the convergence plane at the same place', () => {
        for (const x of [-0.7, 0, 0.4]) {
            const px = x * conv * tanY * aspect;
            expect(ndc('left', px, conv)).toBeCloseTo(ndc('right', px, conv), 12);
            expect(ndc('left', px, conv)).toBeCloseTo(x, 12);
        }
    });

    test('nearer points have crossed disparity, farther ones uncrossed', () => {
        expect(ndc('left', 0, 1)).toBeGreaterThan(ndc('right', 0, 1));
        expect(ndc('left', 0, 10)).toBeLessThan(ndc('right', 0, 10));
    });

    test('matches three.js StereoCamera', () => {
        //eyeSepOnProjection = eyeSep/2 * near / focus; element 8 = (xmax + xmin) / (xmax - xmin)
        const s = 0.064, focus = 10, n = 0.1, a = 1.25;
        const ymax = n * Math.tan((fov * Math.PI) / 360);
        const e = s / 2 * n / focus;
        const el8 = (xmin: number, xmax: number) => (xmax + xmin) / (xmax - xmin);
        expect(eyeFrustum('left', s, focus, n, fov, a).ndcShift).toBeCloseTo(el8(-ymax * a + e, ymax * a + e), 12);
        expect(eyeFrustum('right', s, focus, n, fov, a).ndcShift).toBeCloseTo(el8(-ymax * a - e, ymax * a - e), 12);
        expect(eyeFrustum('left', s, focus, n, fov, a).lateralOffset).toBeCloseTo(-s / 2, 12);
    });

    test('convergence follows the target or the fixed distance', () => {
        const o = { ...MONO, mode: 'checkerboard' as const };
        expect(convergenceDistance(o, 5.83)).toBeCloseTo(5.83, 12);
        expect(convergenceDistance({ ...o, convergeOnTarget: false, convergence: 3 }, 5.83)).toBe(3);
        expect(convergenceDistance(o, 0)).toBeGreaterThan(0);
        expect(stereoActive(o)).toBe(true);
        expect(stereoActive(MONO)).toBe(false);
        expect(stereoActive({ ...o, mode: 'openxr' })).toBe(false);
    });
});

describe('persistence', () => {
    test('round-trips everything but the test pattern, and never restores OpenXR', () => {
        localStorage.clear();
        expect(loadStereoOptions()).toEqual(MONO);
        saveStereoOptions({ ...MONO, mode: 'checkerboard', eyeSeparation: 0.08, swapEyes: true, testPattern: true });
        expect(loadStereoOptions()).toEqual({ ...MONO, mode: 'checkerboard', eyeSeparation: 0.08, swapEyes: true, testPattern: false });
        saveStereoOptions({ ...MONO, mode: 'openxr' });
        expect(loadStereoOptions().mode).toBe('mono');
    });
});
