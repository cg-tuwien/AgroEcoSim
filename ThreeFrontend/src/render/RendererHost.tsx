//Mounts a viewport backend and owns every signal subscription that drives it.
//
//All the reactive wiring that used to be scattered through ThreeSceneFn lives here, so both
//backends get identical update semantics and the three long-standing bugs in that wiring
//(a resize listener leaked on every render, a useEffect with no dependency array, and an
//unguarded removeChild in cleanup) are fixed once rather than per backend.

import { h } from 'preact';
import { useEffect, useRef } from 'preact/compat';
import { useSignalEffect } from '@preact/signals';

import BinaryReader from '../helpers/BinaryReader';
import appstate, { PlayState, clearSceneCache, sceneAt } from '../appstate';
import { buildObstacleLightExport } from '../helpers/ObstacleLight';
import { CameraPose, IViewportRenderer, activeRenderer, PlantFrameInputs, TerrainFrameInputs, SeedFrameInputs, ObstacleFrameInputs, VisibilityInputs } from './IViewportRenderer';
import { VisualMappingOptions } from './core/PlantColors';
import { resolveLighting } from './core/Lighting';
import type { GIFrameInputs, VoxelDebugOptions } from './core/VoxelGrid';
import { Geom, leafGeomKey } from './core/GeometryIds';
import { registerGeometry } from './core/GeometryStore';
import { ThreeRenderer } from './three/ThreeRenderer';
import { Clicks } from './core/Picking';
import { urlBackendOverride } from './supportsWebGPU';

/**
 * Tabs where the user edits scene objects by dragging them.
 *
 * Dragging goes through a Three.js transform gizmo, and no WebGPU equivalent is written, so
 * these tabs mount the Three backend whatever the preference says. Switching on tab rather
 * than on selection is deliberate: it is predictable and never yanks the backend out from
 * under an in-progress drag.
 */
const EDITING_TABS = ['tab-plants', 'tab-obstacles', 'tab-terrain'];

type BackendKind = 'three' | 'webgpu';

/**
 * Backends that have already failed to mount in this session.
 *
 * Enabling WebGPU on Linux Chromium disables WebGL, so on such a machine every attempt to
 * mount Three is doomed. Remembering that is what stops an editing tab from tearing the
 * viewport down, failing, and rebuilding it on every single tab change - the swap is visible
 * even though it recovers.
 */
const failedBackends = new Set<BackendKind>();

/**
 * The backend that will actually be used, given the preference, the tab, and whatever has
 * already proven unusable.
 *
 * Resolving this BEFORE it becomes the mount effect's dependency is the point: if the
 * outcome is unchanged there is no remount at all.
 */
function resolveBackend(preference: string, editing: boolean): BackendKind {
    const order: BackendKind[] = preference === 'three' ? ['three', 'webgpu']
        : preference === 'webgpu' ? ['webgpu', 'three']
        //On 'auto', editing tabs prefer WebGL for its axis gizmo; everything else prefers WebGPU.
        : editing ? ['three', 'webgpu'] : ['webgpu', 'three'];

    return order.find(k => !failedBackends.has(k)) ?? order[0];
}

/** True only when the page was opened with ?debughook, which the visual harness does. */
function debugHookRequested(): boolean {
    try { return new URLSearchParams(window.location.search).has('debughook'); }
    catch { return false; }
}

