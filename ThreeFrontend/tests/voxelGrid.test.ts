//The analytic gate for the leaf-area-density field.
//
//The whole design rests on one property: the field stores AREA, so a known leaf must produce
//a known LAD regardless of how the triangle happens to straddle cells. If that property
//holds, sub-voxel leaves and multi-cell terrain quads are both handled by the same rule.

import {
    EXTINCTION_G, cellIndex, cellVolume, extinctionFromArea, makeVoxelGrid,
    scatterTriangleArea, splatSamples, triangleArea, worldToCell, insideGrid, sampleTriangle,
    MAX_SAMPLE_BATCHES, SAMPLES_PER_INVOCATION, GridBounds, VOXEL_CELL_BUDGET, MAX_GRID_DIM,
    MAX_CELL_GROWTH, GRID_DIM_ALIGN, batchesForBucket, cellCount, fitVoxelGrid, gridBounds,
} from '../src/render/core/VoxelGrid';

const grid = makeVoxelGrid(10, 10, 6, 64);

const sum = (a: Float32Array) => a.reduce((t, v) => t + v, 0);
const field = () => new Float32Array(cellCount(grid));

describe('grid geometry', () => {
    test('cells are cubic, so a cone samples at the same rate on every axis', () => {
        expect(grid.cell.x).toBeCloseTo(grid.cell.y, 12);
        expect(grid.cell.y).toBeCloseTo(grid.cell.z, 12);
    });

    test('the field centre lands in the middle of the grid', () => {
        const c = worldToCell(grid, { x: 5, y: grid.origin.y + grid.size.y * 0.5, z: 5 });
        expect(c.x).toBe(grid.dim.x / 2);
        expect(c.z).toBe(grid.dim.z / 2);
        expect(insideGrid(grid, c)).toBe(true);
    });

    test('the soil surface is inside the grid, not clipped off at the bottom', () => {
        expect(insideGrid(grid, worldToCell(grid, { x: 5, y: 0, z: 5 }))).toBe(true);
    });
});

describe('triangle area', () => {
    test('a unit right triangle has area 1/2', () => {
        expect(triangleArea({ x: 0, y: 0, z: 0 }, { x: 1, y: 0, z: 0 }, { x: 0, y: 0, z: 1 })).toBeCloseTo(0.5, 12);
    });

    test('area is orientation-independent, which is what makes double-sidedness free', () => {
        const a = { x: 0, y: 0, z: 0 }, b = { x: 1, y: 0, z: 0 }, c = { x: 0, y: 0, z: 1 };
        expect(triangleArea(a, c, b)).toBeCloseTo(triangleArea(a, b, c), 12);
    });
});

describe('the splat conserves area', () => {
    test('a 1 m2 leaf deposits exactly 1 m2', () => {
        //Two triangles making a 1x1 m quad, lying flat well inside the grid.
        const y = 1.5;
        const out = field();
        scatterTriangleArea(grid, { x: 4, y, z: 4 }, { x: 5, y, z: 4 }, { x: 5, y, z: 5 }, out);
        scatterTriangleArea(grid, { x: 4, y, z: 4 }, { x: 5, y, z: 5 }, { x: 4, y, z: 5 }, out);
        expect(sum(out)).toBeCloseTo(1, 5);
    });

    test('a sub-voxel leaf keeps its area instead of binarising or vanishing', () => {
        //This is the case opacity-based voxelisation gets wrong in both directions.
        const s = grid.cell.x * 0.1;
        const out = field();
        scatterTriangleArea(grid, { x: 5, y: 2, z: 5 }, { x: 5 + s, y: 2, z: 5 }, { x: 5, y: 2, z: 5 + s }, out);
        const expected = 0.5 * s * s;
        expect(sum(out)).toBeCloseTo(expected, 9);
        //And it lands in exactly one cell.
        expect(out.filter(v => v > 0).length).toBe(1);
    });

    test('a large quad spreads across cells rather than collapsing onto its centroid', () => {
        const out = field();
        scatterTriangleArea(grid, { x: 0.5, y: 1, z: 0.5 }, { x: 9.5, y: 1, z: 0.5 }, { x: 9.5, y: 1, z: 9.5 }, out);
        expect(out.filter(v => v > 0).length).toBeGreaterThan(16);
    });

    test('geometry outside the grid is reported, not silently folded in at the edge', () => {
        const out = field();
        const lost = scatterTriangleArea(grid, { x: 1e3, y: 1e3, z: 1e3 }, { x: 1e3 + 1, y: 1e3, z: 1e3 }, { x: 1e3, y: 1e3, z: 1e3 + 1 }, out);
        expect(sum(out)).toBe(0);
        expect(lost).toBeCloseTo(0.5, 9);
    });
});

