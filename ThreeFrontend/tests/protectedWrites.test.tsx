import { h } from 'preact';

const fixtureRun = {
    id: 'stored-run', status: 'complete', createdUtc: '2026-11-16T10:00:00Z', updatedUtc: '2026-11-16T11:00:00Z',
    hoursPerTick: 4, totalSteps: 2, storedFrames: 2, lastStep: 2, totalHours: 8,
    initialDay: '2026-04-01', seed: 42, renderMode: 0, plants: 1, trays: 1, obstacles: 0, species: 0,
    analytics: true, deterministic: true, bytesOnDisk: 1000, auxBytesOnDisk: 100,
    fieldModelName: null, hasFieldModel: false, hasTerrain: false, hasObstacleLight: false, hasCheckpoint: true,
    parentRunId: null, branchStep: null, protected: true,
};

let state: any, access: any, hub: any, handlers: Record<string, (...args: any[]) => void>, host: HTMLDivElement;
let render: typeof import('preact').render, act: typeof import('preact/test-utils').act;
const response = (body: any, status = 200) => ({
    ok: status >= 200 && status < 300, status,
    text: async () => typeof body === 'string' ? body : JSON.stringify(body), json: async () => body,
    arrayBuffer: async () => body,
});
const settle = async () => { for (let i = 0; i < 12; ++i) await Promise.resolve(); };

beforeEach(async () => {
    jest.resetModules();
    render = require('preact').render;
    act = require('preact/test-utils').act;
    sessionStorage.clear();
    localStorage.clear();
    handlers = {};
    hub = {
        state: 'Disconnected', on: (name: string, callback: any) => { handlers[name] = callback; },
        onclose: jest.fn(), onreconnected: jest.fn(), onreconnecting: jest.fn(),
        start: jest.fn(async () => { hub.state = 'Connected'; }),
        stop: jest.fn(async () => { hub.state = 'Disconnected'; }), invoke: jest.fn(async () => true),
    };
    jest.doMock('@microsoft/signalr', () => ({
        HubConnectionState: { Connected: 'Connected' },
        HubConnectionBuilder: class {
            withUrl() { return this; } withAutomaticReconnect() { return this; } build() { return hub; }
        },
    }));
    jest.doMock('../src/helpers/RunArchive', () => ({
        saveRunArchive: jest.fn(async prepare => prepare()), readRunArchive: jest.fn(), localArchiveFrame: jest.fn(),
    }));
    global.fetch = jest.fn(async (url: string) => {
        if (url.endsWith('/write-access')) return response('', 204);
        if (url.endsWith('/store?events=500')) return response(null);
        if (url.includes('/runs?')) return response([fixtureRun]);
        return response([]);
    }) as any;
    state = require('../src/appstate').default;
    access = require('../src/helpers/WriteAccess').writeAccess;
    await settle();
    host = document.createElement('div'); document.body.appendChild(host);
});

afterEach(() => {
    act(() => render(null, host)); host.remove();
    jest.dontMock('@microsoft/signalr'); jest.dontMock('../src/helpers/RunArchive');
});

test('locked operations leave the loaded result and server untouched', async () => {
    const history = [{ t: 9, s: new Uint8Array([7]), plain: true }];
    state.history = history; state.runId.value = 'previous';
    state.fieldModelPath.value = 'previous.obj';
    (global.fetch as jest.Mock).mockClear();
    await state.run();
    expect(await state.deleteStoredRun(fixtureRun)).toBe(false);
    expect(await state.setRunProtection(fixtureRun, false)).toBe(false);
    expect(await state.resumeStoredRun(fixtureRun, 24)).toBe(false);
    expect(await state.continueStoredRun(fixtureRun, 24)).toBe(false);
    await state.uploadFieldModel({} as File);
    expect(global.fetch).not.toHaveBeenCalled();
    expect(hub.invoke).not.toHaveBeenCalled();
    expect(state.history).toBe(history);
    expect(state.runId.peek()).toBe('previous');
    expect(state.fieldModelPath.peek()).toBe('previous.obj');
});

