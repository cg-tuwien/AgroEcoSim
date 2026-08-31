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
    /** Cells per axis. */
    dim: number;
    /** World units per cell, per axis. */
    cell: Vec3;
};

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
    return { origin, size, dim, cell: { x: size.x / dim, y: size.y / dim, z: size.z / dim } };
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
    return (z * grid.dim + y) * grid.dim + x;
}

export function insideGrid(grid: VoxelGrid, c: { x: number; y: number; z: number }): boolean {
    return c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < grid.dim && c.y < grid.dim && c.z < grid.dim;
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
