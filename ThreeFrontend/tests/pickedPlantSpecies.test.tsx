import { h } from 'preact';
import render from 'preact-render-to-string';
import { signal } from '@preact/signals';
import appstate from '../src/appstate';
import { PickedDetails } from '../src/components/viewport/PickedDetails';
import { Organs, Primitives } from '../src/helpers/Primitives';

jest.mock('../src/appstate', () => {
    const { signal } = require('@preact/signals');
    return { __esModule: true, default: {
        seedPick: signal(-1), plantPick: signal('0_0'), seeds: signal([]), scene: signal([]),
        terrainPick: signal(-1), terrainList: [],
    } };
});

beforeEach(() => {
    appstate.seedPick.value = -1;
    appstate.plantPick.value = '0_0';
    appstate.seeds.value = [{ species: signal('Pending tree') }, { species: signal('Recorded tree') }] as any;
    appstate.scene.value = [{ species: 'Recorded tree', primitives: [{
        type: Primitives.Ellipsoid, organ: Organs.Fruit, color: '#112233',
        affineTransform: new Float32Array([1,0,0,1,0,1,0,2,0,0,1,3]), stats: new Float32Array(9),
    }] }];
});

test('a picked active plant uses recorded species when earlier requested plants are pending', () => {
    const html = render(<PickedDetails />);
    expect(html).toContain('Recorded tree');
    expect(html).not.toContain('Pending tree');
});

test('recorded plant picking works after the planting list is edited or removed', () => {
    appstate.seeds.value = [];
    expect(render(<PickedDetails />)).toContain('Recorded tree');
});

test('a pick that disappeared on another frame or a removed seed is harmless', () => {
    appstate.plantPick.value = '0_1';
    appstate.seedPick.value = appstate.seeds.peek().length;
    expect(render(<PickedDetails />)).toBe('');
});
