//Per-state colours for selectable objects. Renderer-neutral; no three.
//
//Seeds, obstacles and terrain all used the same tinting scheme, expressed three times over
//as THREE.MeshLambertMaterial singletons in their respective helper modules. The scheme is
//now defined once and the materials become the renderer's business.

import { RGB, fromHex, lerpHSL } from './Color';
import { neutralColor } from './Theme';
import { SelectionState } from '../../helpers/Selection';

export type SelectionPalette = Record<SelectionState, RGB>;

/**
 * Builds the standard palette: the base colour, tinted 10% towards the document foreground
 * on hover and 20% on selection.
 *
 * grab and selectHover deliberately alias `select`. The original modules each had distinct
 * grab/selectHover colours commented out with the note "disabled when using the gizmo" -
 * preserved as aliases so re-enabling them is a one-line change here rather than in three
 * places.
 */
export function makeSelectionPalette(baseHex: string): SelectionPalette {
    const base = fromHex(baseHex);
    const select = lerpHSL(base, neutralColor, 0.2);
    return {
        none: base,
        hover: lerpHSL(base, neutralColor, 0.1),
        select,
        grab: select,
        selecthover: select,
    };
}

export function paletteColor(palette: SelectionPalette, state: SelectionState): RGB {
    return palette[state] ?? palette.none;
}
