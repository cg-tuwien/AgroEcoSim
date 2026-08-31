//Turns a PixelReadback into an image Blob, via a detached canvas.
//
//No PNG encoder of our own: the browser already has one behind canvas.toBlob, and it is
//both faster and more correct than anything worth hand-rolling here.

import type { PixelReadback } from '../../IViewportRenderer';
import { canEncodePng, encodePng } from './PngEncoder';

export type ImageFormat = 'image/png' | 'image/jpeg';

export async function encodeImage(px: PixelReadback, format: ImageFormat = 'image/png', quality = 0.95): Promise<Blob> {
    //PNG is encoded directly; see PngEncoder for why the canvas is not trusted with it. JPEG
    //still goes through the canvas, which is the only lossy encoder available without a
    //dependency - and a JPEG that comes out blank would at least be reproducible here.
    if (format === 'image/png' && canEncodePng()) return encodePng(px.data, px.width, px.height);

    const canvas = document.createElement('canvas');
    canvas.width = px.width;
    canvas.height = px.height;

    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Could not obtain a 2D context for image encoding');

    //PixelReadback is already top-down, straight-alpha, sRGB-encoded, so it maps onto
    //ImageData one-to-one with no conversion.
    ctx.putImageData(new ImageData(px.data, px.width, px.height), 0, 0);

    const blob = await new Promise<Blob | null>(resolve =>
        canvas.toBlob(resolve, format, format === 'image/jpeg' ? quality : undefined));

    if (!blob) throw new Error('canvas.toBlob produced no data');
    return blob;
}
