import { h, render } from 'preact';
import { act } from 'preact/test-utils';
import appstate, { sceneAtSync } from '../src/appstate';
import { ExportImport } from '../src/components/hud/ExportImport';
import { downloadText } from '../src/helpers/download';
import { exportCurrentFrame, exportRootsAvailable } from '../src/render/export/CurrentFrameExport';

jest.mock('../src/appstate', () => {
    const { signal } = require('@preact/signals');
    return {
        __esModule: true,
        default: { scene: signal([]), playPointer: signal(0), historyCount: signal(2), load: jest.fn() },
        sceneAtSync: jest.fn(() => []),
    };
});
jest.mock('../src/helpers/download', () => ({ downloadText: jest.fn() }));
jest.mock('../src/render/export/CurrentFrameExport', () => ({
    exportCurrentFrame: jest.fn(),
    exportRootsAvailable: jest.fn(() => false),
}), { virtual: true });

let host: HTMLDivElement;
const input = (id: string) => host.querySelector<HTMLInputElement>(`#${id}`)!;
const exportButton = () => host.querySelector<HTMLButtonElement>('[name="exportCurrentFrame"]')!;
async function clickExport() {
    await act(async () => {
        exportButton().click();
        const results = (exportCurrentFrame as jest.Mock).mock.results;
        await results[results.length - 1].value.catch(() => undefined);
        await Promise.resolve();
    });
}

beforeEach(() => {
    jest.clearAllMocks();
    (exportRootsAvailable as jest.Mock).mockReturnValue(false);
    (sceneAtSync as jest.Mock).mockReturnValue([]);
    (exportCurrentFrame as jest.Mock).mockResolvedValue({ json: '{}', step: 17, warnings: [] });
    appstate.scene.value = [];
    appstate.playPointer.value = 0;
    appstate.historyCount.value = 2;
    host = document.createElement('div');
    document.body.appendChild(host);
});

afterEach(() => {
    act(() => render(null, host));
    host.remove();
});

test('offers frame export defaults and retains only run loading', () => {
    act(() => render(<ExportImport />, host));
    expect(Array.from(host.querySelectorAll('button')).map(button => button.textContent))
        .toEqual(['Load run', 'Export current frame (.gltf)']);
    expect(input('exportRoots').checked).toBe(false);
    expect(input('exportRoots').disabled).toBe(true);
    expect(input('exportSoilAndTrays').checked).toBe(true);
    expect(input('exportObstacles').checked).toBe(true);
    expect(input('exportMetadata').checked).toBe(false);
    act(() => host.querySelector<HTMLButtonElement>('button')!.click());
    expect(appstate.load).toHaveBeenCalledTimes(1);
});

test('exports chosen options and names the download for the captured timestep', async () => {
    (exportRootsAvailable as jest.Mock).mockReturnValue(true);
    act(() => render(<ExportImport />, host));
    act(() => {
        input('exportRoots').click();
        input('exportSoilAndTrays').click();
        input('exportMetadata').click();
    });
    await clickExport();
    expect(exportCurrentFrame).toHaveBeenCalledWith({ roots: true, soilAndTrays: false, obstacles: true, metadata: true });
    expect(downloadText).toHaveBeenCalledWith('{}', 'gltf', '_step-17');
});

test('does not inherit roots availability while the selected frame is cold', () => {
    (sceneAtSync as jest.Mock).mockReturnValue(undefined);
    (exportRootsAvailable as jest.Mock).mockImplementation(scene => scene === appstate.scene.peek());
    act(() => render(<ExportImport />, host));
    expect(input('exportRoots').disabled).toBe(true);
    expect(input('exportRoots').checked).toBe(false);
});

test('disables unavailable exports and displays source errors or legacy warnings', async () => {
    appstate.historyCount.value = 0;
    act(() => render(<ExportImport />, host));
    expect(exportButton().disabled).toBe(true);
    act(() => { appstate.historyCount.value = 2; });
    (exportCurrentFrame as jest.Mock).mockRejectedValueOnce(new Error('The selected frame is no longer available.'));
    await clickExport();
    expect(host.querySelector('[role="alert"]')!.textContent).toBe('The selected frame is no longer available.');
    expect(downloadText).not.toHaveBeenCalled();
    (exportCurrentFrame as jest.Mock).mockResolvedValueOnce({ json: '{}', step: 17, warnings: ['Legacy tray geometry cannot be separated.'] });
    await clickExport();
    expect(host.querySelector('[role="alert"]')).toBeNull();
    expect(host.querySelector('[role="status"]')!.textContent).toBe('Legacy tray geometry cannot be separated.');
});
