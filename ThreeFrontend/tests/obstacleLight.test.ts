import fixture from './__fixtures__/obstacle-light.json';
import {
    IObstacleLight, IObstacleLightDocument, IObstacleLightPeriod, isObstacleLightDocument,
    obstacleValueOn, ObstacleMappingOptions, OBSTACLE_LIGHT_FORMAT, stepDate, totalLeavesShed, weekIndexOn,
} from '../src/helpers/ObstacleLight';
import { writeObstacleColors, unmappedObstacleColor, unmeasuredObstacleColor } from '../src/render/core/ObstacleColors';
import type { MeshData as MeshDataType } from '../src/render/core/Geometry';
import { MeshData } from '../src/render/core/Geometry';

//The document of a 60-day run of one wall with six Geraniums on its outer face, exactly as the
//server serializes it: camelCase, four significant digits, weekly arrays aligned with the periods.
const doc = fixture as unknown as IObstacleLightDocument;
const wall = doc.obstacles[0];
const present = (weeks: (IObstacleLightPeriod | null)[]) => weeks.filter((w): w is IObstacleLightPeriod => w !== null);
const sum = (weeks: (IObstacleLightPeriod | null)[], f: (p: IObstacleLightPeriod) => number) => present(weeks).reduce((a, w) => a + f(w), 0);

