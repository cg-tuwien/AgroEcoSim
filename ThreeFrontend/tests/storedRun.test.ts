//How a stored run is DESCRIBED, which is the whole of what the runs panel can be wrong about.
//
//Three things are pinned here and nothing else pins them.
//
//"Not measured" versus zero. Trays and obstacles are counted off the built world, so a run stored
//before they were counted has no value for them - the server sends 0. Printing "0 trays" would be
//a measurement the server never made, and every world has at least one soil field, so the zero is
//not even a possible answer.
//
//The camelCase conversion between the SERVER's request shape and the species loader's. They carry
//the same values in the same units and share not one key spelling, and the conversion has to stop
//at the top level: the morphology and phenology blobs are handed straight back to the server, so
//camelCasing their insides would break every leaf on the plant while type-checking perfectly.
//
//Byte formatting across five orders of magnitude, because that is the real spread of this store -
//700 KB to 41 GB - and "41000 MB" is not a number anyone compares at a glance.

import {
    IStoredRun, camelizeTopLevel, formatBytes, formatSimulatedSpan, formatWallTime,
    obstacleLabel, reachedHours, runBytes, trayLabel,
} from '../src/helpers/StoredRun';

function run(overrides: Partial<IStoredRun> = {}): IStoredRun {
    return {
        id: 'a'.repeat(32), status: 'complete',
        createdUtc: '2026-09-13T18:39:17.383Z', updatedUtc: '2026-09-13T18:42:45.143Z',
        hoursPerTick: 4, totalSteps: 540, storedFrames: 540, lastStep: 540,
        totalHours: 2160, initialDay: '2026-02-01', seed: 42, renderMode: 1,
        plants: 4134, trays: 857, obstacles: 1622, species: 8,
        analytics: true, deterministic: false,
        bytesOnDisk: 68201033, auxBytesOnDisk: 116655,
        fieldModelName: null, hasFieldModel: false, hasTerrain: true, hasObstacleLight: false, hasCheckpoint: true,
        parentRunId: null, branchStep: null,
        ...overrides,
    };
}

describe('stored run size', () => {
    it('counts everything the run occupies, not only its frames', () => {
        //`bytesOnDisk` is frames alone - the retention budget is spent almost entirely on them -
        //so a panel that showed it would under-report every run by its terrain and its request.
        expect(runBytes(run())).toBe(68201033 + 116655);
    });

    it('scales the unit to the magnitude', () => {
        expect(formatBytes(772809)).toBe('755 KB');
        expect(formatBytes(68201033)).toBe('65 MB');
        expect(formatBytes(17_000_000_000)).toBe('16 GB');
        expect(formatBytes(44_000_000_000)).toBe('41 GB');
    });

    it('says nothing rather than something wrong for a missing size', () => {
        expect(formatBytes(-1)).toBe('-');
        expect(formatBytes(NaN)).toBe('-');
    });
});

describe('unmeasured counts', () => {
    it('reports a tray count of zero as unknown, because no world has zero soil fields', () => {
        expect(trayLabel(run({ trays: 0 }))).toBe('?');
        expect(trayLabel(run({ trays: 1 }))).toBe('1');
        expect(trayLabel(run({ trays: 857 }))).toBe('857');
    });

    it('distinguishes a facade run with no obstacle count from a run that genuinely has none', () => {
        //Without a model, zero obstacles is a fact the request states. With one, the request holds
        //a single 'mesh' entry standing for thousands, so a stored zero means "never counted".
        expect(obstacleLabel(run({ obstacles: 0, fieldModelName: null }))).toBe('0');
        expect(obstacleLabel(run({ obstacles: 0, fieldModelName: 'facade.obj' }))).toBe('?');
        expect(obstacleLabel(run({ obstacles: 1622, fieldModelName: 'facade.obj' }))).toBe('1622');
    });
});

describe('run length', () => {
    it('measures what the run reached, in its own step units', () => {
        //lastStep is the step reached plus one, and a step is hoursPerTick hours. A run stopped at
        //step 300 of 540 covers 1200 simulated hours, not the 2160 it asked for.
        expect(reachedHours(run({ lastStep: 300 }))).toBe(1200);
        expect(reachedHours(run())).toBe(2160);
    });

    it('stays in days up to a year, because that is the unit the scenarios are compared in', () => {
        expect(formatSimulatedSpan(12)).toBe('12 h');
        expect(formatSimulatedSpan(744)).toBe('31 d');
        expect(formatSimulatedSpan(2160)).toBe('90 d');
        expect(formatSimulatedSpan(6480)).toBe('270 d');
        expect(formatSimulatedSpan(24 * 500)).toBe('1.4 y');
        expect(formatSimulatedSpan(0)).toBe('-');
    });

    it('reads the wall clock off the run\'s own timestamps', () => {
        expect(formatWallTime(run())).toBe('3 min');
        //A record whose UpdatedUtc was never advanced past its start says so rather than "0 s".
        expect(formatWallTime(run({ updatedUtc: run().createdUtc }))).toBe('-');
    });
});

describe('stored request to editor shape', () => {
    it('lower-cases the first letter of every top-level key', () => {
        expect(camelizeTopLevel({ Name: 'Default', LeafLength: 0.12, IncludeInRndGen: true }))
            .toEqual({ name: 'Default', leafLength: 0.12, includeInRndGen: true });
    });

    it('leaves nested objects alone, because they go back to the server untouched', () => {
        //LeafMorphology is opaque to the frontend: it is loaded, held, and serialized back as-is.
        //Renaming its keys would type-check, round-trip, and quietly break every leaf.
        const result = camelizeTopLevel({ LeafMorphology: { Leaflets: 3, Shape: { Keys: [1, 2] } } });
        expect(result.leafMorphology).toEqual({ Leaflets: 3, Shape: { Keys: [1, 2] } });
    });

    it('passes through what is not an object', () => {
        expect(camelizeTopLevel(null)).toBe(null);
        expect(camelizeTopLevel(undefined)).toBe(undefined);
        expect(camelizeTopLevel(7)).toBe(7);
    });
});
