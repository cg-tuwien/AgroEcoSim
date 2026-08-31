//Smoke test for the renderer refactor's module graph.
//
//The biggest risk in moving the scene graph out of appstate is not a wrong pixel, it is a
//module that throws while loading or an import cycle that leaves a binding undefined at
//first use. jsdom has no WebGL so the renderer cannot actually be mounted here, but every
//module can be loaded and every non-rendering object constructed, which is what catches
//those failures.

describe('module graph loads', () => {
    test('appstate initialises without a renderer present', () => {
        const appstate = require('../src/appstate').default;
        expect(appstate).toBeDefined();
        //The default seed is pushed at module scope, after the singleton is exported.
        expect(appstate.seeds.value.length).toBe(1);
        //The scene graph is no longer appstate's business.
        expect((appstate as any).objPlants).toBeUndefined();
        expect((appstate as any).transformControls).toBeUndefined();
    });

    test('the static geometry table is published into the neutral store', () => {
        require('../src/render/core/StaticGeometry');
        const { getGeometry, hasGeometry } = require('../src/render/core/GeometryStore');
        const { Geom, STATIC_GEOMETRY_COUNT } = require('../src/render/core/GeometryIds');

        for (let id = 0; id < STATIC_GEOMETRY_COUNT; ++id)
            expect(hasGeometry(id)).toBe(true);

        //Spot-check that the arrays are real geometry, not placeholders.
        const cyl = getGeometry(Geom.Cylinder);
        expect(cyl.positions.length).toBeGreaterThan(0);
        expect(cyl.indices.length % 3).toBe(0);
        expect(cyl.normals.length).toBe(cyl.positions.length);
    });

    //Regression: the table used to be filled only by the Three adapter, so a WebGPU-only
    //load resolved every geometry id to undefined. Loading the generators alone must suffice.
    test('the geometry table is filled without loading the Three adapter', () => {
        jest.resetModules();
        require('../src/render/core/StaticGeometry');
        const { hasGeometry } = require('../src/render/core/GeometryStore');
        const { STATIC_GEOMETRY_COUNT } = require('../src/render/core/GeometryIds');

        for (let id = 0; id < STATIC_GEOMETRY_COUNT; ++id)
            expect(hasGeometry(id)).toBe(true);

        //And the oracle must not be what filled it.
        expect(Object.keys(require.cache).some(k => k.includes('ThreeGeometrySource'))).toBe(false);
    });

    test('the renderer registry starts empty and is readable', () => {
        const { activeRenderer, requestRender } = require('../src/render/IViewportRenderer');
        expect(activeRenderer.current).toBeUndefined();
        //Must be a safe no-op before anything is mounted - appstate and the selection FSM
        //both call through it during startup.
        expect(() => requestRender()).not.toThrow();
    });

    test('selectable objects construct and run their FSM with no renderer mounted', () => {
        const appstate = require('../src/appstate').default;
        const { Seed } = require('../src/helpers/Seed');
        const { Obstacle } = require('../src/helpers/Obstacle');

        const seed = new Seed('sp', 1, 2, 3, 0, false);
        expect(seed.worldPosition()).toEqual({ x: 1, y: 2, z: 3 });

        //selecthover asks the active renderer for a gizmo; with none mounted it must not throw.
        expect(() => { seed.selecthover(); seed.unhover(); }).not.toThrow();
        expect(seed.state.value).toBe('none');

        const wall = new Obstacle('wall', 1, 0, 2, 0, 0, 4, 3, 0.4, undefined, undefined);
        expect(wall.geomId()).toBeDefined();
        //save() used to throw on wall/umbrella because vertices is undefined there.
        expect(() => wall.save()).not.toThrow();
        expect(appstate).toBeDefined();
    });

    test('obstacle model matrices match the old setupWall / setupUmbrella', () => {
        const { Obstacle } = require('../src/helpers/Obstacle');
        const { createMat4 } = require('../src/render/core/Mat4');

        //Matrices are Float32Array, so exact equality would fail on any value that is not
        //representable in f32 (0.4, 2.2). Compare with a tolerance rather than rounding the
        //values, since f32 storage is what both backends actually upload.
        const near = (actual: number[], expected: number[]) =>
            actual.forEach((v, i) => expect(v).toBeCloseTo(expected[i], 6));

        const wall = new Obstacle('wall', 1, 0, 2, 0, 0, 4, 3, 0.4, undefined, undefined);
        const m = wall.writeModel(createMat4());
        near([m[0], m[5], m[10]], [4, 3, 0.4]);          // scale(l, h, t)
        near([m[12], m[13], m[14]], [1, 0, 2]);

        const umbrella = new Obstacle('umbrella', 5, 0, 6, 0, 0, 2, 2.2, 0.08, undefined, undefined);
        const u = umbrella.writeModel(createMat4());
        near([u[0], u[5], u[10]], [1, 2.2, 1]);           // scale(l/2, h, l/2)
        //Umbrellas are anchored at the post, so drawing offsets them up by their height.
        near([u[12], u[13], u[14]], [5, 2.2, 6]);
    });

    test('terrain items expose neutral drawing accessors', () => {
        const { BoxTerrainItem } = require('../src/helpers/Terrain');
        const { createMat4 } = require('../src/render/core/Mat4');
        const { Geom } = require('../src/render/core/GeometryIds');

        //data = [px, py, pz, sx, sy, sz, qx, qy, qz, qw]
        const box = new BoxTerrainItem('t0', new Float32Array([1, 5, 2, 10, 4, 8, 0, 0, 0, 1]), 0);
        expect(box.geomId()).toBe(Geom.BoxCorner);

        const m = box.writeModel(createMat4());
        expect([m[0], m[5], m[10]]).toEqual([10, 4, 8]);
        //BoxCorner's origin is the min corner, so a top-anchored box drops by its height.
        expect([m[12], m[13], m[14]]).toEqual([1, 1, 2]);
    });
});
