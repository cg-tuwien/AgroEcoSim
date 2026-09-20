//The leaf-area-density field that voxel GI traces through. Renderer-neutral; no three, no GPU.
//
//Why area and not opacity. The classic voxel-GI foliage pathology is that a sub-voxel leaf
//either binarises to alpha=1 and over-occludes, or vanishes entirely - because opacity is not
//scale-invariant. Accumulating triangle AREA and resolving to LAD = area/volume (m2/m3) fixes
//that by construction: a sub-voxel leaf yields a small but correct density, mip generation is
//a plain arithmetic mean because a density averages, and double-sidedness costs nothing since
//extinction has no sign. This is also literally the canopy radiative-transfer formalism the
//simulation itself approximates, so the numbers mean something.

import { Vec3 } from './Vec3';
import { computeBounds } from './Geometry';
import { getGeometry } from './GeometryStore';
import { IFlags, InstanceList } from './InstanceList';
import { Mat4 } from './Mat4';

/**
 * Extinction per unit leaf area density, for a spherical leaf-angle distribution.
 *
 * sigma = G * LAD. G = 0.5 is the standard spherical value: averaged over all orientations,
 * a leaf projects half its area onto any given direction.
 */
export const EXTINCTION_G = 0.5;

/** How much larger than the field the grid is, so nothing sits on a boundary. */
export const GRID_MARGIN = 1.04;

/**
 * Cells the fitted grid may not exceed: exactly what a 128 cube costs today.
 *
 * Expressed as a total rather than a per-axis limit because that is what the buffers, the
 * resolve sweep and the texture all scale with. Holding it constant is what lets the domain
 * grow to cover an imported building without the field costing more than it does now - the
 * cube was the waste, not the extent.
 */
export const VOXEL_CELL_BUDGET = 128 ** 3;
/** Per-axis cell counts are rounded up to this, so the mip chain halves exactly for 4 levels. */
export const GRID_DIM_ALIGN = 16;
/** Well under maxTextureDimension3D (2048 on every device that reports WebGPU at all). */
export const MAX_GRID_DIM = 512;
/**
 * The most the fitted grid may coarsen its cells, as a multiple of the field's own cell.
 *
 * A bound rather than a budget-only rule: past 4x the near-field contact shading the fine grid
 * exists for is gone, and a grid that quietly became useless for plants in order to cover a
 * distant import would be a worse answer than saying so.
 */
export const MAX_CELL_GROWTH = 4;

/**
 * Samples one GPU invocation splats, and how many invocations a set is allowed to use.
 *
 * A point splat only fills a cell if a sample lands in it, so a triangle needs roughly one
 * sample per cell it crosses. The terrain is a 100 m2 quad and a cell is ~8 cm, which is
 * ~15000 cells - at 64 samples the ground came out as 128 isolated dense blobs with nothing
 * between them, an occluder full of holes. Plants need no batching at all: an organ is
 * smaller than a cell, so one sample is already correct.
 *
 * Batches cost a dispatch dimension, so this is only affordable because terrain and obstacles
 * are rebuilt on their own timestamps rather than every timestep. Instances in a static
 * bucket that do NOT need the samples (seeds, say) still pay for the extra invocations, which
 * exit immediately - acceptable at that rebuild rate, and the reason plants are kept at one.
 */
export const SAMPLES_PER_INVOCATION = 64;
/**
 * Ceiling on batches for one bucket. The count itself is derived per bucket from the geometry
 * (see VoxelField.batchesFor); this only bounds what a pathological scene can ask for.
 *
 * It has to be high enough for the derived count to actually be reached. At 256 the terrain
 * quad was capped at ~1 sample per cell, so whether a given cell got 0, 1 or 2 samples was
 * down to how the stratified sequence happened to land - the density mottled by 100 % cell to
 * cell, and a grazing cone integrating along the ground amplified that into visible bands.
 * The area/cellArea rule already asks for ~4 samples per cell; this just stops truncating it.
 * Only geometry that genuinely spans thousands of cells requests anything like this many, and
 * such geometry is static, so it is paid once rather than per timestep.
 */
export const MAX_SAMPLE_BATCHES = 1024;

