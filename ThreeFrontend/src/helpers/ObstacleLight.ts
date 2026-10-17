/**
 * The leaf area index document, as the backend writes it: the ray-traced light and foliage
 * readings on the selected obstacles, and the geometric survey of the foliage per planting tray.
 *
 * The shape mirrors `AgroServer.Models.ObstacleLightDocument` exactly; the same document is the
 * file in the run store and the element embedded in the socket result, so what the HUD shows and
 * what the colleagues download cannot disagree. Weekly, annual and whole-run records only, every
 * weekly array dense and aligned with `periods.weeks`, so index is week and a week with no
 * reading is null.
 *
 *   obstacle   energies exact, intensive readings mix orientations - quote a surface instead
 *     └ surface   one compass orientation and side: the facade level, and the one to quote
 *         └ band      one height slice of that surface, whole-run total only
 *   tray       the plants rooted in one imported tray: leaf area, angle, size, thickness
 */

export const OBSTACLE_LIGHT_FORMAT = 'greenfdt.leaf-area-index/2';

/** One period's light and foliage readings on one patch. */
export interface IObstacleLightPeriod {
    /** Hours the sky was bright enough to trace; zero means no reading. */
    daylightHours: number;
    /** Direct plus diffuse irradiation with the vegetation removed, kWh/m². */
    openSky_kWh_m2: number;
    /** Irradiation the vegetation kept off one square metre, kWh/m². */
    avoided_kWh_m2: number;
    /** 1 - received / openSky. The headline shading number. */
    shadedFraction: number;
    beamTransmittance: number;
    diffuseTransmittance: number;
    /** Total one-sided leaf area per unit patch area: the classic definition. */
    leafAreaIndex: number;
    projectedLeafAreaIndex: number;
    leafAreaIndexFromGaps: number;
    /** Largest irradiance the vegetation removed at any moment, W/m². */
    peakReduction_W_m2: number;
    /** Annual and whole-run records only. */
    leafAreaIndexMax?: number;
}

export interface IObstacleLightBand {
    minY: number;
    maxY: number;
    area: number;
    total: IObstacleLightPeriod;
}

export interface IObstacleLightSurface {
    /** "N".."NW", or "up" / "down" for near-horizontal. */
    orientation: string;
    /** outer: the facade face; inner: a face set back from it, a tray's back wall; edge: a rim. */
    side: 'outer' | 'inner' | 'edge';
    azimuthDegrees: number;
    tiltDegrees: number;
    area: number;
    depth: number;
    /** Null for an edge side, and when dropped for size. */
    weeks: (IObstacleLightPeriod | null)[] | null;
    years: IObstacleLightPeriod[] | null;
    total: IObstacleLightPeriod;
    bands: IObstacleLightBand[];
}

export interface IObstacleLight {
    name: string;
    area: number;
    exposedArea: number;
    enclosedArea: number;
    dominantOrientation: string;
    azimuthDegrees: number;
    tiltDegrees: number;
    centroid: number[];
    boundsMin: number[];
    boundsMax: number[];
    /** Null when dropped for size; the total is always there. */
    weeks: (IObstacleLightPeriod | null)[] | null;
    years: IObstacleLightPeriod[] | null;
    total: IObstacleLightPeriod;
    surfaces: IObstacleLightSurface[];
}

/** The whole selection as one patch. Its series are never dropped. */
export interface IObstacleLightSelection {
    area: number;
    exposedArea: number;
    enclosedArea: number;
    obstacles: number;
    weeks: (IObstacleLightPeriod | null)[];
    years: IObstacleLightPeriod[];
    total: IObstacleLightPeriod;
}

export interface IObstacleLightCoverage {
    firstDate: string | null;
    lastDate: string | null;
    litDays: number;
    surveyFirstDate: string | null;
    surveyLastDate: string | null;
    surveyedDays: number;
}

export interface IObstacleLightMeta {
    filter: string | null;
    /** "regex" or "substring": the rule actually applied. */
    filterMode: string | null;
    filterError: string | null;
    renderer: string;
    latitude: number;
    longitude: number;
    altitude: number;
    azimuth: number;
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
    foliageAlbedo: number;
    obstacleAlbedo: number;
    coverage: IObstacleLightCoverage;
    species: { name: string; leafOutlineAreaRatio: number[] }[];
}

export interface IObstacleLightPeriods {
    weekRule: string;
    yearRule: string;
    weeks: { index: number; start: string; end: string }[];
    years: { year: number; start: string; end: string }[];
    total: { start: string; end: string };
}

/** The foliage of one tray, or of the whole facade, over one period. */
export interface IVegetationPeriod {
    days: number;
    leafArea_m2: number;
    leafAngleSine: number;
    leafDiameter_m: number;
    thickness_m: number;
    leaves: number;
    /** One-sided area of blades killed by drought and still attached; absent in documents written before it existed. */
    deadLeafArea_m2?: number;
    leafAreaMax_m2?: number;
    leafAreaFinal_m2?: number;
    thicknessMax_m?: number;
}

export interface IVegetationPatch {
    trays: number;
    plants: number;
    outward: number[];
    weeks: (IVegetationPeriod | null)[];
    years: IVegetationPeriod[];
    total: IVegetationPeriod | null;
}

