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
        st.simStep.value = step;
        st.simLength.value = length;
    });
});

hubConnection.on("result", (result: ISimResponse) => {
    st.history.push(result.scene);
    const binaryScene = base64ToArrayBuffer(result.scene);
    const reader = new BinaryReader(binaryScene);
    const scene = reader.readAgroScene();
    if (result.debug?.length > 0)
        console.log(result.debug);
    console.log(result.stepTimes);
    batch(() => {
        st.plants.value = result.plants;
        st.scene.value = scene;
        st.computing.value = false;
        st.renderer.value = result.renderer;
        st.simLength.value = 0;
        st.historySize.value += binaryScene.byteLength;
    });
});

hubConnection.on("preview", (result: ISimPreview) => {
    st.history.push(result.scene);
    const binaryScene = base64ToArrayBuffer(result.scene);
    if (st.playing.peek() == PlayState.ForwardWaiting)
    {
        st.previewRequestAfterSceneUpdate = true;
        const reader = new BinaryReader(binaryScene);
        const scene = reader.readAgroScene();
        batch(() => {
            st.scene.value = scene;
            st.renderer.value = result.renderer;
            st.historySize.value += binaryScene.byteLength;
        });
    }
    else
    {
        batch(() => {
            st.renderer.value = result.renderer;
            st.historySize.value += binaryScene .byteLength;
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

export enum PlayState { None, Backward, Forward, ForwardWaiting, SeekBackward, SeekForward, NextBackward, NextForward };

class State {
    // SETTINGS
    hoursPerTick = signal(1);
    totalHours = signal(720);
    fieldResolution = signal(0.5);
    fieldCellsX = signal(10);
    fieldCellsZ = signal(10);
    fieldCellsD = signal(4);
    fieldSizeX = computed(() => this.fieldCellsX.value * this.fieldResolution.value);
    fieldSizeZ = computed(() => this.fieldCellsZ.value * this.fieldResolution.value);
    fieldSizeD = computed(() => this.fieldCellsD.value * this.fieldResolution.value);
    initNumber = signal(42);
    randomize = signal(false);
    constantLight = signal(false);

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

    history : Uint8Array[] = [];
    historySize = signal(0);

    //METHODS
    private requestBody = () => {
        return {
        HoursPerTick: Math.trunc(this.hoursPerTick.peek()),
        TotalHours: Math.trunc(this.totalHours.peek()),
        FieldResolution: this.fieldResolution.peek(),
        FieldSize: { X: this.fieldCellsX.peek(), D: this.fieldCellsD.peek(), Z: this.fieldCellsZ.peek() },
        Seed: Math.trunc(this.randomize.peek() ? Math.random() * 4294967295 : this.initNumber.peek()),

        Plants: this.seeds.peek().map((p: Seed) => ({ P: { X: p.px.peek(), Y: p.py.peek(), Z: p.pz.peek() } })),
        Obstacles: this.obstacles.peek().map((o: Obstacle) => exportObstacle(o)),
        RequestGeometry: true,
        ConstantLights: this.constantLight.value
    }};

    run = async() => {
        if (this.computing.peek())
            hubConnection.invoke("abort");
        else
        {
            console.log(this.scene);
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


    clearSelects = (src: BaseRequestObject[], except: Seed | undefined) => {
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

    clearGrabs = (src: BaseRequestObject[], except: Seed | undefined) => {
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
            fieldCellsX: this.fieldCellsX.peek(),
            fieldCellsZ: this.fieldCellsZ.peek(),
            fieldCellsD: this.fieldCellsD.peek(),
            initNumber: this.initNumber.peek(),
            randomize: this.randomize.peek(),
            constantLight: this.constantLight.peek(),
            visualMapping: this.visualMapping.peek(),
            seeds: this.saveSeeds(),
            obstacles: this.saveObstacles()
        };

        this.saveTextFile(JSON.stringify(data), 'json');
    }

    saveSeeds = () => {
        return this.seeds.value.map(s => ({
            px: s.px.peek(),
            py: s.py.peek(),
            pz: s.pz.peek()
        }));
    }

    saveObstacles = () => {
        return this.obstacles.value.map(o => ({
            px: o.px.peek(),
            py: o.py.peek(),
            pz: o.pz.peek(),
            type: o.type.peek(),
            l: o.wallLength_UmbrellaRadius.peek(),
            h: o.height.peek(),
            t: o.thickness.peek(),
            ax: o.angleX.peek(),
            ay: o.angleY.peek(),
        }));
    }

    load = async() => {
        const input = document.createElement("input");;
        input.style.setProperty("display", "none")
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

                batch(() => {
                    self.hoursPerTick.value = data.hoursPerTick;
                    self.totalHours.value = data.totalHours;
                    self.fieldResolution.value = data.fieldResolution;
                    self.fieldCellsX.value = data.fieldCellsX;
                    self.fieldCellsZ.value = data.fieldCellsZ;
                    self.fieldCellsD.value = data.fieldCellsD;
                    self.initNumber.value = data.initNumber;
                    self.randomize.value = data.randomize;
                    self.constantLight.value = data.constantLight;
                    self.visualMapping.value = data.visualMapping;
                    self.seeds.value = data.seeds.map(s => new Seed(s.px, s.py, s.pz));
                    self.obstacles.value = data.obstacles.map(o => new Obstacle(o.type, o.px, o.py, o.pz, o.ax, o.ay, o.l, o.h, o.t));
                });

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
};

const st = new State();
export default st;
//now that the singleton is exported push in the default seed
st.seeds.value = [ new Seed(0.5, -0.01, 0.05) ];

effect(() => {
    if (st.previewRequest.value && hubConnection.state == SignalR.HubConnectionState.Connected) {
        hubConnection.invoke("preview");
        st.previewRequest.value = false;
    }
});

effect(() => {
    switch (st.playing.value)
    {
        case PlayState.Backward: case PlayState.Forward: st.playRequest.value = true; break;
    }
});

effect(() => {
    if (st.playRequest.value)
    {
        let playPointer = st.playPointer.peek();
        switch (st.playing.peek())
        {
            case PlayState.Forward:
                playPointer += 1;
                if (playPointer < st.history.length)
                {
                    st.scene.value = getScene(playPointer);
                    batch(() => {
                        st.playPointer.value = playPointer;
                        st.playRequest.value = false;
                    })
                }
                else
                    batch(() => {
                        st.playPointer.value = st.history.length - 1;
                        st.playing.value = st.computing.peek() ? PlayState.ForwardWaiting : PlayState.None;
                        st.playRequest.value = false;
                    });
                break;
            case PlayState.ForwardWaiting:
                batch(() => {
                    st.playPointer.value = playPointer + 1;
                    st.playRequest.value = false;
                });
                break;
            case PlayState.Backward:
                playPointer -= 1;
                if (playPointer >= 0)
                {
                    batch(() => {
                        st.playPointer.value = playPointer;
                        st.scene.value = getScene(playPointer);
                        st.playRequest.value = false;
                    });
                }
                else
                    batch(() => {
                        st.playPointer.value = 0;
                        st.playing.value = PlayState.None;
                        st.playRequest.value = false;
                    });
                break;
            default: st.playRequest.value = false;
        }
    }
});

const getScene = (index: number) => {
    const src = st.history[index];
    const reader = new BinaryReader(base64ToArrayBuffer(src));
    return reader.readAgroScene();
}
