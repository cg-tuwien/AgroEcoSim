//Tests for the neutral draw-list builders.
//
//These pin the behaviours that are easy to lose when the same logic is reimplemented in a
//second renderer: buds being skipped without leaving a stale pick slot, the showLeaves /
//showRoots toggles marking instances hidden rather than dropping them (so pick ids stay
//aligned with the Three path's mesh order), and debugBoxes overriding every organ.

import { Organs, Primitives, Cylinder, Box, Rectangle, Sphere } from '../src/helpers/Primitives';
import { PlantModel } from '../src/helpers/Scene';
import { InstanceList, IFlags } from '../src/render/core/InstanceList';
import { buildPlants } from '../src/render/core/BuildScene';
import { Geom } from '../src/render/core/GeometryIds';
import { VisualMappingOptions, leafColor, stemColor } from '../src/render/core/PlantColors';

const T = () => new Float32Array([1, 0, 0, 1, 0, 1, 0, 2, 0, 0, 1, 3]);
const stats = () => new Float32Array([0.5, 0.6, 1.2, 0, 0.4, 1, 2, 0.5, 0.25]);

const leaf = (): Rectangle => ({ type: Primitives.Rectangle, organ: Organs.Leaf, affineTransform: T(), color: '#22aa33', stats: stats() });
const stem = (): Cylinder => ({ type: Primitives.Cylinder, organ: Organs.Stem, affineTransform: T(), length: 0.5, radius: 0.01, color: '#886644', stats: stats() });
const root = (): Box => ({ type: Primitives.Box, organ: Organs.Root, affineTransform: T(), length: 0.3, radius: 0.005, stats: stats() });
const bud = (): Sphere => ({ type: Primitives.Sphere, radius: 0.01, center: new Float32Array([1, 2, 3]), stats: stats() });

function inputs(over: Partial<Parameters<typeof buildPlants>[1]> = {}) {
    return {
        scene: [] as PlantModel[],
        visualMapping: VisualMappingOptions.Natural,
        debugBoxes: false,
        showLeaves: true,
        showRoots: true,
        leafGeom: () => Geom.Plane as number,
        petalGeom: () => Geom.Plane as number,
        ...over,
    };
}

