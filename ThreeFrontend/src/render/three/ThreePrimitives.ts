//The shared static BufferGeometry prototypes.
//
//These used to live at the bottom of ThreeSceneFn.tsx while helpers/Plant.ts imported them
//back - a genuine import cycle that only survived on ESM live bindings. Both sides now
//depend on this leaf module instead, and every prototype is derived from the same neutral
//MeshData table the WebGPU renderer will consume, so the two backends cannot drift.

import * as THREE from 'three';
import { Geom } from '../core/GeometryIds';
import { toBufferGeometry } from './ThreeGeometry';
import { staticGeometry } from '../core/StaticGeometry';

const of = (id: Geom) => toBufferGeometry(staticGeometry(id));

/** BoxGeometry().translate(0.5, 0.5, 0.5) - corner at the origin. Terrain boxes. */
export const terrainBoxPrimitive: THREE.BufferGeometry = of(Geom.BoxCorner);
/** BoxGeometry() - origin-centred. Roots. */
export const threeBoxPrimitive: THREE.BufferGeometry = of(Geom.BoxCentered);
/** BoxGeometry().translate(0, 0.5, 0) - bottom face on the origin. Obstacle walls. */
export const obstacleBoxPrimitive: THREE.BufferGeometry = of(Geom.BoxBottomCentered);
/** SphereGeometry(1, 8, 8). Buds - disabled throughout, kept for id stability. */
export const threeSpherePrimitive: THREE.BufferGeometry = of(Geom.Sphere);
/** CylinderGeometry(1, 1, 1, 8). Stems and every cylinder-shaped organ. */
export const threeCylinderPrimitive: THREE.BufferGeometry = of(Geom.Cylinder);
/** PlaneGeometry(2, 2). Leaf fallback when the backend served no per-species mesh. */
export const threePlanePrimitive: THREE.BufferGeometry = of(Geom.Plane);
/** CircleGeometry(0.5, 12).rotateX(-PI/2). Primitives.Disk. */
export const threeCirclePrimitive: THREE.BufferGeometry = of(Geom.CircleHalf);
/** CircleGeometry().rotateX(-PI/2) - unit radius. Obstacle umbrellas. */
export const obstacleDiskPrimitive: THREE.BufferGeometry = of(Geom.CircleUnit);
/** DodecahedronGeometry(0.03). Seed markers. */
export const seedPrimitive: THREE.BufferGeometry = of(Geom.Dodecahedron);
/** Vertex-coloured 2x2x2 XYZ orientation box for the debugBoxes overlay. */
export const debugBoxPrimitive: THREE.BufferGeometry = of(Geom.DebugBox);
