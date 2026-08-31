//The XYZ/RGB orientation box shown when appstate.debugBoxes is on.
//Moved verbatim from helpers/Plant.ts (the boxVerts table) so both renderers share it.
//Hand-built rather than derived from three, because it is the one static geometry with
//per-face vertex colours and a deliberate 2x2x2 size (twice the 1x1x1 root box).
//
//The colour values are consumed by a vertexColors material, and three passes vertex
//colour attributes through to the linear working space unconverted - so these are
//already-linear values and must NOT be run through srgbToLinear.

import { MeshData } from './Geometry';

const LEFT   = [1, 0.0, 0.1];
const RIGHT  = [1, 0.1, 0.1];
const BOTTOM = [0.1, 1, 0.1];
const TOP    = [0, 1, 0];
const FRONT  = [0.1, 0.1, 1];
const BACK   = [0, 0, 1];

const verts: { pos: number[]; norm: number[]; color: number[] }[] = [
    // front
    { pos: [-1, -1,  1], norm: [ 0,  0,  1], color: FRONT },
    { pos: [ 1, -1,  1], norm: [ 0,  0,  1], color: FRONT },
    { pos: [-1,  1,  1], norm: [ 0,  0,  1], color: FRONT },
    { pos: [ 1,  1,  1], norm: [ 0,  0,  1], color: FRONT },
    // right
    { pos: [ 1, -1,  1], norm: [ 1,  0,  0], color: RIGHT },
    { pos: [ 1, -1, -1], norm: [ 1,  0,  0], color: RIGHT },
    { pos: [ 1,  1,  1], norm: [ 1,  0,  0], color: RIGHT },
    { pos: [ 1,  1, -1], norm: [ 1,  0,  0], color: RIGHT },
    // back
    { pos: [ 1, -1, -1], norm: [ 0,  0, -1], color: BACK },
    { pos: [-1, -1, -1], norm: [ 0,  0, -1], color: BACK },
    { pos: [ 1,  1, -1], norm: [ 0,  0, -1], color: BACK },
    { pos: [-1,  1, -1], norm: [ 0,  0, -1], color: BACK },
    // left
    { pos: [-1, -1, -1], norm: [-1,  0,  0], color: LEFT },
    { pos: [-1, -1,  1], norm: [-1,  0,  0], color: LEFT },
    { pos: [-1,  1, -1], norm: [-1,  0,  0], color: LEFT },
    { pos: [-1,  1,  1], norm: [-1,  0,  0], color: LEFT },
    // top
    { pos: [ 1,  1, -1], norm: [ 0,  1,  0], color: TOP },
    { pos: [-1,  1, -1], norm: [ 0,  1,  0], color: TOP },
    { pos: [ 1,  1,  1], norm: [ 0,  1,  0], color: TOP },
    { pos: [-1,  1,  1], norm: [ 0,  1,  0], color: TOP },
    // bottom
    { pos: [ 1, -1,  1], norm: [ 0, -1,  0], color: BOTTOM },
    { pos: [-1, -1,  1], norm: [ 0, -1,  0], color: BOTTOM },
    { pos: [ 1, -1, -1], norm: [ 0, -1,  0], color: BOTTOM },
    { pos: [-1, -1, -1], norm: [ 0, -1,  0], color: BOTTOM },
];

function build(): MeshData {
    const positions = new Float32Array(verts.length * 3);
    const normals = new Float32Array(verts.length * 3);
    const colors = new Float32Array(verts.length * 3);

    verts.forEach((v, i) => {
        positions.set(v.pos, i * 3);
        normals.set(v.norm, i * 3);
        colors.set(v.color, i * 3);
    });

    return {
        positions,
        normals,
        colors,
        indices: new Uint32Array([
             0,  1,  2,   2,  1,  3,  // front
             4,  5,  6,   6,  5,  7,  // right
             8,  9, 10,  10,  9, 11,  // back
            12, 13, 14,  14, 13, 15,  // left
            16, 17, 18,  18, 17, 19,  // top
            20, 21, 22,  22, 21, 23,  // bottom
        ]),
        name: 'DebugBox',
    };
}

export const debugBoxMesh: MeshData = build();
