//What the Runs tab says about the store itself, which is what it could not say before.
//
//Three things are pinned here. Deletions and problems are never filtered away, because "where did
//my run go" is the question the log exists to answer. The log opens by itself when the store is
//disabled or something failed recently, because then it is the only account of what happened. And
//the volume line describes the real store - including the case that caused all of this, an
//anonymous Docker volume - in words someone can act on.

import {
    IStoreEvent, IStoreStatus, autoOpenLog, describeCounts, eventGlyph, isDeletion, isProblem,
    recentCounts, shortRunId, visibleEvents, volumeLine,
} from '../src/helpers/StoreLog';

const NOW = Date.parse('2026-10-23T12:00:00Z');
const HOUR = 3_600_000;

function event(overrides: Partial<IStoreEvent> = {}): IStoreEvent {
    return {
        seq: 1, utc: new Date(NOW - HOUR).toISOString(), level: 'info', kind: 'run-started',
        runId: 'a'.repeat(32), bytes: 0, message: 'Run started.', ...overrides,
    };
}

function status(overrides: Partial<IStoreStatus> = {}): IStoreStatus {
    return {
        enabled: true, root: '/data/AgroEcoStorage/runstore', disabledReason: '',
        mountPoint: '/data/AgroEcoStorage', fileSystem: 'ext4', volumeName: 'agroeco-runstore', anonymousVolume: false,
        totalBytes: 100 * 1073741824, freeBytes: 40 * 1073741824, minFreeBytes: 2 * 1073741824,
        retentionBytes: 64 * 1073741824, modelRetentionBytes: 8 * 1073741824,
        storedBytes: 56 * 1073741824, modelBytes: 0, runs: 3, liveRuns: 0, sweepMinutes: 30,
        warnings: [], events: [], ...overrides,
    };
}

describe('which events the log shows', () => {
    const started = event({ seq: 1, kind: 'run-started' });
    const evicted = event({ seq: 2, kind: 'evicted', level: 'warning' });
    const deleted = event({ seq: 3, kind: 'deleted', level: 'info' });
    const failed = event({ seq: 4, kind: 'frame-failed', level: 'error' });
    const collected = event({ seq: 5, kind: 'model-collected', level: 'info' });

    it('always shows deletions and problems, and routine events only on request', () => {
        const all = [failed, deleted, evicted, started, collected];
        expect(visibleEvents(all, false).map(e => e.seq)).toEqual([4, 3, 2, 5]);
        expect(visibleEvents(all, true)).toHaveLength(5);
    });

    it('counts a user delete as a deletion even though it is not a problem', () => {
        //An explicit delete is information, not a failure - but it is exactly what someone asking
        //where their run went needs to find.
        expect(isProblem(deleted)).toBe(false);
        expect(isDeletion(deleted)).toBe(true);
        expect(eventGlyph(deleted)).toBe('🗑');
        expect(eventGlyph(failed)).toBe('⛔');
        expect(eventGlyph(event({ level: 'warning', kind: 'low-space-stop' }))).toBe('⚠');
        expect(eventGlyph(started)).toBe('·');
    });

    it('summarises the last week, not the whole file', () => {
        const old = event({ seq: 9, level: 'error', utc: new Date(NOW - 10 * 24 * HOUR).toISOString() });
        const counts = recentCounts([failed, evicted, deleted, old], NOW, 7);
        expect(counts).toEqual({ errors: 1, warnings: 1, deletions: 2 });
        expect(describeCounts(counts)).toBe('1 error, 1 warning, 2 deletions');
        expect(describeCounts({ errors: 0, warnings: 0, deletions: 0 })).toBe('no errors or deletions');
    });
});

describe('when the log opens by itself', () => {
    it('opens over a disabled store, which is otherwise an unexplained empty list', () => {
        expect(autoOpenLog(status({ enabled: false, disabledReason: 'No space left on device.' }), NOW)).toBe(true);
    });

    it('opens after a recent error and stays closed for an old one or a warning', () => {
        expect(autoOpenLog(status({ events: [event({ level: 'error' })] }), NOW)).toBe(true);
        expect(autoOpenLog(status({ events: [event({ level: 'error', utc: new Date(NOW - 30 * HOUR).toISOString() })] }), NOW)).toBe(false);
        expect(autoOpenLog(status({ events: [event({ level: 'warning' })] }), NOW)).toBe(false);
        expect(autoOpenLog(null, NOW)).toBe(false);
    });
});

describe('the volume line', () => {
    it('names the mount, the volume, the room left and the budget', () => {
        expect(volumeLine(status())).toBe(
            '/data/AgroEcoStorage (ext4, volume agroeco-runstore): 40 GB free of 100 GB · runs 56 GB of a 64 GB budget · 2.0 GB kept free');
    });

    it('says so when the store sits on an anonymous volume - the deployed mistake', () => {
        expect(volumeLine(status({ mountPoint: '/data', volumeName: 'f'.repeat(64), anonymousVolume: true })))
            .toContain('/data (ext4, anonymous Docker volume)');
    });

    it('leaves out what the server could not measure', () => {
        const line = volumeLine(status({ mountPoint: '', fileSystem: '', volumeName: '', freeBytes: -1, totalBytes: -1, minFreeBytes: 0 }));
        expect(line).toBe('/data/AgroEcoStorage/runstore · runs 56 GB of a 64 GB budget');
    });
});

it('shortens run ids to something a list can carry', () => {
    expect(shortRunId('0123456789abcdef0123456789abcdef')).toBe('01234567');
    expect(shortRunId(null)).toBe('');
});
