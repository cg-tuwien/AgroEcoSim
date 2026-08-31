//The three-derived static geometry, kept ONLY as the oracle for tests/staticGeometry.test.ts.
//
//This used to bootstrap the app's geometry table, because every one of these must match
//three's vertex layout exactly or shading diverges between the two backends, and some are
//easy to get subtly wrong - DodecahedronGeometry in particular is non-indexed via
//PolyhedronGeometry with flat-face normals at detail 0, emitted in (b, c, a) winding.
//
//core/StaticGeometry.ts now generates all ten by hand and is what the app loads. This module
//stays so the two can be compared element-wise: it is the only way to keep the hand-written
//versions honest. It deliberately does NOT publish into GeometryStore any more - an oracle
//that mutates the thing it is checking is no oracle.

import * as THREE from 'three';
import { MeshData } from '../core/Geometry';
import { Geom, STATIC_GEOMETRY_COUNT } from '../core/GeometryIds';
import { debugBoxMesh } from '../core/DebugBox';
import { fromBufferGeometry } from './ThreeGeometry';

function build(): MeshData[] {
    const table: MeshData[] = new Array(STATIC_GEOMETRY_COUNT);

    //Each entry mirrors exactly one of the prototypes that used to live at the bottom of
    //ThreeSceneFn.tsx, plus the two that were private to Obstacle.ts and Seed.ts.
    table[Geom.BoxCentered]       = fromBufferGeometry(new THREE.BoxGeometry(), 'BoxCentered');
    table[Geom.BoxCorner]         = fromBufferGeometry(new THREE.BoxGeometry().translate(0.5, 0.5, 0.5), 'BoxCorner');
    table[Geom.BoxBottomCentered] = fromBufferGeometry(new THREE.BoxGeometry().translate(0, 0.5, 0), 'BoxBottomCentered');
    table[Geom.Cylinder]          = fromBufferGeometry(new THREE.CylinderGeometry(1, 1, 1.0, 8), 'Cylinder');
    table[Geom.Sphere]            = fromBufferGeometry(new THREE.SphereGeometry(1, 8, 8), 'Sphere');
    table[Geom.Plane]             = fromBufferGeometry(new THREE.PlaneGeometry(2, 2), 'Plane');
    table[Geom.CircleHalf]        = fromBufferGeometry(new THREE.CircleGeometry(0.5, 12).rotateX(-Math.PI * 0.5), 'CircleHalf');
    table[Geom.CircleUnit]        = fromBufferGeometry(new THREE.CircleGeometry().rotateX(-Math.PI * 0.5), 'CircleUnit');
    table[Geom.Dodecahedron]      = fromBufferGeometry(new THREE.DodecahedronGeometry(0.03), 'Dodecahedron');
    table[Geom.DebugBox]          = debugBoxMesh;

    return table;
}

/** Indexed by Geom. Built once at module load. */
export const staticGeometries: MeshData[] = build();

export function staticGeometry(id: Geom): MeshData {
    const m = staticGeometries[id];
    if (!m) throw new Error(`No static geometry registered for id ${id}`);
    return m;
}
