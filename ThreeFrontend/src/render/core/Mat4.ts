//Renderer-neutral matrix math. Nothing in this file may import three.
//
//Matrices are Float32Array(16) in COLUMN-MAJOR order, matching THREE.Matrix4.elements and
//WGSL's mat4x4f, so the same array can be handed to either backend without a transpose.
//
//Beware the index convention: three's Matrix4.set() takes ROW-major arguments and stores
//column-major, and its .elements[4] is therefore row 0 / column 1. The helpers below are
//written in terms of the stored column-major layout.

import { Vec3 } from './Vec3';

export type Mat4 = Float32Array;   // 16, column-major
export type Mat3 = Float32Array;   // 9,  column-major

export function identity(out: Mat4): Mat4 {
    out.fill(0);
    out[0] = out[5] = out[10] = out[15] = 1;
    return out;
}

export function createMat4(): Mat4 {
    return identity(new Float32Array(16));
}

/**
 * Decodes the wire format's row-major 3x4 affine transform into a column-major mat4.
 *
 * The backend writes 12 floats as three rows of four (Agro/BinaryWriterExtensions.WriteM32),
 * with the implied bottom row 0,0,0,1. The Three path expressed this as
 *   new THREE.Matrix4().fromArray([...affineTransform, 0,0,0,1]).transpose()
 * which is the same thing: fromArray treats the input as column-major, and the transpose
 * then reinterprets it as row-major.
 */
export function fromRowMajor3x4(src: Float32Array | number[], out: Mat4): Mat4 {
    //src row r, column c  ->  out column c, row r  ->  out[c * 4 + r]
    out[0] = src[0];  out[4] = src[1];  out[8]  = src[2];  out[12] = src[3];
    out[1] = src[4];  out[5] = src[5];  out[9]  = src[6];  out[13] = src[7];
    out[2] = src[8];  out[6] = src[9];  out[10] = src[10]; out[14] = src[11];
    out[3] = 0;       out[7] = 0;       out[11] = 0;       out[15] = 1;
    return out;
}

/** Post-multiplies by a scale, i.e. scales each basis vector. Matches THREE.Matrix4.scale(). */
export function scaleInPlace(m: Mat4, sx: number, sy: number, sz: number): Mat4 {
    m[0] *= sx; m[1] *= sx; m[2] *= sx; m[3] *= sx;
    m[4] *= sy; m[5] *= sy; m[6] *= sy; m[7] *= sy;
    m[8] *= sz; m[9] *= sz; m[10] *= sz; m[11] *= sz;
    return m;
}

/** Overwrites the translation column. Matches THREE.Matrix4.setPosition(). */
export function setPosition(m: Mat4, x: number, y: number, z: number): Mat4 {
    m[12] = x; m[13] = y; m[14] = z;
    return m;
}

/**
 * The inverse-transpose of the upper-left 3x3, for transforming normals.
 *
 * Uses the cofactor matrix rather than a true inverse-transpose: cofactor == det * inv(M)^T,
 * and since the result is normalized in the shader the |det| factor cancels. This also stays
 * finite for the degenerate transforms the simulation can emit for a zero-length organ,
 * where a real inverse would divide by zero.
 *
 * Note V7 leaf transforms are left-handed (ExportV7 builds ay = -z * scale, so det < 0);
 * the sign is absorbed by the shader's front_facing flip.
 */
export function normalMatrix3(m: Mat4, out: Mat3): Mat3 {
    const a = m[0], b = m[1], c = m[2];
    const d = m[4], e = m[5], f = m[6];
    const g = m[8], h = m[9], i = m[10];

    //Columns of the cofactor matrix are the cross products of the other two basis vectors.
    out[0] = e * i - f * h;
    out[1] = f * g - d * i;
    out[2] = d * h - e * g;

    out[3] = c * h - b * i;
    out[4] = a * i - c * g;
    out[5] = b * g - a * h;

    out[6] = b * f - c * e;
    out[7] = c * d - a * f;
    out[8] = a * e - b * d;

    return out;
}

