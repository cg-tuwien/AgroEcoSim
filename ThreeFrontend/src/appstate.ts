import { batch, computed, signal } from "@preact/signals";
import { BackendURI } from "./config";
import BinaryReader from "./helpers/BinaryReader";
import { Scene } from "./helpers/Scene";
import * as SignalR from "@microsoft/signalr";
import { Seed } from "./helpers/Seed";
import * as THREE from 'three';
import { Obstacle } from "./helpers/Obstacle";

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
        state.simStep.value = step;
        state.simLength.value = length;
    });
});

hubConnection.on("result", (result: ISimResponse) => {
    const binaryScene = base64ToArrayBuffer(result.scene);
    const reader = new BinaryReader(binaryScene);
    const scene = reader.readAgroScene();
    console.log(result.debug);
    batch(() => {
        state.plants.value = result.plants;
        state.scene.value = scene;
        state.computing.value = false;
        state.renderer = result.renderer;
        state.simLength.value = 0;
    });
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

function requestBody() {
    return {
    HoursPerTick: Math.trunc(state.hoursPerTick.value),
    TotalHours: Math.trunc(state.totalHours.value),
    FieldResolution: state.fieldResolution.value,
    FieldSize: { X: state.fieldCellsX.value, D: state.fieldCellsD.value, Z: state.fieldCellsZ.value },
    Seed: Math.trunc(state.randomize.value ? Math.random() * 4294967295 : state.initNumber.value),

    Plants: state.seeds.value.map((p: Seed) => ({ P: { X: p.px.value, Y: p.py.value, Z: p.pz.value } })),
    Obstacles: state.obstacles.value.map((o: Obstacle) => exportObstacle(o)),
    "RequestGeometry": true
}};

async function run() {
    if (!state.computing.value)
    {
        state.computing.value = true;
        if (hubConnection.state !== SignalR.HubConnectionState.Connected)
            await start();

        if (hubConnection.state == SignalR.HubConnectionState.Connected)
            hubConnection.invoke("run", requestBody()).catch(e => {
                console.error(e);
                batch(() => {
                    state.computing.value = false;
                    state.renderer.value = "error";
                });
            });
        else
            state.computing.value = false;
    }
}

function base64ToArrayBuffer(base64) {
    var binaryString = atob(base64);
    var bytes = new Uint8Array(binaryString.length);
    for (var i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
}

function removeSeed(i : number) {
    if (i >= 0 && i < state.seeds.value.length) {
        const seed = state.seeds.value.splice(i, 1) as Seed;
        state.threescene.remove(seed.mesh);
        seed.mesh = undefined;
    }
    state.seeds.value = [...state.seeds.value];
}

function removeObstacle(i : number) {
    // if (i >= 0 && i < state.obstacles.value.length)
    //     state.obstacles.value.splice(i, 1);
    //     state.obstacles.value = [...state.obstacles.value];
}

function exportObstacle(o: Obstacle) {
    const base = { T: o.type.value, H: o.height.value, D: o.thickness.value,
                    P: { X: o.px.value, Y: o.py.value, Z: o.pz.value },
                    O: o.angleY.value };
    switch (o.type.value) {
        case "umbrella": return { ...base, R: o.wallLength_UmbrellaRadius.value };
        default: return { ...base, L: o.wallLength_UmbrellaRadius.value };
    }
}

function clearSeedHovers(except: Seed | undefined) {
    state.seeds.value.forEach((s : Seed) => {
        if (s !== except)
            switch (s.state.value) {
                case "hover": s.unhover(); break;
                case "selecthover": s.select(); break;
            }
    });
}

function clearSeedSelects(except: Seed | undefined) {
    state.seeds.value.forEach((s : Seed) => {
        if (s !== except)
            switch(s.state.value) {
                case "select":
                case "selecthover": s.unhover();
            }
    });
}
function clearSeedGrabs(except: Seed | undefined) {
    state.seeds.value.forEach((s : Seed) => {
        if (s !== except)
            if (s.state.value == "grab")
                s.ungrab("select");
    });
}

const threescene = new THREE.Scene();
const state = {
    // SETTINGS
    hoursPerTick: signal(8),
    totalHours: signal(744),
    fieldResolution: signal(0.5),
    fieldCellsX: signal(10),
    fieldCellsZ: signal(10),
    fieldCellsD: signal(4),
    fieldSizeX: computed(() => state.fieldCellsX.value * state.fieldResolution),
    fieldSizeZ: computed(() => state.fieldCellsZ.value * state.fieldResolution),
    fieldSizeD: computed(() => state.fieldCellsD.value * state.fieldResolution),
    initNumber: signal(42),
    randomize: signal(false),

    seeds: signal<Seed[]>([]),
    seedsCount: computed(() => state.seeds.length),

    obstacles: signal<Obstacle[]>([Obstacle.debugWall()]),
    obstaclesCount: computed(() => state.obstacles.length),

    //RENDERING
    threescene: threescene,
    needsRender: signal(false),
    grabbed: computed(() => state.seeds.value.filter((x: Seed) => x.state.value == "grab")),

    //OPERATIONAL STATE
    computing: signal(false),
    simStep: signal(0),
    simLength: signal(0),

    //RESPONSE
    plants: signal<IPlantResponse[]>([]),
    scene: signal<Scene>([]),

    //METHODS
    run: run,
    pushRndSeed : () => { state.seeds.value = [ ...state.seeds.value, Seed.rndItem()] },
    removeSeedAt: removeSeed,
    clearSeedHovers: clearSeedHovers,
    clearSeedSelects: clearSeedSelects,
    clearSeedGrabs: clearSeedGrabs,

    pushRndObstacle: () => { state.obstacles.value = [ ...state.obstacles.value, Obstacle.rndObstacle()] },
    removeObstacleAt: removeObstacle,

};

export default state;

//now that the scene is assigned push in the default seed
state.seeds.value = [ new Seed(0.5, -0.01, 0.05) ];