describe('the document, as the backend actually writes it', () => {
    it('is the format this build reads, complete, with its definitions on board', () => {
        expect(isObstacleLightDocument(doc)).toBe(true);
        expect(doc.format).toBe(OBSTACLE_LIGHT_FORMAT);
        expect(doc.complete).toBe(true);
        expect(doc.omitted).toEqual([]);
        expect(doc.meta.timeZone).toBe('Europe/Vienna');
        expect(doc.meta.filterMode).toBe('regex');
        //The three leaf area index variants differ by a factor of two, so the file has to say which.
        expect(doc.definitions.leafAreaIndex).toContain('one-sided');
        expect(doc.definitions.projectedLeafAreaIndex).toContain('PROJECTED');
        expect(doc.definitions.leafAreaIndexFromGaps).toContain('Beer-Lambert');
        expect(doc.definitions.aggregation).toContain('never means of per-step ratios');
        expect(doc.definitions.leafAngleSine).toContain('sqrt(1 - n_y');
        //An older session must not be half-read into this shape.
        expect(isObstacleLightDocument([{ name: 'wall', days: [] }])).toBe(false);
        expect(isObstacleLightDocument(null)).toBe(false);
    });

    it('aligns every weekly array with the period table', () => {
        const weeks = doc.periods.weeks;
        expect(weeks.length).toBe(9); //60 days from 1 April
        expect(weeks[0]).toEqual({ index: 0, start: '2026-04-01', end: '2026-04-07' });
        expect(weeks[8].end).toBe('2026-05-30');
        expect(doc.periods.years).toEqual([{ year: 2026, start: '2026-04-01', end: '2026-05-30' }]);
        expect(doc.selection!.weeks.length).toBe(weeks.length);
        expect(wall.weeks!.length).toBe(weeks.length);
        for (const s of wall.surfaces)
            if (s.weeks) expect(s.weeks.length).toBe(weeks.length);
        expect(doc.trays[0].weeks.length).toBe(weeks.length);
        expect(doc.facade!.weeks.length).toBe(weeks.length);
    });

    it('preserves shedding totals in new documents while historical counts remain unknown', () => {
        expect(totalLeavesShed(doc)).toBeUndefined();
        const withShedding: IObstacleLightDocument = {
            ...doc,
            facade: {
                ...doc.facade!,
                weeks: doc.facade!.weeks.map((week, i) => week && ({ ...week, leavesShed: i === 0 ? 2 : i === 1 ? 5 : 0 })),
                years: doc.facade!.years.map(year => ({ ...year, leavesShed: 7 })),
                total: { ...doc.facade!.total!, leavesShed: 7 },
            },
            trays: doc.trays.map(tray => ({ ...tray, total: { ...tray.total!, leavesShed: 7 } })),
        };
        const restored = JSON.parse(JSON.stringify(withShedding)) as IObstacleLightDocument;
        expect(isObstacleLightDocument(restored)).toBe(true);
        expect(totalLeavesShed(restored)).toBe(7);
        //Unlike the daily means, weekly removal counts add directly, without weighting by days.
        expect(restored.facade!.weeks.reduce((total, week) => total + (week?.leavesShed ?? 0), 0)).toBe(7);
        expect(restored.facade!.years[0].leavesShed).toBe(7);
        expect(restored.facade!.total!.leaves).toBe(doc.facade!.total!.leaves);
        expect(doc.facade!.total!.leavesShed).toBeUndefined();

        expect(totalLeavesShed({ ...restored, facade: null })).toBe(7);
        expect(totalLeavesShed({ ...restored, facade: null, trays: [...restored.trays, doc.trays[0]] })).toBeUndefined();
        expect(totalLeavesShed({ ...restored, facade: null, trays: [] })).toBeUndefined();
    });

    it('resolves the wall by side and orientation, with series on the facade sides only', () => {
        expect(wall.name).toBe('wall-south');
        //Solar load, not area: a slab has two faces of equal area and only one of them matters.
        expect(wall.dominantOrientation).toBe(wall.surfaces[0].orientation);
        expect(wall.surfaces[0].side).toBe('outer');
        for (const s of wall.surfaces) {
            expect(['outer', 'inner', 'edge']).toContain(s.side);
            //Rims and returns carry a total only; the facade projection is what a series is for.
            expect(s.weeks === null).toBe(s.side === 'edge');
            expect(s.years === null).toBe(s.side === 'edge');
            expect(s.bands.length).toBeGreaterThan(0);
            for (const b of s.bands) expect(b.total.daylightHours).toBeGreaterThan(0);
        }
        //Surface areas must account for the whole exposed surface, or the energies do not add up.
        const surfaceArea = wall.surfaces.reduce((a, s) => a + s.area, 0);
        expect(surfaceArea).toBeCloseTo(wall.exposedArea, 1);
        const bandArea = wall.surfaces[0].bands.reduce((a, b) => a + b.area, 0);
        expect(bandArea).toBeCloseTo(wall.surfaces[0].area, 1);
    });

    it('puts the foliage where the plants are: on the planted face', () => {
        const planted = wall.surfaces[0];
        const rims = wall.surfaces.filter(s => s.side === 'edge');
        expect(planted.total.leafAreaIndexMax!).toBeGreaterThan(0);
        for (const rim of rims)
            expect(planted.total.leafAreaIndexMax!).toBeGreaterThanOrEqual(rim.total.leafAreaIndexMax!);
        //The canopy develops over the run, so the last week carries more leaf than the first.
        const series = present(planted.weeks!);
        expect(series[series.length - 1].leafAreaIndex).toBeGreaterThan(series[0].leafAreaIndex);
    });

    it('keeps every aggregation identity the file promises', () => {
        const levels: { label: string, weeks: (IObstacleLightPeriod | null)[], years: IObstacleLightPeriod[], total: IObstacleLightPeriod }[] = [
            { label: 'selection', weeks: doc.selection!.weeks, years: doc.selection!.years, total: doc.selection!.total },
            { label: wall.name, weeks: wall.weeks!, years: wall.years!, total: wall.total },
            ...wall.surfaces.filter(s => s.weeks).map(s => ({ label: `${s.side}/${s.orientation}`, weeks: s.weeks!, years: s.years!, total: s.total })),
        ];
        for (const { weeks, years, total } of levels) {
            //Sums over the period are sums; four significant digits is the tolerance.
            expect(sum(weeks, w => w.daylightHours)).toBeCloseTo(total.daylightHours, 0);
            expect(years.reduce((a, y) => a + y.daylightHours, 0)).toBeCloseTo(total.daylightHours, 0);
            expect(sum(weeks, w => w.openSky_kWh_m2) / total.openSky_kWh_m2).toBeCloseTo(1, 2);
            //Shaded fraction is a ratio of sums, never a mean of ratios.
            const open = sum(weeks, w => w.openSky_kWh_m2);
            const received = open - sum(weeks, w => w.avoided_kWh_m2);
            expect(1 - received / open).toBeCloseTo(total.shadedFraction, 2);
            //Leaf area indices are means weighted by daylight hours.
            const hours = sum(weeks, w => w.daylightHours);
            expect(sum(weeks, w => w.leafAreaIndex * w.daylightHours) / hours).toBeCloseTo(total.leafAreaIndex, 2);
            //A transmittance is a ratio of energies and cannot leave [0, 1].
            for (const w of present(weeks)) {
                expect(w.beamTransmittance).toBeGreaterThanOrEqual(0);
                expect(w.beamTransmittance).toBeLessThanOrEqual(1);
                expect(w.leafAreaIndexMax).toBeUndefined();
            }
            expect(total.leafAreaIndexMax).toBeDefined();
            for (const y of years) expect(y.leafAreaIndexMax).toBeDefined();
        }
    });

    it('surveys the foliage of the tray the plants are rooted in', () => {
        expect(doc.trays.length).toBe(1);
        const tray = doc.trays[0];
        expect(tray.name).toBe('field');
        expect(tray.plants).toBe(6);
        expect(tray.species).toEqual({ 'Geranium Macrorrhizum': 6 });
        expect(tray.hasMesh).toBe(false);
        expect(doc.meta.species).toEqual([{ name: 'Geranium Macrorrhizum', leafOutlineAreaRatio: [expect.any(Number)] }]);
        const ratio = doc.meta.species[0].leafOutlineAreaRatio[0];
        expect(ratio).toBeGreaterThan(0.5);
        expect(ratio).toBeLessThan(1);

        const weeks = present(tray.weeks);
        expect(weeks.reduce((a, w) => a + w.days, 0)).toBe(tray.total!.days);
        expect(tray.total!.days).toBe(doc.meta.coverage.surveyedDays);
        //The mean over the run is the day-weighted mean of the weeks.
        expect(weeks.reduce((a, w) => a + w.leafArea_m2 * w.days, 0) / tray.total!.days).toBeCloseTo(tray.total!.leafArea_m2, 4);
        expect(tray.total!.leafAreaMax_m2!).toBeGreaterThanOrEqual(tray.total!.leafAreaFinal_m2!);
        expect(tray.total!.leafAreaFinal_m2!).toBeGreaterThan(tray.total!.leafArea_m2);
        for (const w of weeks) {
            expect(w.leafAngleSine).toBeGreaterThanOrEqual(0);
            expect(w.leafAngleSine).toBeLessThanOrEqual(1);
            expect(w.leafAreaMax_m2).toBeUndefined();
        }
        //Without a tray mesh the thickness is the canopy height, measured upward.
        expect(doc.facade!.outward).toEqual([0, 1, 0]);
        expect(doc.facade!.total!.leafArea_m2).toBeCloseTo(tray.total!.leafArea_m2, 4);
    });
});