describe('sample counts adapt to the triangle', () => {
    test('a leaf-sized triangle takes one sample and a terrain quad takes many', () => {
        const cellArea = grid.cell.x * grid.cell.z;
        expect(splatSamples(cellArea * 1e-4, grid)).toBe(1);
        expect(splatSamples(cellArea * 1e3, grid)).toBe(64);
    });
});

describe('LAD and extinction', () => {
    test('a 1 m2 leaf inside one cell gives LAD = 1/cellVolume and sigma = G*LAD', () => {
        const v = cellVolume(grid);
        expect(extinctionFromArea(1, grid)).toBeCloseTo(EXTINCTION_G / v, 9);
    });

    test('sigma is a density, so halving the area halves it - which is why mips can average', () => {
        expect(extinctionFromArea(0.5, grid)).toBeCloseTo(extinctionFromArea(1, grid) / 2, 12);
    });
});

describe('triangle sampling stays inside the triangle', () => {
    test('barycentric weights are non-negative and sum to one', () => {
        const a = { x: 0, y: 0, z: 0 }, b = { x: 2, y: 0, z: 0 }, c = { x: 0, y: 0, z: 3 };
        for (const [u, v] of [[0, 0], [1, 0], [1, 1], [0.5, 0.5], [0.25, 0.75]]) {
            const p = sampleTriangle(a, b, c, u, v);
            //Inside the axis-aligned right triangle x/2 + z/3 <= 1.
            expect(p.x).toBeGreaterThanOrEqual(-1e-12);
            expect(p.z).toBeGreaterThanOrEqual(-1e-12);
            expect(p.x / 2 + p.z / 3).toBeLessThanOrEqual(1 + 1e-12);
        }
    });
});

describe('cellIndex', () => {
    test('is a bijection over the grid', () => {
        const seen = new Set<number>();
        for (let z = 0; z < 4; ++z) for (let y = 0; y < 4; ++y) for (let x = 0; x < 4; ++x)
            seen.add(cellIndex({ ...grid, dim: { x: 4, y: 4, z: 4 } }, x, y, z));
        expect(seen.size).toBe(64);
        expect(Math.max(...seen)).toBe(63);
    });
});

describe('the grid margin keeps the field off the boundary', () => {
    //Without it, a sample at exactly x = fieldSizeX floors to cell `dim` and is discarded,
    //and which side of the boundary a sample lands on comes down to float rounding - which is
    //how the GPU field and this reference came to disagree by half a percent of total area.
    test.each([
        ['origin', 0, 0],
        ['far corner', 10, 10],
    ])('the field %s is strictly inside', (_name, x, z) => {
        const c = worldToCell(grid, { x, y: 0.5, z });
        expect(insideGrid(grid, c)).toBe(true);
        expect(c.x).toBeGreaterThan(0);
        expect(c.z).toBeGreaterThan(0);
        expect(c.x).toBeLessThan(grid.dim.x - 1);
        expect(c.z).toBeLessThan(grid.dim.z - 1);
    });

    test('a quad covering the whole field loses no area to clipping', () => {
        const out = field();
        const lost = scatterTriangleArea(grid, { x: 0, y: 1, z: 0 }, { x: 10, y: 1, z: 0 }, { x: 10, y: 1, z: 10 }, out)
                   + scatterTriangleArea(grid, { x: 0, y: 1, z: 0 }, { x: 10, y: 1, z: 10 }, { x: 0, y: 1, z: 10 }, out);
        expect(lost).toBe(0);
        expect(sum(out)).toBeCloseTo(100, 4);
    });
});

