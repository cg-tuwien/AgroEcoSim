import { Primitive } from "./Primitives";

export type RecordedGeometry = { id: string; profile: number; leaves: number[]; petal: number; fruit?: number; fruitWing?: number };
export type PlantModel = {
    primitives: Primitive[];
    species: string;
    geometry?: RecordedGeometry;
    /** Stable zero-based request order; unavailable in legacy V7/V8 recordings. */
    sourcePlantIndex?: number;
};
export type Index = { entity: number; primitive: number }

/** Capabilities of the recorded frame, independent of the editable simulation settings. */
export interface FrameCapabilities {
    version: number;
    rootsDownloaded: boolean;
    analyticsAvailable: boolean;
    hasRootGeometry: boolean;
    virtualRoots: boolean;
    /**
     * Frame flag 4: every above-ground cylinder's colour bytes are the organ's final colour,
     * stems and meristems included. Absent from every frame recorded before the flag, whose
     * stem bytes were never meant to be shown.
     */
    organColors: boolean;
}

//Keep the array API used by playback and the renderers. The weak key also lets an empty
//recorded frame retain its flags without keeping evicted decoded scenes alive.
const capabilities = new WeakMap<PlantModel[], FrameCapabilities>();

export function frameCapabilities(scene: PlantModel[]): FrameCapabilities | undefined {
    return capabilities.get(scene);
}

export function registerFrameCapabilities(scene: PlantModel[], value: FrameCapabilities): void {
    capabilities.set(scene, value);
}
