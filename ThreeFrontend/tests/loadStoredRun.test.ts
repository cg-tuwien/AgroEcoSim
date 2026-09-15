//Opening a stored run, driven through the real appstate against a faked server.
//
//Everything this touches is on the far side of a network call, so nothing else in the suite covers
//it: the trays and the imported obstacles of a past run arrive over REST rather than over the
//socket, and they are the difference between a replayed run and one that looks like the run.

import fs from 'fs';
import path from 'path';

const terrainBytes = fs.readFileSync(path.join(__dirname, '__fixtures__', 'terrain-facade.bin'));

/** One stored frame index entry, as RunStore.WriteFrame writes it: (step, storedBytes). */
function indexBytes(steps: number[]): ArrayBuffer {
    const buf = new ArrayBuffer(steps.length * 8);
    const view = new DataView(buf);
    steps.forEach((s, i) => { view.setUint32(i * 8, s, true); view.setUint32(i * 8 + 4, 64, true); });
    return buf;
}

const RUN = {
    id: 'abc123', status: 'complete',
    createdUtc: '2026-09-13T18:39:17.383Z', updatedUtc: '2026-09-13T18:42:45.143Z',
    hoursPerTick: 24, totalSteps: 3, storedFrames: 3, lastStep: 3,
    totalHours: 72, initialDay: '2026-04-01', seed: 42, renderMode: 0,
    plants: 1, trays: 1, obstacles: 1, species: 0,
    analytics: true, deterministic: true,
    bytesOnDisk: 1000, auxBytesOnDisk: 100,
    fieldModelName: 'mini.obj', hasFieldModel: true, hasTerrain: true, hasObstacleLight: false, hasCheckpoint: true,
    parentRunId: null, branchStep: null,
};

const REQUEST = {
    HoursPerTick: 24, TotalHours: 72, FieldSize: { X: 2, D: 1, Z: 2 }, FieldResolution: 0.5,
    Seed: 42, InitialDate: '2026-04-01T00:00:00', Species: [], RenderMode: 0, Deterministic: true,
    Plants: [{ S: 'Default', P: { X: 0.5, Y: -0.01, Z: 0.5 }, F: 0 }],
    Obstacles: [{ T: 'wall', H: 2, D: 0.2, P: { X: 1, Y: 0, Z: 1 }, O: 0, L: 1 }],
    FieldItemRegex: 'erde', FieldItemRegexMaterial: true, FieldModelPath: 'mini.obj',
};

describe('opening a stored run', () => {
    let appstate: any;
    let asked: string[];

    beforeEach(() => {
        jest.resetModules();
        asked = [];
        (global as any).fetch = jest.fn(async (url: string) => {
            asked.push(url);
            const ok = (body: any) => ({ ok: true, json: async () => body, arrayBuffer: async () => body });
            if (url.endsWith('/terrain')) return ok(terrainBytes.buffer.slice(
                terrainBytes.byteOffset, terrainBytes.byteOffset + terrainBytes.byteLength));
            if (url.endsWith('/request')) return ok(REQUEST);
            if (url.endsWith('/index')) return ok(indexBytes([0, 1, 2]));
            if (url.endsWith('/runs') || url.includes('/runs?')) return ok([RUN]);
            if (url.includes('/simulation/run/')) return ok(RUN);
            //Species and mesh endpoints the module fetches on import.
            return { ok: true, json: async () => [], arrayBuffer: async () => new ArrayBuffer(0) };
        });
        appstate = require('../src/appstate').default;
    });

    /** Re-points the fake server at one run that has no stored terrain. */
    function serve(run: any) {
        (global as any).fetch = jest.fn(async (url: string) => {
            const ok = (body: any) => ({ ok: true, json: async () => body, arrayBuffer: async () => body });
            if (url.endsWith('/request')) return ok({ ...REQUEST, FieldModelPath: run.fieldModelName ?? '' });
            if (url.endsWith('/index')) return ok(indexBytes([0, 1, 2]));
            if (url.includes('/simulation/run/')) return ok(run);
            return { ok: true, json: async () => [], arrayBuffer: async () => new ArrayBuffer(0) };
        });
    }

    it('restores the trays and the imported obstacles', async () => {
        expect(await appstate.loadStoredRun('abc123')).toBe(true);

        //The trays a seed's fieldIndex indexes into.
        expect(appstate.terrainList.length).toBe(1);

        //The facade, merged into one mesh, PLUS the wall the request describes - and the mesh must
        //survive the scenario being applied on top of it, which rebuilds only the primitives.
        const obstacles = appstate.obstacles.value;
        expect(obstacles.filter((o: any) => o.type.value === 'mesh').length).toBe(1);
        expect(obstacles.filter((o: any) => o.type.value === 'wall').length).toBe(1);
    });

    it('marks the terrain dirty so the viewport rebuilds it', async () => {
        //terrainList is a plain array; terrainTimestamp is the only thing that tells the renderer
        //it changed. Without the bump the trays are in appstate and absent from the screen.
        const before = appstate.terrainTimestamp.value;
        await appstate.loadStoredRun('abc123');
        expect(appstate.terrainTimestamp.value).not.toBe(before);
    });

    it('keeps the loaded trays when the run names the same model they came from', async () => {
        //A run stored before terrains were cached, whose model the tab already has open. Clearing
        //would show nothing where the right trays are already on screen, which is the worse of the
        //two wrong answers - and the seeds of that run are positioned against exactly these trays.
        await appstate.loadStoredRun('abc123');
        expect(appstate.terrainList.length).toBe(1);
        expect(appstate.fieldModelPath.value).toBe('mini.obj');

        serve({ ...RUN, id: 'old1', hasTerrain: false, fieldModelName: 'mini.obj' });
        expect(await appstate.loadStoredRun('old1')).toBe(true);
        expect(appstate.terrainList.length).toBe(1);
    });

    it('drops them when the run names a different model, rather than showing the wrong trays', async () => {
        await appstate.loadStoredRun('abc123');
        expect(appstate.terrainList.length).toBe(1);

        serve({ ...RUN, id: 'old2', hasTerrain: false, fieldModelName: 'somethingelse.obj' });
        expect(await appstate.loadStoredRun('old2')).toBe(true);
        expect(appstate.terrainList.length).toBe(0);
        expect(appstate.obstacles.value.filter((o: any) => o.type.value === 'mesh').length).toBe(0);
    });

    it('drops them when the run names no model at all', async () => {
        await appstate.loadStoredRun('abc123');
        serve({ ...RUN, id: 'plain', hasTerrain: false, fieldModelName: null });
        expect(await appstate.loadStoredRun('plain')).toBe(true);
        expect(appstate.terrainList.length).toBe(0);
    });
});