describe('sample batching covers large surfaces', () => {
    //A point splat only fills a cell a sample lands in. The terrain is a 100 m2 quad over ~8 cm
    //cells - about 15000 of them - so at one batch it deposited its whole area into 128
    //isolated blobs with nothing between: an occluder full of holes. The GPU field was
    //numerically correct the whole time, which is exactly why this needed the debug overlay to
    //be visible at all.
    const quad = (out: Float32Array, batches: number) => {
        const y = 0.01;
        scatterTriangleArea(grid, { x: 0, y, z: 0 }, { x: 10, y, z: 0 }, { x: 10, y, z: 10 }, out, batches);
        scatterTriangleArea(grid, { x: 0, y, z: 0 }, { x: 10, y, z: 10 }, { x: 0, y, z: 10 }, out, batches);
    };

    test('one batch leaves the ground full of holes', () => {
        const out = field();
        quad(out, 1);
        expect(out.filter(v => v > 0).length).toBe(2 * SAMPLES_PER_INVOCATION);
    });

    test('the static budget fills most of the cells the quad crosses', () => {
        const out = field();
        quad(out, MAX_SAMPLE_BATCHES);
        //The quad spans (10 / cell) cells per axis.
        const across = Math.floor(10 / grid.cell.x);
        expect(out.filter(v => v > 0).length).toBeGreaterThan(across * across * 0.5);
    });

    test('batching moves area around but never creates or destroys it', () => {
        for (const batches of [1, 4, MAX_SAMPLE_BATCHES]) {
            const out = field();
            quad(out, batches);
            expect(sum(out)).toBeCloseTo(100, 3);
        }
    });
});

//--- the fitted grid -------------------------------------------------------------------------
//
//The bug these exist for: makeVoxelGrid fits the simulation FIELD, so an imported 37.7 m facade
//in the usual 10 m field had 73 % of itself outside the grid - depositing no density and, for
//any surface point out there, leaving the sun march on its first probe and coming back fully
//lit. Only the strip of building that overlapped the field was ever shadowed.

//The measured bounding box of Nooshin_Export/2026-September, in a Y-up world.
const FACADE: GridBounds = { min: { x: -0.45, y: 0, z: 4.39 }, max: { x: 37.29, y: 7.68, z: 12.07 } };
//What production actually runs: FieldSize {X:10, D:4, Z:10}, so height = 4 + GI_HEADROOM.
const production = () => makeVoxelGrid(10, 10, 10, 128);

const boxContains = (outer: GridBounds, inner: GridBounds) =>
    inner.min.x >= outer.min.x && inner.max.x <= outer.max.x
    && inner.min.y >= outer.min.y && inner.max.y <= outer.max.y
    && inner.min.z >= outer.min.z && inner.max.z <= outer.max.z;

describe('fitVoxelGrid leaves every scene that fits alone', () => {
    test('no statics at all returns the base grid itself', () => {
        const base = production();
        expect(fitVoxelGrid(base, undefined)).toBe(base);
    });

    test('statics inside the field box return the base grid itself', () => {
        const base = production();
        //A plant-sized box near the middle of the field.
        const inside: GridBounds = { min: { x: 4, y: 0, z: 4 }, max: { x: 5, y: 1, z: 5 } };
        expect(fitVoxelGrid(base, inside)).toBe(base);
    });

    test('the grid the fixture renders with is untouched', () => {
        //This is the byte-identity gate. voxelcheck.mjs compares the GPU field against the CPU
        //reference on exactly this grid, and both the parity stills and the 18,333-cell figure
        //assume it has not moved.
        const base = production();
        const fitted = fitVoxelGrid(base, { min: { x: 0, y: 0, z: 0 }, max: { x: 10, y: 5, z: 10 } });
        expect(fitted).toBe(base);
        expect(fitted.dim).toEqual({ x: 128, y: 128, z: 128 });
    });
});

