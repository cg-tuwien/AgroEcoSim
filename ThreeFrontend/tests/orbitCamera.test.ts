//Tests for the WebGPU orbit camera's interaction directions.
//
//Direction bugs are invisible to a type checker and to a static screenshot - a wheel that
//zooms the wrong way renders perfectly, it just behaves backwards - so they are pinned here
//against OrbitControls' conventions.

import { OrbitCamera } from '../src/render/webgpu/OrbitCamera';
import type { CameraPose } from '../src/render/IViewportRenderer';

const pose: CameraPose = {
    position: { x: 0, y: 3, z: 5 },
    target: { x: 0, y: 0, z: 0 },
    up: { x: 0, y: 1, z: 0 },
    fovY: 50, near: 0.025, far: 5000,
};

const distance = (c: OrbitCamera) => {
    const p = c.getPose();
    return Math.hypot(p.position.x - p.target.x, p.position.y - p.target.y, p.position.z - p.target.z);
};

describe('OrbitCamera dolly', () => {
    test('positive ticks move AWAY from the target', () => {
        //The wheel handler passes +1 for deltaY > 0, i.e. a scroll down. OrbitControls
        //dollies in on deltaY < 0, so scrolling down must increase the distance.
        const c = new OrbitCamera(pose);
        const before = distance(c);
        c.dolly(1);
        expect(distance(c)).toBeGreaterThan(before);
    });

    test('negative ticks move TOWARDS the target', () => {
        const c = new OrbitCamera(pose);
        const before = distance(c);
        c.dolly(-1);
        expect(distance(c)).toBeLessThan(before);
    });

    test('dolly is symmetric, so a wheel nudge either way is reversible', () => {
        const c = new OrbitCamera(pose);
        const before = distance(c);
        c.dolly(1);
        c.dolly(-1);
        expect(distance(c)).toBeCloseTo(before, 6);
    });

    test('never crosses the target or the far plane', () => {
        const c = new OrbitCamera(pose);
        for (let i = 0; i < 500; ++i) c.dolly(-1);
        expect(distance(c)).toBeGreaterThan(0);
        for (let i = 0; i < 2000; ++i) c.dolly(1);
        expect(distance(c)).toBeLessThan(pose.far);
    });
});

describe('OrbitCamera rotate', () => {
    test('keeps the distance and the target', () => {
        const c = new OrbitCamera(pose);
        const before = distance(c);
        c.rotate(120, 40, 800);
        expect(distance(c)).toBeCloseTo(before, 5);
        expect(c.getPose().target).toEqual(pose.target);
    });

    test('dragging right swings the camera the same way OrbitControls does', () => {
        //OrbitControls: theta -= 2*PI*dx/height. With theta measured as atan2(x, z) from the
        //target, a rightward drag decreases theta, moving the camera towards -x.
        const c = new OrbitCamera(pose);
        c.rotate(100, 0, 800);
        expect(c.getPose().position.x).toBeLessThan(0);
    });

    test('dragging down lifts the camera, dragging up lowers it', () => {
        //OrbitControls: phi -= 2*PI*dy/height, so a downward drag decreases the polar angle
        //and swings the camera towards the top of the scene.
        const up = new OrbitCamera(pose);
        up.rotate(0, 60, 800);
        expect(up.getPose().position.y).toBeGreaterThan(pose.position.y);

        const down = new OrbitCamera(pose);
        down.rotate(0, -60, 800);
        expect(down.getPose().position.y).toBeLessThan(pose.position.y);
    });

    test('the polar angle is clamped at both poles rather than flipping through them', () => {
        const c = new OrbitCamera(pose);
        const r = distance(c);

        //Far past vertical in both directions. Going under the target is allowed - it is what
        //OrbitControls does - but the camera must never invert or produce a degenerate view.
        for (let i = 0; i < 50; ++i) c.rotate(0, -100, 800);
        expect(distance(c)).toBeCloseTo(r, 5);
        expect(Math.abs(c.getPose().position.y)).toBeLessThanOrEqual(r + 1e-6);

        for (let i = 0; i < 100; ++i) c.rotate(0, 100, 800);
        expect(distance(c)).toBeCloseTo(r, 5);
        expect(Number.isFinite(c.getPose().position.y)).toBe(true);
    });
});

describe('OrbitCamera pan', () => {
    test('moves the target, and dragging right shifts the view right', () => {
        const c = new OrbitCamera(pose);
        c.pan(50, 0, 800);
        const t = c.getPose().target;
        //Camera looks down -Z from +Z, so its right axis is +X; dragging right moves the
        //target towards -X, which slides the scene rightwards on screen.
        expect(t.x).toBeLessThan(0);
        expect(Math.abs(t.z)).toBeLessThan(1e-6);
    });

    test('pan distance scales with the distance to the target', () => {
        const near = new OrbitCamera(pose);
        const far = new OrbitCamera(pose);
        far.dolly(20);   // pull back

        near.pan(50, 0, 800);
        far.pan(50, 0, 800);
        //Panning a fixed number of pixels should cover more world space when zoomed out.
        expect(Math.abs(far.getPose().target.x)).toBeGreaterThan(Math.abs(near.getPose().target.x));
    });
});

describe('OrbitCamera pose round-trip', () => {
    test('setPose then getPose preserves the camera', () => {
        const c = new OrbitCamera();
        c.setPose(pose);
        const got = c.getPose();
        expect(got.position.x).toBeCloseTo(pose.position.x, 5);
        expect(got.position.y).toBeCloseTo(pose.position.y, 5);
        expect(got.position.z).toBeCloseTo(pose.position.z, 5);
        expect(got.fovY).toBe(pose.fovY);
    });
});
