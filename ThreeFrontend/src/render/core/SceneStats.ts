//Object and triangle totals for the HUD.
//
//Two things were wrong with computing these inline in the HUD component.
//
//It was O(primitives) on EVERY render, twice - one nested reduce for objects and another for
//triangles - inside a component subscribed to `scene` and `playPointer`. The production test
//site draws 7,700 plants, about 1.26 million primitives, so that was ~2.5 million iterations
//per frame to display two numbers. Memoised per scene array here instead: the decoder hands out
//a fresh array per timestep, so identity is exactly the right cache key, and a WeakMap lets a
//superseded scene be collected.
//
//And the triangle counts were simply wrong. The table was `[0, 12, 32, 0, 128, 0, 0, 0, 2]`
//indexed by `Primitives` (Disk, Rectangle, Cylinder, Sphere, Box), but those values were
//authored for the legacy V3 wire codes (1/2/4/8) and never remapped - so a leaf reported 12
//triangles instead of 2, a root box 128 instead of 12, and a disk 0. Counting from the real
//geometry rather than a hand-written table means it cannot drift again.

import { PlantModel } from '../../helpers/Scene';
import { Primitives } from '../../helpers/Primitives';
import { Geom } from './GeometryIds';
import { geometryIdFor, getGeometry } from './GeometryStore';
import { triangleCount } from './Geometry';

export type SceneStats = { objects: number; triangles: number };

/**
 * Resolves a species' leaf mesh to the geometry id the renderer is actually drawing.
 *
 * Looks the registration up rather than creating one - RendererHost registers `leaf:<name>`
 * when it builds the scene, and a species whose mesh has not arrived yet draws the fallback
 * plane, which is exactly what should be counted for it.
 */
export function leafGeomIdFor(species: string): number {
    return geometryIdFor(`leaf:${species}`) ?? Geom.Plane;
}

const cache = new WeakMap<PlantModel[], SceneStats>();

function trianglesOf(geomId: number): number {
    const mesh = getGeometry(geomId);
    return mesh ? triangleCount(mesh) : 0;
}

/**
 * Totals for one decoded scene. `leafGeom` resolves a species' leaf mesh the same way
 * BuildScene does; pass undefined and leaves are counted as the fallback plane, which is what
 * the renderer draws when the backend served no per-species mesh anyway.
 */
export function sceneStats(scene: PlantModel[], leafGeom?: (species: string) => number): SceneStats {
    const hit = cache.get(scene);
    if (hit) return hit;

    let objects = 0, triangles = 0;
    for (const plant of scene) {
        const leafTris = trianglesOf(leafGeom ? leafGeom(plant.species) : Geom.Plane);
        objects += plant.primitives.length;

        for (const p of plant.primitives)
            switch (p.type) {
                case Primitives.Rectangle: triangles += leafTris; break;
                case Primitives.Cylinder:  triangles += trianglesOf(Geom.Cylinder); break;
                case Primitives.Box:       triangles += trianglesOf(Geom.BoxCentered); break;
                case Primitives.Disk:      triangles += trianglesOf(Geom.CircleHalf); break;
                //Spheres (buds) are decoded but never emitted by BuildScene, so they draw
                //nothing and must not be counted.
                default: break;
            }
    }

    const stats = { objects, triangles };
    cache.set(scene, stats);
    return stats;
}
