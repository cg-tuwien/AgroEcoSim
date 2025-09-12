import { batch, computed, effect, signal } from "@preact/signals";
import { BackendURI } from "./config";
import BinaryReader from "./helpers/BinaryReader";
import { Scene } from "./helpers/Scene";
import * as SignalR from "@microsoft/signalr";
import { Seed } from "./helpers/Seed";
import * as THREE from 'three';
import { Obstacle } from "./helpers/Obstacle";
import { TransformControls } from 'three/examples/jsm/controls/TransformControls';
import { BaseRequestObject } from "./helpers/BaseRequestObject";
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter';
import { scene } from "./components/viewport/ThreeSceneFn";
import { VisualMappingOptions } from "./helpers/Plant";
import { Species } from "./helpers/Species";
import { FieldItemRegex } from "./components/hud/FieldItemRegex";
import { IObjImport, Parse } from "./helpers/ObjParser";

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

const hubConnection = new SignalR.HubConnectionBuilder().withUrl(`${BackendURI.startsWith("localhost") ? "https:" : location.protocol}//${BackendURI}/SimSocket`).withAutomaticReconnect(InfiniteHubRetry).build();
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
            st.historySize.value += binaryScene .byteLength + 24;
            st.previewRequest.value = true;
        });
    }
});

const start = async() => hubConnection.start();
hubConnection.onclose(start);

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

