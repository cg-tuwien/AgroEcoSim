/**
 * Per-obstacle leaf area index and solar load, as the backend measures it, plus the export the
 * architects and engineers actually consume.
 *
 * The shape mirrors `AgroServer.Models.ObstacleLightResponse` exactly. Three levels, because a
 * single number per named object is misleading rather than merely coarse: an imported wall is a
 * solid, so its surface mixes the planted street face with the bare courtyard face, and foliage
 * grows bottom-up, so a wall spanning storeys mixes a shaded base with a bare top.
 *
 *   object   energies exact, intensive readings mix orientations - quote a surface instead
 *     └ surface   one compass orientation: the facade level, and the one to quote
 *         └ band      one height slice of that surface, summary only
 */

/** Every reading of one day or one whole period. Irradiations are Wh/m², integrated. */
export interface IObstacleLightSummary {
    days?: number;
    daylightHours: number;
    directBeamHours: number;
    beamOpenSky: number;
    beam: number;
    diffuseOpenSky: number;
    diffuse: number;
    total: number;
    totalOpenSky: number;
    /** Irradiation the vegetation kept off one square metre, Wh/m². */
    avoided: number;
    /** The same over the patch's whole area, kWh. */
    avoidedTotal_kWh: number;
    /** 1 - total / totalOpenSky. The headline shading number. */
    shadedFraction: number;
    beamTransmittance: number;
    diffuseTransmittance: number;
    gapFractionDiffuse: number;
    /** Total one-sided leaf area per unit patch area: the classic definition. */
    leafAreaIndex: number;
    leafAreaIndexMax: number;
    leafAreaIndexFinal?: number;
    projectedLeafAreaIndex: number;
    leafAreaIndexFromGaps: number;
    peakOpenSky: number;
    /** Largest irradiance the vegetation removed at any moment, W/m². */
    peakReduction: number;
}

export interface IObstacleLightDay extends IObstacleLightSummary {
    /** Local calendar date at the site, yyyy-MM-dd. */
    date: string;
    steps: number;
}

export interface IObstacleLightBand {
    minY: number;
    maxY: number;
    area: number;
    summary: IObstacleLightSummary;
}

export interface IObstacleLightSurface {
    /** "N".."NW", or "up" / "down" for near-horizontal. */
    orientation: string;
    azimuthDegrees: number;
    tiltDegrees: number;
    area: number;
    summary: IObstacleLightSummary;
    days: IObstacleLightDay[];
    bands: IObstacleLightBand[];
}

export interface IObstacleLight {
    name: string;
    area: number;
    exposedArea: number;
    dominantOrientation: string;
    azimuthDegrees: number;
    tiltDegrees: number;
    centroid: number[];
    boundsMin: number[];
    boundsMax: number[];
    summary: IObstacleLightSummary;
    days: IObstacleLightDay[];
    surfaces: IObstacleLightSurface[];
}

export interface IObstacleLightMeta {
    filter: string;
    renderer: string;
    latitude: number;
    longitude: number;
    timeZone: string;
    initialDate: string;
    hoursPerTick: number;
    totalHours: number;
    canopyDepth: number;
    bandHeight: number;
    sensors: number;
    /** Obstacles the scene offered the filter. */
    obstaclesAvailable: number;
    /** How many it matched. Zero with a non-empty filter means the filter is wrong. */
    obstaclesMatched: number;
    /** A few of the available names, so a mistyped filter can be corrected in place. */
    obstacleNameSamples: string[];
    /** True when the per-obstacle daily series were dropped to keep the payload sane. */
    dailySeriesOmitted: boolean;
    foliageAlbedo: number;
    obstacleAlbedo: number;
}

/** The whole selection as one patch. Always present, and the series to plot when the per-obstacle
 * ones were dropped for size. */
export interface IObstacleLightSelection {
    area: number;
    exposedArea: number;
    obstacles: number;
    summary: IObstacleLightSummary;
    days: IObstacleLightDay[];
}

/** What the viewport can colour obstacles by. */
export enum ObstacleMappingOptions {
    None = 'none',
    LeafAreaIndex = 'leaf area index',
    ShadedFraction = 'shaded fraction',
    AvoidedIrradiation = 'avoided irradiation',
    BeamTransmittance = 'beam transmittance',
}

/**
 * What each mapping reads, how it is labelled and how its range is fixed.
 *
 * Ranges are absolute rather than auto-scaled per run, because the point of the colours is to
 * compare facades - and between runs - not to spread whatever this run happened to contain.
 * `max` of undefined means the range is taken from the data instead, which only the avoided
 * irradiation needs, since its magnitude depends on how long the run was.
 */
