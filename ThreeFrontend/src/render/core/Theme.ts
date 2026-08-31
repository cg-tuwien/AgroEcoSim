//Theme colours, read once from the document's computed style.
//Renderer-neutral: no three, and no dependency on either renderer.
//
//These used to live in helpers/Selection.ts as THREE.Color instances. They are read at
//module load, exactly as before, which means a runtime colour-scheme flip (light <-> dark)
//is not picked up until reload - preserved as-is rather than quietly changing behaviour.

import { RGB, fromCss, lerpHSL } from './Color';

function readCssColor(property: string, fallback: RGB): RGB {
    //jsdom and any non-browser host (jest) have no computed style worth reading.
    if (typeof document === 'undefined' || !document.documentElement) return fallback;
    const raw = getComputedStyle(document.documentElement).getPropertyValue(property);
    return raw ? fromCss(raw) : fallback;
}

/** The document foreground colour (--fgcolor: #444 light, #fff dark). */
export const neutralColor: RGB = readCssColor('color', { r: 0.05, g: 0.05, b: 0.05 });

/** The document background colour (--bgcolor: #fafafa light, #1c1c1c dark). */
export const backgroundColor: RGB = readCssColor('background', { r: 0.95, g: 0.95, b: 0.95 });

/**
 * The grey used for primitives that carry no stats, and as the fallback in every visual
 * mapping's default branch. Mirrors helpers/Plant.ts:
 *   new THREE.Color(neutralColor).lerpHSL(new THREE.Color(backgroundColor), 0.1)
 */
export const neutral: RGB = lerpHSL(neutralColor, backgroundColor, 0.1);
