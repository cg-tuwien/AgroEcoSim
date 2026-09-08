import { batch, computed, effect, signal } from "@preact/signals";
import { BackendURI } from "./config";
import BinaryReader from "./helpers/BinaryReader";
import { PlantModel } from "./helpers/Scene";
import * as SignalR from "@microsoft/signalr";
import { Seed } from "./helpers/Seed";
import { Obstacle } from "./helpers/Obstacle";
import { BaseRequestObject } from "./helpers/BaseRequestObject";
import { VisualMappingOptions } from "./render/core/PlantColors";
import { activeRenderer } from "./render/IViewportRenderer";
import { vec2, addScaled2 } from "./render/core/Vec3";
import { downloadBinary, downloadBlob, downloadText } from "./helpers/download";
import { canDeflate, deflateBytes, inflateBytes } from "./helpers/deflate";
import { DEFAULT_INITIAL_DATE } from "./helpers/Sun";
import { convertCoordsTo_RH_rightPX_upPY } from "./helpers/CoordinateSystem";
import { LIGHTING_OFF, LightingOptions, SHADOW_NORMAL_OFFSET_TEXELS, lightingFromLegacyTier } from "./render/core/Lighting";
import { Species } from "./helpers/Species";
import { IObjImport, Parse } from "./helpers/ObjParser";
import { BoxTerrainItem, ITerrainItem, MeshTerrainItem } from "./helpers/Terrain";
import { CoordinateSystem } from "./helpers/CoordinateSystem";

interface RetryContext {
    readonly previousRetryCount: number; //The number of consecutive failed tries so far.
    readonly elapsedMilliseconds: number; // The amount of time in milliseconds spent retrying so far.
    readonly retryReason: Error; // The error that forced the upcoming retry.
}

const InfiniteHubRetry = {
    nextRetryDelayInMilliseconds(retryContext: RetryContext): number | null {
        switch (retryContext.previousRetryCount)
        {
            case 0: case 1: return 200;
            case 2: case 3: return 500;
            case 4: case 5: case 6: case 7: return 1000;
            case 8: case 9: case 10: case 11: return 2000;
            default: return 5000;
        }
    }
}

const hubConnection = new SignalR.HubConnectionBuilder().withUrl(`${location.protocol}//${BackendURI}/SimSocket`).withAutomaticReconnect(InfiniteHubRetry).build();
//"rejected", not "reject". The server method is Rejected() and the SignalR JS client lower-cases
//handler names, so the old registration never matched and a refused run told the user nothing at
//all - the Simulate button simply did nothing.
hubConnection.on("rejected", () => {
    console.warn("A simulation is already running on this connection. Wait for it to finish, or stop it first.");
    st.computing.value = false;
});
hubConnection.on("progress", (step: number, length: number) => {
    batch(() => {
        st.simStep.value = step + 1;
        st.simLength.value = length;
    });
    //The server is usually well ahead of what this tab holds. It exports and stores every step
    //while the wire sends a frame only when the browser asks for one, and at several thousand
    //plants the browser asks rarely - so the slider used to span the handful of frames that had
    //arrived while the counter below it said 550/1080. Same run, two numbers, and the honest one
    //was the small print.
    maybeSyncTimeline(step + 1);
});

/**
 * Keeps the timeline in step with the run WHILE it computes.
 *
 * The index is eight bytes per frame, so catching up on a 1,080-step run costs about 8 KB - and
 * what it buys is a slider that spans everything computed so far, with each payload fetched only
 * if the user actually scrubs onto it. That is the whole "keep `t`, stream `s`" idea applied to a
 * live run rather than only to a finished one.
 *
 * Throttled on both sides: never more than one request in flight, never more often than every
 * couple of seconds, and only when the run is actually ahead of what is held. `Progress` arrives
 * once per simulated step, which at a large stand is far too often to fetch anything.
 */
let timelineSyncing = false;
let lastTimelineSync = 0;
const TIMELINE_SYNC_INTERVAL_MS = 2000;
const TIMELINE_SYNC_LAG_FRAMES = 4;

function maybeSyncTimeline(serverSteps: number) {
    const runId = st.runId.peek();
    if (!runId || timelineSyncing) return;
    if (serverSteps - st.history.length < TIMELINE_SYNC_LAG_FRAMES) return;
    const now = Date.now();
    if (now - lastTimelineSync < TIMELINE_SYNC_INTERVAL_MS) return;
    lastTimelineSync = now;
    timelineSyncing = true;
    //`false`, so a user who has scrubbed back to look at something is not yanked to the newest
    //frame every two seconds.
    void syncTimelineToIndex(runId, false).finally(() => { timelineSyncing = false; });
}

hubConnection.on("result", (result: ISimResponse) => {
    if (result.debug?.length > 0)
        console.log(result.debug);

    const step = result.stepTimes.length - 1;
    if (st.history.length == 0 || st.history[st.history.length - 1].t < step)
    {
        //Convert once and retain the BYTES, not the base64 the wire delivered. The string is
        //4/3 the size for no benefit, and dropping it here lets it be collected immediately
        //instead of being held for the rest of the session.
        const binaryScene = base64ToArrayBuffer(result.scene);
        lastReceivedIndex = upsertHistoryFrame(step, binaryScene);
        const reader = new BinaryReader(binaryScene);
        const scene = reader.readAgroScene();

        //console.log(result.stepTimes);
        batch(() => {
            st.plants.value = result.plants;
            st.scene.value = scene;
            st.computing.value = false;
            st.renderer.value = result.renderer;
            st.simLength.value = 0;
            st.historyCount.value = st.history.length;
        });
    }
    else
    {
        //console.log(result.stepTimes);
        batch(() => {
            st.plants.value = result.plants;
            st.computing.value = false;
            st.renderer.value = result.renderer;
            st.simLength.value = 0;
        });
    }

    //Stop waiting for frames that are not coming.
    //
    //The playback machine cannot do this for itself. It only advances when something wakes it,
    //and the thing that wakes it is a frame arriving - so after the last one it simply stays in
    //ForwardWaiting for the rest of the session, which leaves a finished run reading as "playing"
    //with the step controls disabled over a history that is entirely there.
    if (st.playing.peek() === PlayState.ForwardWaiting)
        batch(() => {
            st.playing.value = PlayState.None;
            st.playPointer.value = Math.max(0, Math.min(st.playPointer.peek(), st.history.length - 1));
        });

    //The run is over and the stored timeline is final, so take it whole.
    //
    //The wire is deliberately lazy - a frame is sent only when the client asks for one - while
    //the server exports and stores EVERY step. So the history built up during a run is whatever
    //the browser managed to keep up with, and a run that stored 1,440 frames may have streamed
    //sixty. The rest were on disk and unreachable from the page that asked for them. Now they
    //are simply frames whose payload has not been fetched yet, like any evicted one.
    const finishedRun = st.runId.peek();
    if (finishedRun) void syncTimelineToIndex(finishedRun, false);
});

//The server stores every frame it computes, so once a run id arrives the client no longer has
//to be the only place the history exists.
hubConnection.on("runStarted", (runId: string) => {
    st.runId.value = runId;
    //Survives a reload. Without it the id is lost, resubscribe() has nothing to rejoin, and the
    //run's stored frames become unreachable from the page that started them.
    try { sessionStorage.setItem('agro.runId', runId); } catch { /* private mode, not fatal */ }
});

hubConnection.on("preview", (result: ISimPreview) => {
    const binaryScene = base64ToArrayBuffer(result.scene);
    //Filed by step, not appended: the timeline may already know about this step from the run
    //index. `historySize` is updated inside, and before eviction runs - otherwise the loop
    //weighs a total that is missing the very frame that pushed it over.
    lastReceivedIndex = upsertHistoryFrame(result.step, binaryScene);
    st.historyCount.value = st.history.length;
    scheduleCompression();

    //Over budget, drop the oldest payloads - the server has them. Only when it does NOT (an old
    //server, or a session with no run behind it) is there nothing to do but stop the run, which
    //at least leaves a saveable session rather than a dead tab.
    //Read the signal, not the function that seeded it: the HUD measures against the signal, so
    //using the raw function here would let the two disagree - and it would ignore any budget the
    //user or a test had set.
    if (st.historySize.peek() > st.historyBudget.peek()) {
        if (st.runId.peek()) evictOldestPayloads();
        else st.stopForMemory();
    }

    if (st.playing.peek() == PlayState.ForwardWaiting)
    {
        st.previewRequestAfterSceneUpdate = true;
        const reader = new BinaryReader(binaryScene);
        const scene = reader.readAgroScene();
        batch(() => {
            st.scene.value = scene;
            st.renderer.value = result.renderer;
        });
    }
    else
    {
        batch(() => {
            st.renderer.value = result.renderer;
            st.previewRequest.value = true;
        });
    }
});

hubConnection.on("terrain", (response: Uint8Array) => {
    const binaryTerrain = base64ToArrayBuffer(response);
    const reader = new BinaryReader(binaryTerrain);
    const result = reader.readTerrain();
    st.terrainList = result.terrains;
    st.obstacles.value = [...st.obstacles.value.filter(x => x.type.value !== 'mesh'), ...result.obstacles];
    st.terrainTimestamp.value = Date.now();
})

/**
 * Whether the hub is usable, as a signal.
 *
 * `hubConnection.state` is a plain field, so an effect that consulted it could not be woken
 * when it changed - and a preview request made before the socket came up was simply dropped.
 * Because `previewRequest` was already `true`, setting it `true` again was not a change and
 * did not re-run the effect either, so the request loop stalled for the rest of the session:
 * the simulation ran to completion server-side and the viewport only updated at the very end.
 * Whether that race was lost came down to how fast the browser opened the WebSocket, which is
 * why it appeared in Firefox and not in Chromium.
 */
const hubConnected = signal(false);

