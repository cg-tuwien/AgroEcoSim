import BinaryReader from '../src/helpers/BinaryReader';
import { Organs } from '../src/helpers/Primitives';
import { frameCapabilities, PlantModel } from '../src/helpers/Scene';

class Writer {
    private bytes: number[] = [];
    u8(value: number) { this.bytes.push(value & 0xff); return this; }
    u32(value: number) {
        const bytes = new Uint8Array(4);
        new DataView(bytes.buffer).setUint32(0, value, true);
        this.bytes.push(...bytes); return this;
    }
    f32(value: number) {
        const bytes = new Uint8Array(4);
        new DataView(bytes.buffer).setFloat32(0, value, true);
        this.bytes.push(...bytes); return this;
    }
    str(value: string) {
        const bytes = new TextEncoder().encode(value);
        this.u8(bytes.length); this.bytes.push(...bytes); return this;
    }
    append(value: Uint8Array) { this.bytes.push(...value); return this; }
    done() { return new Uint8Array(this.bytes); }
}

const geometryId = 'c'.repeat(64);
const transform = [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4];
const analytics = [0.25, 0.5, 16, 32, 64];

function recordedAssets(w: Writer) {
    const asset = new Writer().u8(0).u8(1).u32(1); //profile, version, one blade slice
    //One triangle for each of the leaf and petal meshes, with one U coordinate per vertex.
    for (let mesh = 0; mesh < 2; ++mesh) {
        asset.u32(3);
        for (const value of [0, 0, 0, 1, 0, 0, 0, 1, 0]) asset.f32(value);
        asset.u32(3).u32(0).u32(1).u32(2).f32(0).f32(1).f32(0);
    }
    const bytes = asset.done();
    w.u32(1).str(geometryId).u32(bytes.length).append(bytes);
}

function frame(version: number, flags: number, roots: 'segments' | 'virtual' | 'empty' = 'empty', empty = false) {
    const w = new Writer().u8(version).u8(flags);
    if (version >= 8) recordedAssets(w);
    w.u32(empty ? 0 : 1);
    if (empty) return w.done();
    w.str('capability fixture');
    if (version >= 8) w.str(geometryId);
    if (version >= 9) w.u32(3); // earlier requests can still be pending or absent
    w.u32(1).u32(0xffffffff).u8(Organs.Leaf);
    if (version >= 8) w.u8(0); //no contact deformation
    for (const value of transform) w.f32(value);
    if (flags & 2) for (const value of analytics) w.f32(value);
    w.u8(0x22).u8(0xaa).u8(0x33);
    if (flags & 2) w.f32(2).f32(128); //auxins and daily efficiency
    if (flags & 1) {
        w.u8(roots === 'virtual' ? 1 : 0);
        if (roots === 'virtual') w.f32(0.2).f32(0.3).f32(0.4);
        else {
            w.u32(roots === 'segments' ? 1 : 0);
            if (roots === 'segments') {
                w.u32(0xffffffff).u8(1).f32(0.05).f32(0.001);
                for (const value of transform) w.f32(value);
                if (flags & 2) for (const value of analytics) w.f32(value);
            }
        }
    }
    return w.done();
}

function decode(version: number, flags: number, roots?: 'segments' | 'virtual' | 'empty', empty?: boolean) {
    const reader = new BinaryReader(frame(version, flags, roots, empty));
    const scene = reader.readAgroScene() as PlantModel[];
    expect(reader.isEnd()).toBe(true);
    return scene;
}

