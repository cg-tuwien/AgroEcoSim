//Hand-written generators for the ten static geometries, replacing the bootstrap that derived
//them from three (see three/ThreeGeometrySource.ts, now kept only as the test oracle).
//
//These are transcriptions of three r152's generators, not reimplementations: the vertex
//order, the winding, the uv conventions and the arithmetic are reproduced literally, because
//anything else changes shading and z-fighting between the two backends. tests/geometry.test.ts
//compares every array element-wise against three and must stay exact.
//
//Two consequences of that literalness are worth knowing before touching anything here:
//
//  - three builds each geometry in f64 and only truncates to f32 at setAttribute. Where a
//    later stage reads a buffer back (the dodecahedron's flat normals, which are computed
//    from the already-stored positions), the f32 rounding is observable, so the generators
//    round at the same points three does.
//  - the transforms are applied through a faithful copy of three's applyMatrix4, including
//    getNormalMatrix's invert-and-transpose. rotateX(-PI/2) has cos(-PI/2) = 6.1e-17 rather
//    than 0, so the rotated circles carry a tiny non-zero component that a "clean" rotation
//    would not produce. It is invisible, but it is what three ships.

import { MeshData, normalizeInPlace } from './Geometry';
import { Geom, STATIC_GEOMETRY_COUNT } from './GeometryIds';
import { debugBoxMesh } from './DebugBox';
import { setStaticGeometry } from './GeometryStore';

/** Geometry under construction: plain f64 arrays, exactly as three accumulates them. */
type Raw = { positions: number[]; normals: number[]; uvs: number[]; indices: number[] };

const raw = (): Raw => ({ positions: [], normals: [], uvs: [], indices: [] });

function finish(r: Raw, name: string): MeshData {
    return {
        positions: new Float32Array(r.positions),
        normals: new Float32Array(r.normals),
        uvs: new Float32Array(r.uvs),
        indices: Uint32Array.from(r.indices),
        name,
    };
}

// ---------------------------------------------------------------- box

export function boxGeometry(width = 1, height = 1, depth = 1,
                            widthSegments = 1, heightSegments = 1, depthSegments = 1): Raw {
    const r = raw();
    widthSegments = Math.floor(widthSegments);
    heightSegments = Math.floor(heightSegments);
    depthSegments = Math.floor(depthSegments);

    let numberOfVertices = 0;
    //Axis indices standing in for three's vector[u]/vector[v]/vector[w] string keys.
    const X = 0, Y = 1, Z = 2;

    const buildPlane = (u: number, v: number, w: number, udir: number, vdir: number,
                        planeW: number, planeH: number, planeD: number, gridX: number, gridY: number) => {
        const segmentWidth = planeW / gridX;
        const segmentHeight = planeH / gridY;
        const widthHalf = planeW / 2, heightHalf = planeH / 2, depthHalf = planeD / 2;
        const gridX1 = gridX + 1, gridY1 = gridY + 1;
        let vertexCounter = 0;
        const vec = [0, 0, 0];

        for (let iy = 0; iy < gridY1; ++iy) {
            const y = iy * segmentHeight - heightHalf;
            for (let ix = 0; ix < gridX1; ++ix) {
                const x = ix * segmentWidth - widthHalf;

                vec[u] = x * udir; vec[v] = y * vdir; vec[w] = depthHalf;
                r.positions.push(vec[0], vec[1], vec[2]);

                vec[u] = 0; vec[v] = 0; vec[w] = planeD > 0 ? 1 : -1;
                r.normals.push(vec[0], vec[1], vec[2]);

                r.uvs.push(ix / gridX, 1 - (iy / gridY));
                vertexCounter += 1;
            }
        }

        for (let iy = 0; iy < gridY; ++iy)
            for (let ix = 0; ix < gridX; ++ix) {
                const a = numberOfVertices + ix + gridX1 * iy;
                const b = numberOfVertices + ix + gridX1 * (iy + 1);
                const c = numberOfVertices + (ix + 1) + gridX1 * (iy + 1);
                const d = numberOfVertices + (ix + 1) + gridX1 * iy;
                r.indices.push(a, b, d, b, c, d);
            }

        numberOfVertices += vertexCounter;
    };

    buildPlane(Z, Y, X, -1, -1, depth, height,  width,  depthSegments, heightSegments); // px
    buildPlane(Z, Y, X,  1, -1, depth, height, -width,  depthSegments, heightSegments); // nx
    buildPlane(X, Z, Y,  1,  1, width, depth,   height, widthSegments, depthSegments);  // py
    buildPlane(X, Z, Y,  1, -1, width, depth,  -height, widthSegments, depthSegments);  // ny
    buildPlane(X, Y, Z,  1, -1, width, height,  depth,  widthSegments, heightSegments); // pz
    buildPlane(X, Y, Z, -1, -1, width, height, -depth,  widthSegments, heightSegments); // nz

    return r;
}