function base64ToArrayBuffer(base64) {
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
    fieldResolution = signal(0.5);
    fieldSizeX = signal(10);
    fieldSizeZ = signal(10);
    fieldSizeD = signal(4);
    fieldCellsX = computed(() => Math.ceil(this.fieldSizeX.value / this.fieldResolution.value));
    fieldCellsZ = computed(() => Math.ceil(this.fieldSizeZ.value / this.fieldResolution.value));
    fieldCellsD = computed(() => Math.ceil(this.fieldSizeD.value / this.fieldResolution.value));
    initNumber = signal(42);
    randomize = signal(false);
    renderMode = signal(1); //1 is Mitsuba
    exactPreview = signal(false);

    //SPECIES
    species = signal<Species[]>([Species.Default()]);

    //INITIAL SCENE SETUP
    seeds = signal<Seed[]>([]);
    seedsCount = computed(() => this.seeds.value.length);

    obstacles = signal<Obstacle[]>([]);
    obstaclesCount = computed(() => this.obstacles.value.length);

    //RENDERING
    objSeeds = new THREE.Object3D();
    objTerrain = new THREE.Object3D();
    objObstacles = new THREE.Object3D();
    objPlants = new THREE.Object3D();

    needsRender = signal(false);
    visualMapping = signal(VisualMappingOptions.Natural);
    plantPick = signal("");

    transformControls: TransformControls | undefined;
    downloadRoots = signal(false);
    debugBoxes = signal(false);
    showLeaves = signal(true);
    showTerrain = signal(true);
    showRoots = signal(true);

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
    scene = signal<Scene>([]);
    renderer = signal("");
    samplesPerPixel = signal(2048);
    simHoursPerTick = 1;

    history : {t: number, s: Uint8Array}[] = [];
    historySize = signal(0);

    fieldModelPath = signal("");
    fieldItemRegex = signal("");
    fieldModelData?: IObjImport = undefined;

    //METHODS
    private requestBody = () => {
        return {
        HoursPerTick: Math.trunc(this.hoursPerTick.peek()),
        TotalHours: Math.trunc(this.totalHours.peek()),
        FieldResolution: this.fieldResolution.peek(),
        FieldSize: { X: this.fieldSizeX.peek(), D: this.fieldSizeD.peek(), Z: this.fieldSizeZ.peek() },
        Seed: Math.trunc(this.randomize.peek() ? Math.random() * 4294967295 : this.initNumber.peek()),
        Species: this.species.peek().map(s => s.serialize()),

        Plants: this.seeds.peek().map((p: Seed) => ({ S: p.species.peek(), P: { X: p.px.peek(), Y: p.py.peek(), Z: p.pz.peek() } })),
        Obstacles: this.obstacles.peek().map((o: Obstacle) => exportObstacle(o)),
        RequestGeometry: true,
        RenderMode: this.renderMode.value,
        SamplesPerPixel: this.samplesPerPixel.value,
        ExactPreview: this.exactPreview.value,
        DownloadRoots: this.downloadRoots.value,

        FieldItemRegex: this.fieldItemRegex.value,
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
            if (hubConnection.state !== SignalR.HubConnectionState.Connected)
                await start();

            if (hubConnection.state == SignalR.HubConnectionState.Connected)
            {
                console.log(this.requestBody());
                hubConnection.invoke("run", this.requestBody()).catch(e => {
                    console.error(e);
                    batch(() => {
                        this.computing.value = false;
                        this.renderer.value = "error";
                    });
                });
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

    pushRndSeed = () => { this.seeds.value = [ ...this.seeds.peek(), Seed.rndItem()] };

    removeSeedAt = (i : number) => {
        const seeds = this.seeds.peek();
        if (i >= 0 && i < seeds.length) {
            const seed = seeds.splice(i, 1)[0];
            this.objSeeds.remove(seed.mesh);
            seed.mesh = undefined;
        }
        this.seeds.value = [...seeds];
        this.needsRender.value = true;
    };

    pushRndSpecies = () => {
        this.species.value = [ ...this.species.peek(), new Species()]
    };

    removeSpeciesAt = (i : number) => {
        const species = this.species.peek();
        if (i >= 0 && i < species.length && species.length > 1) {
            species.splice(i, 1)[0];
        }
        this.species.value = [...species];
    };

    clearHovers = (src: BaseRequestObject[], except: BaseRequestObject | undefined) => {
        src.forEach((x : Seed) => {
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


    clearSelects = (src: BaseRequestObject[], except: Seed | Obstacle | undefined) => {
        src.forEach((s : Seed) => {
            if (s !== except)
                switch(s.state.peek()) {
                    case "select":
                    case "selecthover": s.unhover();
                }
        });
    };

    clearSeedSelects = (except: Seed | undefined) => this.clearSelects(this.seeds.peek(), except);
    clearObstacleSelects = (except: Obstacle | undefined) => this.clearSelects(this.obstacles.peek(), except);

    clearGrabs = (src: BaseRequestObject[], except: Seed | Obstacle | undefined) => {
        src.forEach((s : Seed) => {
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
        {
            const item = obstacles.splice(i, 1)[0];
            this.objObstacles.remove(item.mesh);
            item.mesh = undefined;
        }

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
            fieldModelData: this.fieldModelData,
            fieldModelPath: this.fieldModelPath.peek(),
            fieldItemRegex: this.fieldItemRegex.peek(),
            initNumber: this.initNumber.peek(),
            randomize: this.randomize.peek(),
            constantLight: this.renderMode.peek(),
            downloadRoots: this.downloadRoots.peek(),
            exactPreview: this.exactPreview.peek(),
            visualMapping: this.visualMapping.peek(),
            debugDisplayOrientations: this.debugBoxes.peek(),
            showLeaves: this.showLeaves.peek(),
            seeds: this.seeds.value.map(s => s.save()),
            obstacles: this.obstacles.value.map(o => o.save()),
            species: this.species.value.map(s => s.save()),

            plants: this.plants.peek(),
            history: this.history,
            historySize: this.historySize.peek(),
            simHoursPerTick: this.simHoursPerTick,
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

                const text = reader.result.toString();
                const data = JSON.parse(text);
                self.history = data.history;
                self.simHoursPerTick = data.simHoursPerTick ?? 1;

                batch(() => {
                    self.hoursPerTick.value = data.hoursPerTick;
                    self.totalHours.value = data.totalHours;
                    self.fieldResolution.value = data.fieldResolution;
                    self.fieldSizeX.value = data.fieldSizeX;
                    self.fieldSizeZ.value = data.fieldSizeZ;
                    self.fieldSizeD.value = data.fieldSizeD;
                    self.fieldModelData = data.fieldModelData;
                    self.fieldModelPath.value = data.fieldModelPath;
                    self.fieldItemRegex.value = data.fieldItemRegex;
                    self.initNumber.value = data.initNumber;
                    self.randomize.value = data.randomize;
                    self.renderMode.value = data.constantLight;
                    self.downloadRoots.value = data.downloadRoots;
                    self.exactPreview.value = data.exactPreview;
                    self.visualMapping.value = data.visualMapping;
                    self.debugBoxes.value = data.debugDisplayOrientations;
                    self.showLeaves.value = data.showLeaves;
                    self.seeds.value = data.seeds.map(s => new Seed(s.species, s.px, s. py, s.pz));
                    self.obstacles.value = data.obstacles.map(o => new Obstacle(o.type, o.px, o.py, o.pz, o.ax, o.ay, o.l, o.h, o.t));
                    self.species.value = data.species.map(s => new Species().load(s));

                    self.plants.value = data.plants;
                    self.historySize.value = data.historySize;
                    self.scene.value = getScene(0);
                });

                self.play(PlayState.SeekBackward);

                input.remove();
            };
            reader.readAsText(target.files[0]);
        };
        input.click();
    }

    gltf = () => {
        const exporter = new GLTFExporter();
        exporter.parse(
            scene,
            // called when the gltf has been generated
            gltf => this.saveTextFile(JSON.stringify(gltf), 'gltf'),
            // called when there is an error in the generation
            error => console.log('An error happened', error),
        );
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
    }

    clearFieldModel = () => {
        this.fieldModelData = undefined;
        this.fieldModelPath.value = "";
    }

    private saveTextFile(data: string, ext: string) {
        const blob = new Blob([data], { type: 'text/plain' });
        const url = window.URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.style.setProperty('display', 'none');
        document.body.appendChild(a);
        a.href = url;
        a.download = `AgroEco-${new Date().toISOString().split("T")[0]}_${new Date().getHours()}-${new Date().getMinutes()}-${new Date().getSeconds()}.${ext}`;
        a.click();
        window.URL.revokeObjectURL(url);
        a.remove();
    }

    private saveBinaryFile(data: Uint8Array, frame: string, ext: string) {
        const binaryData = base64ToArrayBuffer(data);
        const blob = new Blob([binaryData], { type: 'application/octet-stream' });
        const url = window.URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.style.setProperty('display', 'none');
        document.body.appendChild(a);
        a.href = url;
        a.download = `AgroEco-${new Date().toISOString().split("T")[0]}_${new Date().getHours()}-${new Date().getMinutes()}-${new Date().getSeconds()}${frame}.${ext}`;
        a.click();
        window.URL.revokeObjectURL(url);
        a.remove();
    }
};

const st = new State();
export default st;
//now that the singleton is exported push in the default seed
st.seeds.value = [ new Seed(st.species.peek()[0].name.peek(), st.fieldSizeX.peek() * 0.5, -0.01, st.fieldSizeZ.peek() * 0.5) ];

effect(() => {
    if (st.previewRequest.value && hubConnection.state == SignalR.HubConnectionState.Connected) {
        hubConnection.invoke("preview");
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
                    st.scene.value = getScene(playPointer);
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
                        st.scene.value = getScene(playPointer);
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
                st.scene.value = getScene(playPointer);
                batch(() => {
                    st.playRequest.value = false;
                    st.playing.value = PlayState.None;
                })
                break;
            default: st.playRequest.value = false;
        }
    }
});

const getScene = (index: number) => {
    const i = Math.min(index, st.history.length - 1);
    const src = st.history[i].s;
    const reader = new BinaryReader(base64ToArrayBuffer(src));
    return reader.readAgroScene();
}