/**
 * World-to-view matrix, equivalent to THREE.Matrix4.lookAt inverted (i.e. what a camera's
 * matrixWorldInverse holds). Right-handed, looking down -Z, matching three's convention so
 * the same pose produces the same image on both backends.
 */
export function lookAt(eye: Vec3, target: Vec3, up: Vec3, out: Mat4): Mat4 {
    let zx = eye.x - target.x, zy = eye.y - target.y, zz = eye.z - target.z;
    let len = Math.hypot(zx, zy, zz);
    //A degenerate eye==target would otherwise produce NaNs across the whole frame.
    if (len < 1e-9) { zx = 0; zy = 0; zz = 1; len = 1; }
    zx /= len; zy /= len; zz /= len;

    let xx = up.y * zz - up.z * zy;
    let xy = up.z * zx - up.x * zz;
    let xz = up.x * zy - up.y * zx;
    len = Math.hypot(xx, xy, xz);
    if (len < 1e-9) {
        //up is parallel to the view direction; nudge it the way three does.
        xx = 1; xy = 0; xz = 0;
    } else {
        xx /= len; xy /= len; xz /= len;
    }

    const yx = zy * xz - zz * xy;
    const yy = zz * xx - zx * xz;
    const yz = zx * xy - zy * xx;

    //Column-major: the rotation is the transpose of the basis, translation is -R*eye.
    out[0] = xx; out[4] = xy; out[8] = xz;  out[12] = -(xx * eye.x + xy * eye.y + xz * eye.z);
    out[1] = yx; out[5] = yy; out[9] = yz;  out[13] = -(yx * eye.x + yy * eye.y + yz * eye.z);
    out[2] = zx; out[6] = zy; out[10] = zz; out[14] = -(zx * eye.x + zy * eye.y + zz * eye.z);
    out[3] = 0;  out[7] = 0;  out[11] = 0;  out[15] = 1;
    return out;
}

/** Transforms a direction by the rotation part only, then normalizes. */
export function transformDirection(m: Mat4, v: Vec3): Vec3 {
    const x = m[0] * v.x + m[4] * v.y + m[8] * v.z;
    const y = m[1] * v.x + m[5] * v.y + m[9] * v.z;
    const z = m[2] * v.x + m[6] * v.y + m[10] * v.z;
    const len = Math.hypot(x, y, z) || 1;
    return { x: x / len, y: y / len, z: z / len };
}

export function multiply(a: Mat4, b: Mat4, out: Mat4): Mat4 {
    for (let c = 0; c < 4; ++c) {
        const b0 = b[c * 4], b1 = b[c * 4 + 1], b2 = b[c * 4 + 2], b3 = b[c * 4 + 3];
        out[c * 4]     = a[0] * b0 + a[4] * b1 + a[8]  * b2 + a[12] * b3;
        out[c * 4 + 1] = a[1] * b0 + a[5] * b1 + a[9]  * b2 + a[13] * b3;
        out[c * 4 + 2] = a[2] * b0 + a[6] * b1 + a[10] * b2 + a[14] * b3;
        out[c * 4 + 3] = a[3] * b0 + a[7] * b1 + a[11] * b2 + a[15] * b3;
    }
    return out;
}

/**
 * Orthographic projection producing WebGPU clip space, z in [0, 1].
 *
 * Used for the shadow camera. As with the perspective version, three's orthographic matrix
 * maps z to [-1, 1] and cannot be reused directly.
 */
export function orthographicZeroToOne(
    left: number, right: number, bottom: number, top: number, near: number, far: number, out: Mat4,
): Mat4 {
    out.fill(0);
    out[0] = 2 / (right - left);
    out[5] = 2 / (top - bottom);
    //Depth maps near -> 0 and far -> 1, looking down -Z.
    out[10] = 1 / (near - far);
    out[12] = -(right + left) / (right - left);
    out[13] = -(top + bottom) / (top - bottom);
    out[14] = near / (near - far);
    out[15] = 1;
    return out;
}

