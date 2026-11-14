//Single-frame exports must pin the requested recorded step across all playback sources.
//These fixtures are independent of the large, developer-local session used by older tests.
import type { PlantModel } from '../src/helpers/Scene';

class Writer {
    bytes: number[] = [];
    u8(value: number) { this.bytes.push(value & 0xff); return this; }
    u32(value: number) {
        const bytes = new Uint8Array(4);
        new DataView(bytes.buffer).setUint32(0, value, true);
        this.bytes.push(...bytes); return this;
    }
    f32(value: number) {
        const bytes = new Uint8Array(4);
        new DataView(bytes.buffer).setFloat32(0, value, true);
        this.bytes.push(...bytes); return this;
    }
    str(value: string) {
        const bytes = new TextEncoder().encode(value);
        this.u8(bytes.length); this.bytes.push(...bytes); return this;
    }
    done() { return new Uint8Array(this.bytes); }
}

function frame(x: number, version = 7): Uint8Array {
    const w = new Writer().u8(version).u8(0), id = 'c'.repeat(64);
    if (version >= 8) {
        const asset = new Writer().u8(0).u8(1).u32(1);
        for (let mesh = 0; mesh < 2; ++mesh) {
            asset.u32(3);
            for (const value of [-1, 0, 0, 1, 0, 0, 0, 1, 0]) asset.f32(value);
            asset.u32(3).u32(0).u32(1).u32(2).f32(0).f32(0).f32(0);
        }
        w.u32(1).str(id).u32(asset.bytes.length);
        w.bytes.push(...asset.bytes);
    }
    w.u32(1).str('export fixture');
    if (version >= 8) w.str(id);
    if (version >= 9) w.u32(5);
    w.u32(1);
    w.u32(0xffffffff).u8(5); //one leaf; no roots or analytics
    if (version >= 8) w.u8(0);
    for (const value of [1, 0, 0, 0, 1, 0, 0, 0, 1, x, 2, 3]) w.f32(value);
    return w.u8(0x22).u8(0xaa).u8(0x33).done();
}

function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>(done => { resolve = done; });
    return { promise, resolve };
}

function position(scene: PlantModel[]): number {
    const primitive = scene[0].primitives[0];
    return 'affineTransform' in primitive ? primitive.affineTransform[9] : NaN;
}

function indexBytes(steps: number[]): ArrayBuffer {
    const bytes = new ArrayBuffer(steps.length * 8), view = new DataView(bytes);
    steps.forEach((step, index) => {
        view.setUint32(index * 8, step, true);
        view.setUint32(index * 8 + 4, 64, true);
    });
    return bytes;
}