export const obstacleMappingSpec: Record<Exclude<ObstacleMappingOptions, ObstacleMappingOptions.None>, {
    read: (s: IObstacleLightSummary) => number;
    unit: string;
    min: number;
    max: number | undefined;
    /** How to phrase the value in a tooltip or legend. */
    format: (v: number) => string;
}> = {
    [ObstacleMappingOptions.LeafAreaIndex]: {
        read: s => s.leafAreaIndex, unit: 'm²/m²', min: 0, max: 6,
        format: v => v.toFixed(2),
    },
    [ObstacleMappingOptions.ShadedFraction]: {
        read: s => s.shadedFraction, unit: '', min: 0, max: 1,
        format: v => `${(v * 100).toFixed(1)} %`,
    },
    [ObstacleMappingOptions.AvoidedIrradiation]: {
        read: s => s.avoided * 1e-3, unit: 'kWh/m²', min: 0, max: undefined,
        format: v => v.toFixed(1),
    },
    [ObstacleMappingOptions.BeamTransmittance]: {
        read: s => s.beamTransmittance, unit: '', min: 0, max: 1,
        format: v => v.toFixed(3),
    },
};

/**
 * The value one obstacle shows for a mapping, on the day the viewport is showing.
 *
 * The series is daily, so a playback step resolves to the day that contains it and the value only
 * changes when the date does - which is right for what these quantities are: a canopy develops over
 * weeks and a daily solar total is what a shading study reports. When the requested date is before
 * the first measured day the obstacle reads as nothing measured; after the last one it holds the
 * last day, so a finished run keeps showing its result while the timeline sits at the end.
 */
export function obstacleValueOn(light: IObstacleLight, mapping: ObstacleMappingOptions, date: string | undefined): number | undefined {
    if (mapping === ObstacleMappingOptions.None) return undefined;
    const spec = obstacleMappingSpec[mapping];
    if (!spec) return undefined;

    //No daily series - either none was asked for, or there were too many obstacles to send one
    //each - so the whole-run summary is what there is to show. Returning nothing would blank the
    //mapping on exactly the large scenes it is most useful on.
    const days = light.days;
    if (date === undefined || !days || days.length === 0) return spec.read(light.summary);
    if (date < days[0].date) return undefined;

    //Linear scan from the end: playback moves forward, so the answer is usually near where it was.
    let chosen = days[days.length - 1];
    if (date < chosen.date)
        for (let i = 0; i < days.length; ++i) {
            if (days[i].date > date) break;
            chosen = days[i];
        }
    return spec.read(chosen);
}

/** Local calendar date of a simulation step, as the backend would have labelled it. */
export function stepDate(initialDate: string, hoursPerTick: number, step: number): string {
    //InitialDate is local midnight at the site and steps advance in whole hours from it, so the
    //date only needs day arithmetic - no time zone conversion, which the backend already did.
    const hours = step * hoursPerTick;
    const base = new Date(`${initialDate}T00:00:00Z`);
    base.setUTCHours(base.getUTCHours() + hours);
    return base.toISOString().slice(0, 10);
}

/**
 * Builds the export document.
 *
 * Daily records and whole-period summaries, at object, surface and band level, with a definitions
 * block so the file can be read without this source. Deliberately not the per-step series: a season
 * of steps over a few hundred objects runs to hundreds of megabytes and answers nothing a daily
 * total does not.
 */
export function buildObstacleLightExport(meta: IObstacleLightMeta | undefined, light: IObstacleLight[],
                                         selection?: IObstacleLightSelection, fullSeriesUrl?: string) {
    const totalArea = light.reduce((a, o) => a + o.exposedArea, 0);
    const avoided = light.reduce((a, o) => a + o.summary.avoidedTotal_kWh, 0);
    const openSky = light.reduce((a, o) => a + o.summary.totalOpenSky * o.exposedArea * 1e-3, 0);

    return {
        format: 'greenfdt.obstacle-light/1',
        generated: new Date().toISOString(),
        meta,
        definitions: DEFINITIONS,
        totals: {
            obstacles: light.length,
            exposedArea_m2: totalArea,
            openSkyIrradiation_kWh: openSky,
            avoidedIrradiation_kWh: avoided,
            /** Share of the whole selection's solar gain the vegetation removed. */
            shadedFraction: openSky > 0 ? avoided / openSky : 0,
        },
        /**
         * Whether every `days` array in this file is populated, and what to do when they are not.
         *
         * Without this the file is silently incomplete: a reader opens it, finds `days: []` on
         * sixteen hundred obstacles, and has no way to tell a payload budget from a broken
         * measurement. The flag existed in `meta` already, as a bare boolean nobody would think to
         * look for. This says it in words, at the top level, next to the empty arrays it explains.
         */
        dailySeries: meta?.dailySeriesOmitted
            ? {
                complete: false,
                reason: 'The per-obstacle and per-surface daily series were left out of this file. '
                    + 'They are dropped above a fixed record budget because they reach a hundred megabytes '
                    + 'of JSON at facade scale, which is more than one socket message can carry.',
                stillHere: 'Every summary is complete and exact, and `selection.days` carries the daily '
                    + 'series of the whole measured selection - the seasonal curve of the facade as a whole.',
                howToGetTheRest: fullSeriesUrl
                    ? 'The complete measurement, with every per-obstacle daily series, was saved beside this '
                      + 'file and is also available at the URL below for as long as the run is retained.'
                    : 'Narrow the obstacle filter so fewer objects are measured, and the per-obstacle series '
                      + 'fit within the budget.',
                fullSeriesUrl,
            }
            : { complete: true },
        //The selection's own daily series. The one that survives however many obstacles were
        //matched, so the seasonal curve of the facade is always in the file.
        selection,
        obstacles: light,
    };
}