const start = async() => {
    try {
        await hubConnection.start();
    } finally {
        hubConnected.value = hubConnection.state === SignalR.HubConnectionState.Connected;
        //Also here, not only in onreconnected. `onclose` falls back to start(), which opens a
        //brand-new connection with a new id - so without this a hard drop would leave the run
        //ticking server-side with this tab silently detached from its group.
        if (hubConnected.value) void resubscribe();
    }
};
hubConnection.onclose(() => { hubConnected.value = false; start(); });
hubConnection.onreconnecting(() => { hubConnected.value = false; });
hubConnection.onreconnected(() => { hubConnected.value = true; resubscribe(); });

/**
 * Rejoins the run after the socket came back.
 *
 * A run no longer belongs to a connection - it belongs to the runner, and its frames go to a
 * SignalR group named by the run id. A reconnect gets a NEW connection id, so without rejoining
 * that group the run would carry on server-side while this tab heard nothing more from it.
 */
async function resubscribe() {
    if (!st.runId.peek()) {
        try {
            const saved = sessionStorage.getItem('agro.runId');
            if (saved) st.runId.value = saved;
        } catch { /* private mode */ }
    }
    const runId = st.runId.peek();
    if (!runId) return;

    //Rebuild the timeline BEFORE rejoining. Frames arriving from here on are appended, so an
    //empty history would start the run again at whatever step the reload happened to land on and
    //everything computed before it would be unreachable from the page that asked for it.
    const restored = st.history.length === 0 ? await syncTimelineToIndex(runId, true) : 0;

    try {
        const ok = await hubConnection.invoke('subscribe', runId);
        if (!ok) {
            //Finished, evicted, or from a previous server process. Its frames may still be
            //readable - which is exactly what `restored` just established - but nothing further
            //will arrive on the socket.
            const held = st.history.length;
            console.warn(held > 0
                ? `Run ${runId} is no longer computing; its ${held} stored frames remain readable.`
                : `Run ${runId} is gone from the server.`);
            //Only when there is genuinely nothing left. `restored` is zero on the second pass
            //through here too - recoverRun() hydrates first and then opens the socket, which
            //re-enters this function with a history it has already rebuilt.
            if (held === 0) forgetRun();
        }
    } catch (err) {
        console.warn('Could not rejoin the run after reconnecting:', err);
    }
}

/**
 * Drops this tab's association with a server-side run.
 *
 * Worth doing explicitly rather than leaving a dead id in place. Eviction is only safe because
 * the server is assumed to hold what it drops, so an id that no longer resolves turns "the server
 * has it" into silent data loss - and a re-fetch against a run that has been replaced returns the
 * same step of a different simulation, which decodes perfectly and is simply the wrong plants.
 */
function forgetRun() {
    st.runId.value = '';
    try { sessionStorage.removeItem('agro.runId'); } catch { /* private mode */ }
}

/**
 * Reattaches to the run this tab started, after a reload.
 *
 * Nothing did this before, and the socket is not opened on page load either, so a reload during a
 * long run left the simulation computing server-side with no one listening until the unwatched
 * grace period stopped it - while the tab showed an empty timeline and no hint that a run existed.
 * The frames were on disk the whole time.
 *
 * Deliberately quiet: no run id, no server, or a run the server has forgotten all end with the
 * page in exactly the state it would have been in anyway.
 */
async function recoverRun() {
    let saved = '';
    try { saved = sessionStorage.getItem('agro.runId') ?? ''; } catch { return; }
    if (!saved) return;

    const meta = await fetchRunMeta(saved);
    if (!meta) { forgetRun(); return; }

    st.runId.value = saved;
    //The clock multiplies a frame's step by this, so without it a recovered run is drawn on the
    //wrong timeline - and it is not otherwise recoverable from the page.
    st.simHoursPerTick = meta.hoursPerTick;
    if (await syncTimelineToIndex(saved, true) === 0) { forgetRun(); return; }

    //A finished run needs no socket at all: its timeline is final and its frames come over REST.
    //That is the common case - reloading to look at a run that has ended - and it should not
    //open a websocket to do nothing.
    if (meta.status !== 'running') return;

    //Still computing, so put the tab back into the state it was in before the reload.
    //
    //Rejoining the group is NOT enough to resume streaming. The wire is deliberately lazy: the
    //server sends a frame only while the client is asking for one, and a freshly loaded page is
    //asking for nothing. Without this the reattached tab sits on a timeline frozen at the moment
    //of the reload while the run computes on, which looks exactly like a failed reattach.
    if (hubConnection.state !== SignalR.HubConnectionState.Connected) await start();
    batch(() => {
        st.computing.value = true;
        st.simLength.value = meta.totalSteps;
        st.simStep.value = meta.storedFrames;
        st.playing.value = PlayState.ForwardWaiting;
    });
    st.previewRequest.value = true;
}

/** A stored run's metadata, or undefined when the server has never heard of it. */
async function fetchRunMeta(runId: string): Promise<{ status: string, hoursPerTick: number,
        totalSteps: number, storedFrames: number } | undefined> {
    try {
        const res = await fetch(`${location.protocol}//${BackendURI}/simulation/run/${runId}`);
        return res.ok ? await res.json() : undefined;
    } catch { return undefined; }
}

interface IPlantResponse
{
    V: number
}

interface ISimResponse
{
    plants: IPlantResponse[],
    scene: Uint8Array,
    renderer: string,
    debug: string,
    stepTimes: Uint32Array,
    ticksPerMillisecond: number,
}

interface ISimPreview
{
    scene: Uint8Array,
    renderer: string,
    step: number,
}

interface ITrayModel
{
    panel: number;
    tray: number;
    section: number;
    row: number;
    trayId: string;
    sectionId: string;
    plants: IPlantModel[];
}

export interface IPlantModel
{
    species?: string;
    key?: string;
    type?: string;
    note?: string;
    status?: number;
    x: number;
    y: number;
    z: number;

    speciesName?: string;
    botanicalName? : string;
}

function base64ToArrayBuffer(base64: any) {
    var binaryString = atob(base64);
    var bytes = new Uint8Array(binaryString.length);
    for (var i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
}

export function arrayBufferToBase64(bytes: Uint8Array): string {
    //Chunked because String.fromCharCode.apply throws RangeError past the argument limit, and
    //a per-byte string concatenation of a 60 MB frame is minutes of work.
    const CHUNK = 8192;
    let s = '';
    for (let i = 0; i < bytes.length; i += CHUNK)
        s += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK) as unknown as number[]);
    return btoa(s);
}

/**
 * What one history entry actually costs the heap.
 *
 * The old accounting was `byteLength + 24` while what was retained was the base64 STRING, so the
 * figure in the HUD under-reported reality by 33% - a user watching it pass 1500 Mb was holding
 * about 2.0 GB. Now that the bytes themselves are retained, `byteLength` is the truth; the
 * constant covers the Uint8Array's own object plus its ArrayBuffer, the {t,s} record, and the
 * array slot. V8 charges roughly 96 bytes for a typed array pair even when it wraps nothing.
 */
const HISTORY_ENTRY_OVERHEAD = 136;

function historyEntryBytes(scene: Uint8Array): number {
    return scene.byteLength + HISTORY_ENTRY_OVERHEAD;
}

/**
 * The index of the newest frame actually RECEIVED on the wire, or -1.
 *
 * Not the same as the end of the history any more. The timeline is synced from the run index
 * while the run computes, so it holds entries for steps the server has stored but has not sent -
 * frames with no payload, ahead of the playhead. The transport behaves like a video player
 * buffering: the slider spans what exists, the playhead sits on what has arrived.
 */
let lastReceivedIndex = -1;

/**
 * Files one received frame into the history, by step.
 *
 * A plain `push` was right while the history only ever held frames that had arrived. It stopped
 * being right the moment the timeline started being filled in from the run index during a run: a
 * preview for step 57 would then append a SECOND entry for a step already present, leaving the
 * history non-monotonic with a duplicate `t`. That breaks more than it looks - `isMonotonic` in
 * FrameSource gives up and the whole animation silently falls back to uniform timing, and the
 * slider grows a step that is not a step.
 *
 * Returns where the frame ended up.
 */
export function upsertHistoryFrame(t: number, scene: Uint8Array): number {
    //Sorted by `t`, so a binary search finds the slot whether the frame is new, already known as
    //an index entry, or - after a reconnect - one that arrives twice.
    let lo = 0, hi = st.history.length - 1, at = -1;
    while (lo <= hi) {
        const mid = (lo + hi) >> 1;
        const e = st.history[mid];
        if (e.t === t) { at = mid; break; }
        if (e.t < t) lo = mid + 1; else hi = mid - 1;
    }

    if (at >= 0) {
        const e = st.history[at];
        //Already held: only the payload is news. Counting it twice is what makes the budget drift.
        if (!e.s) { e.s = scene; e.plain = true; st.historySize.value += historyEntryBytes(scene); }
        return at;
    }

    const entry = { t, s: scene, plain: true };
    //`lo` is the sorted insertion point, and for a live run it is the end - which is the case
    //worth keeping cheap.
    if (lo >= st.history.length) st.history.push(entry); else st.history.splice(lo, 0, entry);
    st.historySize.value += historyEntryBytes(scene);
    return lo;
}

/**
 * Frames kept either side of the playhead once eviction starts.
 *
 * Deliberately small and deliberately a COUNT: these are the frames about to be drawn, and
 * re-fetching one mid-playback would stall it. Everything else is the budget's business.
 */
const NEAR_PLAYHEAD = 4;

/**
 * Drops payloads until the history is back inside budget, and stops the run if it cannot get
 * there.
 *
 * The first version kept a fixed window of the newest 64 frames and only considered dropping
 * anything older, which made it a no-op for exactly the runs it exists for. The budget is in
 * BYTES and the window was a COUNT: at the ~108 MB a mature green-facade frame occupies, the
 * budget is crossed around frame 12, so `history.length - 64` clamped to zero and the loop body
 * never executed. Nothing was dropped, nothing was reported, and because a run id was present the
 * `stopForMemory` fallback was skipped too - so the one path that could have saved the tab was
 * disabled by the presence of the thing meant to make dropping safe.
 *
 * Both halves of that are fixed here: the window is byte-driven, and failing to reach the target
 * escalates instead of returning quietly.
 */