// ---------------------------------------------------------------- plane

export function planeGeometry(width = 1, height = 1, widthSegments = 1, heightSegments = 1): Raw {
    const r = raw();
    const widthHalf = width / 2, heightHalf = height / 2;
    const gridX = Math.floor(widthSegments), gridY = Math.floor(heightSegments);
    const gridX1 = gridX + 1, gridY1 = gridY + 1;
    const segmentWidth = width / gridX, segmentHeight = height / gridY;

    for (let iy = 0; iy < gridY1; ++iy) {
        const y = iy * segmentHeight - heightHalf;
        for (let ix = 0; ix < gridX1; ++ix) {
            const x = ix * segmentWidth - widthHalf;
            r.positions.push(x, -y, 0);
            r.normals.push(0, 0, 1);
            r.uvs.push(ix / gridX, 1 - (iy / gridY));
        }
    }

    for (let iy = 0; iy < gridY; ++iy)
        for (let ix = 0; ix < gridX; ++ix) {
            const a = ix + gridX1 * iy;
            const b = ix + gridX1 * (iy + 1);
            const c = (ix + 1) + gridX1 * (iy + 1);
            const d = (ix + 1) + gridX1 * iy;
            r.indices.push(a, b, d, b, c, d);
        }

    return r;
}

// ---------------------------------------------------------------- circle

export function circleGeometry(radius = 1, segments = 32,
                               thetaStart = 0, thetaLength = Math.PI * 2): Raw {
    const r = raw();
    segments = Math.max(3, segments);

    //Centre vertex first; the fan indices below are relative to it.
    r.positions.push(0, 0, 0);
    r.normals.push(0, 0, 1);
    r.uvs.push(0.5, 0.5);

    for (let s = 0, i = 3; s <= segments; ++s, i += 3) {
        const segment = thetaStart + s / segments * thetaLength;
        //z is never assigned in three either - the Vector3 stays at its constructed 0.
        r.positions.push(radius * Math.cos(segment), radius * Math.sin(segment), 0);
        r.normals.push(0, 0, 1);
        //Read back through the position array, as three does, not from the local.
        r.uvs.push((r.positions[i] / radius + 1) / 2, (r.positions[i + 1] / radius + 1) / 2);
    }

    for (let i = 1; i <= segments; ++i) r.indices.push(i, i + 1, 0);

    return r;
}

// ---------------------------------------------------------------- cylinder

