//The HUD's object/triangle counters.
//
//Two regressions are guarded here. The counts must come from the real geometry - the old
//hand-written table was indexed by `Primitives` but held legacy V3 wire-code values, so a leaf
//reported 12 triangles instead of 2 and a root box 128 instead of 12. And the walk must be
//memoised: it used to run twice per HUD render, which at the production site's 7,700 plants
//(~1.26M primitives) was ~2.5M iterations every time anything redrew.

import { sceneStats } from '../src/render/core/SceneStats';
import { Primitives } from '../src/helpers/Primitives';
import '../src/render/core/StaticGeometry';
import { getGeometry } from '../src/render/core/GeometryStore';
import { Geom } from '../src/render/core/GeometryIds';
import { triangleCount } from '../src/render/core/Geometry';

const prim = (type: Primitives) => ({ type, organ: 0, affineTransform: new Float32Array(12), stats: undefined }) as any;
const plant = (species: string, prims: any[]) => ({ species, primitives: prims }) as any;

describe('sceneStats', () => {
    test('counts objects across every plant', () => {
        const scene = [
            plant('a', [prim(Primitives.Rectangle), prim(Primitives.Cylinder)]),
            plant('b', [prim(Primitives.Box)]),
        ];
        expect(sceneStats(scene).objects).toBe(3);
    });

    test('triangle counts come from the geometry, not a hand-written table', () => {
        const leaf = triangleCount(getGeometry(Geom.Plane)!);
        const cyl = triangleCount(getGeometry(Geom.Cylinder)!);
        const box = triangleCount(getGeometry(Geom.BoxCentered)!);
        const disk = triangleCount(getGeometry(Geom.CircleHalf)!);

        //The values the old table got wrong, pinned so a silent revert fails here.
        expect(leaf).toBe(2);    // table said 12
        expect(box).toBe(12);    // table said 128
        expect(disk).toBe(12);   // table said 0
        expect(cyl).toBe(32);    // the one it had right

        const scene = [plant('a', [
            prim(Primitives.Rectangle), prim(Primitives.Cylinder),
            prim(Primitives.Box), prim(Primitives.Disk),
        ])];
        expect(sceneStats(scene).triangles).toBe(leaf + cyl + box + disk);
    });

    test('spheres draw nothing and are not counted', () => {
        //BuildScene emits no instance for a bud, so counting one would overstate the scene.
        const scene = [plant('a', [prim(Primitives.Sphere), prim(Primitives.Cylinder)])];
        expect(sceneStats(scene).objects).toBe(2);
        expect(sceneStats(scene).triangles).toBe(triangleCount(getGeometry(Geom.Cylinder)!));
    });

    test('a species leaf mesh is counted instead of the fallback plane', () => {
        const scene = [plant('birch', [prim(Primitives.Rectangle)])];
        //Stand in for a registered species mesh with a geometry that has a distinct count.
        const asDodecahedron = () => Geom.Dodecahedron;
        const expected = triangleCount(getGeometry(Geom.Dodecahedron)!);
        expect(expected).not.toBe(2);
        expect(sceneStats(scene, asDodecahedron).triangles).toBe(expected);
    });

    test('the walk is memoised on the scene array', () => {
        //Identity is the right key: the decoder returns a fresh array per timestep.
        const scene = [plant('a', [prim(Primitives.Cylinder)])];
        const first = sceneStats(scene);
        expect(sceneStats(scene)).toBe(first);

        const equalButDistinct = [plant('a', [prim(Primitives.Cylinder)])];
        expect(sceneStats(equalButDistinct)).not.toBe(first);
        expect(sceneStats(equalButDistinct)).toEqual(first);
    });

    test('an empty scene is zero, not a crash', () => {
        expect(sceneStats([])).toEqual({ objects: 0, triangles: 0 });
    });
});
