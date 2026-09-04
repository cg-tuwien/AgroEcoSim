//A leaf-area-density field for the WebGL backend, built on the CPU and sampled as a 3D texture.
//
//The WebGPU backend abandoned shadow maps for exactly this, and the WebGL one needed to follow:
//a zero-thickness leaf writes its own depth into a shadow map and then tests against it, which
//is a tie no bias can settle (see ThreeDensityShader for the full reasoning). Integrating
//extinction along the ray to the sun has no such tie and, as a bonus, the same field answers
//the ambient-occlusion question - so one structure fixes both of the WebGL path's lighting
//complaints.
//
//The grid, the sampling and the area-to-extinction conversion all come from core/VoxelGrid,
//the same code the GPU path is verified against, so the two backends cannot drift apart in
//what they consider dense.

import * as THREE from 'three';
import { InstanceList, IFlags } from '../core/InstanceList';
import { getGeometry } from '../core/GeometryStore';
import { VoxelGrid, extinctionFromArea, makeVoxelGrid, scatterTriangleArea } from '../core/VoxelGrid';
import { Vec3 } from '../core/Vec3';
import {
    DENSITY_FRAGMENT_APPLY, DENSITY_FRAGMENT_HEAD, DENSITY_VERTEX_APPLY, DENSITY_VERTEX_HEAD,
} from './ThreeDensityShader';

/**
 * Cells per axis.
 *
 * 128 over a ~10 m field is an 8 cm cell. That number is chosen against the thing being shaded,
 * not against a memory budget: the plants are about 30 cm across, so at 64 (a 16 cm cell) a
 * whole plant spanned under two cells and ambient occlusion had nothing local to sample - it
 * changed the image by 4/255, which is measurable and invisible. Occlusion is a contact effect
 * and needs cells smaller than the gaps it is meant to darken.
 *
 * The cost is a 4 MB texture re-uploaded whenever the scene changes, and a 2M-cell sweep to
 * convert area to extinction - which is why that sweep skips empty cells, since a canopy fills
 * a few per cent of the grid at most.
 */
export const DENSITY_DIM = 128;

/**
 * Triangles above which the field is skipped entirely.
 *
 * Scattering is linear in triangles and runs on the main thread, so a canopy large enough to
 * make it visible would stall the tab between timesteps. WebGL is already the wrong backend at
 * that size - it draws one call per primitive - so refusing loudly beats freezing quietly.
 */
export const DENSITY_TRIANGLE_BUDGET = 400000;

export class ThreeDensityField {
    grid: VoxelGrid;
    readonly texture: THREE.Data3DTexture;

    /**
     * Shared by every patched material.
     *
     * three copies a material's uniform *object references* into its program, so assigning the
     * same objects to every material means one write here updates all of them - no traverse,
     * and no per-material uniform bookkeeping.
     */
    readonly uniforms = {
        uDensityTex: { value: null as THREE.Data3DTexture | null },
        uDensityOrigin: { value: new THREE.Vector3() },
        uDensityInvSize: { value: new THREE.Vector3(1, 1, 1) },
        uDensitySunDir: { value: new THREE.Vector3(0, 1, 0) },
        uDensityCell: { value: 1 },
        uDensitySun: { value: 0 },
        uDensityAo: { value: 0 },
    };

    private area: Float32Array;
    private sigma: Uint16Array;
    private reportedBudget = false;
    /** Set when the last build was skipped, so the shader is not fed a stale field. */
    private empty = true;

    constructor() {
        this.grid = makeVoxelGrid(1, 1, 1, DENSITY_DIM);
        const cells = DENSITY_DIM * DENSITY_DIM * DENSITY_DIM;
        this.area = new Float32Array(cells);
        this.sigma = new Uint16Array(cells);

        //Half float rather than float: linear filtering of half-float textures is core WebGL2,
        //while filtering a 32-bit float texture needs OES_texture_float_linear, which is not
        //guaranteed. Unfiltered sampling would make the marches step visibly cell to cell.
        this.texture = new THREE.Data3DTexture(this.sigma, DENSITY_DIM, DENSITY_DIM, DENSITY_DIM);
        this.texture.format = THREE.RedFormat;
        this.texture.type = THREE.HalfFloatType;
        this.texture.minFilter = THREE.LinearFilter;
        this.texture.magFilter = THREE.LinearFilter;
        this.texture.wrapS = this.texture.wrapT = this.texture.wrapR = THREE.ClampToEdgeWrapping;
        this.texture.needsUpdate = true;
        this.uniforms.uDensityTex.value = this.texture;
    }