/**
 * Invocations one bucket's injection may dispatch, before the per-triangle rule is cut back.
 *
 * The batch count is derived from the bucket's LARGEST triangle and then applied as the
 * dispatch's z dimension to every triangle in it, so one big slab inside a detailed mesh taxes
 * the whole mesh. A 100 m2 slab in a 1.4 M-triangle imported facade asks for 948 batches, which
 * is 1.3 billion invocations per rebuild - 99.9 % of them returning immediately, because the
 * tiny triangles around it each need one sample. Bounding the product is what keeps a mesh's
 * detail from multiplying its own worst triangle's requirement.
 *
 * Set high enough that nothing which fits today is affected: the terrain quad is two triangles,
 * so it still gets every batch it asks for.
 */
export const SPLAT_INVOCATION_BUDGET = 1 << 26;

/**
 * How many sample batches one bucket's injection gets.
 *
 * Shared so the GPU kernel's dispatch and the CPU reference cannot disagree - if they do, the
 * voxelcheck diff compares two different algorithms and means nothing.
 */
export function batchesForBucket(maxTriangleArea: number, cellArea: number,
                                 triangles: number, instances: number): number {
    const wanted = Math.max(1, Math.ceil(Math.ceil((maxTriangleArea / cellArea) * 4) / SAMPLES_PER_INVOCATION));
    const work = Math.max(1, triangles) * Math.max(1, instances);
    const affordable = Math.max(1, Math.floor(SPLAT_INVOCATION_BUDGET / work));
    return Math.min(MAX_SAMPLE_BATCHES, wanted, affordable);
}

/**
 * Canopy headroom above the soil, added to the field depth to get the grid's height input.
 *
 * Shared because the two backends passed different values for it - WebGPU the depth plus this,
 * WebGL the bare depth - which is exactly the drift that keeping the grid maths in core is
 * meant to prevent.
 */
export const GI_HEADROOM = 6;

/** Everything the GI needs to know about the world, and nothing else. */
export type GIFrameInputs = {
    fieldSizeX: number;
    fieldSizeZ: number;
    /** Headroom above the soil the canopy may occupy. */
    height: number;
};

/** Controls for the density-field debug overlay. */
export type VoxelDebugOptions = {
    /** Mip level to display. 0 is the finest. */
    mip: number;
    /** Multiplies sigma, so a sparse canopy can be made visible without changing the field. */
    gain: number;
    /** Show the stored albedo rather than a density ramp. */
    albedo: boolean;
};

export const DEFAULT_VOXEL_DEBUG: VoxelDebugOptions = { mip: 0, gain: 1, albedo: false };

export type VoxelGrid = {
    /** World-space minimum corner. */
    origin: Vec3;
    /** World-space extent. */
    size: Vec3;
    /**
     * Cells per axis, INDEPENDENTLY per axis.
     *
     * The cells themselves stay cubic - a cone trace steps in world units and would sample at
     * different rates per axis otherwise - but the counts do not have to match. That is what
     * makes a domain large enough to hold an imported building affordable: a cube around a
     * 37 m wide, 7.7 m tall facade is 85 % empty sky, and paying for it at a useful cell size
     * costs 300 MB. Fitting the box instead costs less than the 10 m cube does today.
     */
    dim: Vec3;
    /** World units per cell, per axis. */
    cell: Vec3;
};

/** An axis-aligned world-space box. */
export type GridBounds = { min: Vec3; max: Vec3 };

/**
 * Fits a grid to the FIELD plus headroom, deliberately not to the plants.
 *
 * Same reasoning as the shadow frustum: a grid fitted to the geometry would move and rescale
 * every time a plant grew, so every cell would change meaning between timesteps and the
 * lighting would visibly crawl. A fixed grid means a cell is the same cubic decimetre of
 * world for the whole run.
 */
export function makeVoxelGrid(fieldSizeX: number, fieldSizeZ: number, height: number, dim: number): VoxelGrid {
    //Cube the grid over the largest horizontal extent so cells stay near-isotropic; a cone
    //trace steps in world units and would otherwise sample at different rates per axis.
    //
    //The margin is not cosmetic. Without it the field's edge sits exactly on the grid's, so a
    //sample at x = fieldSizeX floors to cell `dim` and is discarded - and whether a sample
    //lands just inside or just outside then depends on f32 rounding, which made the GPU and
    //the CPU reference disagree by half a percent of total area. It also gives the canopy at
    //the field edge some voxels to bleed light into.
    const span = Math.max(fieldSizeX, fieldSizeZ, height) * GRID_MARGIN;
    const size: Vec3 = { x: span, y: span, z: span };
    //Centre the field in X/Z; anchor Y at the soil so the grid is not half wasted underground.
    const origin: Vec3 = {
        x: fieldSizeX * 0.5 - span * 0.5,
        y: -span * 0.05,
        z: fieldSizeZ * 0.5 - span * 0.5,
    };
    return {
        origin, size,
        dim: { x: dim, y: dim, z: dim },
        cell: { x: size.x / dim, y: size.y / dim, z: size.z / dim },
    };
}