/**
 * Units and meaning of every field, embedded in the export.
 *
 * Worth the bytes: "leaf area index" has no single definition and the variants below differ by a
 * factor of two on the same canopy, so a file that does not say which one it carries invites the
 * reader to assume the wrong one.
 */
const DEFINITIONS = {
    levels: 'An obstacle holds surfaces (one per compass orientation), each holding height bands. '
        + 'An obstacle\'s energies are exact; its intensive readings average over orientations, so quote a surface for those.',
    area: 'm². Total triangle area of the object, every face of a solid included.',
    exposedArea: 'm². The part that can see the sky past the building itself; only it carries readings.',
    daylightHours: 'h. Hours the sky was bright enough to trace.',
    directBeamHours: 'h. Hours the direct beam reached the surface at all, vegetation aside.',
    beamOpenSky: 'Wh/m². Direct beam irradiation with the vegetation removed.',
    beam: 'Wh/m². Direct beam irradiation actually received.',
    diffuseOpenSky: 'Wh/m². Diffuse and inter-reflected irradiation with the vegetation removed.',
    diffuse: 'Wh/m². Diffuse and inter-reflected irradiation actually received, including what the foliage reflects onto the surface.',
    total: 'Wh/m². beam + diffuse.',
    totalOpenSky: 'Wh/m². beamOpenSky + diffuseOpenSky.',
    avoided: 'Wh/m². totalOpenSky - total: the irradiation the vegetation kept off one square metre. '
        + 'Slightly negative in low sun is physical, when a bright canopy reflects more onto the surface than the sky it hides.',
    avoidedTotal_kWh: 'kWh. avoided over the whole exposed area of the patch. This is the heat load the foliage removed.',
    shadedFraction: '1 - total/totalOpenSky, irradiance weighted. The solar transmittance of the greening layer, '
        + 'i.e. what a building energy model would apply as a shading multiplier.',
    beamTransmittance: 'Share of the direct beam that reaches the surface through the foliage, weighted by beam load. '
        + 'Equal to the beam gap fraction, since a blocked beam contributes nothing.',
    diffuseTransmittance: 'Share of the diffuse and inter-reflected load that reaches the surface.',
    gapFractionDiffuse: 'Geometric fraction of the cosine-weighted sky hemisphere free of foliage. Counts rays, not energy.',
    leafAreaIndex: 'm²/m². Total one-sided leaf area per unit surface area, counting foliage within canopyDepth. '
        + 'The classic definition, and what the green facade literature reports - roughly 2 to 8 for a developed living wall. Measured, not inverted.',
    projectedLeafAreaIndex: 'm²/m². Leaf area PROJECTED onto the surface per unit surface area. Equals leafAreaIndex for leaves '
        + 'parallel to the wall and about half of it for randomly oriented ones. This is the "crown shadow over unshaded area" reading, per leaf rather than per crown.',
    leafAreaIndexFromGaps: 'm²/m². Beer-Lambert inversion of gapFractionDiffuse, -ln(P)/0.93, assuming a spherical leaf angle '
        + 'distribution. What a canopy analyser held in front of the wall would report; falls below leafAreaIndex where leaves clump.',
    leafAreaIndexMax: 'm²/m². Highest value reached, i.e. the developed canopy rather than its seasonal mean.',
    peakOpenSky: 'W/m². Highest open-sky irradiance of the period.',
    peakReduction: 'W/m². Largest irradiance the vegetation removed at any moment. The number a peak cooling load wants.',
    days: 'Daily records, one per local calendar date. Empty on an obstacle or a surface means the '
        + 'series was dropped for size, NOT that nothing was measured - see the dailySeries block, and read '
        + 'that object\'s summary, which is always complete. selection.days is never dropped.',
    orientation: 'Compass sector the surface faces: N, NE, E, SE, S, SW, W, NW, or up / down when within 30 degrees of horizontal.',
    aggregation: 'Irradiations are sums over the period. Transmittances and shaded fractions are ratios of those sums, '
        + 'never means of per-step ratios - a plain mean lets a dim step just after dawn count as much as noon. '
        + 'Leaf area indices are means weighted by daylight hours; peaks are maxima.',
};