describe.each([7, 8, 9])('V%i frame capabilities', version => {
    test('only V9 identifies the original plant request', () => {
        expect(decode(version, 0)[0].sourcePlantIndex).toBe(version >= 9 ? 3 : undefined);
    });
    test.each([0, 2])('records flags %i without inferring analytics from zero-filled stats', flags => {
        const scene = decode(version, flags);
        expect(frameCapabilities(scene)).toEqual({
            version, rootsDownloaded: false, analyticsAvailable: !!(flags & 2),
            hasRootGeometry: false, virtualRoots: false, organColors: false,
        });
        expect(Array.from(scene[0].primitives[0].stats!.subarray(0, 7)))
            .toEqual(flags & 2 ? [0.25, 0.5, 2, 0, 16, 32, 64] : [0, 0, 0, 0, 0, 0, 0]);
    });

    test.each([1, 3])('records actual downloaded root segments with flags %i', flags => {
        const scene = decode(version, flags, 'segments');
        expect(frameCapabilities(scene)).toEqual({
            version, rootsDownloaded: true, analyticsAvailable: !!(flags & 2),
            hasRootGeometry: true, virtualRoots: false, organColors: false,
        });
        expect(scene[0].primitives.map(primitive => primitive.organ)).toEqual([Organs.Leaf, Organs.Root]);
        expect(Array.from(scene[0].primitives[1].stats!.subarray(0, 7)))
            .toEqual(flags & 2 ? [0.25, 0.5, 0, 0, 16, 32, 64] : [0, 0, 0, 0, 0, 0, 0]);
    });

    test.each([1, 3])('distinguishes virtual roots from downloadable geometry with flags %i', flags => {
        const scene = decode(version, flags, 'virtual');
        expect(frameCapabilities(scene)).toEqual({
            version, rootsDownloaded: true, analyticsAvailable: !!(flags & 2),
            hasRootGeometry: false, virtualRoots: true, organColors: false,
        });
        expect(scene[0].primitives).toHaveLength(1);
    });

    test.each([4, 5, 6, 7])('records the organ colour flag independently with flags %i', flags => {
        const scene = decode(version, flags, 'segments');
        expect(frameCapabilities(scene)).toEqual({
            version, rootsDownloaded: !!(flags & 1), analyticsAvailable: !!(flags & 2),
            hasRootGeometry: !!(flags & 1), virtualRoots: false, organColors: true,
        });
        expect(scene[0].primitives[0].color).toBe('#22aa33');
    });

    test('distinguishes an empty real root section from omitted roots', () => {
        expect(frameCapabilities(decode(version, 1, 'empty'))).toEqual({
            version, rootsDownloaded: true, analyticsAvailable: false,
            hasRootGeometry: false, virtualRoots: false, organColors: false,
        });
    });

    test.each([0, 1, 2, 3])('retains capabilities for an empty frame with flags %i', flags => {
        const scene = decode(version, flags, 'empty', true);
        expect(scene).toEqual([]);
        expect(frameCapabilities(scene)).toEqual({
            version, rootsDownloaded: !!(flags & 1), analyticsAvailable: !!(flags & 2),
            hasRootGeometry: false, virtualRoots: false, organColors: false,
        });
    });
});

test('V9 preserves request identities when an earlier scheduled plant becomes visible', () => {
    const read = (indices: number[]) => {
        const w = new Writer().u8(9).u8(0);
        recordedAssets(w);
        w.u32(indices.length);
        for (const index of indices) w.str('same species').str(geometryId).u32(index).u32(0);
        const reader = new BinaryReader(w.done());
        const scene = reader.readAgroScene();
        expect(reader.isEnd()).toBe(true);
        return scene;
    };
    const before = read([1, 3]), after = read([0, 1, 3]);
    expect(before.map(plant => plant.sourcePlantIndex)).toEqual([1, 3]);
    expect(after.map(plant => plant.sourcePlantIndex)).toEqual([0, 1, 3]);
    expect(before[0].sourcePlantIndex).toBe(after[1].sourcePlantIndex);
});

test('capabilities stay with each decoded frame and do not alter the array API', () => {
    const roots = decode(7, 3, 'segments');
    const noRoots = decode(8, 0);
    expect(frameCapabilities(roots)!.hasRootGeometry).toBe(true);
    expect(frameCapabilities(noRoots)!.rootsDownloaded).toBe(false);
    expect(frameCapabilities([])).toBeUndefined();
    expect(Object.keys(roots)).toEqual(['0']);
    expect(JSON.parse(JSON.stringify(roots))).toHaveLength(1);
});