export default function RendererHost() {
    const divRef = useRef<HTMLDivElement>(null);
    const rendererRef = useRef<IViewportRenderer | undefined>(undefined);
    //The most recent inputs each effect computed. Two jobs: it pins the signal reads ahead of
    //the guard (see the note on visibilityInputs), and it gives the mount path the current
    //state to catch up on without recomputing it from peeks.
    const latest = useRef<{ visibility?: VisibilityInputs; voxels?: VoxelDebugRequest }>({});
    const carriedPose = useRef<CameraPose | undefined>(undefined);
    //Read through .value so the component re-renders - and the mount effect re-runs - when
    //either changes.
    const preference = urlBackendOverride() ?? appstate.viewportBackend.value;
    const editing = EDITING_TABS.some(t => appstate.activeTab.value.endsWith(t));
    const wanted = resolveBackend(preference, editing);

    //--- mount / unmount --------------------------------------------------------------------
    //Empty dependency array: this must run exactly once per mount. The previous code omitted
    //it entirely, so it re-ran on every render, re-appending the canvas each time.
    useEffect(() => {
        let cancelled = false;

        (async () => {
            if (cancelled || !divRef.current) return;

            //A lost device is unrecoverable for that renderer, but every buffer it holds is
            //rebuildable from signals, so falling back loses nothing but the API.
            const onDeviceLost = (info?: { reason?: string; message?: string }) => {
                console.warn('The WebGPU device was lost; falling back to the WebGL viewport.',
                             info?.reason ?? '', info?.message ?? '');
                appstate.viewportBackend.value = 'three';
            };

            //Try the resolved backend, then the other. Neither API is guaranteed - a browser
            //can have WebGPU but no usable WebGL, or the reverse - so leaving the viewport
            //dead because the first choice failed is never the right outcome. Failures are
            //remembered so the doomed attempt is not repeated on the next tab change.
            const order: BackendKind[] = wanted === 'webgpu' ? ['webgpu', 'three'] : ['three', 'webgpu'];
            let renderer: IViewportRenderer | undefined;

            for (const kind of order) {
                if (failedBackends.has(kind)) continue;
                const candidate = kind === 'webgpu'
                    ? await createWebGPU(onDeviceLost)
                    : new ThreeRenderer();
                if (!candidate) { failedBackends.add(kind); continue; }
                try {
                    await candidate.mount(divRef.current);
                    renderer = candidate;
                    break;
                } catch (e) {
                    failedBackends.add(kind);
                    console.warn(`The ${kind === 'webgpu' ? 'WebGPU' : 'WebGL'} viewport is unavailable here; it will not be tried again this session.`, e);
                    try { candidate.dispose(); } catch { /* never fully constructed */ }
                }
            }

            if (!renderer) {
                console.error('No viewport backend could be mounted: neither WebGPU nor WebGL is usable here.');
                return;
            }

            if (cancelled) { renderer.dispose(); return; }

            rendererRef.current = renderer;
            activeRenderer.current = renderer;
            //Diagnostics handle for the visual harness, which drives the app through the DOM
            //and otherwise has no way to reach a module-scoped registry. Gated on an explicit
            //URL flag so a normal session exposes nothing.
            if (debugHookRequested()) {
                const w = window as unknown as Record<string, unknown>;
                w.__renderer = renderer;
                //appstate too, not just the renderer: the harness needs to assert on things the
                //DOM does not show - the shape of `history` in memory, what save() wrote, the
                //memory ceiling - and those are exactly the invariants that broke silently.
                w.__appstate = appstate;
                //The asynchronous frame read, so the harness can prove a compressed frame still
                //decodes to the same scene.
                w.__sceneAt = sceneAt;
                //Decodes raw V7 bytes, so the harness can prove a frame fetched from the run
                //store is a usable scene and not merely the right number of bytes.
                w.__decodeSceneBytes = (bytes: Uint8Array) => new BinaryReader(bytes).readAgroScene();
                //The decoded-scene cache, so a heap measurement can tell what the CACHE is holding
                //from what the renderer never gives back. Without separating those two, one large
                //number says only "memory is full" and points at nothing.
                w.__clearSceneCache = clearSceneCache;
                //The leaf-area-index export document, so a harness can assert on its CONTENT
                //rather than on the size of a downloaded blob. Empty `days` arrays are a legitimate
                //outcome of the payload budget and a symptom of a broken measurement, and only the
                //document itself distinguishes them.
                w.__buildObstacleLightExport = buildObstacleLightExport;
                //Repaint and upload counters, for telling a flicker in the DRAW from a flicker
                //caused by re-uploading the geometry once per mouse-move event.
                w.__rendererStats = () => (renderer as unknown as { debugStats?: () => unknown }).debugStats?.() ?? null;
            }
            appstate.activeBackend.value = renderer.kind;

            //Carry the camera across a backend switch so the view does not jump.
            if (carriedPose.current) renderer.setCameraPose(carriedPose.current);

            //The signal effects below ran once before this point and did nothing, because
            //the renderer did not exist yet - but they DID subscribe to their signals, so
            //future changes are covered. What is not covered is the current state, hence
            //this one-off catch-up.
            renderer.rebuildTerrain(terrainInputs());
            renderer.rebuildSeeds(seedInputs());
            renderer.rebuildObstacles(obstacleInputs());
            renderer.rebuildPlants(plantInputs());
            renderer.setLighting?.(resolveLighting(
                appstate.lightingOptions(),
                appstate.visualMapping.peek() as VisualMappingOptions,
                appstate.initialDate.peek(),
                appstate.currentTimestep(),
                appstate.simHoursPerTick,
            ));
            const voxels = latest.current.voxels ?? voxelDebugRequest();
            renderer.setVoxelDebug?.(voxels.show, voxels.options, voxels.inputs);
            renderer.refreshVisibility(latest.current.visibility ?? visibilityInputs());
        })();

        return () => {
            cancelled = true;
            //Remember where the user was looking, for whichever backend mounts next.
            try { carriedPose.current = rendererRef.current?.getCameraPose(); } catch { /* never mounted */ }
            //Guarded: the previous cleanup assumed a renderer had been created and threw
            //when WebGL was unavailable.
            rendererRef.current?.dispose();
            if (activeRenderer.current === rendererRef.current) activeRenderer.current = undefined;
            rendererRef.current = undefined;
        };
    }, [wanted]);

    //--- resize -----------------------------------------------------------------------------
    //A ResizeObserver on the container, created once and disconnected on unmount. The old
    //code added a window listener from the component body, leaking one per render.
    useEffect(() => {
        const el = divRef.current;
        if (!el || typeof ResizeObserver === 'undefined') return;

        const observer = new ResizeObserver(() => {
            const r = rendererRef.current;
            if (r && el.clientWidth > 0 && el.clientHeight > 0)
                r.resize(el.clientWidth, el.clientHeight, window.devicePixelRatio || 1);
        });
        observer.observe(el);
        return () => observer.disconnect();
    }, []);

    //--- reactive rebuilds ------------------------------------------------------------------
    //
    //Every signal a rebuild depends on is read here and PASSED to the renderer. Do not
    //"subscribe" with bare expression statements like `appstate.scene.value;` - the
    //production build minifies an unused property read away, so on the first run (when the
    //renderer has not mounted yet and the call below is a no-op) the effect would end up
    //tracking nothing at all and never fire again. That fails only in a minified build,
    //which makes it especially easy to ship.

    useSignalEffect(() => {
        const inputs = plantInputs();
        rendererRef.current?.rebuildPlants(inputs);
        afterPlantsRebuilt();
    });

    useSignalEffect(() => {
        const inputs = terrainInputs();
        rendererRef.current?.rebuildTerrain(inputs);
    });

    useSignalEffect(() => {
        const inputs = seedInputs();
        rendererRef.current?.rebuildSeeds(inputs);
    });

    useSignalEffect(() => {
        const inputs = obstacleInputs();
        rendererRef.current?.rebuildObstacles(inputs);
    });

    useSignalEffect(() => {
        const inputs = latest.current.visibility = visibilityInputs();
        rendererRef.current?.refreshVisibility(inputs);
    });

    useSignalEffect(() => {
        //Reads every input the lighting depends on and passes the result in, for the same
        //reason the rebuilds do: a bare read would be optimised away in a production build.
        const lighting = resolveLighting(
            //Read each switch explicitly: lightingOptions() peeks, which would not subscribe.
            {
                daylight: appstate.lightDaylight.value,
                shadows: appstate.lightShadows.value,
                occlusion: appstate.lightOcclusion.value,
                indirect: appstate.lightIndirect.value,
                secondBounce: appstate.lightSecondBounce.value,
                shadowBiasTexels: appstate.shadowBiasTexels.value,
            },
            appstate.visualMapping.value as VisualMappingOptions,
            appstate.initialDate.value,
            //Pass the pointer in rather than reading it for its subscription and calling
            //currentTimestep() separately: a discarded comma operand is dead code to terser.
            appstate.timestepAt(appstate.playPointer.value),
            appstate.simHoursPerTick,
        );
        rendererRef.current?.setLighting?.(lighting);
    });

    useSignalEffect(() => {
        const request = latest.current.voxels = voxelDebugRequest();
        rendererRef.current?.setVoxelDebug?.(request.show, request.options, request.inputs);
    });

    useSignalEffect(() => {
        if (appstate.needsRender.value) {
            appstate.needsRender.value = false;
            rendererRef.current?.renderOnce();
        }
    });

    //--- pointer input ----------------------------------------------------------------------

    const pick = (e: MouseEvent, clicks: Clicks) => rendererRef.current?.pick(e.clientX, e.clientY, clicks);

    return <div
        id="main3Dviewport"
        ref={divRef}
        onMouseMove={e => pick(e as MouseEvent, Clicks.None)}
        onMouseDown={e => pick(e as MouseEvent, Clicks.Down)}
        onMouseUp={e => pick(e as MouseEvent, Clicks.Up)}
        onDblClick={e => pick(e as MouseEvent, Clicks.Double)}
        onMouseLeave={() => rendererRef.current?.pickCancel()}
    />;
}