export function evictOldestPayloads() {
    const budget = st.historyBudget.peek();
    const target = budget * 0.75;
    const pointer = st.playPointer.peek();
    const last = st.history.length - 1;
    let dropped = 0;

    for (let i = 0; i <= last && st.historySize.peek() > target; ++i) {
        //The newest frame is what the viewport is showing as the run streams; the playhead window
        //is what a user scrubbing is about to ask for.
        if (i === last || Math.abs(i - pointer) <= NEAR_PLAYHEAD) continue;

        const e = st.history[i];
        if (!e || !e.s) continue;
        st.historySize.value -= historyEntryBytes(e.s);
        e.s = undefined;
        ++dropped;
    }

    if (dropped > 0) st.evictedFrames.value += dropped;

    //Still over after dropping everything droppable - a handful of frames alone exceed the
    //budget. Stopping loses the rest of the run; not stopping loses the whole tab.
    if (st.historySize.peek() > budget) st.stopForMemory();
}

/**
 * Rehydrates a saved session's history.
 *
 * The on-disk format keeps base64, so sessions saved before the in-memory switch to bytes still
 * load. Entries are converted in place and the strings dropped as we go, so loading a large
 * session does not hold both representations at once.
 */
export function decodeSavedHistory(saved: unknown): { t: number, s: Uint8Array | undefined, plain: boolean }[] {
    if (!Array.isArray(saved)) return [];
    const out: { t: number, s: Uint8Array | undefined, plain: boolean }[] = new Array(saved.length);
    for (let i = 0; i < saved.length; ++i) {
        const e = saved[i];
        out[i] = { t: e.t, s: typeof e.s === 'string' ? base64ToArrayBuffer(e.s) : e.s, plain: true };
        saved[i] = undefined;
    }
    return out;
}

/**
 * What a history actually occupies, counted from the entries themselves.
 *
 * `save()` writes every frame RAW - it inflates the compressed ones and re-fetches the evicted
 * ones - but the `historySize` it records alongside them is the live figure, which counts those
 * same frames compressed or not at all. Trusting the recorded number after a load therefore
 * under-reports by the compression ratio plus the whole of every evicted frame: a session saved
 * with 96 of 160 frames evicted claims a few megabytes and loads hundreds. The HUD then lies and,
 * far worse, eviction measures against a total that is too small to ever cross the budget - which
 * is the exact failure Stage 1 existed to end. Counting is cheap and cannot drift, so count.
 */
export function measureHistoryBytes(history: { s: Uint8Array | undefined }[]): number {
    let total = 0;
    for (const e of history)
        if (e && e.s) total += historyEntryBytes(e.s);
    return total;
}

/**
 * How much history to hold before refusing more.
 *
 * Derived from the tab's real ceiling where the browser exposes it. The fraction is deliberately
 * well under 1: the decoded scene, the instance list, the GPU staging buffers and a transient
 * spike of several times the frame size all have to fit in the same heap, and the frame arriving
 * when the limit is reached is itself the largest one so far.
 */
const HISTORY_BUDGET_FRACTION = 0.45;
const HISTORY_BUDGET_FALLBACK = 1_200_000_000;

function historyBudgetBytes(): number {
    const limit = (performance as any)?.memory?.jsHeapSizeLimit;
    return typeof limit === 'number' && limit > 0
        ? limit * HISTORY_BUDGET_FRACTION
        : HISTORY_BUDGET_FALLBACK;
}

function exportObstacle(o: Obstacle) {
    const base = { T: o.type.peek(), H: o.height.peek(), D: o.thickness.peek(),
                    P: { X: o.px.peek(), Y: o.py.peek(), Z: o.pz.peek() },
                    O: o.angleY.peek() };
    switch (o.type.peek()) {
        case "umbrella": return { ...base, R: o.wallLength_UmbrellaRadius.peek() };
        default: return { ...base, L: o.wallLength_UmbrellaRadius.peek() };
    }
}

export enum PlayState {
    None, //noop
    Backward, //playing backward
    Forward, //playing forward
    ForwardWaiting, //playing forward and waiting for new frames to come
    SeekBackward, //jump to the first frame
    SeekForward, //jump to the last frame
    NextBackward, //one frame backward
    NextForward, //one frame forward
    Seek //jump to the specified frame
};

class State {
    // SETTINGS
    hoursPerTick = signal(4);
    totalHours = signal(1440);
    fieldResolution = signal(0.05);
    fieldSizeX = signal(10);
    fieldSizeZ = signal(10);
    fieldSizeD = signal(4);
    fieldCellsX = computed(() => Math.ceil(this.fieldSizeX.value / this.fieldResolution.value));
    fieldCellsZ = computed(() => Math.ceil(this.fieldSizeZ.value / this.fieldResolution.value));
    fieldCellsD = computed(() => Math.ceil(this.fieldSizeD.value / this.fieldResolution.value));
    terrainList: ITerrainItem[] = [];
    terrainTimestamp = signal(Date.now());
    initNumber = signal(42);
    randomize = signal(false);
    renderMode = signal(1); //1 is Mitsuba
    exactPreview = signal(false);
    /**
     * Calendar date the simulation starts on, as YYYY-MM-DD.
     *
     * The frontend never used to send this, so the backend silently fell back to its own
     * default of 2026-02-01 - a very low sun at 48°N, which makes light-driven growth behave
     * quite differently from what a summer start would give. See Agro/AgroWorld.cs.
     */
    initialDate = signal(DEFAULT_INITIAL_DATE);

    //SPECIES
    species = signal<Species[]>([Species.Default()]);
    behaviors = signal<string[]>([]);

    //INITIAL SCENE SETUP
    seedsPerField = signal(1);
    seedsOptimalDistance = signal(0.165);
    seeds = signal<Seed[]>([]);
    seedsCount = computed(() => this.seeds.value.length);

    obstacles = signal<Obstacle[]>([]);
    obstaclesCount = computed(() => this.obstacles.value.length);

    //RENDERING
    //The scene graph belongs to whichever renderer is mounted; appstate holds only the data
    //it is built from. See src/render/IViewportRenderer.ts.
    viewportBackend = signal<"auto" | "three" | "webgpu">("auto");
    /**
     * Which HUD tab is showing. Lifted out of app.tsx because the viewport needs it: the
     * transform gizmo is a Three.js control with no WebGPU equivalent, so editing tabs
     * mount the Three backend regardless of preference.
     */
    activeTab = signal("tab-home");

    /**
     * Viewport lighting quality. Purely visual - it never reaches the simulation.
     *
     * Deliberately separate from `renderMode`, which selects the SERVER's light-transport
     * engine and changes the irradiance the plants actually consume.
     */
    //Independent switches rather than a ladder; see LightingOptions. All off is the parity
    //look, which is what keeps the default byte-identical to the WebGL renderer.
    //Physical daylight: a modest sky plus a dominant sun, instead of the historical flat
    //ambient that makes occlusion and indirect light invisible. See LightingOptions.
    lightDaylight = signal(false);
    lightShadows = signal(false);
    lightOcclusion = signal(false);
    lightIndirect = signal(false);
    lightSecondBounce = signal(false);
    //Normal-offset shadow bias in shadow texels. Tunable because the right value depends on
    //the canopy: too little and a leaf shadows itself, too much and light leaks between
    //leaves that genuinely touch.
    shadowBiasTexels = signal(SHADOW_NORMAL_OFFSET_TEXELS);

    lightingOptions(): LightingOptions {
        return {
            daylight: this.lightDaylight.peek(),
            shadows: this.lightShadows.peek(),
            occlusion: this.lightOcclusion.peek(),
            indirect: this.lightIndirect.peek(),
            secondBounce: this.lightSecondBounce.peek(),
            shadowBiasTexels: this.shadowBiasTexels.peek(),
        };
    }
    //Diagnostic overlay for the voxel GI's density field. Not persisted and not sent to the
    //server: it shows a rendering intermediate, not anything the simulation knows about.
    showVoxels = signal(false);
    voxelMip = signal(0);
    voxelGain = signal(1);
    voxelAlbedo = signal(false);
    activeBackend = signal<"three" | "webgpu" | undefined>(undefined);

    /**
     * Set while the offline animation renderer is stepping through history.
     *
     * A plain field, NOT a signal: writing it must not trigger any effect, and its only job
     * is to suppress the playback/preview poke that normally follows a plant rebuild.
     */
    offlineRendering = false;

    needsRender = signal(false);
    visualMapping = signal(VisualMappingOptions.Natural);
    plantPick = signal("");
    seedPick = signal(-1);
    terrainPick = signal(-1);

    downloadRoots = signal(false);
    debugBoxes = signal(false);
    showLeaves = signal(true);
    showTerrain = signal(true);
    showRoots = signal(true);
    showObstacles = signal(true);
    showSeeds = signal(true);

    performPicking = signal(true);

    grabbed = computed(() => this.seeds.value.filter((x: Seed) => x.state.value == "grab"));

    //OPERATIONAL STATE
    computing = signal(false);
    playing = signal(PlayState.None);
    playPointer = signal(0);
    playRequest = signal(false);
    previewRequest = signal(false);
    previewRequestAfterSceneUpdate = false;
    simStep = signal(0);
    simLength = signal(0);

    //RESPONSE
    plants = signal<IPlantResponse[]>([]);
    scene = signal<PlantModel[]>([]);
    renderer = signal("");
    samplesPerPixel = signal(2048);
    simHoursPerTick = 1;

    /**
     * The simulation timestep the viewport is currently showing.
     *
     * NOT the same as playPointer: that is an index into `history`, and previews can be
     * skipped, so the two diverge as soon as the backend drops a frame. Using the pointer to
     * drive the clock puts the sun at the wrong time of day - which reads as broken lighting
     * rather than as the units mistake it is.
     */
    currentTimestep(): number {
        return this.timestepAt(this.playPointer.peek());
    }

