//Orchestrates offscreen rendering, decoupled from the live viewport and from playback.
//
//The one hard rule: this must not perturb the playback state machine. Reading a frame here
//must not advance playPointer, and must not cause the frontend to request another preview
//from the backend - the renderer's rebuild rate is what paces the simulation stream, and an
//export that quietly drove it would change the simulation's behaviour.

import { signal } from '@preact/signals';
import appstate, { PlayState, decodeSceneAt } from '../../appstate';
import { activeRenderer } from '../IViewportRenderer';
import { FrameLighting, resolveLighting } from '../core/Lighting';
import { VisualMappingOptions } from '../core/PlantColors';
import { downloadBlob } from '../../helpers/download';
import { encodeImage } from './encode/PngWriter';
import { StaticCamera, TurntableCamera, ICameraPath } from './CameraPath';
import { OfflineProgress, OfflineQuality, TimingMode, estimateVram } from './types';
import { buildFrameMap, FrameSource } from './FrameSource';
import { IVideoWriter } from './encode/IVideoWriter';
import { ImageSequenceWriter } from './encode/ImageSequenceWriter';
import { WebCodecsWriter } from './encode/WebCodecsWriter';
import { bitrateFor, evenDimension, pickCodec, QualityLevel } from './encode/codecs';

export const progress = signal<OfflineProgress>({ state: 'idle' });

export const settings = {
    width: signal(1920),
    height: signal(1080),
    ssaa: signal(1),
    msaa: signal<1 | 4>(1),
    camera: signal<'current' | 'turntable'>('current'),
    turns: signal(1),

    //Animation
    fps: signal(25),
    timing: signal<TimingMode>('uniform'),
    hoursPerSecond: signal(720),
    from: signal(0),
    to: signal(-1),          // -1 means "to the end of history"
    step: signal(1),
    format: signal<'webm' | 'png-zip' | 'jpeg-zip'>('webm'),
    quality: signal<QualityLevel>('medium'),
};

export function currentQuality(): OfflineQuality {
    return {
        width: settings.width.value,
        height: settings.height.value,
        ssaa: settings.ssaa.value,
        msaa: settings.msaa.value,
    };
}

export function vramEstimateMB(): number {
    return Math.round(estimateVram(currentQuality()) / (1024 * 1024));
}

export function makeCameraPath(): ICameraPath {
    const renderer = activeRenderer.current;
    if (!renderer) throw new Error('No viewport is mounted');
    const pose = renderer.getCameraPose();
    return settings.camera.peek() === 'turntable'
        ? new TurntableCamera(pose, settings.turns.peek())
        : new StaticCamera(pose);
}

/**
 * The lighting an exported frame should be rendered with.
 *
 * An export must not read the renderer's CURRENT lighting: that was resolved for wherever the
 * playhead happens to sit, and the export deliberately never moves the playhead. Leave it at
 * the start of a run - the usual place - and the sun is at midnight of the start date, below
 * the horizon, so every frame of the video comes out ambient-only however the switches are
 * set. It also has to be recomputed per frame rather than once, or a video spanning months of
 * simulated time would be lit by a single instant of it.
 *
 * @param historyIndex The frame's index into `appstate.history`, which is what the playhead
 *   would have been showing.
 */
function lightingFor(historyIndex: number): FrameLighting {
    return resolveLighting(
        appstate.lightingOptions(),
        appstate.visualMapping.peek() as VisualMappingOptions,
        appstate.initialDate.peek(),
        appstate.timestepAt(historyIndex),
        appstate.simHoursPerTick);
}

/**
 * Renders one high-resolution still of whatever the viewport is currently showing.
 *
 * Deliberately the first deliverable of the offline work: it exercises render targets,
 * colour space, the Y flip and the downsample - every part that can silently produce a
 * wrong image - without any video machinery in the way. It is also useful on its own for
 * figures.
 */