export function cellVolume(grid: VoxelGrid): number {
    return grid.cell.x * grid.cell.y * grid.cell.z;
}

/** Cell index per axis, unclamped, so callers can detect "outside the grid". */
export function worldToCell(grid: VoxelGrid, p: Vec3): { x: number; y: number; z: number } {
    return {
        x: Math.floor((p.x - grid.origin.x) / grid.cell.x),
        y: Math.floor((p.y - grid.origin.y) / grid.cell.y),
        z: Math.floor((p.z - grid.origin.z) / grid.cell.z),
    };
}

export function cellIndex(grid: VoxelGrid, x: number, y: number, z: number): number {
    return (z * grid.dim.y + y) * grid.dim.x + x;
}

export function insideGrid(grid: VoxelGrid, c: { x: number; y: number; z: number }): boolean {
    return c.x >= 0 && c.y >= 0 && c.z >= 0
        && c.x < grid.dim.x && c.y < grid.dim.y && c.z < grid.dim.z;
}

/** Total cells, which is what the buffers and the resolve sweep are sized by. */
export function cellCount(grid: VoxelGrid): number {
    return grid.dim.x * grid.dim.y * grid.dim.z;
}

/** The grid's world-space box. */
export function gridBounds(grid: VoxelGrid): GridBounds {
    return {
        min: { ...grid.origin },
        max: {
            x: grid.origin.x + grid.size.x,
            y: grid.origin.y + grid.size.y,
            z: grid.origin.z + grid.size.z,
        },
    };
}

/** Does `inner` lie wholly within `outer`? */
function contains(outer: GridBounds, inner: GridBounds): boolean {
    return inner.min.x >= outer.min.x && inner.max.x <= outer.max.x
        && inner.min.y >= outer.min.y && inner.max.y <= outer.max.y
        && inner.min.z >= outer.min.z && inner.max.z <= outer.max.z;
}

const AXES = ['x', 'y', 'z'] as const;

/**
 * Grows a grid until it also covers the STATIC geometry, at the coarsest cell that is still
 * an exact multiple of the field's own.
 *
 * The bug this exists for: `makeVoxelGrid` fits the simulation FIELD, so an imported 37.7 m
 * facade in the usual 10 m field had 73 % of itself outside the grid. Geometry outside is
 * dropped by `insideGrid`, and a surface point outside leaves the sun march on its first probe
 * and comes back fully lit - so only the strip of building that happened to overlap the field
 * was ever shadowed.
 *
 * It does NOT weaken the reason the grid is fitted to the field rather than to the geometry.
 * That reason is that a grid following the PLANTS would move and rescale as they grew, and
 * every cell would change meaning between timesteps. The bounds here come from statics only -
 * terrain, obstacles, imported models - which are rebuilt on their own timestamps and do not
 * move on their own. Two further properties make it stronger than "rebuilt rarely":
 *
 *  - the cell only ever doubles, so the coarse lattice is an exact nesting of the fine one;
 *  - the origin is snapped to the base lattice, so nudging an obstacle by less than a cell
 *    produces a bit-identical grid and nothing crawls.
 *
 * Returns `base` unchanged whenever the statics already fit, which is every scene that does
 * not import an oversized model.
 */
