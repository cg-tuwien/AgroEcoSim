import { Fragment, h, render } from "preact";
import { act } from "preact/test-utils";
import appstate from "../src/appstate";
import { Seed } from "../src/helpers/Seed";
import { Species } from "../src/helpers/Species";
import { Seeds } from "../src/components/hud/Seeds";
import { Start } from "../src/components/hud/Start";
import { ProgressBar } from "../src/components/hud/ProgressBar";
import { saveRunArchive } from "../src/helpers/RunArchive";
import { writeAccess } from '../src/helpers/WriteAccess';

jest.mock("@microsoft/signalr", () => {
    const handlers: Record<string, (...args: any[]) => void> = {};
    const connection = {
        state: "Connected", on: (name: string, handler: (...args: any[]) => void) => handlers[name] = handler,
        start: jest.fn(async () => {}), invoke: jest.fn(async () => {}),
        onclose: jest.fn(), onreconnecting: jest.fn(), onreconnected: jest.fn(),
    };
    return { __handlers: handlers, HubConnectionState: { Connected: "Connected" },
        HubConnectionBuilder: class {
            withUrl() { return this; }
            withAutomaticReconnect() { return this; }
            build() { return connection; }
        } };
});
jest.mock("../src/helpers/RunArchive", () => ({
    saveRunArchive: jest.fn(async prepare => await prepare()),
    readRunArchive: jest.fn(async () => undefined), localArchiveFrame: jest.fn(),
}));

let host: HTMLDivElement;
const rawOffsets = () => (appstate as any).requestBody().Plants.map((p: any) => p.StartOffsetHours);
const seed = (hours = 0) => new Seed(appstate.species.peek()[0].name.peek(), 0.5, -0.01, 0.5, 0, false, hours);
const ageInput = () => host.querySelector<HTMLInputElement>('[name="seedage-0"]')!;
const unitInput = () => host.querySelector<HTMLSelectElement>('[aria-label="Age unit for plant 0"]')!;
const simulate = () => host.querySelector<HTMLButtonElement>('[title="Simulate"], [title^="Enter a finite Age"]')!;

beforeEach(async () => {
    jest.clearAllMocks();
    appstate.terrainList = [];
    const species = new Species();
    species.name.value = "Fixture";
    appstate.species.value = [species];
    appstate.seeds.value = [seed()];
    appstate.hoursPerTick.value = 4;
    appstate.initialDate.value = "2026-03-01";
    appstate.siteTimeZone.value = "Europe/Vienna";
    appstate.history = [];
    appstate.historyCount.value = 0;
    appstate.playPointer.value = 0;
    appstate.computing.value = false;
    appstate.virtualRoots.value = false;
    appstate.preparation.value = undefined;
    appstate.simStep.value = 0;
    appstate.simLength.value = 0;
    appstate.runId.value = "";
    (global.fetch as jest.Mock).mockResolvedValueOnce({ ok: true, status: 204 });
    await writeAccess.unlock('test-only-secret');
    host = document.createElement("div");
    document.body.appendChild(host);
});

afterEach(() => {
    if (!host) return;
    act(() => render(null, host));
    host.remove();
});

test("unit switches leave exact signed hours untouched and show tick-adjusted seed dates", () => {
    appstate.seeds.value = [seed(-25.125)];
    act(() => render(<Seeds />, host));
    expect(ageInput().value).toBe(String(-25.125 / 24));
    expect(host.textContent).toContain("Effective: -1 days (-24 h). Seed date: 2026-02-28 00:00");
    for (const unit of ["years", "days", "years", "days"]) {
        act(() => { unitInput().value = unit; unitInput().dispatchEvent(new Event("change", { bubbles: true })); });
        expect(appstate.seeds.value[0].startOffsetHours.value).toBe(-25.125);
    }
    expect(rawOffsets()).toEqual([-25.125]);
    act(() => { ageInput().value = "2.5"; ageInput().dispatchEvent(new Event("input", { bubbles: true })); });
    expect(rawOffsets()).toEqual([60]);
    expect(host.textContent).toContain("Seed date: 2026-03-03 12:00");
    expect(host.textContent).not.toContain("Effective:");
});

test("invalid ages disable submission without clearing existing results; future ages stay editable", async () => {
    appstate.history = [{ t: 9, s: new Uint8Array([1]), plain: true }];
    act(() => render(<><Seeds /><Start inclStats={false} /></>, host));
    act(() => { ageInput().value = ""; ageInput().dispatchEvent(new Event("input", { bubbles: true })); });
    expect(simulate().disabled).toBe(true);
    expect(host.querySelector('[role="alert"]')?.textContent).toBe("Enter a finite Age.");
    expect(() => (appstate as any).requestBody()).toThrow("finite");
    await appstate.run();
    expect(appstate.history[0].t).toBe(9);
    expect(appstate.computing.value).toBe(false);
    act(() => { ageInput().value = "3"; ageInput().dispatchEvent(new Event("input", { bubbles: true })); });
    expect(simulate().disabled).toBe(false);
    expect(ageInput().disabled).toBe(false);
    expect(rawOffsets()).toEqual([72]);
    // Stop remains available even while another seed is being edited to an invalid value.
    act(() => { appstate.computing.value = true; appstate.seeds.value[0].startOffsetHours.value = NaN; });
    expect(simulate().disabled).toBe(false);
});

