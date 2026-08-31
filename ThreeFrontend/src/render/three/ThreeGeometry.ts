//Bridge between renderer-neutral MeshData and THREE.BufferGeometry.
//This file is part of the Three adapter, so importing three here is expected.

import * as THREE from 'three';
import { MeshData, computeVertexNormals } from '../core/Geometry';

const cache = new WeakMap<MeshData, THREE.BufferGeometry>();

/**
 * MeshData -> BufferGeometry, cached by MeshData identity so repeated lookups during
 * buildPlants() do not reallocate. MeshData is treated as immutable; mutate one and you
 * must drop it and register a replacement.
 */
export function toBufferGeometry(m: MeshData): THREE.BufferGeometry {
    const hit = cache.get(m);
    if (hit) return hit;

    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(m.positions, 3));
    g.setAttribute('normal', new THREE.BufferAttribute(m.normals, 3));
    if (m.uvs) g.setAttribute('uv', new THREE.BufferAttribute(m.uvs, 2));
    if (m.colors) g.setAttribute('color', new THREE.BufferAttribute(m.colors, 3));
    g.setIndex(new THREE.BufferAttribute(m.indices, 1));
    g.computeBoundingBox();
    g.computeBoundingSphere();
    g.name = m.name;

    cache.set(m, g);
    return g;
}

/**
 * BufferGeometry -> MeshData.
 *
 * Non-indexed geometry is given a trivial 0..n-1 index buffer rather than being left
 * unindexed: the WebGPU atlas draws everything with drawIndexed from one shared index
 * buffer, so a special non-indexed path would exist purely for DodecahedronGeometry.
 * Normals are generated only when genuinely absent - three's own normals are
 * area-weighted, and recomputing them would silently change flat-shaded geometry.
 */
export function fromBufferGeometry(g: THREE.BufferGeometry, name: string): MeshData {
    const position = g.getAttribute('position');
    if (!position) throw new Error(`Geometry "${name}" has no position attribute`);

    const positions = toFloat32(position.array, position.itemSize, 3);
    const vertices = positions.length / 3;

    const indexAttr = g.getIndex();
    const indices = indexAttr
        ? Uint32Array.from(indexAttr.array as ArrayLike<number>)
        : sequentialIndices(vertices);

    const normalAttr = g.getAttribute('normal');
    const normals = normalAttr
        ? toFloat32(normalAttr.array, normalAttr.itemSize, 3)
        : computeVertexNormals(positions, indices);

    const uvAttr = g.getAttribute('uv');
    const colorAttr = g.getAttribute('color');

    return {
        positions,
        normals,
        uvs: uvAttr ? toFloat32(uvAttr.array, uvAttr.itemSize, 2) : undefined,
        colors: colorAttr ? toFloat32(colorAttr.array, colorAttr.itemSize, 3) : undefined,
        indices,
        name,
    };
}

function sequentialIndices(count: number): Uint32Array {
    const out = new Uint32Array(count);
    for (let i = 0; i < count; ++i) out[i] = i;
    return out;
}

function toFloat32(src: ArrayLike<number>, itemSize: number, expected: number): Float32Array {
    if (itemSize !== expected)
        throw new Error(`Expected itemSize ${expected}, got ${itemSize}`);
    //Copy rather than alias: the source may be a view into a pooled buffer, and MeshData
    //outlives the BufferGeometry it was derived from.
    return Float32Array.from(src as ArrayLike<number>);
}