describe('fitVoxelGrid covers an imported building', () => {
    const base = production();
    const fitted = fitVoxelGrid(base, FACADE);

    test('the facade no longer sticks out of the grid', () => {
        //The whole bug, stated as a containment test.
        expect(boxContains(gridBounds(fitted), FACADE)).toBe(true);
    });

    test('the field it was fitted to is still covered', () => {
        expect(boxContains(gridBounds(fitted), gridBounds(base))).toBe(true);
    });

    test('cells stay cubic', () => {
        //A cone trace steps in world units, so anisotropic cells would sample at different
        //rates per axis. Only the COUNTS differ.
        expect(fitted.cell.y).toBe(fitted.cell.x);
        expect(fitted.cell.z).toBe(fitted.cell.x);
    });

    test('the cell is an exact power-of-two multiple of the field cell', () => {
        const ratio = fitted.cell.x / base.cell.x;
        expect(Number.isInteger(Math.log2(ratio))).toBe(true);
        expect(ratio).toBeLessThanOrEqual(MAX_CELL_GROWTH);
    });

    test('the origin lands on the base lattice, so the two nest exactly', () => {
        for (const a of ['x', 'y', 'z'] as const) {
            const steps = (base.origin[a] - fitted.origin[a]) / base.cell.x;
            expect(Math.abs(steps - Math.round(steps))).toBeLessThan(1e-6);
        }
    });

    test('it costs no more cells than the cube it replaces', () => {
        //The point of dropping the cube: a 39 m cube around a 7.7 m tall building is 85 % empty
        //sky, and paying for that at a useful cell size costs hundreds of megabytes.
        expect(cellCount(fitted)).toBeLessThanOrEqual(VOXEL_CELL_BUDGET);
        expect(cellCount(fitted)).toBeLessThanOrEqual(cellCount(base));
        for (const a of ['x', 'y', 'z'] as const) {
            expect(fitted.dim[a]).toBeLessThanOrEqual(MAX_GRID_DIM);
            expect(fitted.dim[a] % GRID_DIM_ALIGN).toBe(0);
        }
    });

    test('the production case resolves to one doubling', () => {
        //Pinned, because it is the number the memory and resolution claims rest on.
        expect(fitted.cell.x).toBeCloseTo(base.cell.x * 2, 9);
        expect(fitted.dim).toEqual({ x: 240, y: 80, z: 80 });
    });

    test('a quad spanning the facade loses no area to clipping', () => {
        //The analytic statement of the defect: on the unfitted grid this area goes missing.
        const out = new Float32Array(cellCount(fitted));
        const a = { x: FACADE.min.x, y: 1, z: FACADE.min.z };
        const b = { x: FACADE.max.x, y: 1, z: FACADE.min.z };
        const c = { x: FACADE.max.x, y: 1, z: FACADE.max.z };
        const lost = scatterTriangleArea(fitted, a, b, c, out, MAX_SAMPLE_BATCHES);
        expect(lost).toBe(0);

        const unfitted = new Float32Array(cellCount(base));
        expect(scatterTriangleArea(base, a, b, c, unfitted, MAX_SAMPLE_BATCHES)).toBeGreaterThan(0);
    });
});

describe('the fitted grid does not crawl', () => {
    //The reason the grid was fitted to the field and not to geometry in the first place: a grid
    //that follows what it contains changes the meaning of every cell when that thing moves, and
    //the lighting visibly swims. Snapping to the base lattice is what buys the same guarantee
    //for statics, which is stronger than "they are rebuilt rarely".
    const base = production();

    test('nudging a wall by less than a cell produces an identical grid', () => {
        const a = fitVoxelGrid(base, FACADE);
        const nudge = base.cell.x * 0.3;
        const moved: GridBounds = {
            min: { x: FACADE.min.x + nudge, y: FACADE.min.y, z: FACADE.min.z },
            max: { x: FACADE.max.x + nudge, y: FACADE.max.y, z: FACADE.max.z },
        };
        expect(fitVoxelGrid(base, moved)).toEqual(a);
    });

    test('fitting the same bounds twice gives the same grid', () => {
        expect(fitVoxelGrid(base, FACADE)).toEqual(fitVoxelGrid(base, FACADE));
    });
});

describe('batchesForBucket bounds the dispatch as well as the samples', () => {
    const cellArea = 0.08125 * 0.08125;

    test('the terrain quad still gets every batch it asks for', () => {
        //Two triangles over 100 m2. This is the case MAX_SAMPLE_BATCHES was raised for, and it
        //must not regress: at one sample per cell the ground mottled 100 % cell to cell.
        const wanted = Math.ceil(Math.ceil((50 / cellArea) * 4) / SAMPLES_PER_INVOCATION);
        expect(batchesForBucket(50, cellArea, 2, 1)).toBe(Math.min(MAX_SAMPLE_BATCHES, wanted));
    });

    test('a detailed mesh is not taxed by its own worst triangle', () => {
        //One 100 m2 slab inside a 1.4 M-triangle facade asked for 948 batches and applied them
        //to every triangle in the bucket: 1.3 billion invocations per rebuild, 99.9 % of which
        //return immediately because the small triangles each need one sample.
        const unbounded = Math.ceil(Math.ceil((100 / cellArea) * 4) / SAMPLES_PER_INVOCATION);
        const bounded = batchesForBucket(100, cellArea, 1_398_079, 1);
        expect(unbounded).toBeGreaterThan(900);
        expect(bounded).toBeLessThan(unbounded);
        expect(bounded * 1_398_079).toBeLessThanOrEqual(1 << 26);
    });

    test('it never returns less than one batch', () => {
        expect(batchesForBucket(0, cellArea, 10_000_000, 1000)).toBe(1);
    });
});