describe('buildPlants', () => {
    test('emits one instance per drawn primitive, in scene order', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({
            scene: [
                { species: 'a', primitives: [leaf(), stem()] },
                { species: 'b', primitives: [root()] },
            ],
        }));

        expect(out.count).toBe(3);
        expect(out.items[0].geom).toBe(Geom.Plane);
        expect(out.items[1].geom).toBe(Geom.Cylinder);
        expect(out.items[2].geom).toBe(Geom.BoxCentered);

        //Pick refs must identify the primitive by its position in the scene, since the
        //WebGPU picker resolves a pick id straight back to a slot.
        expect(out.items[0].ref).toEqual({ type: 'plant', index: { entity: 0, primitive: 0 } });
        expect(out.items[2].ref).toEqual({ type: 'plant', index: { entity: 1, primitive: 0 } });
    });

    test('buds occupy no slot at all', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({ scene: [{ species: 'a', primitives: [leaf(), bud(), stem()] }] }));

        //Two drawn primitives, and crucially the stem's ref still points at primitive 2 -
        //the slot index and the primitive index deliberately diverge.
        expect(out.count).toBe(2);
        expect(out.items[1].ref).toEqual({ type: 'plant', index: { entity: 0, primitive: 2 } });
    });

    test('showLeaves / showRoots mark instances hidden rather than dropping them', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({
            scene: [{ species: 'a', primitives: [leaf(), stem(), root()] }],
            showLeaves: false,
            showRoots: false,
        }));

        expect(out.count).toBe(3);
        expect(out.items[0].flags & IFlags.Hidden).toBeTruthy();   // leaf
        expect(out.items[1].flags & IFlags.Hidden).toBeFalsy();    // stem always visible
        expect(out.items[2].flags & IFlags.Hidden).toBeTruthy();   // root
    });

    test('leaves are double sided, stems and roots are not', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({ scene: [{ species: 'a', primitives: [leaf(), stem(), root()] }] }));
        expect(out.items[0].flags & IFlags.DoubleSided).toBeTruthy();
        expect(out.items[1].flags & IFlags.DoubleSided).toBeFalsy();
        expect(out.items[2].flags & IFlags.DoubleSided).toBeFalsy();
    });

    test('debugBoxes overrides geometry and colour for every organ', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({
            scene: [{ species: 'a', primitives: [leaf(), stem(), root()] }],
            debugBoxes: true,
        }));

        expect(out.count).toBe(3);
        for (let i = 0; i < 3; ++i) {
            expect(out.items[i].geom).toBe(Geom.DebugBox);
            expect(out.items[i].flags & IFlags.VertexColors).toBeTruthy();
        }
    });

    test('colours agree with the shared PlantColors functions', () => {
        const out = new InstanceList();
        const l = leaf(), s = stem();
        buildPlants(out, inputs({ scene: [{ species: 'a', primitives: [l, s] }] }));

        expect(out.items[0].color).toEqual(leafColor(l, 0, VisualMappingOptions.Natural).color);
        expect(out.items[1].color).toEqual(stemColor(s, VisualMappingOptions.Natural).color);
    });

    test('non-Natural mappings put the ramp in emissive and blacken the albedo', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({
            scene: [{ species: 'a', primitives: [leaf()] }],
            visualMapping: VisualMappingOptions.Water,
        }));

        expect(out.items[0].color).toEqual({ r: 0, g: 0, b: 0 });
        expect(out.items[0].emissive.b).toBeGreaterThan(0);
    });

    test('uses the per-species leaf geometry id', () => {
        const out = new InstanceList();
        buildPlants(out, inputs({
            scene: [
                { species: 'withMesh', primitives: [leaf()] },
                { species: 'fallback', primitives: [leaf()] },
            ],
            leafGeom: (sp: string) => (sp === 'withMesh' ? 42 : Geom.Plane),
        }));

        expect(out.items[0].geom).toBe(42);
        expect(out.items[1].geom).toBe(Geom.Plane);
    });

    it('draws a petal with the petal mesh, not the leaf mesh', () => {
        const out = new InstanceList();
        const petal = () => ({ ...leaf(), organ: Organs.FlowerPadel });
        buildPlants(out, inputs({
            scene: [{ species: 'sp', primitives: [leaf(), petal()] }],
            leafGeom: () => 42,
            petalGeom: () => 43,
        }));

        expect(out.items[0].geom).toBe(42);
        expect(out.items[1].geom).toBe(43);
    });

    test('reset recycles instances without reallocating', () => {
        const out = new InstanceList();
        const inp = inputs({ scene: [{ species: 'a', primitives: [leaf(), stem()] }] });

        buildPlants(out, inp);
        const firstMatrix = out.items[0].model;
        const pooled = out.items.length;

        buildPlants(out, inp);
        expect(out.items.length).toBe(pooled);
        //Same backing Float32Array reused - this is what keeps playback allocation-free.
        expect(out.items[0].model).toBe(firstMatrix);
    });
});

//The buildTerrain tests were removed with the function they covered. It was the neutral
//terrain builder the refactor left behind, and nothing in the app ever called it - both
//backends build terrain themselves. The tests passed the whole time, which is precisely why
//it went unnoticed: a change to terrain flags was made there and silently did nothing.

describe('IFlags bits stay disjoint', () => {
    //Adding a bit to this bitfield is how ExcludeFromGI was introduced, and every consumer
    //masks it - the WebGPU pipeline variant, the Three material program key, the visibility
    //check. A bit that collided with an existing one would silently change how things are
    //drawn rather than failing anywhere, so the values themselves are the thing to pin.
    const bits: [string, number][] = [
        ['DoubleSided', IFlags.DoubleSided],
        ['FlatShaded', IFlags.FlatShaded],
        ['VertexColors', IFlags.VertexColors],
        ['Hidden', IFlags.Hidden],
        ['DepthBias', IFlags.DepthBias],
        ['ExcludeFromGI', IFlags.ExcludeFromGI],
    ];

    test('each is a distinct power of two', () => {
        const seen = new Set<number>();
        for (const [name, v] of bits) {
            expect(`${name}=${v}`).toBe(`${name}=${v}`);
            expect(v & (v - 1)).toBe(0);
            expect(v).toBeGreaterThan(0);
            expect(seen.has(v)).toBe(false);
            seen.add(v);
        }
        expect(seen.size).toBe(bits.length);
    });

    test('ExcludeFromGI is invisible to everything that decides how a thing is drawn', () => {
        //The mask ThreeMaterials keys its program on, plus the bits the WebGPU variant picks.
        const drawingBits = IFlags.DoubleSided | IFlags.FlatShaded | IFlags.VertexColors | IFlags.DepthBias;
        expect(IFlags.ExcludeFromGI & drawingBits).toBe(0);
        //And it must not read as Hidden, or terrain would stop being drawn at all.
        expect(IFlags.ExcludeFromGI & IFlags.Hidden).toBe(0);
    });
});