export function fitVoxelGrid(base: VoxelGrid, statics: GridBounds | undefined): VoxelGrid {
    const baseBox = gridBounds(base);
    if (!statics || contains(baseBox, statics)) return base;

    const union: GridBounds = {
        min: {
            x: Math.min(baseBox.min.x, statics.min.x),
            y: Math.min(baseBox.min.y, statics.min.y),
            z: Math.min(baseBox.min.z, statics.min.z),
        },
        max: {
            x: Math.max(baseBox.max.x, statics.max.x),
            y: Math.max(baseBox.max.y, statics.max.y),
            z: Math.max(baseBox.max.z, statics.max.z),
        },
    };

    let best: VoxelGrid | undefined;
    let growth = 1;
    for (; growth <= MAX_CELL_GROWTH; growth *= 2) {
        const cell = base.cell.x * growth;
        const origin = { x: 0, y: 0, z: 0 };
        const dim = { x: 0, y: 0, z: 0 };
        for (const a of AXES) {
            //Snapped to the base lattice, and DOWN, so the grid can only ever move in whole
            //cells - and one cell of pad on each side, for the reason GRID_MARGIN exists.
            origin[a] = base.origin[a] + Math.floor((union.min[a] - base.origin[a]) / cell - 1) * cell;
            //Measured from the snapped origin, so the far face cannot end up short of the
            //union after the snap and the alignment have both moved things.
            const span = union.max[a] + cell - origin[a];
            const cells = Math.ceil(span / cell);
            dim[a] = Math.ceil(cells / GRID_DIM_ALIGN) * GRID_DIM_ALIGN;
        }
        best = {
            origin,
            size: { x: dim.x * cell, y: dim.y * cell, z: dim.z * cell },
            dim,
            cell: { x: cell, y: cell, z: cell },
        };
        if (cellCount(best) <= VOXEL_CELL_BUDGET
            && dim.x <= MAX_GRID_DIM && dim.y <= MAX_GRID_DIM && dim.z <= MAX_GRID_DIM) return best;
    }

    //Past the growth bound. Keep the coarsest grid we are willing to build and say what is
    //still outside it, rather than dropping the remainder silently the way the old code did.
    if (!reportedFitFailure) {
        reportedFitFailure = true;
        console.warn(
            `Voxel grid: the scene needs ${cellCount(best!).toLocaleString()} cells at a `
            + `${(base.cell.x * MAX_CELL_GROWTH).toFixed(3)} m cell, over the `
            + `${VOXEL_CELL_BUDGET.toLocaleString()} budget. Geometry outside `
            + `${best!.size.x.toFixed(1)}x${best!.size.y.toFixed(1)}x${best!.size.z.toFixed(1)} m `
            + `from (${best!.origin.x.toFixed(1)}, ${best!.origin.y.toFixed(1)}, `
            + `${best!.origin.z.toFixed(1)}) will not cast shadows or occlude.`);
    }
    return best!;
}

let reportedFitFailure = false;

/** Local AABBs are memoised on the mesh itself; see giStaticDigest. */
const localBoundsCache = new WeakMap<object, GridBounds>();

function localBounds(geom: number): GridBounds | undefined {
    const mesh = getGeometry(geom);
    if (!mesh) return undefined;
    const hit = localBoundsCache.get(mesh);
    if (hit) return hit;
    const b = computeBounds(mesh);
    const box: GridBounds = {
        min: { x: b.min[0], y: b.min[1], z: b.min[2] },
        max: { x: b.max[0], y: b.max[1], z: b.max[2] },
    };
    localBoundsCache.set(mesh, box);
    return box;
}

/** Mixes one f32 into a 32-bit hash, through its exact bit pattern. */
const hashScratch = new Float32Array(1);
const hashBits = new Int32Array(hashScratch.buffer);
function mixFloat(h: number, v: number): number {
    hashScratch[0] = v;
    return Math.imul(h ^ hashBits[0], 0x01000193) >>> 0;
}

/**
 * The world bounds of everything that will be injected into the field, plus a signature of it.
 *
 * The signature exists because `appendObstacles` runs on every mouse move - picking flips an
 * obstacle's hover state - and re-injecting a 1.4 M-triangle facade per mouse move is not
 * affordable. Comparing it is what turns "statics were rebuilt" into "statics actually
 * changed".
 *
 * The skip mask is the same one the inject kernel uses, so the bounds describe exactly what
 * ends up in the field and nothing else: terrain carries ExcludeFromGI and therefore cannot
 * inflate the grid, which matters because an imported terrain mesh can be enormous.
 */
export function giStaticDigest(list: InstanceList): { bounds: GridBounds | undefined; signature: number } {
    let signature = 0x811c9dc5;
    let min: Vec3 | undefined;
    let max: Vec3 | undefined;

    for (let i = 0; i < list.count; ++i) {
        const inst = list.items[i];
        if (inst.flags & (IFlags.Hidden | IFlags.ExcludeFromGI)) continue;

        signature = Math.imul(signature ^ inst.geom, 0x01000193) >>> 0;
        signature = Math.imul(signature ^ inst.flags, 0x01000193) >>> 0;
        const m: Mat4 = inst.model;
        for (let k = 0; k < 16; ++k) signature = mixFloat(signature, m[k]);

        const box = localBounds(inst.geom);
        if (!box) continue;
        //Eight corners through the model matrix: the instance may be rotated, so transforming
        //only min and max would describe a box the geometry does not fit in.
        for (let c = 0; c < 8; ++c) {
            const lx = (c & 1) ? box.max.x : box.min.x;
            const ly = (c & 2) ? box.max.y : box.min.y;
            const lz = (c & 4) ? box.max.z : box.min.z;
            const wx = m[0] * lx + m[4] * ly + m[8] * lz + m[12];
            const wy = m[1] * lx + m[5] * ly + m[9] * lz + m[13];
            const wz = m[2] * lx + m[6] * ly + m[10] * lz + m[14];
            if (!min || !max) { min = { x: wx, y: wy, z: wz }; max = { x: wx, y: wy, z: wz }; continue; }
            if (wx < min.x) min.x = wx; else if (wx > max.x) max.x = wx;
            if (wy < min.y) min.y = wy; else if (wy > max.y) max.y = wy;
            if (wz < min.z) min.z = wz; else if (wz > max.z) max.z = wz;
        }
    }

    return { bounds: min && max ? { min, max } : undefined, signature };
}