export function cylinderGeometry(radiusTop = 1, radiusBottom = 1, height = 1,
                                 radialSegments = 32, heightSegments = 1, openEnded = false,
                                 thetaStart = 0, thetaLength = Math.PI * 2): Raw {
    const r = raw();
    radialSegments = Math.floor(radialSegments);
    heightSegments = Math.floor(heightSegments);

    let index = 0;
    const indexArray: number[][] = [];
    const halfHeight = height / 2;

    //torso
    const slope = (radiusBottom - radiusTop) / height;
    for (let y = 0; y <= heightSegments; ++y) {
        const indexRow: number[] = [];
        const v = y / heightSegments;
        const radius = v * (radiusBottom - radiusTop) + radiusTop;

        for (let x = 0; x <= radialSegments; ++x) {
            const u = x / radialSegments;
            const theta = u * thetaLength + thetaStart;
            const sinTheta = Math.sin(theta), cosTheta = Math.cos(theta);

            r.positions.push(radius * sinTheta, -v * height + halfHeight, radius * cosTheta);

            //three normalizes through Vector3.set().normalize(), i.e. in f64 before storing.
            const nx = sinTheta, ny = slope, nz = cosTheta;
            const len = Math.sqrt(nx * nx + ny * ny + nz * nz) || 1;
            r.normals.push(nx / len, ny / len, nz / len);

            r.uvs.push(u, 1 - v);
            indexRow.push(index++);
        }
        indexArray.push(indexRow);
    }

    for (let x = 0; x < radialSegments; ++x)
        for (let y = 0; y < heightSegments; ++y) {
            const a = indexArray[y][x];
            const b = indexArray[y + 1][x];
            const c = indexArray[y + 1][x + 1];
            const d = indexArray[y][x + 1];
            r.indices.push(a, b, d, b, c, d);
        }

    const generateCap = (top: boolean) => {
        const centerIndexStart = index;
        const radius = top ? radiusTop : radiusBottom;
        const sign = top ? 1 : -1;

        //One centre vertex per segment: the cap needs one uv set per face.
        for (let x = 1; x <= radialSegments; ++x) {
            r.positions.push(0, halfHeight * sign, 0);
            r.normals.push(0, sign, 0);
            r.uvs.push(0.5, 0.5);
            index++;
        }
        const centerIndexEnd = index;

        for (let x = 0; x <= radialSegments; ++x) {
            const u = x / radialSegments;
            const theta = u * thetaLength + thetaStart;
            const cosTheta = Math.cos(theta), sinTheta = Math.sin(theta);
            r.positions.push(radius * sinTheta, halfHeight * sign, radius * cosTheta);
            r.normals.push(0, sign, 0);
            r.uvs.push(cosTheta * 0.5 + 0.5, sinTheta * 0.5 * sign + 0.5);
            index++;
        }

        for (let x = 0; x < radialSegments; ++x) {
            const c = centerIndexStart + x;
            const i = centerIndexEnd + x;
            if (top) r.indices.push(i, i + 1, c);
            else r.indices.push(i + 1, i, c);
        }
    };

    if (!openEnded) {
        if (radiusTop > 0) generateCap(true);
        if (radiusBottom > 0) generateCap(false);
    }

    return r;
}

// ---------------------------------------------------------------- sphere

export function sphereGeometry(radius = 1, widthSegments = 32, heightSegments = 16,
                               phiStart = 0, phiLength = Math.PI * 2,
                               thetaStart = 0, thetaLength = Math.PI): Raw {
    const r = raw();
    widthSegments = Math.max(3, Math.floor(widthSegments));
    heightSegments = Math.max(2, Math.floor(heightSegments));
    const thetaEnd = Math.min(thetaStart + thetaLength, Math.PI);

    let index = 0;
    const grid: number[][] = [];

    for (let iy = 0; iy <= heightSegments; ++iy) {
        const verticesRow: number[] = [];
        const v = iy / heightSegments;

        //The poles get a half-texel u shift so the degenerate triangle fan is not skewed.
        let uOffset = 0;
        if (iy === 0 && thetaStart === 0) uOffset = 0.5 / widthSegments;
        else if (iy === heightSegments && thetaEnd === Math.PI) uOffset = -0.5 / widthSegments;

        for (let ix = 0; ix <= widthSegments; ++ix) {
            const u = ix / widthSegments;
            const px = -radius * Math.cos(phiStart + u * phiLength) * Math.sin(thetaStart + v * thetaLength);
            const py = radius * Math.cos(thetaStart + v * thetaLength);
            const pz = radius * Math.sin(phiStart + u * phiLength) * Math.sin(thetaStart + v * thetaLength);
            r.positions.push(px, py, pz);

            const len = Math.sqrt(px * px + py * py + pz * pz) || 1;
            r.normals.push(px / len, py / len, pz / len);

            r.uvs.push(u + uOffset, 1 - v);
            verticesRow.push(index++);
        }
        grid.push(verticesRow);
    }

    for (let iy = 0; iy < heightSegments; ++iy)
        for (let ix = 0; ix < widthSegments; ++ix) {
            const a = grid[iy][ix + 1];
            const b = grid[iy][ix];
            const c = grid[iy + 1][ix];
            const d = grid[iy + 1][ix + 1];
            //The pole rows collapse to a single vertex, so one of the two triangles is degenerate.
            if (iy !== 0 || thetaStart > 0) r.indices.push(a, b, d);
            if (iy !== heightSegments - 1 || thetaEnd < Math.PI) r.indices.push(b, c, d);
        }

    return r;
}

// ---------------------------------------------------------------- dodecahedron

//Vertices of a regular dodecahedron, phi-scaled. three feeds these through PolyhedronGeometry,
//which produces a non-indexed triangle soup with flat normals at detail 0.
const DODECAHEDRON_T = (1 + Math.sqrt(5)) / 2;
const DODECAHEDRON_R = 1 / DODECAHEDRON_T;

