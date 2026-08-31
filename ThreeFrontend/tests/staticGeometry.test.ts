//The gate for the hand-written generators: every array must match three element-wise.
//
//This is the whole justification for retiring the three-derived bootstrap. Exact equality is
//demanded rather than a tolerance, because a geometry that is merely close produces different
//z-fighting on the coplanar terrain/umbrella pairs and different flat-shaded facets - both of
//which show up as a WebGPU-vs-WebGL parity regression that is very hard to trace back here.

import { staticGeometries as handWritten } from '../src/render/core/StaticGeometry';
import { staticGeometries as fromThree } from '../src/render/three/ThreeGeometrySource';
import { Geom, STATIC_GEOMETRY_COUNT } from '../src/render/core/GeometryIds';
import { MeshData } from '../src/render/core/Geometry';

const NAMES: Record<number, string> = {
    [Geom.BoxCentered]: 'BoxCentered',
    [Geom.BoxCorner]: 'BoxCorner',
    [Geom.BoxBottomCentered]: 'BoxBottomCentered',
    [Geom.Cylinder]: 'Cylinder',
    [Geom.Sphere]: 'Sphere',
    [Geom.Plane]: 'Plane',
    [Geom.CircleHalf]: 'CircleHalf',
    [Geom.CircleUnit]: 'CircleUnit',
    [Geom.Dodecahedron]: 'Dodecahedron',
    [Geom.DebugBox]: 'DebugBox',
};

//Reports the first mismatch with its index instead of dumping thousands of floats, which is
//the difference between a usable failure and an unreadable one.
function expectSameArray(mine: ArrayLike<number> | undefined, theirs: ArrayLike<number> | undefined, what: string) {
    if (theirs === undefined) { expect(mine).toBeUndefined(); return; }
    expect(mine).toBeDefined();
    expect(mine!.length).toBe(theirs.length);

    for (let i = 0; i < theirs.length; ++i)
        if (mine![i] !== theirs[i])
            throw new Error(`${what}[${i}]: got ${mine![i]}, three has ${theirs[i]}`);
}

describe('hand-written static geometry matches three', () => {
    for (let id = 0; id < STATIC_GEOMETRY_COUNT; ++id) {
        const name = NAMES[id] ?? `id ${id}`;

        test(name, () => {
            const mine: MeshData = handWritten[id];
            const ref: MeshData = fromThree[id];
            expect(mine).toBeDefined();
            expect(ref).toBeDefined();

            expect(mine.name).toBe(ref.name);
            expectSameArray(mine.positions, ref.positions, `${name}.positions`);
            expectSameArray(mine.normals, ref.normals, `${name}.normals`);
            expectSameArray(mine.uvs, ref.uvs, `${name}.uvs`);
            expectSameArray(mine.colors, ref.colors, `${name}.colors`);
            expectSameArray(mine.indices, ref.indices, `${name}.indices`);
        });
    }

    //Guards against the table silently gaining a hole, which would make the loop above pass
    //vacuously for the missing slot.
    test('every static slot is populated', () => {
        for (let id = 0; id < STATIC_GEOMETRY_COUNT; ++id) {
            expect(handWritten[id]).toBeDefined();
            expect(handWritten[id].positions.length).toBeGreaterThan(0);
            expect(handWritten[id].indices.length % 3).toBe(0);
        }
    });

    //The DebugBox is shared by reference rather than regenerated, so this asserts the intent
    //rather than the arrays: it must be the same object, not a copy that could drift.
    test('DebugBox is the shared hand-built mesh, not a three-derived copy', () => {
        expect(handWritten[Geom.DebugBox]).toBe(fromThree[Geom.DebugBox]);
    });
});
