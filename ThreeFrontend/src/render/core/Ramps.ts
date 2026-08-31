//The visual-mapping colour ramps, transcribed from helpers/Plant.ts.
//Renderer-neutral: all inputs and outputs are linear RGB, no three.
//
//The choice of lerp vs lerpHSL is INCONSISTENT between ramps in the original, and that
//inconsistency is load-bearing - it is what the current screenshots look like. Each ramp
//below is annotated with which one it uses so the divergence is obviously deliberate
//rather than a transcription slip.

import { RGB, fromHex, lerp, lerpHSL } from './Color';

const waterEmpty = fromHex('#909090');
const waterFull = fromHex('#00BFE6');
const energyFull = fromHex('#ffc757');
const auxinsFull = fromHex('#FDDE81');
const cytokininsFull = fromHex('#D84315');
const irrEmpty = fromHex('#424d62');
const irrFull = fromHex('#ffcc66');
const effEmpty = fromHex('#0a95ff');
const effFull = fromHex('#ff0a91');

export const woodColor = fromHex('#7f4f1f');
export const greenColor = fromHex('#009900');
export const greyColor = fromHex('#898989');
export const black: RGB = { r: 0, g: 0, b: 0 };

/** RGB lerp. */
export function WaterColor(value: number): RGB {
    return lerp(waterEmpty, waterFull, value);
}

/** HSL lerp. */
export function EnergyColor(value: number): RGB {
    return lerpHSL(waterEmpty, energyFull, value);
}

/** HSL lerp, and note the input is offset by 1 before clamping. */
export function AuxinsColor(value: number): RGB {
    return lerpHSL(waterEmpty, auxinsFull, Math.min(1, Math.max(0, value - 1)));
}

/** HSL lerp. In practice always t=0: stats[3] (cytokinins) is hard-coded to 0 upstream. */
export function CytokininsColor(value: number): RGB {
    return lerpHSL(waterEmpty, cytokininsFull, value);
}

/** RGB lerp. Callers pass stats[4] * 1e-3. */
export function IrradianceColor(value: number): RGB {
    return lerp(irrEmpty, irrFull, value);
}

/** RGB lerp. */
export function EfficiencyColor(value: number): RGB {
    return lerp(effEmpty, effFull, value);
}

const heatColors = [
    fromHex('#003f5c'),
    fromHex('#58508d'),
    fromHex('#bc5090'),
    fromHex('#ff6361'),
    fromHex('#ffa600'),
];
const heatColorsMax = heatColors.length - 1;

/** HSL lerp across 5 stops. Currently unreferenced, kept so the ramp set stays complete. */
export function HeatColor(value: number): RGB {
    const p = Math.max(0, Math.min(1, value)) * heatColorsMax;
    const s = Math.floor(p);
    return s === heatColorsMax ? heatColors[heatColorsMax] : lerpHSL(heatColors[s], heatColors[s + 1], p - s);
}