/**
 * Re-arms the backend preview request and advances the play pointer.
 *
 * This is the coupling that makes the renderer's rebuild rate throttle the simulation
 * stream: the server only emits a preview frame when the client asks for one. Suppressed
 * while the offline renderer is running, which is what decouples an export from playback.
 */
function afterPlantsRebuilt() {
    if (appstate.offlineRendering) return;

    setTimeout(() => {
        if (appstate.previewRequestAfterSceneUpdate) {
            appstate.previewRequestAfterSceneUpdate = false;
            appstate.previewRequest.value = true;
        }
        appstate.playRequest.value = true;
    }, appstate.playing.peek() === PlayState.ForwardWaiting ? 0 : 20);
}

/**
 * Gathers the plant draw list's inputs.
 *
 * Reading `.value` (never `.peek()`) is what registers the caller's effect as a subscriber,
 * and returning them means the reads cannot be optimised away.
 */
function plantInputs(): PlantFrameInputs {
    const species = appstate.species.value;
    return {
        scene: appstate.scene.value,
        visualMapping: appstate.visualMapping.value as VisualMappingOptions,
        debugBoxes: appstate.debugBoxes.value,
        showLeaves: appstate.showLeaves.value,
        showRoots: appstate.showRoots.value,
        leafGeom: (name: string, slice: number) => {
            const s = species.find(x => x.name.peek() === name);
            //A species whose leaf mesh has not arrived yet falls back to the flat plane,
            //matching the old `leafGeometry ?? threePlanePrimitive`.
            if (!s) return Geom.Plane;
            const mesh = s.leafSlice(slice);
            //Keyed by slice, so a blade's eight segments are eight registrations rather than
            //one that every segment overwrites with its own piece.
            return mesh ? registerGeometry(leafGeomKey(name, slice), mesh) : Geom.Plane;
        },
        petalGeom: (name: string) => {
            const s = species.find(x => x.name.peek() === name);
            return s?.petalMesh ? registerGeometry(`petal:${name}`, s.petalMesh) : Geom.Plane;
        },
    };
}

