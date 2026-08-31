import { mapNoon } from '../src/render/offline/FrameSource';
//Tests for the renderer-neutral parts of the offline animation renderer.
//Anything touching WebGL is verified through the visual harness instead (tools/visual).

import { estimateVram, OfflineSizeError } from '../src/render/offline/types';
import { StaticCamera, TurntableCamera } from '../src/render/offline/CameraPath';
import type { CameraPose } from '../src/render/IViewportRenderer';

const pose: CameraPose = {
    position: { x: 0, y: 3, z: 5 },
    target: { x: 0, y: 0, z: 0 },
    up: { x: 0, y: 1, z: 0 },
    fovY: 50, near: 0.025, far: 5000,
};

describe('estimateVram', () => {
    test('scales with supersampling squared, not linearly', () => {
        const base = estimateVram({ width: 1920, height: 1080, ssaa: 1, msaa: 1 });
        const two = estimateVram({ width: 1920, height: 1080, ssaa: 2, msaa: 1 });
        //This is the whole reason the figure is surfaced in the UI: doubling the "quality"
        //slider quadruples the allocation, which is how people lose the GL context.
        expect(two).toBe(base * 4);
    });

    test('multisampling multiplies on top of supersampling', () => {
        const ssaa = estimateVram({ width: 1920, height: 1080, ssaa: 2, msaa: 1 });
        const both = estimateVram({ width: 1920, height: 1080, ssaa: 2, msaa: 4 });
        expect(both).toBe(ssaa * 4);
    });

    test('4K at 2x supersampling is the figure worth warning about', () => {
        const mb = estimateVram({ width: 3840, height: 2160, ssaa: 2, msaa: 1 }) / (1024 * 1024);
        expect(mb).toBeGreaterThan(200);
        expect(mb).toBeLessThan(300);
    });
});

describe('OfflineSizeError', () => {
    test('names the offending size and the limit', () => {
        const e = new OfflineSizeError(15360, 8640, 8192);
        expect(e.message).toContain('15360x8640');
        expect(e.message).toContain('8192');
        expect(e.name).toBe('OfflineSizeError');
    });
});

describe('StaticCamera', () => {
    test('returns the same pose for every frame', () => {
        const c = new StaticCamera(pose);
        expect(c.poseAt()).toBe(pose);
    });
});

describe('TurntableCamera', () => {
    const radius = (p: CameraPose) => Math.hypot(
        p.position.x - p.target.x, p.position.y - p.target.y, p.position.z - p.target.z);

    test('keeps the radius and elevation while sweeping the azimuth', () => {
        const c = new TurntableCamera(pose, 1);
        for (let f = 0; f < 8; ++f) {
            const p = c.poseAt(f, 8);
            expect(radius(p)).toBeCloseTo(radius(pose), 5);
            //Elevation is preserved, so height above the target never changes.
            expect(p.position.y).toBeCloseTo(pose.position.y, 5);
            expect(p.target).toEqual(pose.target);
        }
    });

    test('a full turn returns to the starting position', () => {
        const c = new TurntableCamera(pose, 1);
        const first = c.poseAt(0, 4);
        const wrapped = c.poseAt(4, 4);   // frame == total is one complete revolution
        expect(wrapped.position.x).toBeCloseTo(first.position.x, 5);
        expect(wrapped.position.z).toBeCloseTo(first.position.z, 5);
    });

    test('half a turn puts the camera on the opposite side', () => {
        const c = new TurntableCamera(pose, 1);
        const a = c.poseAt(0, 2);
        const b = c.poseAt(1, 2);
        expect(b.position.x).toBeCloseTo(-a.position.x, 5);
        expect(b.position.z).toBeCloseTo(-a.position.z, 5);
    });
});

describe('noon timing', () => {
    //One frame per simulated day, sampled at the same time of day, so what moves in the
    //animation is the growth rather than the sun.
    const history = (count: number) => Array.from({ length: count }, (_, i) => ({ t: i, s: null }));

    test('picks one frame per day, closest to noon', () => {
        //4 hours per tick: a day is 6 steps, at 0, 4, 8, 12, 16, 20 hours. Noon is exact.
        const map = mapNoon(history(18), 0, 17, 4);
        expect(map.map(f => f.historyIndex)).toEqual([3, 9, 15]);
        expect(map.map(f => f.simHours)).toEqual([12, 36, 60]);
    });

    test('nearest, not the first step after noon', () => {
        //5 hours per tick: steps at 0, 5, 10, 15, 20, 25... Day 0's nearest to 12 is 10 (t=2),
        //not 15. Taking the first step past noon would drift further every day.
        const map = mapNoon(history(15), 0, 14, 5);
        expect(map[0].historyIndex).toBe(2);
        expect(map[0].simHours).toBe(10);
    });

    test('a run shorter than a day still yields its best frame', () => {
        const map = mapNoon(history(3), 0, 2, 4);
        expect(map).toHaveLength(1);
        expect(map[0].historyIndex).toBe(2);
    });

    test('respects the from/to range and stays ordered', () => {
        const map = mapNoon(history(30), 7, 20, 4);
        expect(map.length).toBeGreaterThan(0);
        for (const f of map) { expect(f.historyIndex).toBeGreaterThanOrEqual(7); expect(f.historyIndex).toBeLessThanOrEqual(20); }
        const days = map.map(f => Math.floor(f.simHours / 24));
        expect(days).toEqual([...days].sort((a, b) => a - b));
        expect(new Set(days).size).toBe(days.length);
    });

    test('an empty range yields nothing rather than throwing', () => {
        expect(mapNoon(history(5), 4, 2, 4)).toEqual([]);
    });
});