export async function renderStill(): Promise<void> {
    const renderer = activeRenderer.current;
    if (!renderer?.renderToPixels) {
        progress.value = { state: 'error', message: 'The active viewport cannot render offscreen.' };
        return;
    }

    const q = currentQuality();
    progress.value = { state: 'running', done: 0, total: 1, msPerFrame: 0, phase: 'render' };

    //Suppress the playback poke for the duration; a still does not change appstate.scene,
    //but the guard is cheap and keeps the invariant obvious.
    appstate.offlineRendering = true;
    const started = performance.now();
    try {
        const path = makeCameraPath();
        //A still shows the current frame, so the current playhead is the right clock - but
        //resolve it here rather than trusting whatever the viewport last happened to be told.
        renderer.setLighting?.(lightingFor(appstate.playPointer.peek()));
        const px = await renderer.renderToPixels(q.width, q.height, q.ssaa, q.msaa, path.poseAt(0, 1));

        progress.value = { state: 'running', done: 1, total: 1, msPerFrame: performance.now() - started, phase: 'encode' };
        const blob = await encodeImage(px, 'image/png');
        downloadBlob(blob, `_${px.width}x${px.height}`, 'png');
        progress.value = { state: 'done', bytes: blob.size };
    } catch (e) {
        console.error('Offline still failed', e);
        progress.value = { state: 'error', message: e instanceof Error ? e.message : String(e) };
    } finally {
        appstate.offlineRendering = false;
        appstate.needsRender.value = true;
    }
}


//--- animation ---------------------------------------------------------------------------

let cancelRequested = false;

export function cancel() {
    cancelRequested = true;
}

/**
 * Yields to the browser between frames.
 *
 * rAF is preferred because it guarantees the progress bar actually paints, but it is frozen
 * in a background tab, which would stall an export the moment the user switches away - hence
 * the timeout fallback.
 */
function yieldToUI(): Promise<void> {
    return new Promise(resolve => {
        if (typeof document !== 'undefined' && document.hidden) setTimeout(resolve, 0);
        else requestAnimationFrame(() => resolve());
    });
}

async function createWriter(width: number, height: number, fps: number): Promise<IVideoWriter> {
    const format = settings.format.peek();
    if (format === 'png-zip') return new ImageSequenceWriter('image/png');
    if (format === 'jpeg-zip') return new ImageSequenceWriter('image/jpeg');

    const bitrate = bitrateFor(width, height, fps, settings.quality.peek());
    const choice = await pickCodec(width, height, fps, bitrate);
    if (!choice) {
        //A first-class outcome, not an error: plenty of browsers cannot encode video at all.
        console.warn('No WebCodecs video encoder is available; falling back to a JPEG sequence.');
        return new ImageSequenceWriter('image/jpeg');
    }
    return new WebCodecsWriter(choice, bitrate);
}

export function frameCount(): number {
    const history = appstate.history;
    if (!history?.length) return 0;
    const to = settings.to.peek();
    return buildFrameMap(history, {
        mode: settings.timing.peek(),
        from: settings.from.peek(),
        to: to < 0 ? history.length - 1 : to,
        step: settings.step.peek(),
        fps: settings.fps.peek(),
        hoursPerSecond: settings.hoursPerSecond.peek(),
        hoursPerTick: appstate.simHoursPerTick,
    }).length;
}

/**
 * Renders the whole animation and downloads it.
 *
 * The invariant that matters: this must not touch playPointer or playing, and must not let
 * the frontend ask the backend for another preview. The renderer's rebuild rate is what
 * paces the simulation stream, so an export that drove it would change how the simulation
 * runs. `appstate.offlineRendering` suppresses that feedback for the duration; the viewport
 * visibly scrubs while exporting, which reads as a preview and is acceptable.
 */