test("session saving and loading preserve raw offsets and units; old sessions mean zero", async () => {
    appstate.seeds.value = [-8760.125, 0, 25.25].map(seed);
    appstate.seeds.value[0].ageUnit.value = "years";
    await (appstate as any).saveArchive();
    const contents = await (saveRunArchive as jest.Mock).mock.results[0].value;
    const saved = JSON.parse(JSON.stringify(contents.metadata));
    const loadSession = async (metadata: any) => {
        await appstate.load();
        const picker = document.querySelector<HTMLInputElement>('input[type="file"]')!;
        Object.defineProperty(picker, "files", { value: [{ text: async () => JSON.stringify(metadata) }] });
        await picker.onchange!({ target: picker } as any);
    };
    appstate.seeds.value = [];
    await loadSession(saved);
    expect(rawOffsets()).toEqual([-8760.125, 0, 25.25]);
    expect(appstate.seeds.value[0].ageUnit.value).toBe("years");
    saved.seeds.forEach((p: any) => { delete p.startOffsetHours; delete p.ageUnit; });
    await loadSession(saved);
    expect(rawOffsets()).toEqual([0, 0, 0]);
    expect(appstate.seeds.value[0].ageUnit.value).toBe("days");
});

test("stored request reopen and resubmit preserve negative and future offsets", async () => {
    const plants = [-8760.125, 0, 25.25].map(hours => seed(hours).request());
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, text: async () => JSON.stringify({ Plants: plants }) });
    await appstate.restoreStoredScenario("age-run");
    expect(rawOffsets()).toEqual([-8760.125, 0, 25.25]);
    plants.forEach((p: any) => { delete p.StartOffsetHours; });
    await appstate.restoreStoredScenario("legacy-run");
    expect(rawOffsets()).toEqual([0, 0, 0]);
});

test("tray distribution imports retain signed hours through coordinate conversion", async () => {
    appstate.terrainList = [{ id: "tray", posx: () => 0, posy: () => 0, posz: () => 0 } as any];
    const name = appstate.species.value[0].name.peek();
    await appstate.uploadSeedsDistribution({ text: async () => JSON.stringify([{ sectionId: "tray", plants: [
        { species: name, x: 1, y: 2, z: 3, startOffsetHours: -25.125 },
        { species: name, x: 2, y: 3, z: 4, StartOffsetHours: 60 },
        { species: name, x: 3, y: 4, z: 5 },
    ] }]) } as File);
    expect(rawOffsets()).toEqual([-25.125, 60, 0]);
    expect(appstate.seeds.value[0].py.value).toBe(3);
    expect(appstate.seeds.value[0].pz.value).toBe(-2);
});

test("virtual root mode survives stored-run and session roundtrips, with legacy explicit roots", async () => {
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, text: async () => JSON.stringify({ VirtualRoots: true }) });
    await appstate.restoreStoredScenario("virtual-roots-run");
    expect((appstate as any).requestBody().VirtualRoots).toBe(true);
    await (appstate as any).saveArchive();
    const contents = await (saveRunArchive as jest.Mock).mock.results[0].value;
    const saved = JSON.parse(JSON.stringify(contents.metadata));
    expect(saved.virtualRoots).toBe(true);
    const load = async () => {
        await appstate.load();
        const picker = document.querySelector<HTMLInputElement>('input[type="file"]')!;
        Object.defineProperty(picker, "files", { value: [{ text: async () => JSON.stringify(saved) }] });
        await picker.onchange!({ target: picker } as any);
    };
    appstate.virtualRoots.value = false;
    await load();
    expect((appstate as any).requestBody().VirtualRoots).toBe(true);
    delete saved.virtualRoots;
    await load();
    expect((appstate as any).requestBody().VirtualRoots).toBe(false);
    appstate.virtualRoots.value = true;
    (global.fetch as jest.Mock).mockResolvedValue({ ok: true, text: async () => "{}" });
    await appstate.restoreStoredScenario("legacy-roots-run");
    expect((appstate as any).requestBody().VirtualRoots).toBe(false);
});

test("preparation progress is separate from recorded time and clears on completion and rejection", () => {
    const handlers = require("@microsoft/signalr").__handlers;
    act(() => render(<ProgressBar />, host));
    act(() => handlers.preparation(3, 12));
    expect(host.textContent).toContain("Preparing plants: 3 / 12 (25.00%)");
    expect(appstate.simStep.value).toBe(0);
    expect(appstate.playPointer.value).toBe(0);
    act(() => handlers.preparation(12, 12));
    expect(host.textContent).not.toContain("Preparing plants");
    act(() => { handlers.preparation(1, 12); handlers.rejected(); });
    expect(appstate.preparation.value).toBeUndefined();
});

test("an aborted preparation finishes the UI without creating a frame at minus one", () => {
    const handlers = require("@microsoft/signalr").__handlers;
    appstate.computing.value = true;
    appstate.scene.value = [{ species: "previous run", primitives: [] }];
    handlers.preparation(3, 12);
    expect(() => handlers.result({ plants: [], scene: null, renderer: "", stepTimes: [],
        completedSteps: 0, stopReason: "stopped during preparation" })).not.toThrow();
    expect(appstate.history).toHaveLength(0);
    expect(appstate.historyCount.value).toBe(0);
    expect(appstate.scene.value).toEqual([]);
    expect(appstate.computing.value).toBe(false);
    expect(appstate.preparation.value).toBeUndefined();
    expect(appstate.runStopReason.value).toBe("stopped during preparation");
});

test("a resumed result uses its absolute completed count, with the legacy fallback retained", () => {
    const handlers = require("@microsoft/signalr").__handlers;
    const emptyScene = Buffer.from([7, 0, 0, 0, 0, 0]).toString("base64");
    handlers.result({ plants: [], scene: emptyScene, renderer: "", stepTimes: [1, 1], completedSteps: 12 });
    expect(appstate.history.map(frame => frame.t)).toEqual([11]);
    appstate.history = [];
    handlers.result({ plants: [], scene: emptyScene, renderer: "", stepTimes: [1, 1] });
    expect(appstate.history.map(frame => frame.t)).toEqual([1]);
});