    /**
     * The simulated timestep a playhead position refers to.
     *
     * Takes the pointer as an argument so a caller inside a signal effect can pass
     * `playPointer.value` and actually subscribe to it. The obvious spelling - reading the
     * signal for its subscription and calling `currentTimestep()` for the value - was written
     * as `(playPointer.value, currentTimestep())`, and terser deletes the discarded operand of
     * a comma expression as dead code. The effect then never re-ran as the animation played,
     * so the sun stayed frozen at whatever time it was when the tier last changed.
     */
    timestepAt(pointer: number): number {
        const i = Math.max(0, Math.min(pointer, this.history.length - 1));
        return this.history[i]?.t ?? 0;
    }

    /**
     * One entry per received timestep.
     *
     * `s` is the V7 scene. It arrives raw and is deflated in the background once it falls out of
     * the hot window, so `plain` says which it currently is - roughly 1.7x on real scene data,
     * which is what float-heavy payloads give.
     */
    history : {t: number, s: Uint8Array | undefined, plain: boolean}[] = [];
    historySize = signal(0);

    /**
     * How many frames the history holds, as a signal.
     *
     * `historySize` used to carry this too: the transport bar asked `historySize.value == 0` to
     * mean "there is nothing to play", and the export panel read it purely to subscribe. That
     * conflation held only while every frame in the history had its payload. It stops being true
     * the moment frames are evicted to the server or a reload rebuilds the timeline from the run
     * index - a history of 600 frames then legitimately occupies zero bytes, and every transport
     * control would be disabled over a run that is entirely intact on disk.
     *
     * Bytes and frames are two different questions. This one answers "how many frames".
     */
    historyCount = signal(0);
    /** Set when a run was stopped because the history filled the heap budget. */
    historyLimitHit = signal(false);
    /** The ceiling `historySize` is measured against, so the HUD can show a share, not a number. */
    historyBudget = signal(historyBudgetBytes());
    /**
     * The server-side store backing this run, when there is one.
     *
     * Its presence is what makes dropping a frame from memory safe rather than lossy: with a run
     * id the payload can be fetched back, without one it is gone for good.
     */
    runId = signal('');
    /**
     * Run sequentially so the result can be reproduced.
     *
     * Off by default because it costs throughput. Measured: with several plants the same request
     * and seed diverges at the FIRST timestep on the parallel path - 59 of 60 frames differed -
     * so a branched scenario could never be compared against the run it came from.
     */
    deterministic = signal(false);
    /** Frames whose payload has been dropped and would have to be re-fetched. */
    evictedFrames = signal(0);

    /**
     * Stops the run because there is no room left to keep its results.
     *
     * Aborting is the point: the frames already received stay valid and saveable, whereas letting
     * the run continue ends in the tab being killed and everything lost. Latched so the abort is
     * sent once, not once per remaining frame.
     */
    stopForMemory() {
        if (this.historyLimitHit.peek()) return;
        this.historyLimitHit.value = true;
        console.warn(
            `Simulation stopped: the stored history reached ` +
            `${(this.historySize.peek() / 1048576).toFixed(0)} MB, the limit this browser tab can hold. ` +
            `The frames computed so far are intact and can be saved or exported.`);
        if (this.computing.peek()) hubConnection.invoke("abort");
    }

    fieldModelPath = signal("");
    fieldItemRegex = signal("erde");
    fieldItemRegexMaterial = signal(true);
    fieldModelData?: string = undefined;
    modelParsingProgress = signal(0);
    seedsDistributionData? = "";
    seedsDistributionPath = signal("");

    //METHODS
    private requestBody = () => {
        return {
        HoursPerTick: Math.trunc(this.hoursPerTick.peek()),
        TotalHours: Math.trunc(this.totalHours.peek()),
        FieldResolution: this.fieldResolution.peek(),
        FieldSize: { X: this.fieldSizeX.peek(), D: this.fieldSizeD.peek(), Z: this.fieldSizeZ.peek() },
        Seed: Math.trunc(this.randomize.peek() ? Math.random() * 4294967295 : this.initNumber.peek()),
        InitialDate: this.initialDate.peek(),
        Species: this.species.peek().map(s => s.serialize()),

        Plants: this.seeds.peek().map((p: Seed) => ({ S: p.species.peek(), P: { X: p.px.peek(), Y: p.py.peek(), Z: p.pz.peek() }, F: p.fieldIndex.peek() })),
        Obstacles: this.obstacles.peek().map((o: Obstacle) => exportObstacle(o)),
        RequestGeometry: true,
        RenderMode: this.renderMode.value,
        SamplesPerPixel: this.samplesPerPixel.value,
        ExactPreview: this.exactPreview.value,
        DownloadRoots: this.downloadRoots.value,

        FieldItemRegex: this.fieldItemRegex.value,
        Deterministic: this.deterministic.value,
        FieldItemRegexMaterial: this.fieldItemRegexMaterial.value,
        FieldModelPath: this.fieldModelPath.value,
        //FieldModelData is deliberately NOT sent. `SimulationRequest` has no such member (only
        //`FieldModelKey`, which nothing sets), so System.Text.Json discarded it silently - while
        //every run JSON-stringified, escaped and uploaded the whole facade OBJ again for it.
        //On the 93.7 MB green-facade model that was ~98 MB of upload per run, for nothing. The
        //model reaches the simulation through /simulation/terrain and ClientTerrains instead.
    }};

    run = async() => {
        if (this.computing.peek())
            hubConnection.invoke("abort");
        else
        {
            this.simHoursPerTick = this.hoursPerTick.peek();
            //console.log(this.scene);
            batch(() => {
                this.computing.value = true;
                this.playing.value = PlayState.ForwardWaiting;
                this.playPointer.value = 0;
                this.renderer.value = "";
                this.historySize.value = 0;
                this.historyCount.value = 0;
                lastReceivedIndex = -1;
                this.historyLimitHit.value = false;
                this.runId.value = '';
                this.evictedFrames.value = 0;
            });
            //The stored id has to go at the same moment the signal does. A reload in the window
            //between asking for a run and `runStarted` arriving would otherwise resubscribe to
            //the PREVIOUS run and stream its frames into this one's empty history.
            try { sessionStorage.removeItem('agro.runId'); } catch { /* private mode */ }
            this.history.length = 0;
            clearSceneCache();
            resetCompression();
            if (hubConnection.state !== SignalR.HubConnectionState.Connected)
                await start();

            if (hubConnection.state == SignalR.HubConnectionState.Connected)
            {
                console.log(this.requestBody());
                const prepared = await fetch(`${location.protocol}//${BackendURI}/simulation/upload`, {
                    body: JSON.stringify(this.requestBody()),
                    method: 'post',
                    headers: {
                        'Accept': 'application/json',
                        'Content-Type': 'application/json'
                    }
                });

                const preparedID = await prepared.text();
                if (preparedID?.length > 0)
                    hubConnection.invoke("start", preparedID).catch(e => {
                        console.error(e);
                        batch(() => {
                            this.computing.value = false;
                            this.renderer.value = "error";
                        });
                    });
                // hubConnection.invoke("run", this.requestBody()).catch(e => {
                //     console.error(e);
                //     batch(() => {
                //         this.computing.value = false;
                //         this.renderer.value = "error";
                //     });
                // });
                this.previewRequest.value = true;
            }
            else
                this.computing.value = false;
        }
    }

    play = async(state: PlayState) => {
        if (state != PlayState.Forward || this.playPointer.peek() < this.history.length - 1)
            this.playing.value = state;
        else
            this.playing.value = PlayState.ForwardWaiting;
    }

    clearSeeds = (fieldIndex: number = -1) => {
        if (fieldIndex >= 0)
        {
            const remove : number[] = [];
            const keep : Seed[] = [];
            const src = this.seeds.peek();
            src.forEach((s, i) => {
                if (s.fieldIndex.peek() == fieldIndex)
                    remove.push(i);
                else
                    keep.push(s);
            });
            this.seeds.value = keep;
        }
        else
            this.seeds.value = [ ];
        this.needsRender.value = true;
    }

    batchTerrainsClear(pattern: string) {
        if (pattern?.length > 0 && this.terrainList?.length > 0)
        {
            const fieldIndexesToRemove = new Set<number>();
            for(let i = 0; i < this.terrainList.length; ++i)
                if (this.terrainList[i].id?.match(pattern))
                    fieldIndexesToRemove.add(i);

            const remove : number[] = [];
            const keep : Seed[] = [];
            const src = this.seeds.peek();
            src.forEach((s, i) => {
                if (fieldIndexesToRemove.has(s.fieldIndex.peek()))
                    remove.push(i);
                else
                    keep.push(s);
            });
            this.seeds.value = keep;
            this.needsRender.value = true;
        }
    }

    pushRndSeed = (count?: number) => {
        const species = this.species.peek().filter(s => s.includeInRndGen.value);
        if (species.length == 0)
            console.error("No species selected for random seeding");
        else if (count)
        {
            if (count >= 1)
            {
                const result : Seed[] = [];
                if (this.terrainList?.length > 0)
                {
                    for(let t = 0; t < this.terrainList.length; ++t)
                        for(let i = 0; i < count; ++i)
                            result.push(Seed.rndItem(species, 0, t));
                }
                else
                {
                    for(let i = 0; i < count; ++i)
                        result.push(Seed.rndItem(species));
                }
                this.seeds.value = [ ...this.seeds.peek(), ...result]
            }
            else
                this.seeds.value = [ ...this.seeds.peek(), Seed.rndItem(species)]
        }
    };