function terrainInputs(): TerrainFrameInputs {
    return {
        //terrainList is a plain array, not a signal; terrainTimestamp is what marks it dirty.
        items: (appstate.terrainTimestamp.value, appstate.terrainList) ?? [],
        fieldSizeX: appstate.fieldSizeX.value,
        fieldSizeD: appstate.fieldSizeD.value,
        fieldSizeZ: appstate.fieldSizeZ.value,
        visible: appstate.showTerrain.value,
    };
}

/**
 * Folds the per-object signals into a revision value.
 *
 * The fold exists purely so the reads are consumed by something the minifier cannot discard.
 * Subscribing here rather than relying on the renderer to read them matters because the
 * renderer is absent on the effect's first run.
 */
function revision(parts: unknown[]): number {
    let h = 0;
    for (const p of parts) h = (h * 31 + String(p).length) | 0;
    return h;
}

function seedInputs(): SeedFrameInputs {
    const seeds = appstate.seeds.value;
    const rev = revision(seeds.flatMap(s => [s.state.value, s.px.value, s.py.value, s.pz.value, s.fieldIndex.value, s.species.value]));
    return { seeds, visible: appstate.showSeeds.value, rev };
}

/**
 * Why the effects below assign their inputs into `latest` instead of passing them straight in.
 *
 * Every effect guards on `rendererRef.current`, which is undefined on its FIRST run because
 * the renderer mounts asynchronously. Terser inlines a single-use local into its use site,
 * so the signal reads end up inside the guarded call - and the guard short-circuits before
 * evaluating them. The effect then subscribes to nothing and never fires again: the control
 * silently stops working, in production builds only.
 *
 * Wrapping the reads in a helper is NOT enough; terser inlines a small enough helper too, as
 * it did to `visibilityInputs` here. What it cannot do is move an observable property write,
 * so assigning into `latest` pins the evaluation ahead of the guard.
 *
 * Same class of bug as the elided bare `appstate.x.value;` statements, by a different
 * mechanism, and `no-unused-expressions` does not catch this one. The check is to grep the
 * built bundle and confirm the `.value` reads sit before the `null===` guard, not inside the
 * call after it.
 */
