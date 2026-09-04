//The contract both viewport backends implement, plus the registry that lets non-rendering
//code (appstate, the selection FSM) reach whichever one is currently mounted.
//
//This module is deliberately three-free so that core/ and webgpu/ can import the types, but
//it lives one level up from core/ because it is an application-level seam, not maths.

//All of these are used in type position only. `import type` is not cosmetic here: babel
//cannot tell a type from a value without type information, so a plain import is emitted as
//a real runtime edge and closes an import cycle
//(appstate -> IViewportRenderer -> core/Picking -> helpers/Plant -> appstate) that crashes
//at module-init in the bundled build.
import type { Clicks, DataRef } from './core/Picking';
import type { Vec3 } from './core/Vec3';
import type { PlantModel } from '../helpers/Scene';
import type { VisualMappingOptions } from './core/PlantColors';
import type { FrameLighting } from './core/Lighting';
import type { GIFrameInputs, VoxelDebugOptions } from './core/VoxelGrid';

/** A camera state that can be snapshotted, transferred between backends, or scripted. */
export type CameraPose = {
    position: Vec3;
    target: Vec3;
    up: Vec3;
    /** Vertical field of view, in DEGREES, matching THREE.PerspectiveCamera.fov. */
    fovY: number;
    near: number;
    far: number;
};

/**
 * Pixels read back from an offscreen render.
 *
 * This contract is normative and both backends must satisfy it exactly, or the offline
 * renderer produces different images depending on which backend happened to be active:
 *   - RGBA, 8 bits per channel
 *   - row 0 is the TOP of the image (WebGL readPixels is bottom-up and must be flipped)
 *   - straight, non-premultiplied alpha; always 255, since both contexts are opaque
 *   - already sRGB-encoded, not linear
 *   - any supersample downsampling performed as a box average in ENCODED sRGB space
 */
export type PixelReadback = {
    data: Uint8ClampedArray;
    width: number;
    height: number;
};

/**
 * Everything a frame's draw list depends on, passed in rather than read from appstate.
 *
 * This is not merely tidier - it is load-bearing. RendererHost gathers these inside a signal
 * effect, and a value that is passed as an argument is unambiguously *used*, so the
 * production minifier cannot elide the read and lose the subscription with it. Renderers
 * must therefore never reach back into appstate for this data.
 */
export type PlantFrameInputs = {
    scene: PlantModel[];
    visualMapping: VisualMappingOptions;
    debugBoxes: boolean;
    showLeaves: boolean;
    showRoots: boolean;
    /** Resolves a species name to its registered leaf geometry id. */
    leafGeom: (species: string) => number;
    /**
     * Petal outline for a species. Separate from leafGeom because petals share the lamina
     * wire record with leaves but are not leaf-shaped.
     */
    petalGeom: (species: string) => number;
};

export type TerrainFrameInputs = {
    items: any[];
    fieldSizeX: number;
    fieldSizeD: number;
    fieldSizeZ: number;
    visible: boolean;
};

/**
 * `rev` is a digest of the per-object signals the host subscribed to on the renderer's
 * behalf. Renderers can ignore it; its purpose is to make those reads observable so the
 * minifier keeps them.
 */
export type SeedFrameInputs = { seeds: any[]; visible: boolean; rev: number };
export type ObstacleFrameInputs = { obstacles: any[]; visible: boolean; rev: number };
export type VisibilityInputs = { terrain: boolean; seeds: boolean; obstacles: boolean };

export interface IViewportRenderer {
    readonly kind: 'three' | 'webgpu';

    mount(container: HTMLElement): Promise<void>;
    dispose(): void;
    resize(cssWidth: number, cssHeight: number, dpr: number): void;
    renderOnce(): void;

    rebuildPlants(inputs: PlantFrameInputs): void;
    rebuildTerrain(inputs: TerrainFrameInputs): void;
    rebuildSeeds(inputs: SeedFrameInputs): void;
    rebuildObstacles(inputs: ObstacleFrameInputs): void;
    /** A showX toggle changed. */
    refreshVisibility(inputs: VisibilityInputs): void;
    /**
     * The lighting tier or the simulated time changed.
     *
     * Optional: a backend that only implements the parity tier simply does not offer it.
     */
    setLighting?(lighting: FrameLighting): void;

    /**
     * Show or hide the leaf-area-density debug overlay.
     *
     * Optional, and only the WebGPU backend implements it: the field is built by compute
     * shaders that have no WebGL2 equivalent worth writing.
     */
    setVoxelDebug?(show: boolean, options: VoxelDebugOptions, inputs: GIFrameInputs): void;
    /** Raw per-cell leaf area in square metres, for the visual harness's analytic check. */
    readVoxelArea?(): Promise<Float32Array | undefined>;

    /** Resolve what is under the cursor and drive the selection FSM. May complete async. */
    pick(clientX: number, clientY: number, clicks: Clicks): void;
    /** Cursor left the viewport; drop any hover state that depends on a position. */
    pickCancel(): void;

    getCameraPose(): CameraPose;
    setCameraPose(pose: CameraPose): void;

    /** Only the Three backend offers a transform gizmo; see RendererHost for how editing works. */
    attachGizmo?(target: unknown): void;
    detachGizmo?(target: unknown): void;

    exportGLTF?(save: (json: string) => void): void;

    /** Offscreen render at an arbitrary size. Used by the offline animation renderer. */
    renderToPixels?(width: number, height: number, ssaa: number, msaa: 1 | 4, pose: CameraPose): Promise<PixelReadback>;
}

/**
 * The currently mounted renderer.
 *
 * A mutable holder rather than a signal on purpose: this is read from inside signal effects
 * and from the selection FSM, and making it reactive would re-run those effects on every
 * backend swap for no benefit. RendererHost is the only writer.
 */
export const activeRenderer: { current: IViewportRenderer | undefined } = { current: undefined };

export function requestRender() {
    activeRenderer.current?.renderOnce();
}

export type { Clicks, DataRef };