    pushSeedRaster = (dist: number) => {
        const result : Seed[] = [];
        const species = this.species.peek().filter(s => s.includeInRndGen.value);
        if (species.length == 0)
            console.error("No species selected for random seeding");
        else
            batch(() => {
                if (this.terrainList?.length > 0)
                {
                    for(let i = 0; i < this.terrainList.length; ++i)
                    {
                        const terrain = this.terrainList[i];

                        if (terrain instanceof MeshTerrainItem)
                        {
                            const principatDir = terrain.principalDir;
                            const secondaryDir = terrain.secondaryDir;
                            const sx = terrain.principalSize;
                            const sz = terrain.secondarySize;
                            const xCount = Math.max(1, Math.round(sx / dist));
                            const zCount = Math.max(1, Math.round(sz / dist));
                            const xDist = sx / xCount;
                            const zDist = sz / zCount;
                            const xStart = xDist * 0.5;
                            const start = addScaled2(vec2(principatDir.x * xStart, principatDir.y * xStart), secondaryDir, zDist * 0.5);
                            for(let x = 0; x < xCount; ++x)
                            {
                                const mx = x * xDist;
                                for(let z = 0; z < zCount; ++z)
                                {
                                    const stepped = addScaled2(vec2(principatDir.x * mx, principatDir.y * mx), secondaryDir, z * zDist);
                                    const pos = { x: stepped.x + start.x, y: stepped.y + start.y };
                                    result.push(new Seed(species[Math.floor(Math.random() * species.length)].name.peek(), pos.x, Math.max(0, terrain.sy() - 0.02), pos.y, i, false));
                                }
                            }
                        }
                        else
                        {
                            const sx = terrain.sx();
                            const sz = terrain.sz();
                            const xCount = Math.max(1, Math.round(sx / dist));
                            const zCount = Math.max(1, Math.round(sz / dist));
                            const xDist = sx / xCount;
                            const zDist = sz / zCount;
                            const xStart = xDist * 0.5;
                            const zStart = zDist * 0.5;
                            for(let x = 0; x < xCount; ++x)
                                for(let z = 0; z < zCount; ++z)
                                    result.push(new Seed(species[Math.floor(Math.random() * species.length)].name.peek(), xStart + x * xDist, -0.02, zStart + z * zDist, i, false));
                        }
                    }
                }
                else
                {
                    const xCount = Math.max(1, Math.round(this.fieldSizeX.value / dist));
                    const zCount = Math.max(1, Math.round(this.fieldSizeZ.value / dist));
                    const xDist = this.fieldSizeX.value / xCount;
                    const zDist = this.fieldSizeZ.value / zCount;
                    const xHalf = xDist * 0.5;
                    const zHalf = zDist * 0.5;
                    for(let x = 0; x < xCount; ++x)
                        for(let z = 0; z < zCount; ++z)
                            result.push(new Seed(species[Math.floor(Math.random() * species.length)].name.peek(), xHalf + x * xDist, -0.02, zHalf + z * zDist, 0, false));
                }
                this.seeds.value = [ ...this.seeds.value, ...result];
            });
    }

    removeSeedAt = (i : number) => {
        const seeds = this.seeds.peek();
        if (i >= 0 && i < seeds.length)
            seeds.splice(i, 1);
        this.seeds.value = [...seeds];
        this.needsRender.value = true;
    };

    pushRndSpecies = () => {
        this.species.value = [ ...this.species.peek(), new Species()]
    };

    removeSpeciesAt = (i : number) => {
        const species = this.species.peek();
        if (i >= 0 && i < species.length && species.length > 1) {
            //The [0] this used to end with was vestigial - copied from removeSeedAt, which
            //actually needed the removed element. splice does the work.
            species.splice(i, 1);
        }
        this.species.value = [...species];
    };

    clearHovers = (src: BaseRequestObject[], except: BaseRequestObject | undefined) => {
        src.forEach((x : BaseRequestObject) => {
            if (x !== except)
            {
                switch (x.state.peek()) {
                    case "hover": x.unhover(); break;
                    case "selecthover": x.select(); break;
                }
            }
        });
    };

    clearSeedHovers = (except: Seed | undefined) => this.clearHovers(this.seeds.peek(), except);
    clearObstacleHovers = (except: Obstacle | undefined) => this.clearHovers(this.obstacles.peek(), except);
    clearTerrainHovers = (except: ITerrainItem | undefined) => this.clearHovers(this.terrainList, except);

    clearSelects = (src: BaseRequestObject[], except: Seed | Obstacle | ITerrainItem | undefined) => {
        src.forEach((s : BaseRequestObject) => {
            if (s !== except)
                switch(s.state.peek()) {
                    case "select":
                    case "selecthover": s.unhover();
                }
        });
    };

    clearSeedSelects = (except: Seed | undefined) => this.clearSelects(this.seeds.peek(), except);
    clearObstacleSelects = (except: Obstacle | undefined) => this.clearSelects(this.obstacles.peek(), except);
    clearTerrainSelects = (except: ITerrainItem | undefined) => this.clearSelects(this.terrainList, except);

    clearGrabs = (src: BaseRequestObject[], except: Seed | Obstacle | undefined) => {
        src.forEach((s : BaseRequestObject) => {
            if (s !== except)
                if (s.state.peek() == "grab")
                    s.ungrab("select");
        });
    };

    clearSeedGrabs = (except: Seed | undefined) => this.clearGrabs(this.seeds.peek(), except);
    clearObstacleGrabs = (except: Obstacle | undefined) => this.clearGrabs(this.obstacles.peek(), except);

    pushRndObstacle = () => { this.obstacles.value = [ ...this.obstacles.peek(), Obstacle.rndObstacle()] };

    removeObstacleAt = (i : number) => {
        const obstacles = this.obstacles.peek();
        if (i >= 0 && i < obstacles.length)
            obstacles.splice(i, 1);

        this.obstacles.value = [...obstacles];
        this.needsRender.value = true;
    };

    save = async () => {
        const data = {
            hoursPerTick: this.hoursPerTick.peek(),
            totalHours: this.totalHours.peek(),
            fieldResolution: this.fieldResolution.peek(),
            fieldSizeX: this.fieldSizeX.peek(),
            fieldSizeZ: this.fieldSizeZ.peek(),
            fieldSizeD: this.fieldSizeD.peek(),
            terrainList: this.terrainList.map(item => item.save()),
            terrainTimestamp: this.terrainTimestamp.value,
            initNumber: this.initNumber.peek(),
            randomize: this.randomize.peek(),
            renderMode: this.renderMode.peek(),
            initialDate: this.initialDate.peek(),
            lighting: this.lightingOptions(),
            viewportBackend: this.viewportBackend.peek(),
            downloadRoots: this.downloadRoots.peek(),
            exactPreview: this.exactPreview.peek(),

            species: this.species.value.map(s => s.save()),
            behaviors: this.behaviors.value,

            seedsPerField: this.seedsPerField.peek(),
            seedsOptimalDistance: this.seedsOptimalDistance.peek(),
            seeds: this.seeds.value.map(s => s.save()),

            obstacles: this.obstacles.value.map(o => o.save()),

            visualMapping: this.visualMapping.peek(),
            debugDisplayOrientations: this.debugBoxes.peek(),
            showLeaves: this.showLeaves.peek(),
            showTerrain: this.showTerrain.peek(),
            showRoots: this.showRoots.peek(),
            showObstacles: this.showObstacles.peek(),
            showSeeds: this.showSeeds.peek(),
            performPicking: this.performPicking.peek(),

            plants: this.plants.peek(),
            historySize: this.historySize.peek(),
            //History is emitted separately, entry by entry - see below.
            simHoursPerTick: this.simHoursPerTick,

            fieldModelPath: this.fieldModelPath.peek(),
            fieldItemRegex: this.fieldItemRegex.peek(),
            fieldItemRegexMaterial: this.fieldItemRegexMaterial.peek(),
            fieldModelData: this.fieldModelData,
        };

        //Assembled as Blob parts rather than one string.
        //
        //`JSON.stringify` of the whole session used to build a single contiguous JS string, which
        //fails twice over on a long run: it needs the entire history live a second time, and V8
        //caps a string at ~537 million characters, so any history past roughly 512 MB threw
        //`RangeError: Invalid string length`. The user could not save the very session that was
        //about to die. A Blob takes its parts separately and never materialises the whole.
        const settings = JSON.stringify(data);
        const parts: BlobPart[] = [settings.slice(0, -1), ',"history":['];
        for (let i = 0; i < this.history.length; ++i) {
            const e = this.history[i];
            //Written raw, so the session format does not change and files saved before frames
            //were compressed in memory still load. Frames outside the hot window have to be
            //inflated on the way out, which is why save() is async.
            const raw = await rawFrameBytes(e);
            parts.push(`${i ? ',' : ''}{"t":${e.t},"s":"${arrayBufferToBase64(raw)}"}`);
        }
        parts.push(']}');

        downloadBlob(new Blob(parts, { type: 'application/json' }), '', 'json');
    }

