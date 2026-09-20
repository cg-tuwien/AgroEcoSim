//Populates an InstanceList from simulation data. Renderer-neutral; no three.
//
//These are the neutral replacements for buildPlants/buildTerrain in ThreeSceneFn.tsx. They
//take their inputs explicitly rather than reading appstate, which keeps core free of the
//app's dependency graph, makes them unit-testable, and lets the offline renderer build a
//frame under settings other than the ones the live viewport is showing.

import { PlantModel } from '../../helpers/Scene';
import { Organs, Primitive, Primitives } from '../../helpers/Primitives';
import { Geom } from './GeometryIds';
import { IFlags, Instance, InstanceList } from './InstanceList';
import { decodePrimitive } from './PrimitiveTransform';
import { VisualMappingOptions, leafColor, stemColor, rootColor } from './PlantColors';
import { neutral } from './Theme';
import { black } from './Ramps';
import { RGB } from './Color';
import { Mat4, identity, scaleInPlace, setPosition } from './Mat4';

export type PlantBuildInputs = {
    scene: PlantModel[];
    visualMapping: VisualMappingOptions;
    debugBoxes: boolean;
    showLeaves: boolean;
    showRoots: boolean;
    /**
     * Geometry id for one slice of a species' leaf mesh - a blade split into segments is drawn
     * a slice at a time, and slice 0 is the whole leaf for a species that is not split at all.
     * Returns Geom.Plane when the backend served no per-species mesh, matching
     * `leafGeometry ?? threePlanePrimitive` in the Three path.
     */
    leafGeom: (species: string, slice: number) => number;
    /** Petal outline for a species; falls back to the flat plane like leafGeom does. */
    petalGeom: (species: string) => number;
};

function assign(inst: Instance, geom: number, color: RGB, emissive: RGB, flags: number, ref: Instance['ref']) {
    inst.geom = geom;
    inst.color = color;
    inst.emissive = emissive;
    inst.flags = flags;
    inst.ref = ref;
}

/**
 * One instance per drawn primitive, in scene order.
 *
 * Ordering matters beyond aesthetics: the WebGPU picker maps a pick id straight back to an
 * instance slot, so the slot order is the identity of the primitive for that frame.
 */
export function buildPlants(out: InstanceList, inp: PlantBuildInputs) {
    out.reset();

    for (let i = 0; i < inp.scene.length; ++i) {
        const entity = inp.scene[i];
        const petalGeomId = inp.petalGeom(entity.species);

        for (let j = 0; j < entity.primitives.length; ++j) {
            const p = entity.primitives[j];
            const inst = out.push();

            if (!decodePrimitive(p, inst.model)) {
                //Buds. Not drawn, so give the slot back rather than leaving a stale
                //instance that picking could resolve to.
                out.count--;
                continue;
            }

            const ref = { type: 'plant' as const, index: { entity: i, primitive: j } };

            if (inp.debugBoxes) {
                //The debug overlay replaces geometry AND material for every organ, exactly
                //as SetupMesh did. Colour comes from the vertex attribute.
                assign(inst, Geom.DebugBox, black, black, IFlags.VertexColors | hiddenFlag(p, inp), ref);
                continue;
            }

            switch (p.type) {
                case Primitives.Rectangle: {
                    const c = p.stats ? leafColor(p, j, inp.visualMapping) : { color: neutral, emissive: black };
                    //Leaves and petals are both laminae and arrive in the same record, so the
                    //organ is the only thing that distinguishes them.
                    const laminaGeom = p.organ === Organs.FlowerPadel
                        ? petalGeomId
                        : inp.leafGeom(entity.species, p.laminaSlice);
                    assign(inst, laminaGeom, c.color, c.emissive, IFlags.DoubleSided | IFlags.Foliage | hiddenFlag(p, inp), ref);
                    break;
                }
                case Primitives.Cylinder: {
                    const c = p.stats ? stemColor(p, inp.visualMapping) : { color: neutral, emissive: black };
                    assign(inst, Geom.Cylinder, c.color, c.emissive, hiddenFlag(p, inp), ref);
                    break;
                }
                case Primitives.Box: {
                    const c = p.stats ? rootColor(p, inp.visualMapping) : { color: neutral, emissive: black };
                    assign(inst, Geom.BoxCentered, c.color, c.emissive, hiddenFlag(p, inp), ref);
                    break;
                }
                case Primitives.Disk:
                    //Legacy V3 obstacles only; always the shared neutral grey, double-sided.
                    assign(inst, Geom.CircleHalf, neutral, black, IFlags.DoubleSided, ref);
                    break;
                default:
                    out.count--;
                    break;
            }
        }
    }
}

/** Mirrors the per-primitive visibility SetupMesh applied from the showX toggles. */
function hiddenFlag(p: Primitive, inp: PlantBuildInputs): number {
    switch (p.type) {
        case Primitives.Rectangle: return inp.showLeaves ? IFlags.None : IFlags.Hidden;
        case Primitives.Box: return inp.showRoots ? IFlags.None : IFlags.Hidden;
        default: return IFlags.None;
    }
}

//--- terrain --------------------------------------------------------------------------------

export type TerrainBoxInput = {
    kind: 'box';
    /** Corner-anchored: the BoxCorner geometry has its origin at the min corner. */
    sx: number; sy: number; sz: number;
    px: number; py: number; pz: number;
    index: number;
    item?: unknown;
};

export type TerrainMeshInput = {
    kind: 'mesh';
    geom: number;
    px: number; py: number; pz: number;
    index: number;
    item?: unknown;
};

export type TerrainBuildInputs = {
    items: (TerrainBoxInput | TerrainMeshInput)[];
    color: RGB;
};

//Terrain has no neutral builder. Both backends build it themselves - ThreeRenderer.rebuildTerrain
//and WebGPURenderer.appendTerrain - because the two need different things from it, and the
//neutral version that used to sit here had no callers at all. It was left behind by the
//refactor and was actively harmful: it looked like the shared implementation, so a change to
//terrain flags landed there and silently did nothing. If terrain is ever unified, delete both
//and put the result here rather than adding a third.

/** Applies a uniform scale then a translation into an existing matrix. */
export function composeScaleTranslate(out: Mat4, sx: number, sy: number, sz: number, px: number, py: number, pz: number): Mat4 {
    identity(out);
    scaleInPlace(out, sx, sy, sz);
    return setPosition(out, px, py, pz);
}
