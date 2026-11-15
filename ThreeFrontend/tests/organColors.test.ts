//The OrganColors frame flag (value 4): a frame that carries it has the final colour of every
//above-ground cylinder in its colour bytes, stems and meristems included. A frame without it -
//every stored run and every fixture - must keep drawing stems with the wood ramp, exactly.

import BinaryReader from '../src/helpers/BinaryReader';
import { Organs } from '../src/helpers/Primitives';
import { frameCapabilities, PlantModel } from '../src/helpers/Scene';
import { buildPlants } from '../src/render/core/BuildScene';
import { fromHex, lerpHSL } from '../src/render/core/Color';
import { Geom } from '../src/render/core/GeometryIds';
import { InstanceList } from '../src/render/core/InstanceList';
import { HEMI_GROUND, HEMI_INTENSITY, HEMI_SKY } from '../src/render/core/Lighting';
import { VisualMappingOptions, stemColor } from '../src/render/core/PlantColors';

const ORGAN_COLORS = 4, ANALYTICS = 2;
const WOOD = 0.375; //exact in float32
const transform = [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4];
//Stem, meristem and petiole, each with its own wire colour.
const organs: [Organs, number[]][] = [
    [Organs.Stem, [0x6b, 0x5a, 0x4c]],
    [Organs.Meristem, [0x4f, 0x8a, 0x2e]],
    [Organs.Petiole, [0x33, 0x66, 0x22]],
];
const wire = ['#6b5a4c', '#4f8a2e', '#336622'].map(fromHex);

function frame(flags: number, species: string) {
    const bytes: number[] = [];
    const u8 = (v: number) => bytes.push(v & 0xff);
    const u32 = (v: number) => { const b = new Uint8Array(4); new DataView(b.buffer).setUint32(0, v, true); bytes.push(...b); };
    const f32 = (v: number) => { const b = new Uint8Array(4); new DataView(b.buffer).setFloat32(0, v, true); bytes.push(...b); };
    u8(7); u8(flags); u32(1);
    const name = new TextEncoder().encode(species);
    u8(name.length); bytes.push(...name);
    u32(organs.length);
    for (const [organ, rgb] of organs) {
        u32(0xffffffff); u8(organ);
        f32(0.5); f32(0.01); //length, radius
        for (const v of transform) f32(v);
        if (flags & ANALYTICS) for (const v of [0.25, 0.5, WOOD, 16, 32]) f32(v);
        for (const c of rgb) u8(c);
        if (flags & ANALYTICS) { f32(2); f32(128); }
    }
    return new Uint8Array(bytes);
}

function decode(flags: number, species = 'fixture species') {
    const reader = new BinaryReader(frame(flags, species));
    const scene = reader.readAgroScene() as PlantModel[];
    expect(reader.isEnd()).toBe(true);
    return scene;
}

function naturalColors(scene: PlantModel[], visualMapping = VisualMappingOptions.Natural) {
    const out = new InstanceList();
    buildPlants(out, {
        scene, visualMapping, debugBoxes: false, showLeaves: true, showRoots: true,
        leafGeom: () => Geom.Plane as number, petalGeom: () => Geom.Plane as number,
    });
    expect(out.count).toBe(organs.length);
    return out.items.slice(0, out.count);
}

describe('the OrganColors frame flag', () => {
    test.each([0, ANALYTICS])('is absent from flags %i', flags => {
        expect(frameCapabilities(decode(flags))!.organColors).toBe(false);
    });

    test.each([ORGAN_COLORS, ORGAN_COLORS | ANALYTICS])('decodes from flags %i without disturbing the others', flags => {
        expect(frameCapabilities(decode(flags))).toEqual({
            version: 7, rootsDownloaded: false, analyticsAvailable: !!(flags & ANALYTICS),
            hasRootGeometry: false, virtualRoots: false, organColors: true,
        });
    });

    test('a flagged frame draws stems and meristems in the wire colour', () => {
        const items = naturalColors(decode(ORGAN_COLORS | ANALYTICS));
        for (let i = 0; i < organs.length; ++i) {
            expect(items[i].color).toEqual(wire[i]);
            expect(items[i].emissive).toEqual({ r: 0, g: 0, b: 0 });
        }
    });

    test('the wire colour replaces the bark map as well', () => {
        const items = naturalColors(decode(ORGAN_COLORS | ANALYTICS, 'Celtis australis'));
        expect(items[0].color).toEqual(wire[0]);
        expect(items[1].color).toEqual(wire[1]);
    });

    test('an unflagged frame reproduces the legacy wood ramp exactly', () => {
        const items = naturalColors(decode(ANALYTICS));
        const ramp = lerpHSL(fromHex('#009900'), fromHex('#7f4f1f'), WOOD);
        expect(items[0].color).toEqual(ramp);
        expect(items[1].color).toEqual(ramp);
        expect(items[2].color).toEqual(wire[2]); //the petiole was always the backend's
        //No analytics: wood ratio 0, the green end of the ramp (through HSL, so not bit-equal to #009900).
        const green = naturalColors(decode(0));
        const start = lerpHSL(fromHex('#009900'), fromHex('#7f4f1f'), 0);
        expect(green[0].color).toEqual(start);
        expect(green[1].color).toEqual(start);
        expect(start.g).toBeCloseTo(fromHex('#009900').g, 12);
    });

    test('an unflagged frame reproduces the Celtis bark exactly', () => {
        const items = naturalColors(decode(ANALYTICS, 'Celtis australis'));
        const bark = lerpHSL(fromHex('#009900'), fromHex('#929087'), WOOD);
        expect(items[0].color).toEqual(bark);
        expect(items[1].color).toEqual(bark);
        expect(items[2].color).toEqual(wire[2]);
    });

    test('data mappings do not read the flag', () => {
        const flagged = naturalColors(decode(ORGAN_COLORS | ANALYTICS), VisualMappingOptions.Water);
        const legacy = naturalColors(decode(ANALYTICS), VisualMappingOptions.Water);
        for (let i = 0; i < organs.length; ++i) {
            expect(flagged[i].color).toEqual({ r: 0, g: 0, b: 0 });
            expect(flagged[i].emissive).toEqual(legacy[i].emissive);
        }
    });

    test('stemColor defaults to the legacy ramp', () => {
        const stem = decode(ORGAN_COLORS | ANALYTICS)[0].primitives[0];
        const ramp = lerpHSL(fromHex('#009900'), fromHex('#7f4f1f'), WOOD);
        expect(stemColor(stem, VisualMappingOptions.Natural).color).toEqual(ramp);
        expect(stemColor(stem, VisualMappingOptions.Natural, undefined, false).color).toEqual(ramp);
        expect(stemColor(stem, VisualMappingOptions.Natural, undefined, true).color).toEqual(wire[0]);
    });
});

//The backend divides every authored colour by the light the viewport puts on it, so these
//mirror LaminaExposure and RoundOrganExposure in Agro/Plant/Leaf/ColorUtils.cs and must
//change together with them: a different hemisphere here shifts every organ colour on screen.
describe('the exposure the wire colours are encoded for', () => {
    test('a sky-facing lamina receives the hemisphere intensity', () => {
        expect(HEMI_SKY).toEqual({ r: 1, g: 1, b: 1 });
        expect(HEMI_INTENSITY).toBe(3.175);
    });

    test('a round organ receives the mean of sky and ground', () => {
        expect(Math.abs(HEMI_INTENSITY * (1 + HEMI_GROUND.r / HEMI_SKY.r) / 2 - 1.845)).toBeLessThan(1e-3);
    });
});