test('anonymous viewers open and download stored snapshots of an active run without hub calls', async () => {
    const frame = new Uint8Array([7, 0, 0, 0, 0, 0]);
    const index = new ArrayBuffer(16);
    const view = new DataView(index);
    view.setUint32(4, frame.byteLength, true);
    view.setUint32(8, 1, true); view.setUint32(12, frame.byteLength, true);
    (global.fetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.endsWith('/index')) return response(index);
        if (url.includes('/frame/')) return response(frame.buffer);
        if (url.endsWith('/request')) return response({ HoursPerTick: 4, TotalHours: 8, Plants: [], Species: [] });
        if (url.endsWith('/site')) return response({});
        return response({ ...fixtureRun, status: 'running' });
    });
    expect(await state.loadStoredRun(fixtureRun.id)).toBe(true);
    expect(state.history.map((entry: any) => entry.t)).toEqual([0, 1]);
    expect(state.computing.peek()).toBe(false);
    expect(hub.invoke).not.toHaveBeenCalled();
    expect(hub.start).not.toHaveBeenCalled();
    await state.saveArchive();
    const archive = require('../src/helpers/RunArchive').saveRunArchive;
    const contents = await archive.mock.results[0].value;
    expect(contents.history).toHaveLength(2);
    expect(global.fetch).not.toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ method: expect.any(String) }));
});

test('upload rejection and direct hub rejection preserve the old scene and never launch a run', async () => {
    await access.unlock('test-only-secret');
    const history = [{ t: 9, s: new Uint8Array([7]), plain: true }];
    state.history = history; state.runId.value = 'previous';
    (global.fetch as jest.Mock).mockResolvedValue(response('WRITE_ACCESS_REQUIRED', 401));
    await state.run();
    expect(access.unlocked.peek()).toBe(false);
    expect(state.history).toBe(history);
    expect(state.runId.peek()).toBe('previous');
    expect(hub.invoke).not.toHaveBeenCalledWith('start', expect.anything(), expect.anything());
    (global.fetch as jest.Mock).mockImplementation(async (url: string) => response(url.endsWith('/upload') ? 'prepared-id' : '', 200));
    await access.unlock('test-only-secret');
    hub.invoke.mockRejectedValueOnce(new Error('WRITE_ACCESS_REQUIRED: Unlock writes'));
    await state.run();
    expect(access.unlocked.peek()).toBe(false);
    expect(state.history).toBe(history);
    expect(state.runId.peek()).toBe('previous');
});

test.each([
    ['continue', 'http'], ['continue', 'hub'], ['resume', 'http'], ['resume', 'hub'],
])('%s preserves the previous view after %s authorization rejection', async (operation, rejection) => {
    await access.unlock('test-only-secret');
    const history = [{ t: 9, s: new Uint8Array([7]), plain: true }];
    const scene = [{ species: 'previous', primitives: [] }];
    const terrains = [{}];
    state.history = history; state.scene.value = scene; state.terrainList = terrains;
    state.runId.value = 'previous'; state.viewingRunId.value = 'previous';
    state.fieldModelPath.value = 'previous.obj'; state.hoursPerTick.value = 12;
    state.totalHours.value = 120; sessionStorage.setItem('agro.runId', 'previous');
    (global.fetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.endsWith('/upload')) return response(rejection === 'http' ? 'WRITE_ACCESS_REQUIRED' : 'prepared-id', rejection === 'http' ? 401 : 200);
        if (url.endsWith('/request')) return response({ HoursPerTick: 4, TotalHours: 8, Plants: [], Species: [] });
        if (url.endsWith('/index')) return response(new ArrayBuffer(0));
        if (url.endsWith('/site')) return response({});
        return response(fixtureRun);
    });
    if (rejection === 'hub') hub.invoke.mockRejectedValueOnce(new Error('WRITE_ACCESS_REQUIRED: Unlock writes'));
    const accepted = operation === 'resume'
        ? await state.resumeStoredRun(fixtureRun, 24) : await state.continueStoredRun(fixtureRun, 24);
    expect(accepted).toBe(false);
    expect(access.unlocked.peek()).toBe(false);
    expect(state.history).toBe(history);
    expect(state.scene.peek()).toBe(scene);
    expect(state.terrainList).toBe(terrains);
    expect(state.runId.peek()).toBe('previous');
    expect(state.viewingRunId.peek()).toBe('previous');
    expect(state.fieldModelPath.peek()).toBe('previous.obj');
    expect(state.hoursPerTick.peek()).toBe(12);
    expect(state.totalHours.peek()).toBe(120);
    expect(sessionStorage.getItem('agro.runId')).toBe('previous');
    expect(hub.invoke).not.toHaveBeenCalledWith('preview', expect.anything());
});

