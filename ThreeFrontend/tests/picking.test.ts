//Verifies the selection behaviour survived being lifted out of ThreeSceneFn.
//
//applyPick is the whole of the old raycastScene() below the raycast itself: it drives the
//hover/select/grab FSM and publishes plantPick/seedPick/terrainPick. It needs no GL, so it
//can be exercised directly - which is the closest thing to a functional test of the viewport
//available without a browser.

import appstate from '../src/appstate';
import { Seed } from '../src/helpers/Seed';
import { Obstacle } from '../src/helpers/Obstacle';
import { Clicks, applyPick } from '../src/render/core/Picking';

const RAY = { o: { x: 0, y: 5, z: 0 }, d: { x: 0, y: -1, z: 0 } };

function freshSeeds(n: number) {
    const seeds = Array.from({ length: n }, (_, i) => new Seed('sp', i, 0, 0, 0, false));
    appstate.seeds.value = seeds;
    return seeds;
}

describe('applyPick', () => {
    beforeEach(() => {
        appstate.seeds.value = [];
        appstate.obstacles.value = [];
        appstate.plantPick.value = '';
        appstate.seedPick.value = -1;
        appstate.terrainPick.value = -1;
    });

    test('hovering a seed sets its state and publishes its index', () => {
        const [a] = freshSeeds(2);
        applyPick({ type: 'seed', seed: a }, Clicks.None, RAY);

        expect(a.state.value).toBe('hover');
        expect(appstate.seedPick.value).toBe(0);
        expect(appstate.plantPick.value).toBe('');
    });

    test('hovering one seed clears the hover on every other seed', () => {
        const [a, b] = freshSeeds(2);

        applyPick({ type: 'seed', seed: a }, Clicks.None, RAY);
        expect(a.state.value).toBe('hover');

        applyPick({ type: 'seed', seed: b }, Clicks.None, RAY);
        expect(b.state.value).toBe('hover');
        expect(a.state.value).toBe('none');
    });

    test('double click selects, and a later hover elsewhere keeps the selection', () => {
        const [a, b] = freshSeeds(2);

        applyPick({ type: 'seed', seed: a }, Clicks.Double, RAY);
        expect(a.state.value).toBe('selecthover');
        expect(a.isSelected()).toBe(true);

        //Moving away demotes selecthover to select, not to none - the object stays selected.
        applyPick({ type: 'seed', seed: b }, Clicks.None, RAY);
        expect(a.state.value).toBe('select');
        expect(a.isSelected()).toBe(true);
    });

    test('double clicking a different seed clears the previous selection', () => {
        const [a, b] = freshSeeds(2);

        applyPick({ type: 'seed', seed: a }, Clicks.Double, RAY);
        applyPick({ type: 'seed', seed: b }, Clicks.Double, RAY);

        expect(b.isSelected()).toBe(true);
        expect(a.state.value).toBe('none');
    });

    test('picking empty space clears hovers and all pick signals', () => {
        const [a] = freshSeeds(1);
        applyPick({ type: 'seed', seed: a }, Clicks.None, RAY);
        expect(appstate.seedPick.value).toBe(0);

        applyPick(undefined, Clicks.None, RAY);
        expect(a.state.value).toBe('none');
        expect(appstate.seedPick.value).toBe(-1);
        expect(appstate.plantPick.value).toBe('');
    });

    test('a plant pick publishes the encoded name, and only on a plain hover', () => {
        applyPick({ type: 'plant', index: { entity: 3, primitive: 17 } }, Clicks.None, RAY);
        expect(appstate.plantPick.value).toBe('3_17');

        //Clicks on a plant are deliberately ignored - plants are not selectable objects.
        applyPick({ type: 'plant', index: { entity: 3, primitive: 17 } }, Clicks.Double, RAY);
        expect(appstate.plantPick.value).toBe('');
    });

    test('a plant pick suppresses the seed index even when a seed is selected', () => {
        const [a] = freshSeeds(1);
        applyPick({ type: 'seed', seed: a }, Clicks.None, RAY);
        expect(appstate.seedPick.value).toBe(0);

        applyPick({ type: 'plant', index: { entity: 0, primitive: 0 } }, Clicks.None, RAY);
        expect(appstate.seedPick.value).toBe(-1);
    });

    test('obstacles follow the same FSM as seeds', () => {
        const wall = new Obstacle('wall', 0, 0, 0, 0, 0, 4, 3, 0.4, undefined, undefined);
        appstate.obstacles.value = [wall];

        applyPick({ type: 'obstacle', obstacle: wall }, Clicks.None, RAY);
        expect(wall.state.value).toBe('hover');

        applyPick({ type: 'obstacle', obstacle: wall }, Clicks.Double, RAY);
        expect(wall.isSelected()).toBe(true);

        applyPick(undefined, Clicks.Double, RAY);
        expect(wall.state.value).toBe('none');
    });
});
