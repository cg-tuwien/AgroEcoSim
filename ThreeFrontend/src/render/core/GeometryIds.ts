//Stable identifiers for every geometry the renderers can draw.
//The WebGPU path uses these to index into its geometry atlas; the Three path uses them
//to look up a cached BufferGeometry. Nothing here may import three.

export const enum Geom {
    /** BoxGeometry() - origin-centred. Roots. */
    BoxCentered = 0,
    /** BoxGeometry().translate(0.5, 0.5, 0.5) - corner at the origin. Terrain boxes. */
    BoxCorner = 1,
    /** BoxGeometry().translate(0, 0.5, 0) - bottom face on the origin. Obstacle walls. */
    BoxBottomCentered = 2,
    /** CylinderGeometry(1, 1, 1, 8). Stems, petioles, meristems, flower parts. */
    Cylinder = 3,
    /** SphereGeometry(1, 8, 8). Buds - disabled throughout, kept so the ids stay stable. */
    Sphere = 4,
    /** PlaneGeometry(2, 2). Leaf fallback when the backend served no per-species mesh. */
    Plane = 5,
    /** CircleGeometry(0.5, 12).rotateX(-PI/2). Primitives.Disk, legacy V3 obstacles. */
    CircleHalf = 6,
    /** CircleGeometry().rotateX(-PI/2) - unit radius. Obstacle umbrellas. */
    CircleUnit = 7,
    /** DodecahedronGeometry(0.03). Seed markers. */
    Dodecahedron = 8,
    /** Hand-built vertex-coloured XYZ box, 2x2x2. The debugBoxes orientation overlay. */
    DebugBox = 9,

    /** Runtime-registered geometries start here: per-species leaf meshes, terrain meshes, mesh obstacles. */
    FirstDynamic = 10,
}

export const STATIC_GEOMETRY_COUNT = Geom.FirstDynamic;

/**
 * Registry key for one slice of a species' leaf mesh.
 *
 * A blade simulated as a chain of leaf organs is drawn a slice at a time, so the species name
 * alone no longer identifies a geometry. Slice 0 is the whole leaf for a species whose blades
 * are not segmented. One definition because the renderer registers under this key and the HUD's
 * triangle counter looks it up; a divergence would silently count the fallback plane instead.
 */
export function leafGeomKey(species: string, slice: number): string {
    return `leaf:${species}:${slice}`;
}

/**
 * Allocates ids for geometry that only exists at runtime.
 *
 * Registration is keyed so that re-registering the same logical geometry (a species' leaf
 * mesh reloaded, a terrain re-imported) reuses its id instead of leaking a new one on every
 * refresh - the WebGPU atlas never reclaims space, so leaking here would grow unboundedly
 * across a long session.
 */
export class GeometryRegistry<T> {
    private byKey = new Map<string, number>();
    private items: (T | undefined)[] = [];

    /** Returns the id for `key`, creating it on first use. */
    register(key: string, value: T): number {
        const existing = this.byKey.get(key);
        if (existing !== undefined) {
            this.items[existing - Geom.FirstDynamic] = value;
            return existing;
        }

        const id = Geom.FirstDynamic + this.items.length;
        this.items.push(value);
        this.byKey.set(key, id);
        return id;
    }

    get(id: number): T | undefined {
        return id >= Geom.FirstDynamic ? this.items[id - Geom.FirstDynamic] : undefined;
    }

    idFor(key: string): number | undefined {
        return this.byKey.get(key);
    }

    get count() {
        return this.items.length;
    }
}
