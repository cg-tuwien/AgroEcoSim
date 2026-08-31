//Writes frames as an image sequence inside a stored ZIP.
//
//Always available: it depends on nothing beyond canvas.toBlob, so it is both the fallback
//when no video codec can be configured and the highest-fidelity option, since it is lossless
//if PNG is chosen. `ffmpeg -framerate N -i frame_%05d.png out.mp4` finishes the job.

import type { PixelReadback } from '../../IViewportRenderer';
import { IVideoWriter } from './IVideoWriter';
import { ZipStoreWriter } from './ZipStoreWriter';
import { encodeImage, ImageFormat } from './PngWriter';

export class ImageSequenceWriter implements IVideoWriter {
    readonly extension = 'zip';
    readonly label: string;
    private zip = new ZipStoreWriter();
    private format: ImageFormat;
    private quality: number;

    constructor(format: ImageFormat = 'image/jpeg', quality = 0.95) {
        this.format = format;
        this.quality = quality;
        this.label = format === 'image/png' ? 'PNG sequence (zip)' : 'JPEG sequence (zip)';
    }

    async init() { /* nothing to configure */ }

    async addFrame(px: PixelReadback, frameIndex: number) {
        const blob = await encodeImage(px, this.format, this.quality);
        const bytes = new Uint8Array(await blob.arrayBuffer());
        const ext = this.format === 'image/png' ? 'png' : 'jpg';
        this.zip.add(`frame_${String(frameIndex).padStart(5, '0')}.${ext}`, bytes);
    }

    async finish(): Promise<Blob> {
        return this.zip.finish();
    }

    abort() { /* the buffer is dropped with this object */ }

    get bytesBuffered() { return this.zip.bytesWritten; }
}
