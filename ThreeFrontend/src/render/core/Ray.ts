//Renderer-neutral ray math for picking and dragging. Nothing in this file may import three.
//
//The Three path builds these from THREE.Raycaster; the WebGPU path will build them from an
//inverse view-projection matrix. Keeping the downstream consumers (the selection FSM, the
//plane drag) expressed in terms of this type is what lets both backends share them.

import { Mat4 } from './Mat4';
import { Vec3, normalize, sub, add, scale, dot, lengthSq } from './Vec3';

export type Ray = { o: Vec3; d: Vec3 };

/**
 * Builds a world-space ray through a normalized-device-coordinate point.
 *
 * Mirrors what ThreeSceneFn did with THREE.Vector3.unproject: take the NDC point at the far
 * plane, unproject it, and aim from the camera position at it. `ndcZ` is 1 for the GL
 * convention the Three path uses; the WebGPU path also uses 1 for its far plane under the
 * [0,1] depth range, so the same value works for both.
 */
export function rayFromNDC(invViewProj: Mat4, camPos: Vec3, ndcX: number, ndcY: number, ndcZ = 1): Ray {
    const p = transformPoint(invViewProj, ndcX, ndcY, ndcZ);
    return { o: camPos, d: normalize(sub(p, camPos)) };
}

/** Applies a 4x4 (column-major) to a point, dividing through by w. */
export function transformPoint(m: Mat4, x: number, y: number, z: number): Vec3 {
    const px = m[0] * x + m[4] * y + m[8] * z + m[12];
    const py = m[1] * x + m[5] * y + m[9] * z + m[13];
    const pz = m[2] * x + m[6] * y + m[10] * z + m[14];
    const pw = m[3] * x + m[7] * y + m[11] * z + m[15];
    const inv = pw !== 0 ? 1 / pw : 1;
    return { x: px * inv, y: py * inv, z: pz * inv };
}

/**
 * Intersects with the horizontal plane at the given height.
 *
 * Equivalent to THREE.Ray.intersectPlane(new THREE.Plane(new Vector3(0,1,0), -y)), which is
 * how BaseRequestObject.grab/move projected the cursor onto the object's own height. Returns
 * undefined when the ray is parallel to the plane or points away from it, matching three's
 * behaviour of returning null rather than a point behind the origin.
 */
export function intersectPlaneY(ray: Ray, y: number): Vec3 | undefined {
    const denom = ray.d.y;
    if (Math.abs(denom) < 1e-12) return undefined;

    const t = (y - ray.o.y) / denom;
    if (t < 0) return undefined;

    return add(ray.o, scale(ray.d, t));
}

/**
 * Closest point on an infinite axis to the ray, for axis-constrained gizmo dragging.
 * Returns undefined when the ray and the axis are parallel.
 */
export function closestPointOnAxis(ray: Ray, origin: Vec3, axis: Vec3): Vec3 | undefined {
    const a = normalize(axis);
    const w0 = sub(ray.o, origin);
    const b = dot(ray.d, a);
    const denom = 1 - b * b;
    if (Math.abs(denom) < 1e-12) return undefined;

    const d = dot(ray.d, w0);
    const e = dot(a, w0);
    const t = (b * d - e) / denom;
    return add(origin, scale(a, t));
}

/** Squared distance from a point to the ray, used for proximity picking fallbacks. */
export function distanceSqToPoint(ray: Ray, p: Vec3): number {
    const w = sub(p, ray.o);
    const t = dot(w, ray.d);
    const closest = add(ray.o, scale(ray.d, Math.max(0, t)));
    return lengthSq(sub(p, closest));
}