    load = async() => {
        const input = document.createElement("input");
        input.style.setProperty("display", "none");
        input.type = "file";
        input.accept = "text/json";
        document.body.appendChild(input);
        const self = this;
        input.onchange = e => {
            const target = (e.target as HTMLInputElement);
            const reader = new FileReader();
            reader.onload = function() {
                const seeds = self.seeds.peek();
                for(let i = seeds.length - 1; i >= 0; --i)
                    self.removeSeedAt(i);

                const obstacles = self.obstacles.peek();
                for(let i = obstacles.length - 1; i >= 0; --i)
                    self.removeObstacleAt(i);

                const text = reader.result?.toString() ?? "";
                const data = JSON.parse(text);
                self.history = decodeSavedHistory(data.history);
                clearSceneCache();
                resetCompression();
                //A loaded session arrives entirely raw and would otherwise stay that way: the
                //compressor is normally driven by frames arriving from a run, and loading is the
                //other way a large history appears.
                scheduleCompression();
                self.simHoursPerTick = data.simHoursPerTick ?? 1;

                batch(() => {
                    self.hoursPerTick.value = data.hoursPerTick;
                    self.totalHours.value = data.totalHours;
                    self.fieldResolution.value = data.fieldResolution;
                    self.fieldSizeX.value = data.fieldSizeX;
                    self.fieldSizeZ.value = data.fieldSizeZ;
                    self.fieldSizeD.value = data.fieldSizeD;
                    self.terrainList = data.terrainList?.map((item, index) => item.hasOwnProperty('points') ? MeshTerrainItem.load(item, index) : BoxTerrainItem.load(item, index)) ?? [];
                    self.terrainTimestamp.value = data.terrainTimestamp;
                    self.initNumber.value = data.initNumber;
                    self.randomize.value = data.randomize;
                    self.renderMode.value = data.renderMode;
                    self.initialDate.value = data.initialDate ?? DEFAULT_INITIAL_DATE;
                    //Sessions saved before the ladder became independent switches carry a
                    //single number instead; translate rather than dropping their setting.
                    const lighting: LightingOptions = data.lighting
                        ?? (data.lightingTier !== undefined ? lightingFromLegacyTier(data.lightingTier) : LIGHTING_OFF);
                    self.lightDaylight.value = lighting.daylight ?? false;
                    self.lightShadows.value = lighting.shadows;
                    self.lightOcclusion.value = lighting.occlusion;
                    self.lightIndirect.value = lighting.indirect;
                    self.lightSecondBounce.value = lighting.secondBounce;
                    self.shadowBiasTexels.value = lighting.shadowBiasTexels ?? SHADOW_NORMAL_OFFSET_TEXELS;
                    self.viewportBackend.value = data.viewportBackend ?? "auto";
                    self.downloadRoots.value = data.downloadRoots;
                    self.exactPreview.value = data.exactPreview;

                    self.species.value = data.species.map(s => new Species().load(s));
                    self.behaviors.value = data.behaviors;

                    self.seedsPerField.value = data.seedsPerField;
                    self.seedsOptimalDistance.value = data.seedsOptimalDistance;
                    self.seeds.value = data.seeds.map(s => new Seed(s.species, s.px, s. py, s.pz, s.fi, false));

                    self.obstacles.value = data.obstacles.map(o => new Obstacle(o.type, o.px, o.py, o.pz, o.ax, o.ay, o.l, o.h, o.t, new Float32Array(o.vt), o.fc));

                    self.visualMapping.value = data.visualMapping;
                    self.debugBoxes.value = data.debugDisplayOrientations;
                    self.showLeaves.value = data.showLeaves;
                    self.showTerrain.value = data.showTerrain;
                    self.showRoots.value = data.showRoots;
                    self.showObstacles.value = data.showObstacles;
                    self.showSeeds.value = data.showSeeds;

                    self.performPicking.value = data.performPicking;

                    self.plants.value = data.plants;
                    //Measured, not read back - see measureHistoryBytes for why the recorded
                    //figure describes a different history than the one just loaded.
                    self.historySize.value = measureHistoryBytes(self.history);
                    self.historyCount.value = self.history.length;
                    //A loaded session has received nothing on the wire; every frame is simply held.
                    lastReceivedIndex = self.history.length - 1;
                    //A loaded session is not the run this tab may have just computed. Leaving the
                    //id in place points eviction and every re-fetch at a run that does not contain
                    //these frames: the payloads would be dropped as though the server held them,
                    //and fetching one back returns the SAME step of a DIFFERENT simulation, which
                    //decodes perfectly and is simply the wrong plants.
                    self.runId.value = '';
                    self.evictedFrames.value = 0;
                    self.historyLimitHit.value = false;
                    try { sessionStorage.removeItem('agro.runId'); } catch { /* private mode */ }
                    //History was already rehydrated above, before this batch. It used to be
                    //assigned twice - harmless while it was a plain reference copy, but
                    //decodeSavedHistory consumes its source as it converts, so a second call
                    //saw entries it had already released and threw on `e.t`.
                    self.simHoursPerTick = data.simHoursPerTick;

                    self.fieldModelPath.value = data.fieldModelPath;
                    self.fieldItemRegex.value = data.fieldItemRegex;
                    self.fieldItemRegexMaterial.value = data.fieldItemRegexMaterial;
                    self.fieldModelData = data.fieldModelData;

                    showSceneAt(0);
                });

                self.play(PlayState.SeekBackward);

                input.remove();
            };
            reader.readAsText(target.files[0]);
        };
        input.click();
    }

    gltf = () => {
        //The scene graph lives in the renderer now, so it does the export. Backends that
        //cannot export simply do not implement the optional method.
        const renderer = activeRenderer.current;
        if (renderer?.exportGLTF)
            renderer.exportGLTF(json => this.saveTextFile(json, 'gltf'));
        else
            console.warn('The active viewport does not support glTF export.');
    }

    prim = async () => {
        const i = Math.min(this.playPointer.peek(), st.history.length - 1);
        if (i < 0) return;
        const e = this.history[i];
        this.saveBinaryFile(await rawFrameBytes(e), `_${e.t}`, 'prim');
    }

    uploadFieldModel = async (f: File) => {
        this.fieldModelData = await Parse(f);
        // const bytes = new Uint8Array(await f.arrayBuffer());
        // this.fieldModelData = bytes;
        this.fieldModelPath.value = f.name;
        if (hubConnection.state !== SignalR.HubConnectionState.Connected)
            await start();

        if (hubConnection.state == SignalR.HubConnectionState.Connected)
        {
            const bufferResponse = await fetch(`${location.protocol}//${BackendURI}/simulation/terrain`, {
                body: this.fieldModelData,
                method: 'post',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'text/plain'
                }
            });
            const terrainId = await bufferResponse.text();
            hubConnection.invoke("terrain", terrainId, this.hoursPerTick.value, this.fieldItemRegex.value, this.fieldItemRegexMaterial.value, this.fieldResolution.value);
        }
    }

    clearFieldModel = () => {
        this.fieldModelData = undefined;
        this.fieldModelPath.value = "";
    }


    uploadSeedsDistribution = async (f: File) => {
        const text = await f.text();
        const data = JSON.parse(text) as ITrayModel[];

        this.clearSeeds();

        const speciesMap = new Map<string, Species>();
        const normalize = (s: string) => {
            return s?.toLowerCase()
                    .normalize('NFD') // Decomposes accented characters
                    .replace(/[\u0300-\u036f]/g, '') // Removes diacritics
                    .replace(/[^a-zA-Z0-9\s]/g, '') // Removes special characters
                    .replace(/\s{1,}/g, ' ') //Remove multiple spaecs
                    .trim();
        };
        this.species.value.forEach(x => {
            if (x.name.value?.length > 0)
                speciesMap.set(normalize(x.name.value), x);
            if (x.aka.value?.length > 0)
                speciesMap.set(normalize(x.aka.value), x);
        });

        const speciesWordsMap : {words: string[], key: string}[] = [];
        Array.from(speciesMap.keys()).forEach(key => {
            speciesWordsMap.push({words: key.split(" "), key: key});
        });

        function bestSpeciesWordMatch(input: string)
        {
            const needle = input.split(" ");
            let bestValue = -1;
            let bestKey = undefined;
            speciesWordsMap.forEach(item => {
                let i = 0;
                for(; i < needle.length && i < item.words.length && needle[i] === item.words[i]; ++i) { }
                if (i > bestValue)
                {
                    bestValue = i;
                    bestKey = item.key;
                }
            });

            if (bestKey)
            {
                const result = speciesMap.get(bestKey);
                if (result) speciesMap.set(input, result);
                return result;
            }
            else
                return undefined;
        }

        const result : Seed[] = [];
        data.forEach(section => {
            section.plants.forEach(seed => {
                const options : string[] = [];
                if (seed.species)
                    options.push(normalize(seed.species));
                if (seed.speciesName)
                    options.push(normalize(seed.speciesName));
                if (seed.botanicalName)
                    options.push(normalize(seed.botanicalName));

                let species : Species | undefined = undefined;

                for(let i = 0; i < options.length; ++i)
                    if (!species) species = speciesMap.get(options[i]);

                if (!species)
                {
                    for(let i = 0; i < options.length; ++i)
                        if (!species)
                            species = bestSpeciesWordMatch(options[i]);
                }

                if (species)
                {
                    const sectionIndex = this.terrainList.findIndex(x => x.id == section.sectionId);
                    if (sectionIndex >= 0)
                    {
                        seed = convertCoordsTo_RH_rightPX_upPY(seed, CoordinateSystem.RH_rightPX_upPZ);
                        result.push(new Seed(species.name.peek(), seed.x, seed.y, seed.z, sectionIndex, true));
                    }
                }
            });
        });
        this.seeds.value = [ ...result]

        // this.seedsDistributionData = await f.text();
        // this.seedsDistributionPath.value = f.name;
        // if (hubConnection.state !== SignalR.HubConnectionState.Connected)
        //     await start();

        // if (hubConnection.state == SignalR.HubConnectionState.Connected)
        // {
        //     const bufferResponse = await fetch(`${location.protocol}//${BackendURI}/simulation/seeds`, {
        //         body: this.seedsDistributionData,
        //         method: 'post',
        //         headers: {
        //             'Accept': 'application/json',
        //             'Content-Type': 'application/json'
        //         }
        //     });
        //     const distributionId = await bufferResponse.text();
        //     hubConnection.invoke("seeds", distributionId);
        // }
    }

    clearSeedsDistribution = () => {
        this.seedsDistributionData = undefined;
        this.seedsDistributionPath.value = "";
    }

    speciesLinksCount(species: string) {
        return this.seeds.value.reduce((a, c) => a + (c.species.value == species ? 1 : 0), 0)
    }

    private saveTextFile(data: string, ext: string) {
        downloadText(data, ext);
    }

    private saveBinaryFile(data: Uint8Array, frame: string, ext: string) {
        //The cast is the generic-Uint8Array/BlobPart variance in newer lib.dom typings, not a
        //real mismatch - Blob accepts any ArrayBufferView. Copying to satisfy it would duplicate
        //a 60 MB frame for nothing.
        downloadBinary(data as unknown as BlobPart, ext, frame);
    }
};