export function triangleArea(a: Vec3, b: Vec3, c: Vec3): number {
    const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
    const vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
    const cx = uy * vz - uz * vy;
    const cy = uz * vx - ux * vz;
    const cz = ux * vy - uy * vx;
    return 0.5 * Math.hypot(cx, cy, cz);
}

/**
 * How many point samples a triangle is split into.
 *
 * A leaf triangle is far smaller than a cell and needs exactly one; a terrain quad spans
 * hundreds of cells and would otherwise dump all its area into whichever cell holds its
 * centroid. Scaling with area/cellArea is what makes the two cases one rule.
 */
export function splatSamples(area: number, grid: VoxelGrid, max = SAMPLES_PER_INVOCATION): number {
    const cellArea = grid.cell.x * grid.cell.z;
    return Math.max(1, Math.min(max, Math.ceil((area / cellArea) * 4)));
}

/** Radical inverse base 2, for the Hammersley points the splat is stratified with. */
function radicalInverse2(bits: number): number {
    let b = bits >>> 0;
    b = ((b << 16) | (b >>> 16)) >>> 0;
    b = (((b & 0x55555555) << 1) | ((b & 0xaaaaaaaa) >>> 1)) >>> 0;
    b = (((b & 0x33333333) << 2) | ((b & 0xcccccccc) >>> 2)) >>> 0;
    b = (((b & 0x0f0f0f0f) << 4) | ((b & 0xf0f0f0f0) >>> 4)) >>> 0;
    b = (((b & 0x00ff00ff) << 8) | ((b & 0xff00ff00) >>> 8)) >>> 0;
    return b * 2.3283064365386963e-10;
}

/** Uniform barycentric coordinates from a unit-square sample. */
export function sampleTriangle(a: Vec3, b: Vec3, c: Vec3, u: number, v: number): Vec3 {
    const su = Math.sqrt(u);
    const w0 = 1 - su, w1 = su * (1 - v), w2 = su * v;
    return {
        x: a.x * w0 + b.x * w1 + c.x * w2,
        y: a.y * w0 + b.y * w1 + c.y * w2,
        z: a.z * w0 + b.z * w1 + c.z * w2,
    };
}

/**
 * The CPU reference implementation of the scatter, which the WGSL kernel mirrors.
 *
 * Kept because the GPU version cannot be unit-tested in jsdom, and the property that matters -
 * that the total area deposited equals the triangle's actual area - is exactly the kind of
 * thing that silently breaks. Also used to state the analytic expectation in tests.
 *
 * @param out Area accumulator, one entry per cell.
 * @returns The area that fell outside the grid and was discarded.
 */
export function scatterTriangleArea(grid: VoxelGrid, a: Vec3, b: Vec3, c: Vec3, out: Float32Array,
                                    batches = 1): number {
    const area = triangleArea(a, b, c);
    if (area <= 0) return 0;

    const n = splatSamples(area, grid, SAMPLES_PER_INVOCATION * batches);
    const share = area / n;
    let lost = 0;
    for (let i = 0; i < n; ++i) {
        const p = sampleTriangle(a, b, c, (i + 0.5) / n, radicalInverse2(i));
        const cellPos = worldToCell(grid, p);
        if (!insideGrid(grid, cellPos)) { lost += share; continue; }
        out[cellIndex(grid, cellPos.x, cellPos.y, cellPos.z)] += share;
    }
    return lost;
}

/** Extinction coefficient for a cell holding this much leaf area. */
export function extinctionFromArea(area: number, grid: VoxelGrid): number {
    return (EXTINCTION_G * area) / cellVolume(grid);
}