    /**
     * Rebuilds the field from the current plant instances.
     *
     * Only plants are injected. Terrain and obstacles are the things being shaded, not the
     * canopy doing the shading, and a ground plane's worth of density at the bottom of the grid
     * would darken everything standing on it.
     */
    build(list: InstanceList, fieldSizeX: number, fieldSizeZ: number, height: number) {
        this.grid = makeVoxelGrid(fieldSizeX, fieldSizeZ, height, DENSITY_DIM);

        let triangles = 0;
        for (let i = 0; i < list.count; ++i) {
            const inst = list.items[i];
            if (inst.flags & IFlags.Hidden) continue;
            const mesh = getGeometry(inst.geom);
            if (mesh) triangles += mesh.indices.length / 3;
        }

        if (triangles > DENSITY_TRIANGLE_BUDGET) {
            if (!this.reportedBudget) {
                this.reportedBudget = true;
                console.warn(
                    `Density field skipped: ${triangles} triangles exceeds the ${DENSITY_TRIANGLE_BUDGET} ` +
                    `budget for CPU voxelisation. Volumetric shadows and occlusion are off for this scene ` +
                    `on the WebGL backend; the WebGPU backend voxelises on the GPU and has no such limit.`);
            }
            this.clear();
            return;
        }

        this.area.fill(0);
        //Cleared here rather than per cell below: the conversion sweep only writes cells that
        //have area, so anything left from the previous scene has to go first. memset over a few
        //megabytes is far cheaper than a branchless write per cell.
        this.sigma.fill(0);

        const a: Vec3 = { x: 0, y: 0, z: 0 };
        const b: Vec3 = { x: 0, y: 0, z: 0 };
        const c: Vec3 = { x: 0, y: 0, z: 0 };

        for (let i = 0; i < list.count; ++i) {
            const inst = list.items[i];
            if (inst.flags & IFlags.Hidden) continue;

            const mesh = getGeometry(inst.geom);
            if (!mesh) continue;

            const m = inst.model;
            const p = mesh.positions;
            const idx = mesh.indices;

            for (let t = 0; t < idx.length; t += 3) {
                transformInto(m, p, idx[t] * 3, a);
                transformInto(m, p, idx[t + 1] * 3, b);
                transformInto(m, p, idx[t + 2] * 3, c);
                //One sample batch: an organ is smaller than a cell, so a single splat per
                //triangle already lands in the right place. Only the terrain quad, which is not
                //injected here, ever needed more.
                scatterTriangleArea(this.grid, a, b, c, this.area, 1);
            }
        }

        //A canopy occupies a small fraction of the grid, so skipping empty cells turns a
        //2M-element float conversion into a few thousand.
        for (let i = 0; i < this.area.length; ++i)
            if (this.area[i] > 0)
                this.sigma[i] = THREE.DataUtils.toHalfFloat(extinctionFromArea(this.area[i], this.grid));

        this.empty = false;
        this.upload();
    }

    /** Zeroes the field, so nothing occludes and nothing shadows. */
    clear() {
        if (this.empty) return;
        this.sigma.fill(0);
        this.empty = true;
        this.upload();
    }

    private upload() {
        const g = this.grid;
        this.uniforms.uDensityOrigin.value.set(g.origin.x, g.origin.y, g.origin.z);
        this.uniforms.uDensityInvSize.value.set(1 / g.size.x, 1 / g.size.y, 1 / g.size.z);
        //Cells are near-cubic by construction, so one scalar step length is enough.
        this.uniforms.uDensityCell.value = g.cell.x;
        this.texture.needsUpdate = true;
    }

    /** World-space direction towards the sun. */
    setSunDirection(d: Vec3) {
        this.uniforms.uDensitySunDir.value.set(d.x, d.y, d.z).normalize();
    }

    /** Both are 0..1 mixes, so a disabled term multiplies by exactly 1. */
    setStrengths(sun: number, ao: number) {
        this.uniforms.uDensitySun.value = sun;
        this.uniforms.uDensityAo.value = ao;
    }

    /**
     * Injects the field lookups into one of three's standard materials.
     *
     * The code is injected unconditionally and gated by uniforms rather than by a #define. A
     * define would mean recompiling every material in the scene each time the user ticks a
     * lighting box - which is what made the earlier shadow toggle appear to do nothing until the
     * next timestep - whereas a uniform at zero costs one branch and multiplies by exactly 1.0.
     *
     * `customProgramCacheKey` keeps these programs distinct from any unpatched material's, so
     * three cannot hand a patched material a cached program compiled without the varyings.
     */
    patch(material: THREE.Material) {
        material.onBeforeCompile = shader => {
            Object.assign(shader.uniforms, this.uniforms);

            shader.vertexShader = shader.vertexShader
                .replace('#include <common>', `#include <common>\n${DENSITY_VERTEX_HEAD}`)
                .replace('#include <project_vertex>', `#include <project_vertex>\n${DENSITY_VERTEX_APPLY}`);

            shader.fragmentShader = shader.fragmentShader
                .replace('#include <common>', `#include <common>\n${DENSITY_FRAGMENT_HEAD}`)
                .replace('#include <lights_fragment_begin>',
                         `#include <lights_fragment_begin>\n${DENSITY_FRAGMENT_APPLY}`);
        };
        material.customProgramCacheKey = () => 'density-field';
        material.needsUpdate = true;
    }

    dispose() {
        this.texture.dispose();
    }
}

/** Transforms one vertex by a column-major 4x4 into `out`. */
function transformInto(m: Float32Array, p: Float32Array, at: number, out: Vec3) {
    const x = p[at], y = p[at + 1], z = p[at + 2];
    out.x = m[0] * x + m[4] * y + m[8] * z + m[12];
    out.y = m[1] * x + m[5] * y + m[9] * z + m[13];
    out.z = m[2] * x + m[6] * y + m[10] * z + m[14];
}