const st = new State();
export default st;
//A run this tab started may still be computing, or may have finished while the page was away.
void recoverRun();
//now that the singleton is exported push in the default seed
st.seeds.value = [ new Seed(st.species.peek()[0].name.peek(), st.fieldSizeX.peek() * 0.5, -0.01, st.fieldSizeZ.peek() * 0.5, 0, false) ];
fetch(`${location.protocol}//${BackendURI}/Simulation/species`, { headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }}).then(response => response.json()).then((list: Species[]) => {
    const species = list.map(x => new Species().load(x));
    Promise.all(species.flatMap(s => [
        fetch(`${location.protocol}//${BackendURI}/Simulation/mesh/leaf/${s.name}`).then(resp => resp.arrayBuffer()).then(b => s.loadLeafGeometry(b)),
        //A petal is a lamina in the same wire record as a leaf, so it needs its own
        //outline or it is drawn with the leaf's.
        fetch(`${location.protocol}//${BackendURI}/Simulation/mesh/petal/${s.name}`).then(resp => resp.arrayBuffer()).then(b => s.loadPetalGeometry(b)),
    ]));
    st.species.value = species;
});

fetch(`${location.protocol}//${BackendURI}/Simulation/behaviors`, { headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }}).then(response => response.json()).then((list: string[]) => st.behaviors.value = list);

effect(() => {
    //Depending on hubConnected is what lets a request survive being made too early: the
    //effect re-runs when the socket comes up and flushes it. The short-circuit is safe here
    //only because the second read happens whenever the first is true, which is the only case
    //that does anything.
    if (st.previewRequest.value && hubConnected.value) {
        //The hub can drop between the state check and the call, and an unhandled rejection
        //would take out the effect. Re-arm only if the connection actually went away: that
        //case is picked up again when hubConnected flips back, whereas re-arming on a
        //server-side failure while still connected would spin the effect.
        hubConnection.invoke("preview").catch(e => {
            console.warn("preview request failed", e);
            if (hubConnection.state !== SignalR.HubConnectionState.Connected)
                st.previewRequest.value = true;
        });
        st.previewRequest.value = false;
    }
});

effect(() => {
    st.playRequest.value = st.playing.value != PlayState.None;
});

effect(() => {
    if (st.playRequest.value)
    {
        let playPointer = Math.max(0, Math.min(st.playPointer.peek(), st.history.length - 1));
        const pv =  st.playing.peek();
        switch (pv)
        {
            case PlayState.Forward: case PlayState.NextForward: case PlayState.SeekForward:
                if (pv == PlayState.SeekForward)
                    playPointer = st.history.length - 1;
                else
                    playPointer += 1;

                if (playPointer < st.history.length)
                {
                    showSceneAt(playPointer);
                    batch(() => {
                        st.playPointer.value = playPointer;
                        st.playRequest.value = false;
                        st.playing.value = pv == PlayState.NextForward || pv == PlayState.SeekForward ? PlayState.None : pv;
                    })
                }
                else
                    batch(() => {
                        st.playPointer.value = st.history.length - 1;
                        st.playing.value = (pv == PlayState.Forward || pv == PlayState.NextForward) && st.computing.peek() ? PlayState.ForwardWaiting : PlayState.None;
                        st.playRequest.value = false;
                    });
                break;
            case PlayState.ForwardWaiting: {
                //Follow the newest frame that has actually ARRIVED.
                //
                //This used to be `playPointer + 1`, unbounded - the only branch here that does not
                //check where it is going. At the end of a run the playhead therefore came to rest
                //one past the last frame. Nothing threw, because every reader clamps, but the
                //transport was left with "Previous frame" disabled over a perfectly good history
                //and the clock reporting a step that did not exist.
                //
                //`+ 1` is also no longer the right target. The timeline is filled in from the run
                //index while the run computes, so there are entries AHEAD of what has been sent -
                //stored frames with no payload. Advancing onto one of those would put the playhead
                //on a frame the preview handler is not drawing and make the clock run ahead of the
                //picture. The transport should behave like a player buffering: the slider spans
                //what exists, the playhead sits on what has arrived.
                const arrived = lastReceivedIndex >= 0 && lastReceivedIndex < st.history.length
                    ? lastReceivedIndex
                    : Math.min(playPointer + 1, st.history.length - 1);
                const more = st.computing.peek();
                batch(() => {
                    st.playPointer.value = Math.max(0, arrived);
                    st.playRequest.value = false;
                    //Nothing further is coming, so stop waiting for it. Staying armed is what left
                    //a finished run permanently "playing".
                    st.playing.value = more ? PlayState.ForwardWaiting : PlayState.None;
                });
                break;
            }
            case PlayState.Backward: case PlayState.NextBackward: case PlayState.SeekBackward:
                if (pv == PlayState.SeekBackward)
                    playPointer = 0;
                else
                    playPointer -= 1;

                if (playPointer >= 0)
                {
                    batch(() => {
                        st.playPointer.value = playPointer;
                        showSceneAt(playPointer);
                        st.playRequest.value = false;
                        st.playing.value = pv == PlayState.Backward ? PlayState.Backward : PlayState.None;
                    });
                }
                else
                    batch(() => {
                        st.playPointer.value = 0;
                        st.playing.value = PlayState.None;
                        st.playRequest.value = false;
                    });
                break;
            case PlayState.Seek:
                showSceneAt(playPointer);
                batch(() => {
                    st.playRequest.value = false;
                    st.playing.value = PlayState.None;
                })
                break;
            default: st.playRequest.value = false;
        }
    }
});

/**
 * How many of the most recent frames stay uncompressed.
 *
 * Compression is what makes retrieval asynchronous, and asynchrony is what the playback state
 * machine cannot absorb - a preact effect cannot be async and `batch()` cannot span an await.
 * Keeping a window of recent frames raw means sequential playback and single-stepping, which is
 * what the viewport does almost all of the time, never leave the synchronous path at all. Only
 * scrubbing back into old history pays for a decompress.
 */
const HOT_FRAMES = 8;

/** Frames below this index have been offered to the compressor already. */
let compressFrontier = 0;
let compressing = false;

/** Invalidates any decompression still in flight. Bumped whenever history is replaced. */
let sceneRequestToken = 0;

function resetCompression() {
    compressFrontier = 0;
    compressing = false;
    ++sceneRequestToken;
}

function scheduleCompression() {
    if (compressing || !canDeflate()) return;
    compressing = true;
    //Idle rather than immediate: the frame that just arrived is about to be decoded and drawn,
    //and compressing an older one must not compete with that.
    const run = () => { void drainCompression(); };
    if (typeof requestIdleCallback === 'function') requestIdleCallback(run, { timeout: 2000 });
    else setTimeout(run, 0);
}

async function drainCompression() {
    try {
        while (compressFrontier < st.history.length - HOT_FRAMES) {
            const i = compressFrontier;
            const e = st.history[i];
            if (!e || !e.s || !e.plain) { ++compressFrontier; continue; }

            const packed = await deflateBytes(e.s);
            //A run may have restarted, or a session loaded, while that await was outstanding.
            if (st.history[i] !== e) return;

            //Keep whichever is smaller. Compression can lose on a tiny frame - the six-byte zlib
            //wrapper alone outweighs what there is to gain on the empty t=0 scene - and storing
            //the larger one while flagging it compressed would corrupt it on the way back.
            if (packed.byteLength < e.s.byteLength) {
                st.historySize.value -= e.s.byteLength - packed.byteLength;
                e.s = packed;
                e.plain = false;
            }
            ++compressFrontier;
        }
    } finally { compressing = false; }
}

/** Moves a cache hit to the young end of the LRU. */
function touchCache(i: number): PlantModel[] | undefined {
    const hit = sceneCache.get(i);
    if (!hit) return undefined;
    //A Map iterates in insertion order, which is all the ordering an LRU needs.
    sceneCache.delete(i);
    sceneCache.set(i, hit);
    return hit;
}

/**
 * The scene at `index` if it can be had without awaiting, otherwise undefined.
 *
 * This is the path the playback machine takes: a decoded frame still in the LRU, or a frame in
 * the hot window that has not been compressed yet.
 */
export function sceneAtSync(index: number): PlantModel[] | undefined {
    const i = Math.min(index, st.history.length - 1);
    if (i < 0) return [];

    const hit = touchCache(i);
    if (hit) return hit;

    const e = st.history[i];
    //No payload means it was evicted to the server; not plain means it is compressed. Either way
    //the caller has to go asynchronous.
    if (!e || !e.s || !e.plain) return undefined;

    const decoded = new BinaryReader(e.s).readAgroScene();
    cacheScene(i, decoded);
    return decoded;
}

/**
 * The scene at `index`, decompressing first if it has been packed away.
 *
 * Exported because the offline animation renderer iterates history independently of the playback
 * state machine, and is already asynchronous throughout.
 */
/**
 * Re-fetches an evicted frame from the run store.
 *
 * The bytes are stored deflated and served with `Content-Encoding: deflate`, so the browser
 * inflates them on the way in and there is nothing to undo here. Frames are immutable once
 * written, so the HTTP cache holds the working set without the client managing one.
 */
async function fetchFrame(step: number): Promise<Uint8Array> {
    const runId = st.runId.peek();
    if (!runId) return new Uint8Array(0);
    try {
        const res = await fetch(`${location.protocol}//${BackendURI}/simulation/run/${runId}/frame/${step}`);
        if (!res.ok) return new Uint8Array(0);
        return new Uint8Array(await res.arrayBuffer());
    } catch (err) {
        console.warn(`Could not fetch frame ${step} of run ${runId}:`, err);
        return new Uint8Array(0);
    }
}

/**
 * The steps recorded in a run's frame index, in order.
 *
 * The format is fixed by `RunStore.WriteFrame` and this is the only thing that reads it, so it is
 * worth stating: **eight little-endian bytes per frame - the addressable step, then the stored
 * size**. Only the step is wanted here; the size is what makes a progress estimate possible
 * without fetching anything.
 *
 * The file is APPENDED to while the run computes, so a read can catch it mid-entry and come back
 * with a length that is not a multiple of eight. A trailing part-entry is dropped rather than
 * decoded: half a step number is a plausible-looking wrong step, and fetching it would return
 * some other frame that decodes perfectly. The frames still arriving on the socket carry the
 * remainder anyway.
 */
