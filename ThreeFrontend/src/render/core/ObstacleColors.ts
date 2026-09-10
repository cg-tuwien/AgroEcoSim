//Per-vertex colours for obstacles coloured by a measured value. Renderer-neutral; no three.

import { MeshData } from './Geometry';
import { RGB } from './Color';
import { HeatColor } from './Ramps';

/** What an unmapped obstacle reads as: neutral grey, so it stays visible as context. */
export const unmappedObstacleColor: RGB = { r: 0.06, g: 0.06, b: 0.07 };

/** Anything the measurement did not cover - a name with no reading - reads as this. */
export const unmeasuredObstacleColor: RGB = { r: 0.02, g: 0.02, b: 0.024 };

/** A value's place on the obstacle mapping ramp, as linear RGB. */
export function obstacleColor(value: number, min: number, max: number): RGB {
    const span = max - min;
    return HeatColor(span > 1e-9 ? (value - min) / span : 0);
}

/** One named object inside a merged obstacle mesh: which vertices it owns. */
export type ColorGroup = { name: string; vertexFirst: number; vertexCount: number };

/**
 * `unpaintable` - the mesh carries no colour array, so it never came from an import and the
 * caller should keep using its palette colour. `unchanged` - already painted with exactly these
 * values, so the caller must NOT re-upload. `repainted` - the colours moved and the buffer has to
 * go to the GPU.
 */
export type ObstaclePaintResult = 'unpaintable' | 'unchanged' | 'repainted';

/**
 * What each mesh was last painted with.
 *
 * The guard this enables is not an optimisation, it is the difference between usable and not.
 * Picking runs on every mouse move and flips an obstacle's hover state, which the renderer host
 * digests into the obstacle frame revision - so orbiting the camera re-runs the obstacle rebuild
 * once per mouse-move event. On a million-vertex imported facade an unguarded repaint rewrote 2.9M
 * floats twice and had the WebGPU atlas re-upload its whole 42 MB vertex buffer, every event,
 * which is exactly as bad as it sounds. Nothing about a hover changes a measured value, so the
 * work was pure waste.
 *
 * Keyed by MeshData and holding the values map by identity, which is sound because it comes from a
 * computed signal: a new map means the mapping, the data or the shown day actually changed.
 */
const lastPaint = new WeakMap<MeshData, { values: Map<string, number> | undefined; min: number; max: number }>();

/**
 * Paints each named object of a merged mesh with the colour of its measured value.
 *
 * Written in place into the mesh's existing colour array rather than by registering a new
 * geometry: an imported facade is a million vertices, the WebGPU atlas never reclaims space, and
 * the mapping changes as playback moves through the season. Both backends can refresh a buffer
 * whose length did not change, so this stays one allocation for the life of the mesh.
 */
export function writeObstacleColors(
    mesh: MeshData | undefined,
    groups: ColorGroup[] | undefined,
    values: Map<string, number> | undefined,
    min: number,
    max: number): ObstaclePaintResult
{
    const colors = mesh?.colors;
    if (!colors || !groups)
        return 'unpaintable';

    const last = lastPaint.get(mesh);
    if (last !== undefined && last.values === values && last.min === min && last.max === max)
        return 'unchanged';
    lastPaint.set(mesh, { values, min, max });

    const vertexCount = colors.length / 3;
    //Anything no group claims - and everything, when nothing is mapped - is context grey.
    const background = values ? unmeasuredObstacleColor : unmappedObstacleColor;
    fill(colors, 0, vertexCount, background);

    if (values)
        for (const group of groups) {
            const value = values.get(group.name);
            if (value === undefined)
                continue;
            const to = Math.min(vertexCount, group.vertexFirst + group.vertexCount);
            fill(colors, group.vertexFirst, to, obstacleColor(value, min, max));
        }
    return 'repainted';
}

function fill(colors: Float32Array, from: number, to: number, c: RGB) {
    for (let i = Math.max(0, from); i < to; ++i) {
        colors[i * 3] = c.r;
        colors[i * 3 + 1] = c.g;
        colors[i * 3 + 2] = c.b;
    }
}
