import { neutralColor as neutralRGB, backgroundColor as backgroundRGB } from "../render/core/Theme";
import { toThreeColor } from "../render/three/ThreeColor";

export type SelectionState = "none" | "hover" | "select" | "selecthover" | "grab";

//The CSS parsing moved to src/render/core/Theme.ts so the WebGPU renderer can read the same
//theme without three. These stay as THREE.Color for the existing Three-side call sites
//(scene.background, the lerpHSL material tinting in Seed/Obstacle/Terrain).
export const neutralColor = toThreeColor(neutralRGB);
export const backgroundColor = toThreeColor(backgroundRGB);