test.each(['continue', 'resume'])('%s commits its staged scene at acceptance before requesting previews', async operation => {
    await access.unlock('test-only-secret');
    const history = [{ t: 9, s: new Uint8Array([7]), plain: true }];
    const previousScene = [{ species: 'previous', primitives: [] }];
    state.history = history; state.scene.value = previousScene; state.runId.value = 'previous';
    state.fieldModelPath.value = 'previous.obj'; state.hoursPerTick.value = 12;
    const index = new ArrayBuffer(16);
    const view = new DataView(index); view.setUint32(8, 1, true);
    const request = '{"Seed":18446744073709551615,"HoursPerTick":4,"TotalHours":8,"Plants":[],"Species":[]}';
    (global.fetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.endsWith('/request')) return response(request);
        if (url.endsWith('/index')) return response(index);
        if (url.endsWith('/site')) return response({ timeZone: 'UTC' });
        if (url.endsWith('/upload')) return response('prepared-id');
        return response({ ...fixtureRun, id: operation === 'resume' ? fixtureRun.id : 'continued-run' });
    });
    hub.invoke.mockImplementation(async (method: string) => {
        if (method === 'start') {
            expect(state.history).toBe(history);
            expect(state.scene.peek()).toBe(previousScene);
            expect(state.runId.peek()).toBe('previous');
            handlers.runStarted(operation === 'resume' ? fixtureRun.id : 'continued-run');
            expect(state.history.map((entry: any) => entry.t)).toEqual(operation === 'resume' ? [0, 1] : []);
            expect(state.hoursPerTick.peek()).toBe(4);
            expect(state.totalHours.peek()).toBe(32);
            expect(state.fieldModelPath.peek()).toBe('');
        }
        if (method === 'preview') {
            expect(state.runId.peek()).toBe(operation === 'resume' ? fixtureRun.id : 'continued-run');
            expect(state.siteTimeZone.peek()).toBe('UTC');
        }
        return true;
    });
    const accepted = operation === 'resume'
        ? await state.resumeStoredRun(fixtureRun, 24) : await state.continueStoredRun(fixtureRun, 24);
    expect(accepted).toBe(true);
    expect(state.history).not.toBe(history);
    expect(hub.invoke).toHaveBeenCalledWith('preview', 'test-only-secret');
    const upload = (global.fetch as jest.Mock).mock.calls.find(([url]) => url.endsWith('/upload'));
    const submitted = JSON.parse(upload[1].body);
    if (operation === 'resume') expect(submitted.ResumeRunId).toBe(fixtureRun.id);
    else {
        expect(submitted.Seed).toBe('18446744073709551615');
        expect(submitted.ParentRunId).toBe(fixtureRun.id);
        expect(submitted.ResumeRunId).toBeNull();
    }
});

