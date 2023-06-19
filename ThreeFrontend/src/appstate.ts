import { batch, computed, signal } from "@preact/signals";
import { BackendURI } from "./config";
import BinaryReader from "./helpers/BinaryReader";
import { Scene } from "./helpers/Scene";
import * as SignalR from "@microsoft/signalr";
import { Seed } from "./helpers/Seed";
import * as THREE from 'three';
import { Obstacle } from "./helpers/Obstacle";
import { TransformControls } from 'three/examples/jsm/controls/TransformControls';

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
    const binaryScene = base64ToArrayBuffer(result.scene);
    const reader = new BinaryReader(binaryScene);
    const scene = reader.readAgroScene();
    if (result.debug?.length > 0)
        console.log(result.debug);
    batch(() => {
        st.plants.value = result.plants;
        st.scene.value = scene;
        st.computing.value = false;
        st.renderer.value = result.renderer;
        st.simLength.value = 0;
    });
});

hubConnection.on("preview", (result: ISimPreview) => {
    const reader = new BinaryReader(base64ToArrayBuffer(result.scene));
    st.scene.value = reader.readAgroScene();
    st.renderer.value = result.renderer;
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

    transformControls: TransformControls | undefined;

    grabbed = computed(() => this.seeds.value.filter((x: Seed) => x.state.value == "grab"));

    //OPERATIONAL STATE
    computing = signal(false);
    simStep = signal(0);
    simLength = signal(0);

    //RESPONSE
    plants = signal<IPlantResponse[]>([]);
    scene = signal<Scene>([]);
    renderer = signal("");

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
        "RequestGeometry": true
    }};

    run = async() => {
        if (!this.computing.peek())
        {
            batch(() => {
                this.computing.value = true;
                this.renderer.value = "";
            });
            if (hubConnection.state !== SignalR.HubConnectionState.Connected)
                await start();

            if (hubConnection.state == SignalR.HubConnectionState.Connected)
                hubConnection.invoke("run", this.requestBody()).catch(e => {
                    console.error(e);
                    batch(() => {
                        this.computing.value = false;
                        this.renderer.value = "error";
                    });
                });
            else
                this.computing.value = false;
        }
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

    clearSeedHovers = (except: Seed | undefined) => {
        this.seeds.peek().forEach((s : Seed) => {
            if (s !== except)
                switch (s.state.peek()) {
                    case "hover": s.unhover(); break;
                    case "selecthover": s.select(); break;
                }
        });
    };

    clearSeedSelects = (except: Seed | undefined) => {
        this.seeds.peek().forEach((s : Seed) => {
            if (s !== except)
                switch(s.state.peek()) {
                    case "select":
                    case "selecthover": s.unhover();
                }
        });
    };

    clearSeedGrabs = (except: Seed | undefined) => {
        this.seeds.peek().forEach((s : Seed) => {
            if (s !== except)
                if (s.state.peek() == "grab")
                    s.ungrab("select");
        });
    };

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

};

const st = new State();
export default st;
//now that the singleton is exported push in the default seed
st.seeds.value = [ new Seed(0.5, -0.01, 0.05) ];

