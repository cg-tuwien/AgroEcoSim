//The terrain stream, which is written by C# and read only here.
//
//Nothing type-checks across that seam, and it broke exactly once in a way worth pinning: the three
//soil implementations did not agree on the format. `SoilFormationsList.Serialize` wrote version 1
//with an obstacle section; `SoilFormationRegularVoxels` and `SoilFormationTetrahedrons` wrote
//version 0 and simply stopped after the fields. That was invisible while the only terrain ever
//sent came from an imported model - a box field was never on this wire - and storing every run's
//terrain put one there, where `readTerrain` ran off the end of its own buffer with a RangeError.
//
//So: a stream that stops after the fields must still be REJECTED loudly rather than half-read, and
//the shape the producers now emit must decode. Both directions matter - the first is what makes a
//future regression visible instead of silent.

import BinaryReader from '../src/helpers/BinaryReader';

/** One box field record, as `SoilFormationRegularVoxels.Write` emits it. */
function field(view: DataView, buf: Uint8Array, p: number, id: string): number {
    buf[p++] = 0;                                                            //type: box
    for (const f of [0, 0, 0]) { view.setFloat32(p, f, true); p += 4; }      //position
    for (const f of [10, 3, 10]) { view.setFloat32(p, f, true); p += 4; }    //metric size
    for (const f of [0, 0, 0, 1]) { view.setFloat32(p, f, true); p += 4; }   //orientation
    buf[p++] = id.length;                                                    //7-bit encoded length
    for (let i = 0; i < id.length; ++i) buf[p++] = id.charCodeAt(i);
    return p;
}

/** What the soil serializers write: version, field count, fields, then the obstacle section. */
function terrainBytes(version: number, withObstacleSection: boolean): Uint8Array {
    const id = 'DEFAULT';
    const size = 1 + 4 + (1 + 12 + 12 + 16 + 1 + id.length) + (withObstacleSection ? 8 : 0);
    const buf = new Uint8Array(size);
    const view = new DataView(buf.buffer);
    let p = 0;
    buf[p++] = version;
    view.setInt32(p, 1, true); p += 4;
    p = field(view, buf, p, id);
    if (withObstacleSection) {
        view.setInt32(p, 0, true); p += 4;  //no obstacles
        view.setInt32(p, 0, true); p += 4;  //no groups
    }
    expect(p).toBe(buf.length);
    return buf;
}

describe('terrain stream', () => {
    it('decodes a field-only terrain that carries its obstacle section', () => {
        const result = new BinaryReader(terrainBytes(1, true)).readTerrain();
        expect(result.terrains.length).toBe(1);
        expect(result.obstacles.length).toBe(0);
    });

    it('throws rather than half-reads a stream that stops after the fields', () => {
        //The regression this file exists for. A truncated stream must not decode to "one tray and
        //no obstacles" - that is indistinguishable from the correct result above, so a producer
        //that forgot the obstacle section would look fine until something read past the end.
        expect(() => new BinaryReader(terrainBytes(0, false)).readTerrain()).toThrow(RangeError);
    });

    it('reads the group table only from version 1, so the obstacle count is not mistaken for it', () => {
        //Version 0 with an obstacle section: the count is read, the group table is not. Pinned
        //because the reader branches on the version and the producers all now write 1.
        const result = new BinaryReader(terrainBytes(0, true)).readTerrain();
        expect(result.terrains.length).toBe(1);
        expect(result.obstacles.length).toBe(0);
    });
});
