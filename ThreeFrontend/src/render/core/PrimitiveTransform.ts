//Primitive -> model matrix. Renderer-neutral; no three.
//
//This is the trickiest piece of geometry logic in the app, and it was previously duplicated
//between buildObject3D() and updateObject3D() in ThreeSceneFn.tsx. Extracting it verbatim as
//one function is the point: two renderers reimplementing this independently would drift, and
//the failure mode (organs offset by half their length) is subtle enough to ship unnoticed.

import { Box, Cylinder, Disk, Primitive, Primitives, Rectangle } from '../../helpers/Primitives';
import { Mat4, fromRowMajor3x4, scaleInPlace, setPosition } from './Mat4';

/**
 * Cylinders (stems, petioles, meristems, flower parts) and boxes (roots).
 *
 * three's CylinderGeometry and BoxGeometry are both centred on the origin, but the
 * simulation anchors a segment at its base and stores only the base position in the
 * transform's translation column. So after scaling by (radius, length, radius) the matrix is
 * shifted along its own post-scale Y basis vector by half a length.
 *
 * The three indices read below - m[4], m[5], m[6] - are that post-scale Y basis vector in
 * column-major order. They MUST be read after scaleInPlace, not before; reading them first
 * yields an unscaled offset and every organ lands at its base instead of its centre.
 */
export function decodeCylinderish(p: Cylinder | Box, out: Mat4): Mat4 {
    fromRowMajor3x4(p.affineTransform, out);
    scaleInPlace(out, p.radius, p.length, p.radius);
    return setPosition(out,
        p.affineTransform[3]  + out[4] * 0.5,
        p.affineTransform[7]  + out[5] * 0.5,
        p.affineTransform[11] + out[6] * 0.5);
}

/**
 * Leaves, petals and legacy disks use the wire transform unchanged.
 *
 * For V7 laminae the transform's third column is a plain unit normal rather than a scale:
 * the per-species leaf mesh authors x and y normalized to half-length and half-width, but z
 * as relief in metres, so it must not be scaled again. See the ExportV7 lamina branch in
 * Agro/IrradianceClient.cs.
 */
export function decodePlanar(p: Disk | Rectangle | Cylinder | Box, out: Mat4): Mat4 {
    return fromRowMajor3x4(p.affineTransform, out);
}

/**
 * Dispatches on primitive type. Returns false for primitives that are not drawn at all,
 * which today means only Sphere: buds are disabled in both the parser and both build paths.
 * Sphere is also the one member of the union with no affineTransform, so the early return
 * is what lets the compiler narrow the rest.
 */
export function decodePrimitive(p: Primitive, out: Mat4): boolean {
    switch (p.type) {
        case Primitives.Sphere:
            return false;
        case Primitives.Cylinder:
        case Primitives.Box:
            decodeCylinderish(p, out);
            return true;
        default:
            decodePlanar(p, out);
            return true;
    }
}