const DODECAHEDRON_VERTICES = [
    -1, -1, -1,  -1, -1, 1,
    -1, 1, -1,   -1, 1, 1,
    1, -1, -1,   1, -1, 1,
    1, 1, -1,    1, 1, 1,

    0, -DODECAHEDRON_R, -DODECAHEDRON_T,   0, -DODECAHEDRON_R, DODECAHEDRON_T,
    0, DODECAHEDRON_R, -DODECAHEDRON_T,    0, DODECAHEDRON_R, DODECAHEDRON_T,

    -DODECAHEDRON_R, -DODECAHEDRON_T, 0,   -DODECAHEDRON_R, DODECAHEDRON_T, 0,
    DODECAHEDRON_R, -DODECAHEDRON_T, 0,    DODECAHEDRON_R, DODECAHEDRON_T, 0,

    -DODECAHEDRON_T, 0, -DODECAHEDRON_R,   DODECAHEDRON_T, 0, -DODECAHEDRON_R,
    -DODECAHEDRON_T, 0, DODECAHEDRON_R,    DODECAHEDRON_T, 0, DODECAHEDRON_R,
];

const DODECAHEDRON_FACES = [
    3, 11, 7,   3, 7, 15,   3, 15, 13,
    7, 19, 17,  7, 17, 6,   7, 6, 15,
    17, 4, 8,   17, 8, 10,  17, 10, 6,
    8, 0, 16,   8, 16, 2,   8, 2, 10,
    0, 12, 1,   0, 1, 18,   0, 18, 16,
    6, 10, 2,   6, 2, 13,   6, 13, 15,
    2, 16, 18,  2, 18, 3,   2, 3, 13,
    18, 1, 9,   18, 9, 11,  18, 11, 3,
    4, 14, 12,  4, 12, 0,   4, 0, 8,
    11, 9, 5,   11, 5, 19,  11, 19, 7,
    19, 5, 14,  19, 14, 4,  19, 4, 17,
    1, 12, 14,  1, 14, 5,   1, 5, 9,
];

const azimuth = (x: number, z: number) => Math.atan2(z, -x);
const inclination = (x: number, y: number, z: number) => Math.atan2(-y, Math.sqrt(x * x + z * z));

/**
 * PolyhedronGeometry at detail 0: the face list is expanded to a triangle soup, projected onto
 * the sphere of the given radius, then given spherical uvs with the seam and pole corrections.
 * Returns a MeshData directly rather than a Raw, because the flat normals must be computed
 * from the f32 positions - the same rounding three sees.
 */