export function decodeFrameIndex(buf: Uint8Array): number[] {
    const whole = Math.floor(buf.byteLength / 8);
    if (whole === 0) return [];
    const view = new DataView(buf.buffer, buf.byteOffset, whole * 8);
    const steps: number[] = new Array(whole);
    for (let i = 0; i < whole; ++i) steps[i] = view.getUint32(i * 8, true);
    return steps;
}

/**
 * Brings the timeline into line with what the server has actually stored.
 *
 * This is the eager half of "keep `t` on the client, stream `s`". `GET /run/{id}/index` returns
 * eight bytes per stored frame - the addressable step, then its stored size - so knowing the
 * shape of a 1,440-step run costs about 11 KB against the gigabytes its payloads occupy. Entries
 * the client does not already hold come back with no payload at all and are fetched one at a time
 * as the user scrubs onto them.
 *
 * Called in two situations, which is why it merges rather than replaces:
 *
 *  - after a reload, when the history is empty and the whole timeline has to come back;
 *  - when a run finishes, because the streamed history has HOLES the stored one does not. The
 *    server exports every step; the wire only carries the ones a `Preview` request happened to
 *    land on, so what the browser ended up with depended on how fast it was keeping up. A run
 *    that stored 1,440 frames and streamed 60 left the other 1,380 unreachable from the page
 *    that asked for them. Merging at the end is what makes the stored run and the visible run
 *    the same run.
 *
 * Payloads already resident are kept - they are the same bytes - and matched by step, not by
 * position, because the two sequences differ exactly in the positions.
 */
async function syncTimelineToIndex(runId: string, seekToEnd: boolean): Promise<number> {
    try {
        const res = await fetch(`${location.protocol}//${BackendURI}/simulation/run/${runId}/index`);
        if (!res.ok) return 0;
        const steps = decodeFrameIndex(new Uint8Array(await res.arrayBuffer()));
        if (steps.length === 0) return 0;

        //A union keyed by step, not a positional zip and not a replacement.
        //
        //Keyed by step because the stored sequence has entries the streamed one does not, so
        //index-for-index would pair unrelated frames and mislabel every payload after the first
        //gap. A union rather than "take the server's list" because the reverse is also possible:
        //with a frame stride the client is sent previews of steps that were never stored, and
        //those are real frames it already holds. Neither side is a superset of the other.
        const held = new Map<number, { t: number, s: Uint8Array | undefined, plain: boolean }>();
        for (const e of st.history) if (e) held.set(e.t, e);
        for (const t of steps) if (!held.has(t)) held.set(t, { t, s: undefined, plain: true });
        if (held.size === st.history.length) return st.history.length;

        const atPointer = st.history[st.playPointer.peek()];
        //Remembered by step before the array is replaced: `lastReceivedIndex` is a position, and
        //every position here is about to move.
        const receivedStep = lastReceivedIndex >= 0 && lastReceivedIndex < st.history.length
            ? st.history[lastReceivedIndex].t : -1;
        const merged = [...held.values()].sort((a, b) => a.t - b.t);

        st.history = merged;
        lastReceivedIndex = receivedStep < 0 ? -1 : merged.findIndex(e => e.t === receivedStep);
        //Where the user was, by step rather than by position - the positions have just moved.
        const restoredPointer = seekToEnd || !atPointer
            ? merged.length - 1
            : Math.max(0, merged.findIndex(e => e.t === atPointer.t));

        const pointerMoved = restoredPointer !== st.playPointer.peek();
        batch(() => {
            //Recomputed rather than adjusted. Zero bytes over hundreds of frames is not a
            //contradiction here - every payload is on the server - and it is exactly why the
            //transport bar cannot ask `historySize` whether there is anything to play.
            st.historySize.value = measureHistoryBytes(merged);
            st.historyCount.value = merged.length;
            st.evictedFrames.value = merged.reduce((n, e) => n + (e.s ? 0 : 1), 0);
            st.playPointer.value = restoredPointer;
        });
        //Only when the visible frame actually changed. This now runs repeatedly WHILE a run
        //streams, and redrawing the same frame every couple of seconds would fight the playback
        //machine for the renderer at exactly the scale where that is expensive.
        if (pointerMoved) showSceneAt(restoredPointer);
        console.log(`Timeline synced to ${merged.length} stored frames of run ${runId}.`);
        return merged.length;
    } catch (err) {
        console.warn(`Could not read the frame index of run ${runId}:`, err);
        return 0;
    }
}

/**
 * Warms the HTTP cache for a frame that is going to be needed shortly.
 *
 * Written for the animation exporter, where every frame used to be a network round trip that the
 * render had to wait for: fetch, inflate, decode, render, repeat, with nothing overlapping. The
 * frames are served `immutable`, so simply asking for one puts it in the browser's own cache and
 * the real read a moment later is a local hit - which means this needs no cache, no eviction and
 * no memory budget of its own. Deliberately fetching the BYTES and not decoding: a decoded scene
 * is thousands of objects and tens of megabytes, and buffering those ahead is what the frame
 * source has always been right to refuse.
 *
 * Silent about everything. A prefetch that fails costs nothing; the real fetch will report it.
 */
export async function prefetchFrame(index: number): Promise<void> {
    const runId = st.runId.peek();
    if (!runId || index < 0 || index >= st.history.length) return;
    const e = st.history[index];
    if (!e || e.s) return;
    try {
        const res = await fetch(`${location.protocol}//${BackendURI}/simulation/run/${runId}/frame/${e.t}`);
        //Drained rather than discarded: an unread body may never reach the cache.
        if (res.ok) await res.arrayBuffer();
    } catch { /* the real read will say so */ }
}

/**
 * The raw V7 bytes for one history entry, whatever state it is in.
 *
 * Three cases and they are easy to conflate: held raw, held compressed, or evicted to the server.
 * save() and the .prim download both need the raw bytes and neither should have to know which.
 */
async function rawFrameBytes(e: { t: number, s: Uint8Array | undefined, plain: boolean }): Promise<Uint8Array> {
    if (!e.s) return fetchFrame(e.t);
    return e.plain ? e.s : inflateBytes(e.s);
}

export async function sceneAt(index: number): Promise<PlantModel[]> {
    const ready = sceneAtSync(index);
    if (ready) return ready;

    const i = Math.min(index, st.history.length - 1);
    if (i < 0) return [];
    const e = st.history[i];
    if (!e) return [];

    const raw = await rawFrameBytes(e);
    if (!raw.length) return [];
    //If history was replaced while inflating, those bytes describe a scene that no longer
    //exists. Fall back to whatever is there now rather than rendering the old run.
    if (st.history[i] !== e) return sceneAtSync(index) ?? [];

    const decoded = new BinaryReader(raw).readAgroScene();
    cacheScene(i, decoded);
    return decoded;
}

/**
 * Puts the frame at `index` on screen.
 *
 * The synchronous case writes `scene` immediately, which is what every caller inside a `batch()`
 * and the offline renderer's rebuild assumption rely on. A cold frame resolves later, and the
 * token guards the one hazard that introduces: dragging the timeline fast issues several
 * requests, and without it the slowest could land last and leave the wrong frame showing.
 */
function showSceneAt(index: number) {
    const ready = sceneAtSync(index);
    if (ready) { st.scene.value = ready; return; }

    const token = ++sceneRequestToken;
    void sceneAt(index).then(scene => {
        if (token === sceneRequestToken) st.scene.value = scene;
    });
}


/**
 * Decoded scenes, keyed by history index, bounded by total primitive count rather than by
 * entry count.
 *
 * A budget in entries cannot work here: one timestep of a seedling is a few dozen primitives
 * and one of a mature stand is tens of thousands, so any fixed entry count is either useless
 * early or hundreds of megabytes late. Counting primitives makes the cache hold many small
 * frames or few large ones, which is the behaviour wanted in both cases.
 *
 * This does nothing for straight playback, where the pointer only ever advances onto a frame
 * that has not been seen. It is for scrubbing - dragging the timeline back and forth over the
 * same span - and for stepping back a frame to compare, which were re-decoding every time.
 *
 * The budget was 200,000, which made the cache INERT at production scale: a 4,134-plant frame is
 * about 674,000 primitives, so `size > budget` refused every single frame and every scrub paid a
 * full decode. It now sits above one mature frame. Note a decoded primitive costs ~365 bytes -
 * mostly V8 object headers on two small typed arrays - so this budget is roughly 550 MB of heap
 * at worst, and it is deliberately the largest single discretionary allocation the client makes.
 */
export const SCENE_CACHE_PRIMITIVES = 1500000;
const sceneCache = new Map<number, PlantModel[]>();
let sceneCachePrimitives = 0;

function cacheScene(index: number, scene: PlantModel[]) {
    let size = 0;
    for (const plant of scene) size += plant.primitives.length;

    //A single frame larger than the whole budget is not worth evicting everything for.
    if (size > SCENE_CACHE_PRIMITIVES) return;

    sceneCache.set(index, scene);
    sceneCachePrimitives += size;

    while (sceneCachePrimitives > SCENE_CACHE_PRIMITIVES && sceneCache.size > 1) {
        const oldest = sceneCache.keys().next();
        if (oldest.done) break;
        const evicted = sceneCache.get(oldest.value)!;
        for (const plant of evicted) sceneCachePrimitives -= plant.primitives.length;
        sceneCache.delete(oldest.value);
    }
}

/**
 * Drops every cached scene. Must be called whenever `history` is replaced or truncated, since
 * the cache is keyed by index and a new run reuses indices from zero - a stale entry would
 * render a previous simulation's plants and look like a decoder bug.
 */
export function clearSceneCache() {
    sceneCache.clear();
    sceneCachePrimitives = 0;
}
