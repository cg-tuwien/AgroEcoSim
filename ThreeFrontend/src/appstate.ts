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
import { downloadBinary, downloadText } from "./helpers/download";
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
hubConnection.on("reject", () => { console.log("You have another simulation already running. Please wait until it is finished."); });
hubConnection.on("progress", (step: number, length: number) => {
    batch(() => {
        st.simStep.value = step + 1;
        st.simLength.value = length;
    });
});

hubConnection.on("result", (result: ISimResponse) => {
    if (result.debug?.length > 0)
        console.log(result.debug);

    const step = result.stepTimes.length - 1;
    if (st.history.length == 0 || st.history[st.history.length - 1].t < step)
    {
        st.history.push({ t: result.stepTimes.length - 1, s: result.scene });
        const binaryScene = base64ToArrayBuffer(result.scene);
        const reader = new BinaryReader(binaryScene);
        const scene = reader.readAgroScene();

        //console.log(result.stepTimes);
        batch(() => {
            st.plants.value = result.plants;
            st.scene.value = scene;
            st.computing.value = false;
            st.renderer.value = result.renderer;
            st.simLength.value = 0;
            st.historySize.value += binaryScene.byteLength + 24;
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
});

hubConnection.on("preview", (result: ISimPreview) => {
    st.history.push({t: result.step, s: result.scene});
    const binaryScene = base64ToArrayBuffer(result.scene);
    if (st.playing.peek() == PlayState.ForwardWaiting)
    {
        st.previewRequestAfterSceneUpdate = true;
        const reader = new BinaryReader(binaryScene);
        const scene = reader.readAgroScene();
        batch(() => {
            st.scene.value = scene;
            st.renderer.value = result.renderer;
            st.historySize.value += binaryScene.byteLength + 24;
        });
    }
    else
    {
        batch(() => {
            st.renderer.value = result.renderer;
            st.historySize.value += binaryScene.byteLength + 24;
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
    }
};
hubConnection.onclose(() => { hubConnected.value = false; start(); });
hubConnection.onreconnecting(() => { hubConnected.value = false; });
hubConnection.onreconnected(() => { hubConnected.value = true; });

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

    history : {t: number, s: Uint8Array}[] = [];
    historySize = signal(0);

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
        FieldItemRegexMaterial: this.fieldItemRegexMaterial.value,
        FieldModelPath: this.fieldModelPath.value,
        FieldModelData: this.fieldModelData
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
            });
            this.history.length = 0;
            clearSceneCache();
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
            history: this.history,
            simHoursPerTick: this.simHoursPerTick,

            fieldModelPath: this.fieldModelPath.peek(),
            fieldItemRegex: this.fieldItemRegex.peek(),
            fieldItemRegexMaterial: this.fieldItemRegexMaterial.peek(),
            fieldModelData: this.fieldModelData,
        };

        this.saveTextFile(JSON.stringify(data), 'json');
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
                self.history = data.history;
                clearSceneCache();
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
                    self.historySize.value = data.historySize;
                    self.history = data.history;
                    clearSceneCache();
                    self.simHoursPerTick = data.simHoursPerTick;

                    self.fieldModelPath.value = data.fieldModelPath;
                    self.fieldItemRegex.value = data.fieldItemRegex;
                    self.fieldItemRegexMaterial.value = data.fieldItemRegexMaterial;
                    self.fieldModelData = data.fieldModelData;

                    self.scene.value = decodeSceneAt(0);
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

    prim = () => {
        const i = Math.min(this.playPointer.peek(), st.history.length - 1);
        this.saveBinaryFile(this.history[i].s, `_${st.history[i].t}`, 'prim');
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
        downloadBinary(base64ToArrayBuffer(data), ext, frame);
    }
};

const st = new State();
export default st;
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
                    st.scene.value = decodeSceneAt(playPointer);
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
            case PlayState.ForwardWaiting:
                batch(() => {
                    st.playPointer.value = playPointer + 1;
                    st.playRequest.value = false;
                    st.playing.value = st.computing.peek() ? PlayState.ForwardWaiting : PlayState.None;
                });
                break;
            case PlayState.Backward: case PlayState.NextBackward: case PlayState.SeekBackward:
                if (pv == PlayState.SeekBackward)
                    playPointer = 0;
                else
                    playPointer -= 1;

                if (playPointer >= 0)
                {
                    batch(() => {
                        st.playPointer.value = playPointer;
                        st.scene.value = decodeSceneAt(playPointer);
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
                st.scene.value = decodeSceneAt(playPointer);
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
 * Decodes the stored frame at `index` into renderable data.
 *
 * Exported because the offline animation renderer iterates history independently of the
 * playback state machine. Results are memoised - see sceneCache below.
 */
export const decodeSceneAt = (index: number) => {
    const i = Math.min(index, st.history.length - 1);
    if (i < 0) return [];

    const hit = sceneCache.get(i);
    if (hit) {
        //Re-insert to move it to the young end: a Map iterates in insertion order, which is
        //all the ordering an LRU needs.
        sceneCache.delete(i);
        sceneCache.set(i, hit);
        return hit;
    }

    const decoded = new BinaryReader(base64ToArrayBuffer(st.history[i].s)).readAgroScene();
    cacheScene(i, decoded);
    return decoded;
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
 * same span - and for stepping back a frame to compare, which were re-parsing from base64
 * every time.
 */
const SCENE_CACHE_PRIMITIVES = 200000;
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
