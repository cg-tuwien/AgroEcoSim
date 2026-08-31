//Minimal renderer-neutral vector math. Nothing in this file may import three.
//
//Deliberately small: this exists to replace the handful of THREE.Vector2/Vector3 uses that
//were scattered through appstate and the interaction helpers, not to become a maths library.
//Vectors are plain objects rather than typed arrays because they are used one at a time in
//cold paths (picking, seed placement); the hot per-instance paths use Float32Array via Mat4.

export type Vec3 = { x: number; y: number; z: number };
export type Vec2 = { x: number; y: number };

export function vec3(x = 0, y = 0, z = 0): Vec3 {
    return { x, y, z };
}

export function add(a: Vec3, b: Vec3): Vec3 {
    return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z };
}

export function sub(a: Vec3, b: Vec3): Vec3 {
    return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z };
}

export function scale(a: Vec3, s: number): Vec3 {
    return { x: a.x * s, y: a.y * s, z: a.z * s };
}

export function dot(a: Vec3, b: Vec3): number {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

export function cross(a: Vec3, b: Vec3): Vec3 {
    return {
        x: a.y * b.z - a.z * b.y,
        y: a.z * b.x - a.x * b.z,
        z: a.x * b.y - a.y * b.x,
    };
}

export function lengthSq(a: Vec3): number {
    return a.x * a.x + a.y * a.y + a.z * a.z;
}

export function length(a: Vec3): number {
    return Math.sqrt(lengthSq(a));
}

export function normalize(a: Vec3): Vec3 {
    const l = length(a);
    return l > 0 ? scale(a, 1 / l) : { x: 0, y: 0, z: 0 };
}

export function distanceSq(a: Vec3, b: Vec3): number {
    const dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
    return dx * dx + dy * dy + dz * dz;
}

//--- 2D, for the MeshTerrainItem principal-direction PCA ------------------------------------

export function vec2(x = 0, y = 0): Vec2 {
    return { x, y };
}

export function normalize2(a: Vec2): Vec2 {
    const l = Math.sqrt(a.x * a.x + a.y * a.y);
    return l > 0 ? { x: a.x / l, y: a.y / l } : { x: 0, y: 0 };
}

/** The 2D perpendicular THREE.Vector2 users spell as (-y, x). */
export function perp2(a: Vec2): Vec2 {
    return { x: -a.y, y: a.x };
}

export function addScaled2(a: Vec2, b: Vec2, s: number): Vec2 {
    return { x: a.x + b.x * s, y: a.y + b.y * s };
}
