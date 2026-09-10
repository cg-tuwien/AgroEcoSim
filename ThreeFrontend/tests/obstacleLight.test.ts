import fixture from './__fixtures__/obstacle-light.json';
import {
    buildObstacleLightExport, IObstacleLight, IObstacleLightMeta, obstacleValueOn,
    ObstacleMappingOptions, stepDate,
} from '../src/helpers/ObstacleLight';
import { writeObstacleColors, unmappedObstacleColor, unmeasuredObstacleColor } from '../src/render/core/ObstacleColors';
import type { MeshData as MeshDataType } from '../src/render/core/Geometry';
import { MeshData } from '../src/render/core/Geometry';

const light = fixture.obstacleLight as unknown as IObstacleLight[];
const meta = fixture.obstacleLightMeta as unknown as IObstacleLightMeta;

describe('the measured data, as the backend actually sends it', () => {
    it('resolves each obstacle by orientation and height', () => {
        const wall = light[0];
        expect(wall.name).toBe('wall-south');
        //Solar load, not area: a slab has two faces of equal area and only one of them matters.
        expect(wall.dominantOrientation).toBe('S');
        expect(wall.surfaces.map(s => s.orientation)).toEqual(['S', 'N', 'up', 'W', 'E']);

        //Surface areas must account for the whole exposed surface, or the energies do not add up.
        const surfaceArea = wall.surfaces.reduce((a, s) => a + s.area, 0);
        expect(surfaceArea).toBeCloseTo(wall.exposedArea, 1);

        //And a surface's bands must account for the whole surface.
        const south = wall.surfaces[0];
        const bandArea = south.bands.reduce((a, b) => a + b.area, 0);
        expect(bandArea).toBeCloseTo(south.area, 1);
    });

    it('puts the shading where the plants are: on the planted face, low down', () => {
        const [south, north] = light[0].surfaces;
        expect(south.summary.shadedFraction).toBeGreaterThan(north.summary.shadedFraction);
        //Bands run bottom to top and the foliage grows from the planting level.
        const bottom = south.bands[0].summary;
        const top = south.bands[south.bands.length - 1].summary;
        expect(bottom.leafAreaIndexMax).toBeGreaterThan(top.leafAreaIndexMax);
        expect(bottom.shadedFraction).toBeGreaterThan(top.shadedFraction);
    });

    it('keeps every energy identity the export relies on', () => {
        for (const o of light)
            for (const s of [o.summary, ...o.surfaces.map(x => x.summary), ...o.days]) {
                expect(s.total).toBeCloseTo(s.beam + s.diffuse, 0);
                expect(s.totalOpenSky).toBeCloseTo(s.beamOpenSky + s.diffuseOpenSky, 0);
                expect(s.avoided).toBeCloseTo(s.totalOpenSky - s.total, 0);
                if (s.totalOpenSky > 1)
                    expect(s.shadedFraction).toBeCloseTo(1 - s.total / s.totalOpenSky, 3);
                //A transmittance is a ratio of energies and cannot leave [0, 1].
                expect(s.beamTransmittance).toBeGreaterThanOrEqual(0);
                expect(s.beamTransmittance).toBeLessThanOrEqual(1);
            }
    });

    it('has one day per simulated day, in order', () => {
        const days = light[0].days;
        expect(days.length).toBe(40);
        expect(days[0].date).toBe('2026-04-01');
        for (let i = 1; i < days.length; ++i)
            expect(days[i].date > days[i - 1].date).toBe(true);
    });
});

