//Common interface for the things that can turn a stream of frames into one downloadable file.

import type { PixelReadback } from '../../IViewportRenderer';

export interface IVideoWriter {
    /** File extension for the download, without a dot. */
    readonly extension: string;
    readonly label: string;
    init(cfg: { width: number; height: number; fps: number }): Promise<void>;
    /** Must apply backpressure: resolve only when it is safe to submit the next frame. */
    addFrame(px: PixelReadback, frameIndex: number): Promise<void>;
    finish(): Promise<Blob>;
    /** Tear down without producing a file, e.g. after a cancel. */
    abort(): void;
}
