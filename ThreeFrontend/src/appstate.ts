import { Signal, batch, computed, signal } from "@preact/signals";
import { BackendURI } from "./config";
import BinaryReader from "./helpers/BinaryReader";
import { Scene } from "./helpers/Scene";
import * as SignalR from "@microsoft/signalr";

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
//start();

export interface IPlantRequest
{
    PX: Signal<number>,
    PY: Signal<number>,
    PZ: Signal<number>
}

export interface IWallObstacle extends IPlantRequest
{
    Type: "wall",
    AngleX: Signal<number>,
    AngleY: Signal<number>,
    Length: Signal<number>,
    Height: Signal<number>,
    Thickness: Signal<number>,
}

export interface IUmbrellaObstacle extends IPlantRequest
{
    Type: "umbrella",
    PoleThickness: Signal<number>,
    DiskRadius: Signal<number>,
    Height: Signal<number>,
}

export type IObstacleRequest = IWallObstacle | IUmbrellaObstacle

export interface IPlantResponse
{
    V: number
}

export interface ISimResponse
{
    plants: IPlantResponse[],
    scene: Uint8Array,
    renderer: string,
    debug: string,
}

const state = {
    // SETTINGS
    hoursPerTick: signal(1),
    totalHours: signal(744),
    fieldResolution: signal(0.5),
    fieldSizeX: signal(10),
    fieldSizeZ: signal(10),
    fieldSizeD: signal(4),
    initNumber: signal(42),
    randomize: signal(false),

    seeds: signal<IPlantRequest[]>([{PX: signal(0.5), PY: signal(-0.01), PZ: signal(0.5)}]),
    seedsCount: computed(() => state.seeds.length),

    obstacles: signal<IObstacleRequest[]>([]),
    obstaclesCount: computed(() => state.obstacles.length),

    //OPERATIONAL STATE
    computing: signal(false),
    simStep: signal(0),
    simLength: signal(0),

    //RESPONSE
    plants: signal<IPlantResponse[]>([]),
    scene: signal<Scene>([]),

    //METHODS
    run: run,
    pushRndSeed : pushRndSeed,
    removeSeedAt: removeSeed,
};

export default state;

function requestBody() {
    return {
    HoursPerTick: Math.trunc(state.hoursPerTick.value),
    TotalHours: Math.trunc(state.totalHours.value),
    FieldResolution: state.fieldResolution.value,
    FieldSize: { X: state.fieldSizeX.value, D: state.fieldSizeD.value, Z: state.fieldSizeZ.value },
    Seed: Math.trunc(state.randomize.value ? Math.random() * 4294967295 : state.initNumber.value),

    Plants: state.seeds.value.map(p => ({ P: { X: p.PX.value, Y: p.PY.value, Z: p.PZ.value } })),
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

function pushRndSeed(){
    state.seeds.value = [ ...state.seeds.value, { PX: signal(Math.random() * state.fieldSizeX.value), PY: signal(0), PZ: signal(Math.random() * state.fieldSizeZ.value) }];
}

function removeSeed(i : number) {
    if (i >= 0 && i < state.seeds.value.length)
        state.seeds.value.splice(i, 1);
        state.seeds.value = [...state.seeds.value];
}

function pushRndObstacle(){
    const item = Math.random() > 0.5 ? {
        Type: "wall",
        Height: signal(3),
        Length: signal(3),
        Thickness: signal(0.4),
        AngleX: signal(0),
        AngleY: signal(0),
        PX: signal(Math.random() * state.fieldSizeX.value),
        PY: signal(0),
        PZ: signal(Math.random() * state.fieldSizeZ.value)
    } : {
        Type: "umbrella",
        PoleThickness: signal(0.08),
        DiskRadius: signal(1),
        Height: signal(2.2),
        PX: signal(Math.random() * state.fieldSizeX.value),
        PY: signal(0),
        PZ: signal(Math.random() * state.fieldSizeZ.value)
    };
    state.obstacles.value = [ ...state.obstacles.value, item];
}

function removeObstacle(i : number) {
    if (i >= 0 && i < state.obstacles.value.length)
        state.obstacles.value.splice(i, 1);
        state.obstacles.value = [...state.obstacles.value];
}