describe('the viewport mapping', () => {
    it('follows the shown day and holds the last one afterwards', () => {
        const wall = light[0];
        const first = wall.days[0].date;
        const last = wall.days[wall.days.length - 1].date;
        const M = ObstacleMappingOptions.LeafAreaIndex;

        //Before anything was measured there is no value to show, rather than a misleading zero.
        expect(obstacleValueOn(wall, M, '2026-03-01')).toBeUndefined();
        expect(obstacleValueOn(wall, M, first)).toBe(wall.days[0].leafAreaIndex);
        expect(obstacleValueOn(wall, M, last)).toBe(wall.days[wall.days.length - 1].leafAreaIndex);
        //Past the end the run is over and its result should stay on screen.
        expect(obstacleValueOn(wall, M, '2027-01-01')).toBe(wall.days[wall.days.length - 1].leafAreaIndex);
        //No date at all means the whole-run summary.
        expect(obstacleValueOn(wall, M, undefined)).toBe(wall.summary.leafAreaIndex);
    });

    it('falls back to the whole-run summary when there is no daily series', () => {
        //Which is what a large selection gets: too many obstacles to send a series each.
        const summaryOnly = { ...light[0], days: [] } as IObstacleLight;
        const M = ObstacleMappingOptions.ShadedFraction;
        expect(obstacleValueOn(summaryOnly, M, '2026-04-15')).toBe(light[0].summary.shadedFraction);
        expect(obstacleValueOn(summaryOnly, M, undefined)).toBe(light[0].summary.shadedFraction);
    });

    it('maps a playback step onto the day the backend labelled it with', () => {
        expect(stepDate(meta.initialDate, meta.hoursPerTick, 0)).toBe('2026-04-01');
        //Six four-hour steps to the day.
        expect(stepDate(meta.initialDate, meta.hoursPerTick, 6)).toBe('2026-04-02');
        expect(stepDate(meta.initialDate, meta.hoursPerTick, 5)).toBe('2026-04-01');
    });

    it('paints only the vertices of the named object it measured', () => {
        const mesh: MeshData = {
            positions: new Float32Array(9 * 3),
            normals: new Float32Array(9 * 3),
            colors: new Float32Array(9 * 3),
            indices: new Uint32Array([0, 1, 2]),
            name: 'test',
        };
        const groups = [
            { name: 'wall-south', vertexFirst: 0, vertexCount: 3 },
            { name: 'not-measured', vertexFirst: 3, vertexCount: 3 },
        ];

        expect(writeObstacleColors(mesh, groups, new Map([['wall-south', 1]]), 0, 1)).toBe('repainted');
        //The array is Float32, so compare per component rather than by structural equality.
        const at = (v: number) => [mesh.colors![v * 3], mesh.colors![v * 3 + 1], mesh.colors![v * 3 + 2]];
        const isColor = (v: number, c: { r: number, g: number, b: number }) => {
            const [r, g, b] = at(v);
            expect(r).toBeCloseTo(c.r, 5);
            expect(g).toBeCloseTo(c.g, 5);
            expect(b).toBeCloseTo(c.b, 5);
        };

        //The measured group takes the top of the ramp, which is not the unmeasured background.
        expect(at(0)).not.toEqual(at(3));
        expect(at(0)).toEqual(at(2));
        //A name with no reading, and the vertices no group claims, stay background.
        isColor(3, unmeasuredObstacleColor);
        isColor(8, unmeasuredObstacleColor);

        //Nothing mapped repaints everything as plain context grey.
        writeObstacleColors(mesh, groups, undefined, 0, 1);
        isColor(0, unmappedObstacleColor);

        //An obstacle that never came from an import has no colour array and must be left alone.
        expect(writeObstacleColors({ ...mesh, colors: undefined }, groups, new Map(), 0, 1)).toBe('unpaintable');
    });

    it('does not repaint when nothing that affects the colours changed', () => {
        //This is what stops orbiting the camera from flickering. Picking runs on every mouse move
        //and flips an obstacle's hover state, which re-runs the obstacle rebuild; an unguarded
        //repaint then rewrote the whole colour array and had the atlas re-upload 42 MB per event.
        const mesh: MeshDataType = {
            positions: new Float32Array(6 * 3),
            normals: new Float32Array(6 * 3),
            colors: new Float32Array(6 * 3),
            indices: new Uint32Array([0, 1, 2]),
            name: 'flicker',
        };
        const groups = [{ name: 'wall-south', vertexFirst: 0, vertexCount: 3 }];
        const values = new Map([['wall-south', 0.5]]);

        expect(writeObstacleColors(mesh, groups, values, 0, 1)).toBe('repainted');
        //Same map, same range: a hover changed, not a measurement.
        expect(writeObstacleColors(mesh, groups, values, 0, 1)).toBe('unchanged');
        expect(writeObstacleColors(mesh, groups, values, 0, 1)).toBe('unchanged');

        //A different range is a different picture and has to be redrawn.
        expect(writeObstacleColors(mesh, groups, values, 0, 2)).toBe('repainted');
        //So is a new values map, which is what a new shown day or a new mapping produces.
        expect(writeObstacleColors(mesh, groups, new Map([['wall-south', 0.5]]), 0, 2)).toBe('repainted');
        //And switching the mapping off.
        expect(writeObstacleColors(mesh, groups, undefined, 0, 2)).toBe('repainted');
        expect(writeObstacleColors(mesh, groups, undefined, 0, 2)).toBe('unchanged');
    });
});

describe('the export document', () => {
    const document_ = buildObstacleLightExport(meta, light);

    it('carries the metadata and definitions that make it readable on its own', () => {
        expect(document_.format).toBe('greenfdt.obstacle-light/1');
        expect(document_.meta!.latitude).toBeCloseTo(48.2083, 3);
        expect(document_.meta!.timeZone).toBe('Europe/Vienna');
        //The three leaf area index variants differ by a factor of two, so the file has to say which.
        expect(document_.definitions.leafAreaIndex).toContain('one-sided');
        expect(document_.definitions.projectedLeafAreaIndex).toContain('PROJECTED');
        expect(document_.definitions.leafAreaIndexFromGaps).toContain('Beer-Lambert');
        expect(document_.definitions.aggregation).toContain('never means of per-step ratios');
    });

    it('totals the selection the way a cooling estimate would', () => {
        const t = document_.totals;
        expect(t.obstacles).toBe(light.length);
        expect(t.exposedArea_m2).toBeCloseTo(light.reduce((a, o) => a + o.exposedArea, 0), 3);
        expect(t.avoidedIrradiation_kWh).toBeGreaterThan(0);
        expect(t.shadedFraction).toBeGreaterThan(0);
        expect(t.shadedFraction).toBeLessThan(1);
    });

    it('is small enough to hand to someone', () => {
        //Daily rather than per-step is the whole reason: 40 days of one wall must stay tiny.
        expect(JSON.stringify(document_).length).toBeLessThan(400_000);
    });
});