describe('the viewport mapping', () => {
    const M = ObstacleMappingOptions.LeafAreaIndex;

    it('finds the week a date falls in by arithmetic, not by scanning', () => {
        expect(weekIndexOn(doc.periods, '2026-03-31')).toBe(-1);
        expect(weekIndexOn(doc.periods, '2026-04-01')).toBe(0);
        expect(weekIndexOn(doc.periods, '2026-04-07')).toBe(0);
        expect(weekIndexOn(doc.periods, '2026-04-08')).toBe(1);
        expect(weekIndexOn(doc.periods, '2026-05-10')).toBe(5);
        expect(weekIndexOn(doc.periods, '2026-05-30')).toBe(8);
        //Past the end the run is over and its last week is what there is.
        expect(weekIndexOn(doc.periods, '2027-01-01')).toBe(8);
    });

    it('follows the shown week and holds the last one afterwards', () => {
        const weeks = wall.weeks!;
        //Before anything was measured there is no value to show, rather than a misleading zero.
        expect(obstacleValueOn(doc, wall, M, '2026-03-01')).toBeUndefined();
        expect(obstacleValueOn(doc, wall, M, '2026-04-03')).toBe(weeks[0]!.leafAreaIndex);
        expect(obstacleValueOn(doc, wall, M, '2026-04-20')).toBe(weeks[2]!.leafAreaIndex);
        expect(obstacleValueOn(doc, wall, M, '2027-01-01')).toBe(weeks[8]!.leafAreaIndex);
        //No date at all means the whole-run total.
        expect(obstacleValueOn(doc, wall, M, undefined)).toBe(wall.total.leafAreaIndex);
    });

    it('holds the latest reading through a week without one', () => {
        const gappy = { ...wall, weeks: wall.weeks!.map((w, i) => i === 2 ? null : w) } as IObstacleLight;
        expect(obstacleValueOn(doc, gappy, M, '2026-04-20')).toBe(wall.weeks![1]!.leafAreaIndex);
        const dark = { ...wall, weeks: wall.weeks!.map((w, i) => i === 0 ? null : w) } as IObstacleLight;
        expect(obstacleValueOn(doc, dark, M, '2026-04-03')).toBeUndefined();
    });

    it('falls back to the whole-run total when the series was dropped for size', () => {
        //Which is what a large selection gets: too many obstacles to send a series each.
        const totalOnly = { ...wall, weeks: null, years: null } as IObstacleLight;
        const S = ObstacleMappingOptions.ShadedFraction;
        expect(obstacleValueOn(doc, totalOnly, S, '2026-04-15')).toBe(wall.total.shadedFraction);
        expect(obstacleValueOn(doc, totalOnly, S, undefined)).toBe(wall.total.shadedFraction);
        expect(obstacleValueOn(doc, wall, ObstacleMappingOptions.None, '2026-04-15')).toBeUndefined();
    });

    it('maps a playback step onto the day the backend credits it to: its midpoint', () => {
        expect(stepDate(doc.meta.initialDate, doc.meta.hoursPerTick, 0)).toBe('2026-04-01');
        //Six four-hour steps to the day; the last one of 1 April starts at 20:00 and ends at midnight.
        expect(stepDate(doc.meta.initialDate, doc.meta.hoursPerTick, 5)).toBe('2026-04-01');
        expect(stepDate(doc.meta.initialDate, doc.meta.hoursPerTick, 6)).toBe('2026-04-02');
        //A 24-hour step belongs to the day it mostly covers.
        expect(stepDate('2026-04-01', 24, 1)).toBe('2026-04-02');
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
        //So is a new values map, which is what a new shown week or a new mapping produces.
        expect(writeObstacleColors(mesh, groups, new Map([['wall-south', 0.5]]), 0, 2)).toBe('repainted');
        //And switching the mapping off.
        expect(writeObstacleColors(mesh, groups, undefined, 0, 2)).toBe('repainted');
        expect(writeObstacleColors(mesh, groups, undefined, 0, 2)).toBe('unchanged');
    });
});

describe('the document is small enough to hand to someone', () => {
    it('stays under a few tens of kilobytes for sixty days of one wall', () => {
        //Weekly rather than daily, four significant digits, and series only where they mean
        //something: nine weeks of one wall must stay tiny.
        expect(JSON.stringify(doc).length).toBeLessThan(60_000);
    });
});
