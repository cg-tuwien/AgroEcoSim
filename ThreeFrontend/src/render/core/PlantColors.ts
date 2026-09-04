//Per-organ colour selection, transcribed from helpers/Plant.ts.
//Renderer-neutral: takes the visual mapping as an argument instead of reading appstate, so
//it is pure and unit-testable, and so the offline renderer can render a frame under a
//mapping other than the one the live viewport is showing.

import { Primitive, Primitives, Organs } from '../../helpers/Primitives';
import { RGB, fromHex, lerpHSL } from './Color';
import { greenColors } from './Palette';
import { neutral } from './Theme';
import {
    AuxinsColor, CytokininsColor, EfficiencyColor, EnergyColor,
    IrradianceColor, WaterColor, black, greenColor, greyColor, woodColor,
} from './Ramps';

export enum VisualMappingOptions {
    Natural = 'natural',
    Water = 'water',
    Auxins = 'auxins',
    Cytokinins = 'cytokinins',
    Energy = 'energy',
    Irradiance = 'irradiance',
    Resource = 'resource',
    Production = 'production',
}

/**
 * A primitive's shading inputs.
 *
 * The split matters: `Natural` puts the value in `color` (lit by the hemisphere light),
 * every other mapping puts it in `emissive` with `color` black, so data ramps read as
 * flat unlit values rather than being modulated by geometry orientation. That is what makes
 * them legible as quantitative readouts, and it is why GI must be forced off whenever the
 * mapping is not Natural.
 */
export type OrganColor = { color: RGB; emissive: RGB };

//stats layout, per helpers/BinaryReader.ts:
//  [0] water  [1] energy  [2] auxins  [3] cytokinins (always 0)
//  [4] irradiance for leaves / woodRatio for stems and roots
//  [5] dailyResource  [6] dailyProduction
//  [7] [5] normalised by the per-plant max  [8] [6] normalised by the per-plant max
const S_WATER = 0, S_ENERGY = 1, S_AUXINS = 2, S_CYTOKININS = 3;
const S_IRRADIANCE_OR_WOOD = 4, S_REL_RESOURCE = 7, S_REL_PRODUCTION = 8;

/** The mappings that behave identically for every organ, i.e. everything except Natural. */
function sharedMapping(mapping: VisualMappingOptions, stats: Float32Array): OrganColor | undefined {
    switch (mapping) {
        case VisualMappingOptions.Water: return { emissive: WaterColor(stats[S_WATER]), color: black };
        case VisualMappingOptions.Energy: return { emissive: EnergyColor(stats[S_ENERGY]), color: black };
        case VisualMappingOptions.Auxins: return { emissive: AuxinsColor(stats[S_AUXINS]), color: black };
        case VisualMappingOptions.Cytokinins: return { emissive: CytokininsColor(stats[S_CYTOKININS]), color: black };
        case VisualMappingOptions.Resource: return { emissive: EfficiencyColor(stats[S_REL_RESOURCE]), color: black };
        case VisualMappingOptions.Production: return { emissive: EfficiencyColor(stats[S_REL_PRODUCTION]), color: black };
        default: return undefined;
    }
}

export function leafColor(primitive: Primitive, index: number, mapping: VisualMappingOptions): OrganColor {
    if (mapping === VisualMappingOptions.Natural)
        return {
            color: primitive.type === Primitives.Rectangle && primitive.color
                ? fromHex(primitive.color)
                : greenColors[index % greenColors.length],
            emissive: black,
        };

    //Irradiance is leaf-only: stems and roots reuse stats[4] for woodRatio.
    if (mapping === VisualMappingOptions.Irradiance)
        return { emissive: IrradianceColor(primitive.stats[S_IRRADIANCE_OR_WOOD] * 1e-3), color: black };

    return sharedMapping(mapping, primitive.stats) ?? { color: neutral, emissive: black };
}

/**
 * Cylinder organs whose colour the backend decides rather than the wood ramp: the
 * petiole, and every part of an inflorescence. A scape is stem tissue and a flower's
 * centre is its own colour, and both were being greyed towards wood by lignification -
 * which is why a Fragaria flower had no yellow eye and a scape never matched the stems.
 */
const backendColoredCylinders = new Set<number>([
    Organs.Petiole,
    Organs.FlowerStem,
    Organs.FlowerMeristem,
    Organs.FlowerPedicel,
    Organs.FlowerBud,
]);

export function stemColor(primitive: Primitive, mapping: VisualMappingOptions): OrganColor {
    if (mapping === VisualMappingOptions.Natural)
        return {
            color: primitive.type === Primitives.Cylinder && backendColoredCylinders.has(primitive.organ) && primitive.color
                ? fromHex(primitive.color)
                : lerpHSL(greenColor, woodColor, primitive.stats[S_IRRADIANCE_OR_WOOD]),
            emissive: black,
        };

    //Deliberately no Irradiance case: stats[4] is woodRatio here, so the original falls
    //through to the neutral default rather than showing a meaningless ramp.
    return sharedMapping(mapping, primitive.stats) ?? { color: neutral, emissive: black };
}

export function rootColor(primitive: Primitive, mapping: VisualMappingOptions): OrganColor {
    if (mapping === VisualMappingOptions.Natural)
        return { color: lerpHSL(greyColor, woodColor, primitive.stats[S_IRRADIANCE_OR_WOOD]), emissive: black };

    return sharedMapping(mapping, primitive.stats) ?? { color: neutral, emissive: black };
}

export function budColor(primitive: Primitive, mapping: VisualMappingOptions): OrganColor {
    if (mapping === VisualMappingOptions.Natural)
        return { color: greenColor, emissive: black };

    //Buds deliberately have no Resource/Production cases - unlike the other organs they fall
    //through to neutral for those, which is what helpers/Plant.ts did.
    switch (mapping) {
        case VisualMappingOptions.Water:
        case VisualMappingOptions.Energy:
        case VisualMappingOptions.Auxins:
        case VisualMappingOptions.Cytokinins:
            return sharedMapping(mapping, primitive.stats) ?? { color: neutral, emissive: black };
        default:
            return { color: neutral, emissive: black };
    }
}
