import { MeshData } from './Geometry';

type IndexGroup = { name: string; indexFirst: number; indexCount: number };

/** Cover depth rounding and subpixel rasterization differences between coplanar triangles. */
export const OBSTACLE_MAPPING_DEPTH_UNITS = 4;
//Reversed float depth exposes interpolation roundoff that the coarser unorm buffer rounds
//away. 32 float ulps remain a few parts per million of distance, unlike 32 unorm depth units.
export const OBSTACLE_MAPPING_FLOAT_DEPTH_UNITS = 32;
//A quarter pixel of depth slope also covers the facade's very narrow rasterized triangles.
export const OBSTACLE_MAPPING_DEPTH_SLOPE = 1 / 4;

export type ObstacleMappingGeometry = {
    mesh: MeshData;
    /** Unmeasured context first, measured faces second, each drawn exactly once. */
    contextCount: number;
    version: number;
};

type Cached = ObstacleMappingGeometry & {
    groups: IndexGroup[];
    values: Map<string, number>;
    measured: boolean[];
};
const cache = new WeakMap<MeshData, Cached>();

/**
 * Split the draw into context and measured surfaces. In imported facades a tray's back and the
 * facade panel can occupy the same plane with different triangulations. Giving BOTH the same
 * polygon offset leaves that depth tie intact. Only the measured range gets a depth preference.
 *
 * The source topology and all vertex arrays stay untouched. One extra index array is reused;
 * changing values, hovering or moving the camera does not repartition it. Both backends use the
 * same ranges, with no duplicate vertex allocation or draw call per named object.
 */
export function obstacleMappingGeometry(
    source: MeshData, groups: IndexGroup[], values: Map<string, number>): ObstacleMappingGeometry
{
    let result = cache.get(source);
    if (result?.groups === groups && result.values === values) return result;
    const measured = groups.map(g => values.get(g.name) !== undefined);
    if (result?.groups === groups && measured.every((v, i) => v === result!.measured[i])) {
        result.values = values;
        return result;
    }

    const selected = new Uint8Array(source.indices.length / 3);
    groups.forEach((g, i) => {
        if (measured[i]) selected.fill(1, Math.max(0, g.indexFirst / 3), (g.indexFirst + g.indexCount) / 3);
    });
    let contextCount = 0;
    for (const v of selected) if (!v) contextCount += 3;
    if (!result) {
        result = {
            mesh: { ...source, indices: new Uint32Array(source.indices.length) },
            contextCount, version: 0, groups, values, measured,
        };
        cache.set(source, result);
    }
    let contextAt = 0, measuredAt = contextCount;
    for (let t = 0; t < selected.length; ++t) {
        const at = selected[t] ? measuredAt : contextAt;
        for (let k = 0; k < 3; ++k) result.mesh.indices[at + k] = source.indices[t * 3 + k];
        if (selected[t]) measuredAt += 3; else contextAt += 3;
    }
    Object.assign(result, { contextCount, version: result.version + 1, groups, values, measured });
    return result;
}