/**
 * Perspective projection producing WebGPU clip space, z in [0, 1].
 *
 * three's PerspectiveCamera builds an OpenGL-style matrix with z in [-1, 1]. Reusing that
 * unmodified under WebGPU silently clips everything in the near half of the depth range,
 * which is the single most likely "nothing renders" bug in the port - hence a separate
 * function rather than borrowing the camera's matrix.
 */
export function perspectiveZeroToOne(fovYRadians: number, aspect: number, near: number, far: number, out: Mat4): Mat4 {
    const f = 1 / Math.tan(fovYRadians * 0.5);
    out.fill(0);
    out[0] = f / aspect;
    out[5] = f;
    out[11] = -1;
    if (far === Infinity) {
        out[10] = -1;
        out[14] = -near;
    } else {
        const nf = 1 / (near - far);
        out[10] = far * nf;
        out[14] = far * near * nf;
    }
    return out;
}

/**
 * Reversed-Z perspective projection: the near plane maps to 1 and the far plane to 0.
 *
 * Why bother. Depth precision under a standard [0,1] mapping is governed by 1/z, so almost
 * every representable value is spent just in front of the near plane. This scene's frustum is
 * near = 0.025 m against far = 5000 m - a ratio of 200,000 - which is about as hostile as it
 * gets, and it showed up as z-fighting between the terrain and the umbrellas lying on it.
 *
 * Reversing the range makes the two curves work against each other instead of compounding:
 * a float depth buffer's exponent gives its finest steps near 0, which is now the far plane,
 * exactly where 1/z is coarsest. The result is near-uniform relative precision across the
 * whole range.
 *
 * The reversal is only worth anything with a floating-point depth buffer. On a normalised
 * integer format the quantisation is uniform across [0,1], so flipping the range is a pure
 * relabelling and buys nothing - which is why DEPTH_FORMAT moved to depth32float in the same
 * change. Anything consuming this must also flip its depth compare and clear to 0; those live
 * as DEPTH_COMPARE and DEPTH_CLEAR_VALUE next to the format so they cannot drift apart.
 *
 * Equivalent to perspectiveZeroToOne with near and far exchanged, but written out because the
 * infinite-far case collapses to something much simpler than the swap suggests.
 */
export function perspectiveReversedZ(fovYRadians: number, aspect: number, near: number, far: number, out: Mat4): Mat4 {
    const f = 1 / Math.tan(fovYRadians * 0.5);
    out.fill(0);
    out[0] = f / aspect;
    out[5] = f;
    out[11] = -1;
    if (far === Infinity) {
        //z_ndc = near / -z_view: exactly 1 at the near plane, approaching 0 at infinity, with
        //no far plane to choose at all.
        out[10] = 0;
        out[14] = near;
    } else {
        const fn = 1 / (far - near);
        out[10] = near * fn;
        out[14] = far * near * fn;
    }
    return out;
}

/**
 * Inverse of a rigid transform (rotation plus translation, no scale or shear).
 *
 * A view matrix is always rigid, so the inverse is the transpose of the rotation and the
 * negated, rotated translation - no determinant, no division, and exact. Using a general
 * inverse here would be slower and would introduce rounding where none is needed.
 */
export function invertRigid(m: Mat4, out: Mat4): Mat4 {
    out[0] = m[0]; out[1] = m[4]; out[2] = m[8];  out[3] = 0;
    out[4] = m[1]; out[5] = m[5]; out[6] = m[9];  out[7] = 0;
    out[8] = m[2]; out[9] = m[6]; out[10] = m[10]; out[11] = 0;
    const tx = m[12], ty = m[13], tz = m[14];
    out[12] = -(out[0] * tx + out[4] * ty + out[8] * tz);
    out[13] = -(out[1] * tx + out[5] * ty + out[9] * tz);
    out[14] = -(out[2] * tx + out[6] * ty + out[10] * tz);
    out[15] = 1;
    return out;
}
