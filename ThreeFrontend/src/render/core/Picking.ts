//Renderer-neutral picking types and the selection state machine driver.
//Nothing in this file may import three.
//
//The FSM itself (none -> hover -> select -> selecthover -> grab) is unchanged from
//ThreeSceneFn.tsx; only its inputs are abstracted. Both backends produce a DataRef and a
//Ray, so the same transitions and the same appstate signal writes serve WebGL and WebGPU.

import { batch } from '@preact/signals';
import appstate from '../../appstate';
import { Index } from '../../helpers/Scene';
import { Ray } from './Ray';
import { SelectionState } from '../../helpers/Selection';
import { EncodePlantName } from '../../helpers/Plant';

export enum Clicks { None, Down, Up, Double }

export interface ISeedRef { type: 'seed'; seed: any }
export interface IObstacleRef { type: 'obstacle'; obstacle: any }
export interface IPlantRef { type: 'plant'; index: Index }
export interface ITerrainRef { type: 'terrain'; index?: number; terrain?: any }

export type DataRef = ISeedRef | IObstacleRef | IPlantRef | ITerrainRef;

/**
 * The subset of BaseRequestObject the FSM driver needs.
 *
 * Declared structurally rather than importing the class so that core stays free of the
 * helpers' dependency graph; BaseRequestObject satisfies it by construction.
 */
export interface ISelectable {
    state: { value: SelectionState; peek(): SelectionState };
    hover(): void;
    unhover(): void;
    select(): void;
    selecthover(): void;
    grab(ray: Ray): void;
    move(ray: Ray): void;
    ungrab(s: SelectionState): void;
}

/**
 * Drives one object's selection transitions for a single interaction.
 * Lifted verbatim from ThreeSceneFn.pickingLogic - the transition table is the behaviour
 * users are used to, so it is reproduced exactly rather than tidied.
 */
export function pickingLogic(clicks: Clicks, target: ISelectable, ray: Ray) {
    if (clicks === Clicks.Double) {
        if (target.state.value != 'select')
            target.selecthover();
    }
    else if (clicks === Clicks.Down) {
        if (target.state.value == 'selecthover')
            target.grab(ray);
    }
    else if (clicks === Clicks.Up) {
        if (target.state.value == 'grab')
            target.ungrab('selecthover');
    }
    else switch (target.state.value) {
        case 'none': target.hover(); break;
        case 'select': target.selecthover(); break;
    }
}

/** Converts a client-space pointer position into normalized device coordinates. */
export function toNDC(clientX: number, clientY: number, rect: { left: number; top: number; width: number; height: number }) {
    return {
        x: ((clientX - rect.left) / rect.width) * 2 - 1,
        y: 1 - ((clientY - rect.top) / rect.height) * 2,
    };
}

/**
 * Applies a resolved pick: drives the FSM for the hit object and publishes the pick signals.
 *
 * This is everything the old raycastScene() did below the raycast itself. It is deliberately
 * backend-independent - the Three renderer reaches it with a raycast hit, and the WebGPU
 * picker will reach it by mapping a pick id back to a DataRef - so hover, selection and
 * clearing behave identically on both.
 *
 * Synchronous on purpose: mousemove/mousedown/mouseup must be applied in order, and a
 * deferred batch would let a later event overtake an earlier one.
 */
export function applyPick(ref: DataRef | undefined, clicks: Clicks, ray: Ray) {
    batch(() => {
        let seedPick: any;
        let obstaclePick: any;
        let plantPick = '';
        let terrainPick: any;

        switch (ref?.type) {
            case 'seed':
                seedPick = ref.seed;
                pickingLogic(clicks, seedPick, ray);
                break;
            case 'obstacle':
                obstaclePick = ref.obstacle;
                pickingLogic(clicks, obstaclePick, ray);
                break;
            case 'plant':
                //Plants are not selectable objects; only a plain hover reports one.
                if (clicks === Clicks.None) plantPick = EncodePlantName(ref.index);
                break;
            case 'terrain':
                terrainPick = ref.terrain;
                if (terrainPick) pickingLogic(clicks, terrainPick, ray);
                break;
        }

        appstate.clearSeedHovers(seedPick);
        appstate.clearObstacleHovers(obstaclePick);
        appstate.clearTerrainHovers(terrainPick);

        if (clicks === Clicks.Double) {
            appstate.clearSeedSelects(seedPick);
            appstate.clearObstacleSelects(obstaclePick);
            appstate.clearTerrainSelects(terrainPick);
        }
        if (clicks === Clicks.Up) {
            appstate.clearSeedGrabs(seedPick);
            appstate.clearObstacleGrabs(obstaclePick);
        }

        appstate.plantPick.value = plantPick;
        appstate.seedPick.value = !plantPick && seedPick ? appstate.seeds.peek().indexOf(seedPick) : -1;
        appstate.terrainPick.value = terrainPick ? appstate.terrainList.indexOf(terrainPick) : -1;
    });
}
