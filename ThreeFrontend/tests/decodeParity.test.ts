//The new DataView-based BinaryReader must decode byte-for-byte what the old one did.
//
//The rewrite replaced every multi-byte read, so "it still renders" is not evidence: a wrong
//endianness or a mis-stepped offset produces plausible-looking garbage, and the decoded scene
//feeds both the renderer and the analytics ramps. The old implementation is kept verbatim as
//the oracle and the two are compared field by field on real payloads.

import BinaryReader from '../src/helpers/BinaryReader';
import LegacyBinaryReader from './__fixtures__/BinaryReaderLegacy';
const fs = require('fs');

const SESSION = '/home/ubuntu/agro-fixture/session.json';

function bytes(s: string): Uint8Array {
    const b = Buffer.from(s, 'base64');
    return new Uint8Array(b.buffer, b.byteOffset, b.byteLength);
}

describe('BinaryReader rewrite decodes identically', () => {
    const session = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
    const frames: Uint8Array[] = session.history.map((h: any) => bytes(h.s));

    //history[0] is the empty t=0 scene - six bytes, no plants - so the structural checks below
    //use the largest frame instead of the first.
    const biggest = frames.reduce((a, b) => (b.length > a.length ? b : a));

    test('the fixture has a real payload to compare', () => {
        expect(frames.length).toBeGreaterThan(0);
        expect(biggest.length).toBeGreaterThan(1000);
    });

    frames.forEach((frame, i) => {
        test(`history[${i}] matches the legacy decoder exactly`, () => {
            const mine = new BinaryReader(frame).readAgroScene();
            const ref = new LegacyBinaryReader(frame).readAgroScene();

            expect(mine.length).toBe(ref.length);

            let primitives = 0;
            for (let p = 0; p < ref.length; ++p) {
                expect(mine[p].species).toBe(ref[p].species);
                expect(mine[p].primitives.length).toBe(ref[p].primitives.length);

                for (let q = 0; q < ref[p].primitives.length; ++q) {
                    const a: any = mine[p].primitives[q], b: any = ref[p].primitives[q];
                    //toEqual on the whole primitive would compare Float32Arrays elementwise
                    //too, but naming the fields makes a failure say which one moved.
                    expect(a.type).toBe(b.type);
                    expect(a.organ).toBe(b.organ);
                    expect(a.color).toBe(b.color);
                    expect(a.length).toBe(b.length);
                    expect(a.radius).toBe(b.radius);
                    expect(Array.from(a.affineTransform ?? [])).toEqual(Array.from(b.affineTransform ?? []));
                    expect(Array.from(a.stats ?? [])).toEqual(Array.from(b.stats ?? []));
                    ++primitives;
                }
            }
            //Only the empty t=0 frame is allowed to carry nothing.
            if (frame.length > 1000) expect(primitives).toBeGreaterThan(0);
        });
    });

    //A Uint8Array that is a view into a larger buffer is the normal case in Node, and was the
    //one way the DataView could have been wired up wrong while still passing everything else.
    test('reads correctly from a view with a non-zero byteOffset', () => {
        const src = biggest;
        const padded = new Uint8Array(src.length + 7);
        padded.set(src, 7);
        const offsetView = padded.subarray(7);
        expect(offsetView.byteOffset).toBe(7);

        const fromOffset = new BinaryReader(offsetView).readAgroScene();
        const fromZero = new BinaryReader(src.slice()).readAgroScene();
        expect(fromOffset.length).toBe(fromZero.length);
        expect(Array.from(fromOffset[0].primitives[0].affineTransform ?? []))
            .toEqual(Array.from(fromZero[0].primitives[0].affineTransform ?? []));
    });

    //Interning must hand back equal strings, not merely equal-looking ones.
    test('interned colours are correct and shared', () => {
        const a = new BinaryReader(biggest).readAgroScene();
        const b = new BinaryReader(biggest).readAgroScene();
        const ca = a[0].primitives.find((p: any) => p.color)?.color;
        const cb = b[0].primitives.find((p: any) => p.color)?.color;
        expect(ca).toBeDefined();
        expect(ca).toMatch(/^#[0-9a-f]{6}$/);
        //Same instance across decodes - that is the point of the cache.
        expect(ca).toBe(cb);
    });
});
