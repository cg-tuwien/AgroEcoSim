//A PNG encoder, so exporting an image does not depend on a 2D canvas.
//
//`canvas.toBlob` is the obvious way to make a PNG and it was the original one. It is also a
//GPU-backed surface in Chromium, and on a machine already under GPU pressure it can hand back
//a blank image without failing: the readback was verified good, the video export of the very
//same pixels worked, and the PNG still came out empty. Nothing in that path reports an error,
//so there is nothing to catch.
//
//Encoding here removes the canvas from the picture entirely. It is also strictly better for
//this use: colour type 2 stores RGB with **no alpha channel**, which is what these exports
//want - the renderer is opaque, and an alpha channel is only somewhere for a bug to hide.
//
//Compression is `CompressionStream('deflate')`, which is the zlib format PNG requires, so no
//dependency and no hand-rolled deflate.

import { crc32 } from './ZipStoreWriter';
import { canDeflate, deflateBytes } from '../../../helpers/deflate';

const PNG_SIGNATURE = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** True when this browser can deflate for us; without it the caller must fall back. */
export function canEncodePng(): boolean {
    return canDeflate();
}

function chunk(type: string, body: Uint8Array): Uint8Array {
    const out = new Uint8Array(12 + body.length);
    const view = new DataView(out.buffer);
    view.setUint32(0, body.length);
    for (let i = 0; i < 4; ++i) out[4 + i] = type.charCodeAt(i);
    out.set(body, 8);
    //The CRC covers the type and the body, not the length.
    view.setUint32(8 + body.length, crc32(out.subarray(4, 8 + body.length)) >>> 0);
    return out;
}

/**
 * RGBA input to RGB scanlines, each prefixed with filter type 0.
 *
 * Filter 0 (None) rather than one of the predictive filters: these are flat-shaded renders of
 * large uniform areas, which deflate compresses well on its own, and the filters would cost a
 * pass over every pixel for a few percent.
 */
function scanlines(rgba: Uint8ClampedArray, width: number, height: number): Uint8Array {
    const stride = width * 3;
    const out = new Uint8Array(height * (stride + 1));
    for (let y = 0; y < height; ++y) {
        let at = y * (stride + 1);
        out[at++] = 0;
        let from = y * width * 4;
        for (let x = 0; x < width; ++x) {
            out[at++] = rgba[from];
            out[at++] = rgba[from + 1];
            out[at++] = rgba[from + 2];
            from += 4;
        }
    }
    return out;
}


/** The PNG bytes. Split from the Blob so the encoding can be tested without a Blob at all. */
export async function encodePngBytes(rgba: Uint8ClampedArray, width: number, height: number): Promise<Uint8Array> {
    const ihdr = new Uint8Array(13);
    const view = new DataView(ihdr.buffer);
    view.setUint32(0, width);
    view.setUint32(4, height);
    ihdr[8] = 8;      // bit depth
    ihdr[9] = 2;      // colour type 2: truecolour, no alpha
    ihdr[10] = 0;     // deflate
    ihdr[11] = 0;     // adaptive filtering
    ihdr[12] = 0;     // no interlace

    const idat = await deflateBytes(scanlines(rgba, width, height));
    const parts = [PNG_SIGNATURE, chunk('IHDR', ihdr), chunk('IDAT', idat), chunk('IEND', new Uint8Array(0))];

    const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
    let at = 0;
    for (const part of parts) { out.set(part, at); at += part.length; }
    return out;
}

export async function encodePng(rgba: Uint8ClampedArray, width: number, height: number): Promise<Blob> {
    return new Blob([await encodePngBytes(rgba, width, height)], { type: 'image/png' });
}
