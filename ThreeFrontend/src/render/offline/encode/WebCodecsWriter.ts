//Encodes frames with WebCodecs and muxes them into WebM.

import type { PixelReadback } from '../../IViewportRenderer';
import { IVideoWriter } from './IVideoWriter';
import { WebMWriter } from './WebMWriter';
import { CodecChoice } from './codecs';

/** Frames in flight. Each 4K frame is ~33 MB, so this bounds peak memory at ~130 MB. */
const MAX_QUEUE = 4;

export class WebCodecsWriter implements IVideoWriter {
    readonly extension = 'webm';
    readonly label: string;

    private choice: CodecChoice;
    private bitrate: number;
    private encoder: VideoEncoder | undefined;
    private muxer: WebMWriter | undefined;
    private fps = 25;
    private gop = 50;
    private failure: Error | undefined;
    private canUseBufferFrames = true;

    constructor(choice: CodecChoice, bitrate: number) {
        this.choice = choice;
        this.bitrate = bitrate;
        this.label = `WebM (${choice.label})`;
    }

    async init(cfg: { width: number; height: number; fps: number }) {
        this.fps = cfg.fps;
        //A keyframe every two seconds: frequent enough to seek comfortably, rare enough not
        //to dominate the bitrate.
        this.gop = Math.max(1, Math.round(cfg.fps * 2));

        this.muxer = new WebMWriter({
            codecId: this.choice.muxerCodec,
            width: cfg.width,
            height: cfg.height,
        });
        this.muxer.setFrameInterval(1000 / cfg.fps);

        this.encoder = new VideoEncoder({
            //Chunks go straight into the muxer rather than being collected: peak memory is
            //then the size of the finished file, not frames x count.
            output: (chunk) => {
                const buf = new Uint8Array(chunk.byteLength);
                chunk.copyTo(buf);
                this.muxer?.addFrame(buf, chunk.timestamp / 1000, chunk.type === 'key');
            },
            error: (e) => { this.failure = e instanceof Error ? e : new Error(String(e)); },
        });

        this.encoder.configure({
            codec: this.choice.codec,
            width: cfg.width,
            height: cfg.height,
            bitrate: this.bitrate,
            framerate: cfg.fps,
            latencyMode: 'quality',
            hardwareAcceleration: 'no-preference',
        });
    }

    async addFrame(px: PixelReadback, frameIndex: number) {
        if (this.failure) throw this.failure;
        const encoder = this.encoder;
        if (!encoder) throw new Error('WebCodecsWriter.init was not called');

        while (encoder.encodeQueueSize > MAX_QUEUE) await this.waitForDequeue(encoder);

        //Timestamps are microseconds.
        const timestamp = Math.round((frameIndex * 1e6) / this.fps);
        const duration = Math.round(1e6 / this.fps);
        const frame = this.makeFrame(px, timestamp, duration);

        try {
            encoder.encode(frame, { keyFrame: frameIndex % this.gop === 0 });
        } finally {
            //Mandatory. A VideoFrame holds a GPU-side buffer that is not garbage collected;
            //leaking a handful is enough to exhaust memory within seconds at 4K.
            frame.close();
        }
    }

    private makeFrame(px: PixelReadback, timestamp: number, duration: number): VideoFrame {
        if (this.canUseBufferFrames) {
            try {
                return new VideoFrame(px.data, {
                    format: 'RGBA',
                    codedWidth: px.width,
                    codedHeight: px.height,
                    timestamp,
                    duration,
                });
            } catch {
                //Not every engine accepts a raw buffer; fall back to a canvas source once and
                //remember, rather than throwing on every frame.
                this.canUseBufferFrames = false;
            }
        }

        const canvas = document.createElement('canvas');
        canvas.width = px.width;
        canvas.height = px.height;
        canvas.getContext('2d')!.putImageData(new ImageData(px.data, px.width, px.height), 0, 0);
        return new VideoFrame(canvas, { timestamp, duration });
    }

    private waitForDequeue(encoder: VideoEncoder): Promise<void> {
        return new Promise(resolve => {
            let settled = false;
            const done = () => {
                if (settled) return;
                settled = true;
                encoder.removeEventListener('dequeue', done);
                resolve();
            };
            //`dequeue` is the precise signal; the poll is a safety net for engines that do
            //not fire it.
            encoder.addEventListener('dequeue', done);
            setTimeout(done, 8);
        });
    }

    async finish(): Promise<Blob> {
        if (this.failure) throw this.failure;
        await this.encoder?.flush();
        this.encoder?.close();
        if (!this.muxer) throw new Error('WebCodecsWriter.init was not called');
        return this.muxer.finish();
    }

    abort() {
        //close, not flush: a cancelled job should not spend time finishing frames nobody
        //will see.
        try { this.encoder?.close(); } catch { /* already closed */ }
    }
}
