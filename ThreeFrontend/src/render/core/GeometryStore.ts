//The one table mapping geometry id -> MeshData. Renderer-neutral; no three.
//
//Static slots (Geom.*) are populated at startup by core/StaticGeometry, which generates them
//by hand. Both renderers import that module for its side effect, so neither depends on the
//other having been loaded to resolve an id.

import { MeshData } from './Geometry';
import { Geom, GeometryRegistry, STATIC_GEOMETRY_COUNT } from './GeometryIds';

const staticTable: (MeshData | undefined)[] = new Array(STATIC_GEOMETRY_COUNT);
const dynamic = new GeometryRegistry<MeshData>();

export function setStaticGeometry(id: Geom, mesh: MeshData) {
    staticTable[id] = mesh;
}

/**
 * Registers a runtime geometry and returns its id. Re-registering the same key (a species'
 * leaf mesh reloaded, a terrain re-imported) reuses the id rather than leaking a new one,
 * which matters because the WebGPU atlas never reclaims space.
 */
export function registerGeometry(key: string, mesh: MeshData): number {
    return dynamic.register(key, mesh);
}

export function getGeometry(id: number): MeshData | undefined {
    return id < STATIC_GEOMETRY_COUNT ? staticTable[id] : dynamic.get(id);
}

/**
 * The id a runtime geometry was registered under, or undefined if it never was.
 *
 * Read-only on purpose: callers that only want to know what the renderer is drawing - the HUD's
 * triangle counter, for one - must not cause a registration as a side effect of asking.
 */
export function geometryIdFor(key: string): number | undefined {
    return dynamic.idFor(key);
}

export function hasGeometry(id: number): boolean {
    return getGeometry(id) !== undefined;
}