export interface IVegetationTray {
    name: string;
    soils: string[];
    plants: number;
    species: Record<string, number>;
    hasMesh: boolean;
    width_m: number;
    height_m: number;
    depth_m: number;
    centroid: number[];
    weeks: (IVegetationPeriod | null)[];
    years: IVegetationPeriod[];
    total: IVegetationPeriod | null;
}

export interface IObstacleLightDocument {
    format: string;
    generated: string;
    runId: string | null;
    /** False when a series was dropped for size; `omitted` says which. */
    complete: boolean;
    omitted: string[];
    meta: IObstacleLightMeta;
    definitions: Record<string, string>;
    periods: IObstacleLightPeriods;
    /** Null when the filter matched nothing. */
    selection: IObstacleLightSelection | null;
    obstacles: IObstacleLight[];
    facade: IVegetationPatch | null;
    trays: IVegetationTray[];
}

/** True for a value that is the document this build understands - a loaded session may carry an
 * older measurement in another shape, which is dropped rather than half-read. */
export function isObstacleLightDocument(value: unknown): value is IObstacleLightDocument {
    return typeof value === 'object' && value !== null
        && (value as { format?: unknown }).format === OBSTACLE_LIGHT_FORMAT
        && Array.isArray((value as { obstacles?: unknown }).obstacles);
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
 * irradiation needs, since its magnitude depends on how long the period was.
 */
export const obstacleMappingSpec: Record<Exclude<ObstacleMappingOptions, ObstacleMappingOptions.None>, {
    read: (p: IObstacleLightPeriod) => number;
    unit: string;
    min: number;
    max: number | undefined;
    /** How to phrase the value in a tooltip or legend. */
    format: (v: number) => string;
}> = {
    [ObstacleMappingOptions.LeafAreaIndex]: {
        read: p => p.leafAreaIndex, unit: 'm²/m²', min: 0, max: 6,
        format: v => v.toFixed(2),
    },
    [ObstacleMappingOptions.ShadedFraction]: {
        read: p => p.shadedFraction, unit: '', min: 0, max: 1,
        format: v => `${(v * 100).toFixed(1)} %`,
    },
    [ObstacleMappingOptions.AvoidedIrradiation]: {
        read: p => p.avoided_kWh_m2, unit: 'kWh/m²', min: 0, max: undefined,
        format: v => v.toFixed(1),
    },
    [ObstacleMappingOptions.BeamTransmittance]: {
        read: p => p.beamTransmittance, unit: '', min: 0, max: 1,
        format: v => v.toFixed(3),
    },
};

const MS_PER_DAY = 86_400_000;

/**
 * Index of the week of the document's period table that contains `date`: -1 before the first
 * week, and the last index past the end. Weeks are consecutive 7-day blocks from the first one's
 * start, so this is day arithmetic rather than a scan.
 */
export function weekIndexOn(periods: IObstacleLightPeriods, date: string): number {
    const weeks = periods.weeks;
    if (weeks.length === 0 || date < weeks[0].start) return -1;
    const days = Math.round((Date.parse(`${date}T00:00:00Z`) - Date.parse(`${weeks[0].start}T00:00:00Z`)) / MS_PER_DAY);
    return Math.min(weeks.length - 1, Math.floor(days / 7));
}

/**
 * The value one obstacle shows for a mapping, on the day the viewport is showing.
 *
 * The series is weekly, so a playback step resolves to the week that contains it and the value
 * only changes when the week does - which is right for what these quantities are: a canopy
 * develops over weeks and a weekly solar total is what the study asked for. Before the first
 * week the obstacle reads as nothing measured; after the last one it holds the last week, so a
 * finished run keeps showing its result while the timeline sits at the end. A week with no
 * reading holds the latest one before it.
 */
export function obstacleValueOn(doc: IObstacleLightDocument, light: IObstacleLight, mapping: ObstacleMappingOptions,
                                date: string | undefined): number | undefined {
    if (mapping === ObstacleMappingOptions.None) return undefined;
    const spec = obstacleMappingSpec[mapping];
    if (!spec) return undefined;

    //No weekly series - too many obstacles to send one each - so the whole-run total is what
    //there is to show. Returning nothing would blank the mapping on exactly the large scenes it
    //is most useful on.
    const weeks = light.weeks;
    if (date === undefined || !weeks || weeks.length === 0) return spec.read(light.total);
    const index = weekIndexOn(doc.periods, date);
    if (index < 0) return undefined;
    for (let i = Math.min(index, weeks.length - 1); i >= 0; --i) {
        const record = weeks[i];
        if (record) return spec.read(record);
    }
    return undefined;
}

/** Local calendar date of a simulation step, as the backend would have labelled it. */
export function stepDate(initialDate: string, hoursPerTick: number, step: number): string {
    //InitialDate is local midnight at the site and steps advance in whole hours from it, so the
    //date only needs day arithmetic - no time zone conversion, which the backend already did.
    //The backend credits a step to the date of its midpoint, so the half step is added here too.
    const hours = step * hoursPerTick + hoursPerTick / 2;
    const base = new Date(`${initialDate}T00:00:00Z`);
    base.setUTCHours(base.getUTCHours() + Math.floor(hours));
    return base.toISOString().slice(0, 10);
}