export function dodecahedronGeometry(radius = 1, name = 'Dodecahedron'): MeshData {
    const vertexBuffer: number[] = [];
    const V = DODECAHEDRON_VERTICES;

    //subdivideFace at detail 0. Two details of it are load-bearing and neither is obvious:
    //the emitted winding is (b, c, a) rather than (a, b, c), and the two non-`a` corners come
    //out of Vector3.lerp with alpha 1, i.e. `p + (q - p)`, which is not bit-identical to q.
    const lerp1 = (pi: number, qi: number, c: number) => V[pi * 3 + c] + (V[qi * 3 + c] - V[pi * 3 + c]);

    for (let f = 0; f < DODECAHEDRON_FACES.length; f += 3) {
        const a = DODECAHEDRON_FACES[f], b = DODECAHEDRON_FACES[f + 1], c = DODECAHEDRON_FACES[f + 2];
        vertexBuffer.push(lerp1(a, b, 0), lerp1(a, b, 1), lerp1(a, b, 2));   // v[0][1]
        vertexBuffer.push(lerp1(a, c, 0), lerp1(a, c, 1), lerp1(a, c, 2));   // v[1][0]
        vertexBuffer.push(V[a * 3], V[a * 3 + 1], V[a * 3 + 2]);             // v[0][0]
    }

    for (let i = 0; i < vertexBuffer.length; i += 3) {
        const x = vertexBuffer[i], y = vertexBuffer[i + 1], z = vertexBuffer[i + 2];
        const len = Math.sqrt(x * x + y * y + z * z) || 1;
        const s = radius / len;
        vertexBuffer[i] = x * s; vertexBuffer[i + 1] = y * s; vertexBuffer[i + 2] = z * s;
    }

    const uvBuffer: number[] = [];
    for (let i = 0; i < vertexBuffer.length; i += 3) {
        const x = vertexBuffer[i], y = vertexBuffer[i + 1], z = vertexBuffer[i + 2];
        uvBuffer.push(azimuth(x, z) / 2 / Math.PI + 0.5, 1 - (inclination(x, y, z) / Math.PI + 0.5));
    }

    //correctUVs: a vertex sitting exactly on the pole has no defined azimuth of its own, so it
    //borrows the face centroid's.
    for (let i = 0, j = 0; i < vertexBuffer.length; i += 9, j += 6) {
        const cx = (vertexBuffer[i] + vertexBuffer[i + 3] + vertexBuffer[i + 6]) / 3;
        const cz = (vertexBuffer[i + 2] + vertexBuffer[i + 5] + vertexBuffer[i + 8]) / 3;
        const azi = azimuth(cx, cz);

        for (let k = 0; k < 3; ++k) {
            const vs = i + k * 3, us = j + k * 2;
            if (azi < 0 && uvBuffer[us] === 1) uvBuffer[us] = uvBuffer[us] - 1;
            if (vertexBuffer[vs] === 0 && vertexBuffer[vs + 2] === 0) uvBuffer[us] = azi / 2 / Math.PI + 0.5;
        }
    }

    //correctSeam: a face straddling u=0/u=1 would otherwise sample the whole texture backwards.
    for (let i = 0; i < uvBuffer.length; i += 6) {
        const x0 = uvBuffer[i], x1 = uvBuffer[i + 2], x2 = uvBuffer[i + 4];
        if (Math.max(x0, x1, x2) > 0.9 && Math.min(x0, x1, x2) < 0.1) {
            if (x0 < 0.2) uvBuffer[i] += 1;
            if (x1 < 0.2) uvBuffer[i + 2] += 1;
            if (x2 < 0.2) uvBuffer[i + 4] += 1;
        }
    }

    const positions = new Float32Array(vertexBuffer);
    const vertices = positions.length / 3;
    const indices = new Uint32Array(vertices);
    for (let i = 0; i < vertices; ++i) indices[i] = i;

    return { positions, normals: flatNormals(positions), uvs: new Float32Array(uvBuffer), indices, name };
}

/**
 * three's non-indexed computeVertexNormals: one face normal written to all three of its
 * vertices, stored unnormalized and only then normalized - so the f32 truncation of the raw
 * cross product is part of the result. Note the cross is (C-B) x (A-B), which is
 * mathematically the same as (B-A) x (C-A) but not bit-for-bit, so the order is kept.
 */
function flatNormals(positions: Float32Array): Float32Array {
    const normals = new Float32Array(positions.length);

    for (let i = 0; i < positions.length; i += 9) {
        const ax = positions[i], ay = positions[i + 1], az = positions[i + 2];
        const bx = positions[i + 3], by = positions[i + 4], bz = positions[i + 5];
        const cx = positions[i + 6], cy = positions[i + 7], cz = positions[i + 8];

        const cbx = cx - bx, cby = cy - by, cbz = cz - bz;
        const abx = ax - bx, aby = ay - by, abz = az - bz;

        const nx = cby * abz - cbz * aby;
        const ny = cbz * abx - cbx * abz;
        const nz = cbx * aby - cby * abx;

        for (let k = 0; k < 9; k += 3) {
            normals[i + k] = nx; normals[i + k + 1] = ny; normals[i + k + 2] = nz;
        }
    }

    normalizeInPlace(normals);
    return normals;
}

// ---------------------------------------------------------------- transforms

/**
 * BufferGeometry.applyMatrix4, transcribed. `m` is column-major, as three's Matrix4.elements is.
 *
 * Takes a finished MeshData, not a Raw, and mutates it in place. That ordering is not
 * cosmetic: three's `new CircleGeometry(...).rotateX(...)` truncates the generated positions
 * to f32 in the constructor's setAttribute and only then transforms them, so transforming the
 * f64 accumulator instead produces a different f32 result. It is a one-ulp difference, and it
 * was caught only by demanding exact equality against three.
 *
 * The normal path matters for the same reason: three does not assume the matrix is rigid, it
 * derives the normal matrix by inverting and transposing the upper 3x3 and then re-normalizes
 * every normal. Both steps are reproduced, invert included, so that the rounding matches.
 */
