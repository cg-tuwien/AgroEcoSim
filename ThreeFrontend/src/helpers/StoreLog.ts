import { formatBytes } from "./StoredRun";

/**
 * The run store as the Runs tab sees it, and the small amount of logic needed to show it.
 *
 * Mirrors `AgroServer/Models/RunResponse.cs` (RunStoreStatusResponse) and
 * `AgroServer/Services/RunStoreLog.cs` (RunStoreEvent), camelCased by ASP.NET on the way out.
 *
 * It exists because the listing alone could not tell an empty store from a broken one. A store that
 * could not open answered `/runs` with an empty array, the panel printed "No stored runs", and a
 * 56 GB run had in fact filled its disk, frozen and taken the store down with it - all of which was
 * said only in a log on that same full disk.
 */

/** One thing the store did or failed to do. Numbered, so order survives a restart. */
export interface IStoreEvent {
    seq: number;
    /** When, as an ISO instant in UTC. */
    utc: string;
    /** info | warning | error */
    level: string;
    /** A short fixed name: deleted, evicted, frame-failed, low-space-stop, ... */
    kind: string;
    runId: string | null;
    /** What the event freed or concerned, when it is about a size. */
    bytes: number;
    message: string;
}

export interface IStoreStatus {
    enabled: boolean;
    root: string;
    /** Why the store could not open, with what to do about it. Empty while it works. */
    disabledReason: string;
    /** The mount containing the root; empty off Linux. */
    mountPoint: string;
    fileSystem: string;
    /** The Docker volume behind that mount, or empty. */
    volumeName: string;
    anonymousVolume: boolean;
    /** -1 when unknown. */
    totalBytes: number;
    freeBytes: number;
    /** Runs stop cleanly when free space reaches this. 0 = no guard. */
    minFreeBytes: number;
    retentionBytes: number;
    modelRetentionBytes: number;
    /** What the runs occupy. */
    storedBytes: number;
    modelBytes: number;
    runs: number;
    liveRuns: number;
    sweepMinutes: number;
    /** Configuration that will lose data, worked out at the time of asking. */
    warnings: string[];
    /** Newest first. */
    events: IStoreEvent[];
}

/**
 * Kinds that removed something from the store.
 *
 * Always shown, whatever the filter, because "where did my run go" is the question the log exists
 * to answer - and an eviction or a delete by a colleague is the answer more often than an error.
 */
export const DELETION_KINDS: ReadonlySet<string> = new Set([
    'deleted', 'evicted', 'model-collected', 'model-temp-removed', 'temp-removed',
]);

export function isProblem(e: IStoreEvent): boolean {
    return e.level === 'warning' || e.level === 'error';
}

export function isDeletion(e: IStoreEvent): boolean {
    return DELETION_KINDS.has(e.kind);
}

/**
 * What the log shows: deletions and problems always, routine events - a run started, a model
 * stored, the store opening - only when asked for. Order is kept, newest first as served.
 */
export function visibleEvents(events: IStoreEvent[], showRoutine: boolean): IStoreEvent[] {
    return showRoutine ? events : events.filter(e => isProblem(e) || isDeletion(e));
}

/** Errors, warnings and deletions in the last `days` days, for the log's one-line summary. */
export function recentCounts(events: IStoreEvent[], nowMs: number, days: number) {
    const since = nowMs - days * 86_400_000;
    let errors = 0, warnings = 0, deletions = 0;
    for (const e of events) {
        const t = Date.parse(e.utc);
        if (!isFinite(t) || t < since) continue;
        if (e.level === 'error') ++errors;
        else if (e.level === 'warning') ++warnings;
        if (isDeletion(e)) ++deletions;
    }
    return { errors, warnings, deletions };
}

/** "2 errors, 1 warning, 3 deletions" - or an honest "nothing" - for the summary line. */
export function describeCounts(counts: { errors: number, warnings: number, deletions: number }): string {
    const parts: string[] = [];
    const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;
    if (counts.errors > 0) parts.push(plural(counts.errors, 'error'));
    if (counts.warnings > 0) parts.push(plural(counts.warnings, 'warning'));
    if (counts.deletions > 0) parts.push(plural(counts.deletions, 'deletion'));
    return parts.length > 0 ? parts.join(', ') : 'no errors or deletions';
}

/**
 * Should the log be open without being asked? When the store is disabled - the log is then the only
 * account of why - or when something went wrong in the last day.
 */
export function autoOpenLog(status: IStoreStatus | null, nowMs: number): boolean {
    if (!status) return false;
    if (!status.enabled) return true;
    return status.events.some(e => e.level === 'error' && nowMs - Date.parse(e.utc) < 86_400_000);
}

/**
 * Where the store writes and how much room is left, in one line:
 * "/data/AgroEcoStorage (ext4, volume agroeco-runstore): 12 GB free of 98 GB · runs 41 GB of a 64 GB budget · 2.0 GB kept free".
 */
export function volumeLine(status: IStoreStatus): string {
    const where = status.mountPoint || status.root;
    const what = [
        status.fileSystem,
        status.anonymousVolume ? 'anonymous Docker volume' : status.volumeName ? `volume ${status.volumeName}` : '',
    ].filter(x => x.length > 0).join(', ');
    const room = status.freeBytes >= 0
        ? `: ${formatBytes(status.freeBytes)} free of ${formatBytes(status.totalBytes)}`
        : '';
    const reserve = status.minFreeBytes > 0 ? ` · ${formatBytes(status.minFreeBytes)} kept free` : '';
    return `${where}${what ? ` (${what})` : ''}${room} · runs ${formatBytes(status.storedBytes)} of a ` +
        `${formatBytes(status.retentionBytes)} budget${reserve}`;
}

/** The glyph a log row starts with: what kind of thing happened, before its sentence is read. */
export function eventGlyph(e: IStoreEvent): string {
    if (e.level === 'error') return '⛔';
    if (isDeletion(e)) return '🗑';
    if (e.level === 'warning') return '⚠';
    return '·';
}

/** Run ids are 32 hex characters; eight tell them apart in a list a person reads. */
export function shortRunId(id: string | null | undefined): string {
    return id ? id.slice(0, 8) : '';
}
