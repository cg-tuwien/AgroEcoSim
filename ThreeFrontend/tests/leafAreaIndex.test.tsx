import { h, render } from 'preact';
import { act } from 'preact/test-utils';
import fixture from './__fixtures__/obstacle-light.json';
import appstate from '../src/appstate';
import { LeafAreaIndex } from '../src/components/hud/LeafAreaIndex';
import { IObstacleLightDocument } from '../src/helpers/ObstacleLight';

jest.mock('../src/appstate', () => {
    const { signal } = require('@preact/signals');
    return { __esModule: true, default: {
        obstacleLight: signal(null), leafAreaIndexObstacles: signal('wall'),
        leafAreaIndexSensors: signal(64), leafAreaIndexDepth: signal(3), leafAreaIndexBandHeight: signal(1),
        exportObstacleLight: jest.fn(),
    } };
});

const doc = fixture as unknown as IObstacleLightDocument;
let host: HTMLDivElement;

beforeEach(() => {
    appstate.obstacleLight.value = doc;
    host = document.createElement('div');
    document.body.appendChild(host);
});

afterEach(() => {
    act(() => render(null, host));
    host.remove();
});

test('labels the displayed whole-run maximum and leaves historical shedding unknown', () => {
    act(() => render(<LeafAreaIndex />, host));
    expect(Array.from(host.querySelectorAll('th')).map(header => header.textContent)).toContain('peak LAI');
    expect(host.textContent).toContain('Leaf shedding was not recorded in this run.');
    expect(host.textContent).not.toContain('Leaves shed during run: 0');
});

test('shows the facade shedding total without counting the same tray twice', () => {
    appstate.obstacleLight.value = {
        ...doc,
        facade: { ...doc.facade!, total: { ...doc.facade!.total!, leavesShed: 42 } },
        trays: doc.trays.map(tray => ({ ...tray, total: { ...tray.total!, leavesShed: 42 } })),
    };
    act(() => render(<LeafAreaIndex />, host));
    expect(host.textContent).toContain('Leaves shed during run: 42.');
    expect(host.textContent).not.toContain('Leaves shed during run: 84.');
});

test('shows a recorded zero for survey-only runs without obstacle measurements', () => {
    appstate.obstacleLight.value = {
        ...doc, obstacles: [], selection: null,
        facade: { ...doc.facade!, total: { ...doc.facade!.total!, leavesShed: 0 } },
    };
    act(() => render(<LeafAreaIndex />, host));
    expect(host.textContent).toContain('Leaves shed during run: 0.');
    expect(host.querySelector('table')).toBeNull();
    expect(host.querySelector('button')!.disabled).toBe(false);
});
