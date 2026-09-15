/**
 * A run the server has kept, and the small amount of arithmetic needed to describe one.
 *
 * The shape mirrors `AgroServer/Models/RunResponse.cs` exactly, camelCased by ASP.NET on the way
 * out. It is the whole record rather than a summary: what a listing has to show - length, plants,
 * trays, obstacles, size, lineage, and which actions will actually work - is a few hundred bytes,
 * and fetching each run separately to find it out would be a request per row.
 */
export interface IStoredRun {
    id: string;
    /** running | complete | aborted */
    status: string;
    createdUtc: string;
    updatedUtc: string;
    hoursPerTick: number;
    totalSteps: number;
    /** Frames actually on disk. Below `lastStep` whenever the server used a frame stride. */
    storedFrames: number;
    /** The step the run reached, plus one. */
    lastStep: number;
    totalHours: number;
    /** The simulated start date as a calendar day, yyyy-MM-dd. Not an instant - see formatDay. */
    initialDay: string;
    seed: number;
    renderMode: number;
    plants: number;
    /** Soil fields. 0 means NOT RECORDED - see `trayLabel`. */
    trays: number;
    obstacles: number;
    species: number;
    analytics: boolean;
    deterministic: boolean;
    /** Frame bytes. */
    bytesOnDisk: number;
    /** The request, the index, the terrain and the measurement. */
    auxBytesOnDisk: number;
    fieldModelName: string | null;
    /** Whether the run can be continued or forked without uploading its model again. */
    hasFieldModel: boolean;
    /** Whether viewing this run draws its trays and facade. */
    hasTerrain: boolean;
    hasObstacleLight: boolean;
    parentRunId: string | null;
    branchStep: number | null;
}

/** Everything a run occupies. `bytesOnDisk` alone is the frames and reads low by the terrain. */
export function runBytes(run: IStoredRun): number {
    return run.bytesOnDisk + run.auxBytesOnDisk;
}

/**
 * A byte count a person can read.
 *
 * Two significant decimals below a gigabyte and none above: the runs here span five orders of
 * magnitude - 700 KB to 41 GB - and "41000 MB" is not a number anyone compares at a glance.
 */
export function formatBytes(bytes: number): string {
    if (!isFinite(bytes) || bytes < 0) return '-';
    if (bytes >= 1073741824) return `${(bytes / 1073741824).toFixed(bytes >= 10737418240 ? 0 : 1)} GB`;
    if (bytes >= 1048576) return `${(bytes / 1048576).toFixed(bytes >= 10485760 ? 0 : 1)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${bytes} B`;
}

/**
 * How much simulated time a run covers, as a person would say it.
 *
 * The simulated span, not the wall clock: a run is a length of plant life, and the hours it took
 * to compute are a separate fact reported separately. `totalHours` is what was ASKED for, so a
 * run stopped early is described by what it reached instead.
 */
export function formatSimulatedSpan(hours: number): string {
    if (!isFinite(hours) || hours <= 0) return '-';
    const days = hours / 24;
    if (days < 1) return `${Math.round(hours)} h`;
    //Days all the way to a year, rather than switching to months at a season. The scenarios here
    //are set up in hours and compared against each other in days - 90, 180, 270 - and "8.9 mo" is
    //both less precise and harder to line up against the number the user typed.
    if (days < 365) return `${days % 1 === 0 ? days : days.toFixed(1)} d`;
    return `${(days / 365.25).toFixed(1)} y`;
}

/** Simulated hours a run actually reached, which is below what it asked for when it was stopped. */
export function reachedHours(run: IStoredRun): number {
    return run.lastStep * run.hoursPerTick;
}

/** How long the run took to compute, from its own timestamps. */
export function formatWallTime(run: IStoredRun): string {
    const ms = Date.parse(run.updatedUtc) - Date.parse(run.createdUtc);
    if (!isFinite(ms) || ms <= 0) return '-';
    const minutes = ms / 60000;
    if (minutes < 1) return `${Math.round(ms / 1000)} s`;
    if (minutes < 90) return `${minutes.toFixed(0)} min`;
    return `${(minutes / 60).toFixed(1)} h`;
}

/**
 * The tray count, or an admission that it was never measured.
 *
 * Zero is not "no trays" - every world has at least one soil field. It means the run predates the
 * field being recorded, and the number cannot be recovered afterwards because it comes out of the
 * imported model rather than out of the request. Printing "0" would be a wrong measurement; this
 * prints no measurement.
 */
export function trayLabel(run: IStoredRun): string {
    return run.trays > 0 ? `${run.trays}` : '?';
}

/** As `trayLabel`, for obstacles: an imported facade's count is unknown for older runs. */
export function obstacleLabel(run: IStoredRun): string {
    return run.obstacles > 0 ? `${run.obstacles}` : run.fieldModelName ? '?' : '0';
}

const pad = (n: number) => `${n}`.padStart(2, '0');

/** When something happened, in the reader's own timezone. For real instants only. */
export function formatDate(iso: string): string {
    const t = Date.parse(iso);
    if (!isFinite(t)) return '-';
    const d = new Date(t);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * The simulated start date, which arrives as the calendar day it is.
 *
 * Deliberately not parsed. A day is not an instant: sending it as one gave it a zone somewhere
 * between the request, LiteDB and the serializer, and re-rendering it in the reader's zone moved
 * "1 April" to 31 March for everyone on one side of Greenwich. The server now sends `yyyy-MM-dd`
 * and the only job left is to refuse anything that is not one.
 */
export function formatDay(day: string): string {
    return /^\d{4}-\d{2}-\d{2}$/.test(day ?? '') ? day : '-';
}

/**
 * Lower-cases the first letter of every top-level key.
 *
 * The stored request is PascalCase - it is what the C# serializer wrote - while the species loader
 * reads the camelCase shape a saved session uses. The two carry the same values in the same units
 * (angles are radians on both), so the whole conversion is the first character of each key.
 *
 * Deliberately NOT recursive. `leafMorphology` and `leafPhenology` are opaque blobs that the
 * frontend never reads into and hands straight back to the server, so their inner PascalCase keys
 * are what the server expects and camelCasing them would quietly break every leaf on the plant.
 */
export function camelizeTopLevel(source: any): any {
    if (!source || typeof source !== 'object') return source;
    const result: any = {};
    for (const key of Object.keys(source))
        result[key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key] = source[key];
    return result;
}