export async function renderAnimation(): Promise<void> {
    const renderer = activeRenderer.current;
    if (!renderer?.renderToPixels) {
        progress.value = { state: 'error', message: 'The active viewport cannot render offscreen.' };
        return;
    }
    if (appstate.computing.peek()) {
        progress.value = { state: 'error', message: 'Wait for the simulation to finish before exporting.' };
        return;
    }

    const history = appstate.history;
    if (!history?.length) {
        progress.value = { state: 'error', message: 'There is nothing to export yet - run a simulation first.' };
        return;
    }

    cancelRequested = false;

    //Even dimensions keep 4:2:0 encoders happy.
    const width = evenDimension(settings.width.peek());
    const height = evenDimension(settings.height.peek());
    const ssaa = settings.ssaa.peek();
    const msaa = settings.msaa.peek();
    const fps = settings.fps.peek();
    const to = settings.to.peek();

    const map = buildFrameMap(history, {
        mode: settings.timing.peek(),
        from: settings.from.peek(),
        to: to < 0 ? history.length - 1 : to,
        step: settings.step.peek(),
        fps,
        hoursPerSecond: settings.hoursPerSecond.peek(),
        hoursPerTick: appstate.simHoursPerTick,
    });

    if (map.length === 0) {
        progress.value = { state: 'error', message: 'The selected frame range is empty.' };
        return;
    }

    const restorePointer = appstate.playPointer.peek();
    const path = makeCameraPath();
    const source = new FrameSource(index => decodeSceneAt(index));

    //Pausing first stops the playback machine writing appstate.scene underneath us.
    appstate.playing.value = PlayState.None;
    appstate.offlineRendering = true;

    let writer: IVideoWriter | undefined;
    const guard = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ''; };
    window.addEventListener('beforeunload', guard);
    //Best effort: long exports otherwise die when the screen locks.
    const wakeLock = await (navigator as any).wakeLock?.request?.('screen').catch(() => undefined);

    let msPerFrame = 0;
    try {
        writer = await createWriter(width, height, fps);
        await writer.init({ width, height, fps });

        let lastPixels: Awaited<ReturnType<NonNullable<typeof renderer.renderToPixels>>> | undefined;

        for (let i = 0; i < map.length; ++i) {
            if (cancelRequested) break;
            const started = performance.now();

            const { scene, reused } = source.at(map[i].historyIndex);
            if (!reused || !lastPixels) {
                //Writing appstate.scene runs the rebuild effects synchronously, so the
                //renderer is up to date by the time renderToPixels is called.
                appstate.scene.value = scene;
                //The sun belongs to the frame being rendered, not to where the playhead is.
                renderer.setLighting?.(lightingFor(map[i].historyIndex));
                lastPixels = await renderer.renderToPixels!(width, height, ssaa, msaa, path.poseAt(i, map.length));
            }

            await writer.addFrame(lastPixels, i);

            //Exponential moving average: a plain per-frame figure jitters too much to read.
            const elapsed = performance.now() - started;
            msPerFrame = msPerFrame === 0 ? elapsed : msPerFrame * 0.8 + elapsed * 0.2;
            progress.value = { state: 'running', done: i + 1, total: map.length, msPerFrame, phase: 'render' };

            await yieldToUI();
        }

        if (cancelRequested) {
            writer.abort();
            progress.value = { state: 'cancelled' };
            return;
        }

        progress.value = { state: 'running', done: map.length, total: map.length, msPerFrame, phase: 'mux' };
        const blob = await writer.finish();
        downloadBlob(blob, `_${width}x${height}_${map.length}f`, writer.extension);
        progress.value = { state: 'done', bytes: blob.size };
    } catch (e) {
        console.error('Animation export failed', e);
        writer?.abort();
        progress.value = { state: 'error', message: e instanceof Error ? e.message : String(e) };
    } finally {
        window.removeEventListener('beforeunload', guard);
        wakeLock?.release?.().catch(() => undefined);
        appstate.offlineRendering = false;
        //Put the viewport back where the user left it - the scene AND its sun, which the loop
        //has been advancing frame by frame.
        appstate.scene.value = decodeSceneAt(restorePointer);
        renderer.setLighting?.(lightingFor(restorePointer));
        appstate.needsRender.value = true;
    }
}
