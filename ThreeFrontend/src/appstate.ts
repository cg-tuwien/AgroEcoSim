import { Signal, computed, signal } from "@preact/signals";
import { BackendURI } from "./config";

// const appInitState = {
//     computing: false,
//     resultScene: ""
// };

// const appReducer = (state, action) => {
//     switch(action) {
//         case 'compute': return state.computing ? {...state} : {...state, computing: true};
//     }
// };

// const [appState, appDispatcher] = useReducer(appReducer, appInitState);

// const Simulation = signal({state: appState, dispatcher: appDispatcher});

export interface IPlantRequest
{
    PX: Signal<number>,
    PY: Signal<number>,
    PZ: Signal<number>
}

export interface IPlantResponse
{
    V: number
}

export interface ISimResponse
{
    plants: IPlantResponse[],
    scene: string
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

    seeds: signal<IPlantRequest[]>([]),
    seedsCount: computed(() => state.seeds.length),

    obstacles: signal([""]),
    obstaclesCount: obstaclesCount,

    //OPERATIONAL STATE
    computing: signal(false),

    //RESPONSE
    platns: signal<IPlantResponse[]>([]),
    scene: signal(""),

    //METHODS
    run: run,
    pushRndSeed : pushRndSeed,
    removeSeedAt: removeSeed
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
        const response = await fetch(`${BackendURI.startsWith("localhost") ? "https:" : location.protocol}//${BackendURI}/Simulation`, {
            method: "POST",

            cache: "no-cache",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(requestBody()),

        });

        const json = await response.json() as ISimResponse;
        state.platns.value = json.plants;
        state.scene.value = json.scene;
        state.computing.value = false;
    }
}

function pushRndSeed(){
    console.log(state.seeds.value);
    state.seeds.value.push({PX: signal(Math.random() * state.fieldSizeX.value), PY: signal(0), PZ: signal(Math.random() * state.fieldSizeZ.value) });
    console.log(state.seeds.value);
    console.log("---------");
}

function removeSeed(i : number) {
    if (i >= 0 && i < state.seeds.value.length)
        state.seeds.value.splice(i, 1);
}

function obstaclesCount() {
    return computed(() => state.obstacles.length)
}