function visibilityInputs(): VisibilityInputs {
    return {
        terrain: appstate.showTerrain.value,
        seeds: appstate.showSeeds.value,
        obstacles: appstate.showObstacles.value,
    };
}

type VoxelDebugRequest = { show: boolean; options: VoxelDebugOptions; inputs: GIFrameInputs };

function voxelDebugRequest(): VoxelDebugRequest {
    return {
        show: appstate.showVoxels.value,
        options: {
            mip: appstate.voxelMip.value,
            gain: appstate.voxelGain.value,
            albedo: appstate.voxelAlbedo.value,
        },
        inputs: {
            fieldSizeX: appstate.fieldSizeX.value,
            fieldSizeZ: appstate.fieldSizeZ.value,
            //Headroom above the soil for the canopy to grow into.
            height: appstate.fieldSizeD.value + 6,
        },
    };
}

function obstacleInputs(): ObstacleFrameInputs {
    const obstacles = appstate.obstacles.value;
    const rev = revision(obstacles.flatMap(o => [
        o.state.value, o.type.value, o.height.value, o.thickness.value,
        o.wallLength_UmbrellaRadius.value, o.px.value, o.py.value, o.pz.value,
    ]));
    //Read here, in the host, so the effect that calls this re-runs when the mapping or the shown
    //day changes - the renderers only peek.
    const mapping = {
        values: appstate.obstacleMappingValues.value,
        range: appstate.obstacleMappingRange.value,
    };
    return { obstacles, visible: appstate.showObstacles.value, rev, mapping };
}

async function createWebGPU(onLost: () => void): Promise<IViewportRenderer | undefined> {
    try {
        //Dynamically imported so the WebGPU tree stays out of the main bundle for anyone
        //running the WebGL backend.
        const { WebGPURenderer } = await import('./webgpu/WebGPURenderer');
        return new WebGPURenderer(onLost);
    } catch (e) {
        console.warn('The WebGPU renderer module could not be loaded.', e);
        return undefined;
    }
}