export function applyMatrix4(r: MeshData, m: number[]): MeshData {
    const p = r.positions;
    for (let i = 0; i < p.length; i += 3) {
        const x = p[i], y = p[i + 1], z = p[i + 2];
        const w = 1 / (m[3] * x + m[7] * y + m[11] * z + m[15]);
        p[i] = (m[0] * x + m[4] * y + m[8] * z + m[12]) * w;
        p[i + 1] = (m[1] * x + m[5] * y + m[9] * z + m[13]) * w;
        p[i + 2] = (m[2] * x + m[6] * y + m[10] * z + m[14]) * w;
    }

    const n = normalMatrix(m);
    const nrm = r.normals;
    for (let i = 0; i < nrm.length; i += 3) {
        const x = nrm[i], y = nrm[i + 1], z = nrm[i + 2];
        const rx = n[0] * x + n[3] * y + n[6] * z;
        const ry = n[1] * x + n[4] * y + n[7] * z;
        const rz = n[2] * x + n[5] * y + n[8] * z;
        const len = Math.sqrt(rx * rx + ry * ry + rz * rz) || 1;
        nrm[i] = rx / len; nrm[i + 1] = ry / len; nrm[i + 2] = rz / len;
    }

    return r;
}

/** Matrix3.getNormalMatrix: setFromMatrix4, invert, transpose. Column-major throughout. */
function normalMatrix(m: number[]): number[] {
    //setFromMatrix4 takes the upper-left 3x3 and stores it column-major.
    const n11 = m[0], n21 = m[1], n31 = m[2];
    const n12 = m[4], n22 = m[5], n32 = m[6];
    const n13 = m[8], n23 = m[9], n33 = m[10];

    const t11 = n33 * n22 - n32 * n23;
    const t12 = n32 * n13 - n33 * n12;
    const t13 = n23 * n12 - n22 * n13;
    const det = n11 * t11 + n21 * t12 + n31 * t13;
    if (det === 0) return [0, 0, 0, 0, 0, 0, 0, 0, 0];
    const d = 1 / det;

    const inv = [
        t11 * d, (n31 * n23 - n33 * n21) * d, (n32 * n21 - n31 * n22) * d,
        t12 * d, (n33 * n11 - n31 * n13) * d, (n31 * n12 - n32 * n11) * d,
        t13 * d, (n21 * n13 - n23 * n11) * d, (n22 * n11 - n21 * n12) * d,
    ];

    //transpose
    return [inv[0], inv[3], inv[6], inv[1], inv[4], inv[7], inv[2], inv[5], inv[8]];
}

export function translation(x: number, y: number, z: number): number[] {
    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
}

/** Matrix4.makeRotationX. cos(-PI/2) is 6.1e-17, not 0 - see the note at the top of the file. */
export function rotationX(theta: number): number[] {
    const c = Math.cos(theta), s = Math.sin(theta);
    return [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1];
}

// ---------------------------------------------------------------- the table

function build(): MeshData[] {
    const t: MeshData[] = new Array(STATIC_GEOMETRY_COUNT);

    t[Geom.BoxCentered]       = finish(boxGeometry(), 'BoxCentered');
    t[Geom.BoxCorner]         = applyMatrix4(finish(boxGeometry(), 'BoxCorner'), translation(0.5, 0.5, 0.5));
    t[Geom.BoxBottomCentered] = applyMatrix4(finish(boxGeometry(), 'BoxBottomCentered'), translation(0, 0.5, 0));
    t[Geom.Cylinder]          = finish(cylinderGeometry(1, 1, 1.0, 8), 'Cylinder');
    t[Geom.Sphere]            = finish(sphereGeometry(1, 8, 8), 'Sphere');
    t[Geom.Plane]             = finish(planeGeometry(2, 2), 'Plane');
    t[Geom.CircleHalf]        = applyMatrix4(finish(circleGeometry(0.5, 12), 'CircleHalf'), rotationX(-Math.PI * 0.5));
    t[Geom.CircleUnit]        = applyMatrix4(finish(circleGeometry(), 'CircleUnit'), rotationX(-Math.PI * 0.5));
    t[Geom.Dodecahedron]      = dodecahedronGeometry(0.03);
    t[Geom.DebugBox]          = debugBoxMesh;

    return t;
}

/** Indexed by Geom. Built once at module load. */
export const staticGeometries: MeshData[] = build();

staticGeometries.forEach((m, id) => setStaticGeometry(id, m));

export function staticGeometry(id: Geom): MeshData {
    const m = staticGeometries[id];
    if (!m) throw new Error(`No static geometry registered for id ${id}`);
    return m;
}