describe('captured frame export source', () => {
    let mod: typeof import('../src/appstate');
    let state: typeof mod.default;
    let handlers: Map<string, (...args: any[]) => void>;

    beforeEach(async () => {
        jest.resetModules();
        sessionStorage.clear();
        handlers = new Map();
        jest.doMock('@microsoft/signalr', () => ({
            HubConnectionState: { Connected: 'Connected' },
            HubConnectionBuilder: class {
                withUrl() { return this; }
                withAutomaticReconnect() { return this; }
                build() {
                    return {
                        state: 'Disconnected',
                        on: (name: string, callback: (...args: any[]) => void) => handlers.set(name, callback),
                        onclose() {}, onreconnecting() {}, onreconnected() {},
                        start: async () => {}, invoke: async () => true,
                    };
                }
            },
        }));
        global.fetch = jest.fn(async () => ({
            ok: true, json: async () => [], text: async () => '',
            arrayBuffer: async () => new ArrayBuffer(0),
        })) as any;
        mod = require('../src/appstate');
        state = mod.default;
        await Promise.resolve();
        await Promise.resolve();
        state.history = [
            { t: 3, s: frame(3), plain: true },
            { t: 41, s: frame(41), plain: true },
            { t: 88, s: frame(88), plain: true },
        ];
        state.simHoursPerTick = 4;
        state.playPointer.value = 0;
        mod.clearSceneCache();
        (global.fetch as jest.Mock).mockClear();
    });

    afterEach(() => { jest.dontMock('@microsoft/signalr'); });

    test('pins a selected nonfinal step and timestep without moving the playhead', async () => {
        const source = mod.captureFrameSource(1);
        state.simHoursPerTick = 12;
        expect(source.step).toBe(41);
        expect(source.hoursPerTick).toBe(4);
        expect(source.runId).toBe('');
        expect(position(await source.read())).toBe(41);
        expect(state.playPointer.peek()).toBe(0);
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('reading stays on the captured step when playback advances', async () => {
        const source = mod.captureFrameSource(1);
        state.playPointer.value = 2;
        expect(position(await source.read())).toBe(41);
        expect(state.playPointer.peek()).toBe(2);
    });

    test('reuses an already decoded playback frame and its capabilities', async () => {
        const cached = mod.sceneAtSync(1);
        expect(await mod.captureFrameSource(1).read()).toBe(cached);
        const { frameCapabilities } = require('../src/helpers/Scene');
        expect(frameCapabilities(cached).analyticsAvailable).toBe(false);
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('inflates a compressed held frame and caches exactly that decoded scene', async () => {
        const { deflateBytes } = require('../src/helpers/deflate');
        state.history[1] = { t: 41, s: await deflateBytes(frame(41)), plain: false };
        const source = mod.captureFrameSource(1), scene = await source.read();
        expect(position(scene)).toBe(41);
        expect(await source.read()).toBe(scene);
        expect(mod.sceneAtSync(1)).toBe(scene);
        expect(state.playPointer.peek()).toBe(0);
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('loads an evicted backend frame by recorded step, then reuses the decoded cache', async () => {
        state.runId.value = 'run-a';
        state.history[1] = { t: 41, s: undefined, plain: true };
        global.fetch = jest.fn(async () => ({ ok: true, arrayBuffer: async () => frame(41).buffer })) as any;
        const source = mod.captureFrameSource(1), scene = await source.read();
        expect(source.runId).toBe('run-a');
        expect(position(scene)).toBe(41);
        expect(await source.read()).toBe(scene);
        expect(global.fetch).toHaveBeenCalledTimes(1);
        expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining('/run/run-a/frame/41?format=2'));
        expect(state.playPointer.peek()).toBe(0);
    });

    test.each(['held', 'compressed', 'backend'])('exports a cold V9 %s frame with its stable request identity', async storage => {
        const bytes = frame(41, 9);
        if (storage === 'backend') {
            state.runId.value = 'run-v9';
            state.history[1] = { t: 41, s: undefined, plain: true };
            global.fetch = jest.fn(async () => ({ ok: true, arrayBuffer: async () => bytes.buffer })) as any;
        } else {
            const { deflateBytes } = require('../src/helpers/deflate');
            state.history[1] = { t: 41, s: storage === 'compressed' ? await deflateBytes(bytes) : bytes, plain: storage !== 'compressed' };
        }
        state.playPointer.value = 1;
        mod.clearSceneCache();
        const { exportCurrentFrame } = require('../src/render/export/CurrentFrameExport');
        const exported = await exportCurrentFrame({ roots: false, soilAndTrays: false, obstacles: false, metadata: true });
        const doc = JSON.parse(exported.json);
        expect(exported.step).toBe(41);
        expect(doc.scenes[0].extras.greenFdt.sourceFormatVersion).toBe(9);
        expect(doc.nodes.find((n: any) => n.name === 'plant:5 export fixture').extras.greenFdt)
            .toMatchObject({ plantIndex: 0, sourcePlantIndex: 5 });
        expect(position(await mod.captureFrameSource(1).read())).toBe(41);
        expect(state.playPointer.peek()).toBe(1);
        expect(global.fetch).toHaveBeenCalledTimes(storage === 'backend' ? 1 : 0);
    });

    test('reads a lazy local archive entry through the actual archive registration', async () => {
        const { writeRunArchive, readRunArchive } = require('../src/helpers/RunArchive');
        const chunks: Uint8Array[] = [];
        const entries = [{ t: 3, s: frame(3), plain: true }, { t: 41, s: frame(41), plain: true }];
        await writeRunArchive({}, entries, async (entry: typeof entries[number]) => ({ bytes: entry.s, plain: true }), {
            write: async (bytes: Uint8Array) => { chunks.push(bytes.slice()); }, close: async () => {},
        });
        //Node's Blob implements slice/arrayBuffer/text; Jest 27's jsdom Blob does not.
        const { Blob: NodeBlob } = require('node:buffer');
        const archive = await readRunArchive(new NodeBlob(chunks));
        expect(archive.history[1].s).toBeUndefined();
        state.history = archive.history;
        const source = mod.captureFrameSource(1), scene = await source.read();
        expect(source.step).toBe(41);
        expect(position(scene)).toBe(41);
        expect(await source.read()).toBe(scene);
        expect(state.history[1].s).toBeUndefined();
        expect(state.playPointer.peek()).toBe(0);
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('refuses capture when no recorded frame exists', () => {
        state.history = [];
        expect(() => mod.captureFrameSource(0)).toThrow('There is no recorded frame to export.');
    });

    test('reports a missing held frame without substituting another recorded frame', async () => {
        state.history[1] = { t: 41, s: undefined, plain: true };
        await expect(mod.captureFrameSource(1).read()).rejects.toThrow('Recorded frame 41 is unavailable');
        expect(state.playPointer.peek()).toBe(0);
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('reports a backend frame that is no longer available', async () => {
        state.runId.value = 'run-a';
        state.history[1] = { t: 41, s: undefined, plain: true };
        global.fetch = jest.fn(async () => ({ ok: false, status: 404 })) as any;
        await expect(mod.captureFrameSource(1).read()).rejects.toThrow('Recorded frame 41 is unavailable');
        expect(state.playPointer.peek()).toBe(0);
    });

    test.each([new Uint8Array(0), new Uint8Array([6, 0, 0, 0, 0, 0])])('rejects unavailable or unsupported bytes', async bytes => {
        state.history[1] = { t: 41, s: bytes, plain: true };
        await expect(mod.captureFrameSource(1).read()).rejects.toThrow('Recorded frame 41 is unavailable or has an unsupported format.');
    });

    test.each(['run', 'entry'])('rejects when the %s changes during a backend read', async changed => {
        state.runId.value = 'run-a';
        state.history[1] = { t: 41, s: undefined, plain: true };
        const bytes = deferred<ArrayBuffer>();
        global.fetch = jest.fn(async () => ({ ok: true, arrayBuffer: () => bytes.promise })) as any;
        const source = mod.captureFrameSource(1), reading = source.read();
        if (changed === 'run') state.runId.value = 'run-b';
        else state.history[1] = { t: 41, s: frame(999), plain: true };
        bytes.resolve(frame(41).buffer);
        await expect(reading).rejects.toThrow('The selected run changed while loading the frame. Please export again.');
        expect(() => source.verify()).toThrow('The selected run changed');
        expect(state.playPointer.peek()).toBe(0);
    });

    test('rejects before using a cached frame when its run has changed', async () => {
        mod.sceneAtSync(1);
        const source = mod.captureFrameSource(1);
        state.runId.value = 'other-run';
        await expect(source.read()).rejects.toThrow('The selected run changed');
        expect(global.fetch).not.toHaveBeenCalled();
    });

    test('rejects a changed backend revision even when the selected entry is retained', async () => {
        state.runId.value = 'run-a';
        const entry = { t: 41, s: undefined, plain: true };
        state.history[1] = entry;
        const bytes = deferred<ArrayBuffer>(), indexRead = deferred<void>();
        global.fetch = jest.fn(async (url: string) => url.endsWith('/index') ? {
            ok: true, arrayBuffer: async () => indexBytes([3, 41, 88]),
            headers: { get: (name: string) => {
                if (name === 'X-Agro-Frame-Revision') return '1';
                //A restart after this frame preserves its entry identity, isolating revision checks.
                indexRead.resolve(); return '999';
            } },
        } : { ok: true, arrayBuffer: () => bytes.promise }) as any;
        const source = mod.captureFrameSource(1), reading = source.read();
        handlers.get('progress')!(100, 101);
        await indexRead.promise;
        expect(state.history[1]).toBe(entry);
        expect(() => source.verify()).toThrow('The selected run changed');
        bytes.resolve(frame(41).buffer);
        await expect(reading).rejects.toThrow('The selected run changed');
        expect(state.playPointer.peek()).toBe(0);
    });
});
