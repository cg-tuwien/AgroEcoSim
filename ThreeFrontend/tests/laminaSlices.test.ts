//A long blade is simulated as a chain of leaf organs, and each of them has to be drawn with its
//own slice of the species outline. Drawn with the whole outline each - which is what happened
//before - the blade pinches to a point at every joint and comes out as a row of separate lenses.
//
//The chain position is not on the wire. It is derived from the parent link, which is, and that
//derivation is what these tests pin: it depends on parents preceding children in the record
//order, and on organ indices not being primitive indices once a bud has been dropped.

import BinaryReader from '../src/helpers/BinaryReader';
import { Organs, Primitives, Rectangle } from '../src/helpers/Primitives';
import { PlantModel } from '../src/helpers/Scene';
import { InstanceList } from '../src/render/core/InstanceList';
import { buildPlants } from '../src/render/core/BuildScene';
import { Geom, leafGeomKey } from '../src/render/core/GeometryIds';
import { VisualMappingOptions } from '../src/render/core/PlantColors';
import { sceneStats } from '../src/render/core/SceneStats';
import { setStaticGeometry } from '../src/render/core/GeometryStore';

//--- a hand-built V7 frame ------------------------------------------------------------------

class Writer {
    bytes: number[] = [];
    u8(v: number) { this.bytes.push(v & 0xff); return this; }
    i32(v: number) { const b = new Uint8Array(4); new DataView(b.buffer).setInt32(0, v, true); this.bytes.push(...b); return this; }
    u32(v: number) { const b = new Uint8Array(4); new DataView(b.buffer).setUint32(0, v, true); this.bytes.push(...b); return this; }
    f32(v: number) { const b = new Uint8Array(4); new DataView(b.buffer).setFloat32(0, v, true); this.bytes.push(...b); return this; }
    str(s: string) { const e = new TextEncoder().encode(s); this.u8(e.length); this.bytes.push(...e); return this; }
    lamina(parent: number, organ: Organs) {
        this.i32(parent).u8(organ);
        for (let i = 0; i < 12; ++i) this.f32(i);
        return this.u8(0x22).u8(0xaa).u8(0x33); //no analytics: colour only
    }
    cylinder(parent: number, organ: Organs) {
        this.i32(parent).u8(organ).f32(0.05).f32(0.001);
        for (let i = 0; i < 12; ++i) this.f32(i);
        return this.u8(0x88).u8(0x66).u8(0x44);
    }
    bud(parent: number) {
        return this.i32(parent).u8(Organs.Bud).f32(1).f32(2).f32(3).f32(0.01);
    }
    done() { return new Uint8Array(this.bytes); }
}

/** Stem, bud, petiole, then a four-segment blade - the bud is what pulls organ and primitive indices apart. */
function frameWithABlade() {
    const w = new Writer();
    w.u8(7).u8(0).u32(1);          //version, flags (no roots, no analytics), one plant
    w.str('Carex').u32(8);         //eight organs
    w.cylinder(-1, Organs.Stem);   //organ 0, primitive 0
    w.bud(0);                      //organ 1, dropped
    w.cylinder(0, Organs.Petiole); //organ 2, primitive 1
    w.lamina(2, Organs.Leaf);      //organ 3, primitive 2 - blade base
    w.lamina(3, Organs.Leaf);      //organ 4, primitive 3
    w.lamina(4, Organs.Leaf);      //organ 5, primitive 4
    w.lamina(5, Organs.Leaf);      //organ 6, primitive 5 - blade tip
    w.lamina(0, Organs.FlowerPadel); //organ 7, primitive 6 - a petal is never a chain
    return w.done();
}

describe('blade chain position', () => {
    const scene = new BinaryReader(frameWithABlade()).readAgroScene() as PlantModel[];
    const primitives = scene[0].primitives;
    const slices = primitives
        .filter(p => p.type === Primitives.Rectangle)
        .map(p => (p as Rectangle).laminaSlice);

    test('a plant decodes with its bud dropped', () => {
        expect(scene.length).toBe(1);
        expect(primitives.length).toBe(7);
    });

    test('segments count up from the sheath, and a petal is not a segment', () => {
        expect(slices).toEqual([0, 1, 2, 3, 0]);
    });

    test('a leaf hanging off a petiole starts a new chain', () => {
        //Primitive 2 is the blade base; its parent is a petiole, not a leaf.
        expect((primitives[2] as Rectangle).laminaSlice).toBe(0);
    });
});

//--- what the draw list does with it ---------------------------------------------------------

const T = () => new Float32Array([1, 0, 0, 1, 0, 1, 0, 2, 0, 0, 1, 3]);
const stats = () => new Float32Array([0.5, 0.6, 1.2, 0, 0.4, 1, 2, 0.5, 0.25]);
const segment = (slice: number): Rectangle =>
    ({ type: Primitives.Rectangle, organ: Organs.Leaf, affineTransform: T(), color: '#22aa33', stats: stats(), laminaSlice: slice });

describe('buildPlants draws a blade a slice at a time', () => {
    test('each segment asks for its own geometry', () => {
        const asked: Array<[string, number]> = [];
        const out = new InstanceList();
        buildPlants(out, {
            scene: [{ species: 'Carex', primitives: [segment(0), segment(1), segment(2)] }],
            visualMapping: VisualMappingOptions.Natural,
            debugBoxes: false,
            showLeaves: true,
            showRoots: true,
            leafGeom: (s: string, slice: number) => { asked.push([s, slice]); return Geom.FirstDynamic + slice; },
            petalGeom: () => Geom.Plane as number,
        });

        expect(asked).toEqual([['Carex', 0], ['Carex', 1], ['Carex', 2]]);
        expect(out.items.slice(0, 3).map(i => i.geom))
            .toEqual([Geom.FirstDynamic, Geom.FirstDynamic + 1, Geom.FirstDynamic + 2]);
    });

    test('a petal still takes the petal outline, whatever its slice reads', () => {
        const out = new InstanceList();
        buildPlants(out, {
            scene: [{ species: 'Carex', primitives: [{ ...segment(2), organ: Organs.FlowerPadel }] }],
            visualMapping: VisualMappingOptions.Natural,
            debugBoxes: false,
            showLeaves: true,
            showRoots: true,
            leafGeom: () => { throw new Error('a petal must not resolve a leaf slice'); },
            petalGeom: () => Geom.CircleHalf as number,
        });

        expect(out.items[0].geom).toBe(Geom.CircleHalf);
    });
});

describe('sceneStats counts each slice for itself', () => {
    test('slices of different triangle counts are not collapsed into one figure', () => {
        //Two static slots stand in for two slices of unequal weight.
        setStaticGeometry(Geom.Plane, { positions: new Float32Array(12), indices: new Uint32Array(6), normals: new Float32Array(12), name: 'two' });
        setStaticGeometry(Geom.CircleHalf, { positions: new Float32Array(9), indices: new Uint32Array(3), normals: new Float32Array(9), name: 'one' });

        const stats = sceneStats(
            [{ species: 'Carex', primitives: [segment(0), segment(1)] }],
            (_s: string, slice: number) => (slice === 0 ? Geom.Plane : Geom.CircleHalf));

        //Two triangles for the base slice, one for the tip - not two of either.
        expect(stats.triangles).toBe(3);
    });

    test('the registry key is what the renderer and the counter agree on', () => {
        expect(leafGeomKey('Carex', 3)).toBe('leaf:Carex:3');
    });
});
