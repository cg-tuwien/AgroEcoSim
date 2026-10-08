//The renderer-neutral draw list. Nothing in this file may import three.
//
//This replaces the buildPlants / buildObject3D / updateObject3D / buildTerrain trio in
//ThreeSceneFn.tsx with a single description of "what should be on screen", which both
//backends then realise in their own way: the Three renderer pools THREE.Mesh objects against
//it, the WebGPU renderer packs it into an instance storage buffer.
//
//Instances are pooled and recycled by index exactly as objPlants.children was, so a steady
//state playback does not allocate.

import { RGB } from './Color';
import { Fold } from './ContactDeformation';
import { Geom } from './GeometryIds';
import { Mat4, createMat4 } from './Mat4';
import { DataRef } from './Picking';

export const enum IFlags {
    None = 0,
    /** Render both faces. Leaves, legacy disks, terrain, obstacles. */
    DoubleSided = 1,
    /**
     * A thin translucent lamina: leaves and petals, and nothing else.
     *
     * Deliberately distinct from DoubleSided, which also covers terrain and obstacles. The
     * leaf shading model - wrap-around diffuse, transmission to the shaded face, Oren-Nayar -
     * used to be selected on double-sidedness, so a wall and the ground were shaded as leaves.
     * That was invisible while the model only touched the SUN term, because the default
     * lighting has no sun at all; it stops being invisible the moment the ambient term learns
     * to bleed through a surface, since a wall would then glow from its shaded side.
     */
    Foliage = 256,
    /** Use the face normal rather than the interpolated vertex normal. Terrain, obstacles. */
    FlatShaded = 2,
    /** Take the albedo from the vertex colour attribute. Only the debug orientation box. */
    VertexColors = 4,
    /**
     * Take the EMISSIVE from the vertex colour attribute, and no albedo.
     *
     * The unlit counterpart of VertexColors, and what a data readout needs: obstacles coloured by
     * a measured leaf area index or shading fraction must not have that value modulated by which
     * way the wall happens to face. Same reasoning as the plant visual mappings, which put their
     * value in emissive and leave colour black - see core/PlantColors.
     */
    VertexColorsEmissive = 64,
    /** Excluded from drawing AND from picking, per the showLeaves/showRoots toggles. */
    Hidden = 8,
    /** Pull towards the viewer to survive coplanarity. Mirrors polygonOffsetFactor -2. */
    DepthBias = 16,
    /** A small depth preference for measured faces over coplanar context. */
    MappingDepthBias = 128,
    /**
     * Keep out of the GI density field. Set on terrain.
     *
     * The field models leaf area: a density is the right description of foliage, and a cone
     * traced through it gives canopy self-shading and colour bleed. It is the WRONG
     * description of a large flat surface. Injected, the ground becomes a slab that smears
     * vertically at coarse mip levels, and every cone from a point ON that ground samples it
     * - so open field darkened by about a quarter, uniformly, which is pure artifact. This is
     * a known limitation of isotropic voxel cone tracing, properly solved with 6-directional
     * voxels at six times the memory.
     *
     * The cost of excluding it is that the ground no longer bounces light onto leaf
     * undersides. That is a real second-order effect and it is deferred, not denied; what is
     * kept is the first-order one the tier exists for, plants shading and colouring each
     * other and the soil beneath them.
     */
    ExcludeFromGI = 32,
}

export type Instance = {
    geom: number;            // Geom, or a runtime id from GeometryRegistry
    model: Mat4;             // 16 floats, column-major
    color: RGB;              // linear
    emissive: RGB;           // linear
    flags: number;           // IFlags bitfield
    ref: DataRef | undefined; // what a pick on this instance resolves to
    /** Optional index range within geom; shares its vertices and GPU allocation. */
    indexRange?: { first: number; count: number };
    fold?: Fold;
};

function blankInstance(): Instance {
    return {
        geom: Geom.BoxCentered,
        model: createMat4(),
        color: { r: 0, g: 0, b: 0 },
        emissive: { r: 0, g: 0, b: 0 },
        flags: IFlags.None,
        ref: undefined,
    };
}

/**
 * A growable, index-recycled list of instances.
 *
 * `reset()` does NOT release the backing objects - it only rewinds `count`, so the next
 * rebuild reuses the same Instance objects and the same Float32Array matrices. That is the
 * same trick the Three path already used with objPlants.children, and it is what keeps a
 * 30k-primitive scene from allocating on every timestep.
 */
export class InstanceList {
    items: Instance[] = [];
    count = 0;

    reset() {
        this.count = 0;
    }

    /** Returns a recycled Instance to be filled in. Never returns undefined. */
    push(): Instance {
        if (this.count === this.items.length)
            this.items.push(blankInstance());
        const inst = this.items[this.count++];
        inst.indexRange = undefined;
        inst.fold = undefined;
        return inst;
    }

    /** Iterates only the live prefix. */
    forEach(fn: (inst: Instance, index: number) => void) {
        for (let i = 0; i < this.count; ++i) fn(this.items[i], i);
    }

    /**
     * Drops pooled objects beyond the live prefix. Only worth calling after a large scene
     * shrinks permanently; during playback the pooling is the point.
     */
    trim() {
        if (this.items.length > this.count) this.items.length = this.count;
    }
}