test('all live calls carry the password; locking detaches without Abort and preserves history', async () => {
    await access.unlock('test-only-secret');
    (global.fetch as jest.Mock).mockImplementation(async (url: string) => response(url.endsWith('/upload') ? 'prepared-id' : fixtureRun));
    hub.invoke.mockImplementation(async (method: string) => {
        if (method === 'start') handlers.runStarted('new-run');
        return true;
    });
    await state.run();
    expect(hub.invoke).toHaveBeenCalledWith('start', 'prepared-id', 'test-only-secret');
    expect(hub.invoke).toHaveBeenCalledWith('preview', 'test-only-secret');
    state.history = [{ t: 0, s: new Uint8Array([7]), plain: true }];
    const history = state.history;
    hub.invoke.mockClear();
    access.lock();
    expect(hub.stop).toHaveBeenCalled();
    expect(hub.invoke).not.toHaveBeenCalled();
    expect(state.computing.peek()).toBe(false);
    expect(state.previewRequest.peek()).toBe(false);
    expect(state.history).toBe(history);
    expect(state.runId.peek()).toBe('new-run');
    hub.onclose.mock.calls[0][0]();
    expect(hub.start).toHaveBeenCalledTimes(1);
});

test('protection is editable during a run and protected deletion still needs two clicks', async () => {
    const { RunsList } = require('../src/components/hud/RunsList');
    state.runs.value = [fixtureRun];
    act(() => render(<RunsList />, host));
    const button = (text: string) => Array.from(host.querySelectorAll('button')).find(b => b.textContent === text)!;
    expect(button('Unprotect').disabled).toBe(true);
    expect(button('🗑').disabled).toBe(true);
    expect(button('View').disabled).toBe(false);
    await act(async () => { await access.unlock('test-only-secret'); });
    state.runs.value = [{ ...fixtureRun, status: 'running' }];
    await act(async () => { await settle(); });
    expect(button('Unprotect').disabled).toBe(false);
    expect(button('🗑').disabled).toBe(true);
    (global.fetch as jest.Mock).mockImplementation(async (url: string, init?: RequestInit) => {
        if (url.endsWith('/protection')) return response({ ...fixtureRun, status: 'running', protected: false });
        return response([]);
    });
    await act(async () => { button('Unprotect').click(); await settle(); });
    expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining('/protection'), expect.objectContaining({
        method: 'PUT', body: '{"protected":false}', headers: expect.objectContaining({ 'X-Agro-Write-Password': 'test-only-secret' }),
    }));
    expect(host.textContent).toContain('Unprotected');
    await act(async () => { state.runs.value = [fixtureRun]; });
    (global.fetch as jest.Mock).mockClear();
    act(() => button('🗑').click());
    expect(host.textContent).toContain('Delete protected');
    expect(global.fetch).not.toHaveBeenCalled();
    const confirmation = Array.from(host.querySelectorAll('button')).find(b => b.textContent?.startsWith('Delete protected'))!;
    (global.fetch as jest.Mock).mockResolvedValue(response({ deleted: false, reason: 'test stop' }));
    await act(async () => { confirmation.click(); await settle(); });
    expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining('/run/stored-run'), expect.objectContaining({ method: 'DELETE' }));
});

test('unlock control clears its masked input and credentials never enter archive metadata', async () => {
    const { WriteAccess } = require('../src/components/hud/WriteAccess');
    act(() => render(<WriteAccess />, host));
    const field = host.querySelector<HTMLInputElement>('input')!;
    expect(field.type).toBe('password');
    await act(async () => {
        field.value = 'test-only-secret'; field.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
        host.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); await settle();
    });
    expect(host.textContent).toContain('Writes unlocked');
    expect(host.querySelector('input')).toBeNull();
    await state.saveArchive();
    const archive = require('../src/helpers/RunArchive').saveRunArchive;
    const contents = await archive.mock.results[0].value;
    expect(JSON.stringify(contents.metadata)).not.toContain('test-only-secret');
    expect(JSON.stringify(state)).not.toContain('test-only-secret');
    act(() => host.querySelector<HTMLButtonElement>('button')!.click());
    expect(host.querySelector<HTMLInputElement>('input')!.value).toBe('');
});
