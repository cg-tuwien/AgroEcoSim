//Minimal ambient declarations for the WebCodecs subset used by the video writer.
//
//TypeScript 4.9's lib.dom.d.ts predates WebCodecs entirely (`declare var VideoEncoder`
//does not appear in it), and the no-new-dependencies constraint rules out
//@types/dom-webcodecs. Only what is actually called is declared here; anything broader
//would have to be kept in sync by hand for no benefit.

interface VideoEncoderEncodeOptions { keyFrame?: boolean }

interface EncodedVideoChunk {
    readonly type: 'key' | 'delta';
    readonly timestamp: number;
    readonly duration: number | null;
    readonly byteLength: number;
    copyTo(destination: BufferSource): void;
}

interface EncodedVideoChunkMetadata {
    decoderConfig?: {
        codec: string;
        description?: BufferSource;
        codedWidth?: number;
        codedHeight?: number;
    };
}

interface VideoEncoderConfig {
    codec: string;
    width: number;
    height: number;
    bitrate?: number;
    framerate?: number;
    latencyMode?: 'quality' | 'realtime';
    hardwareAcceleration?: 'no-preference' | 'prefer-hardware' | 'prefer-software';
    avc?: { format?: 'annexb' | 'avc' };
}

interface VideoEncoderSupport {
    supported: boolean;
    config: VideoEncoderConfig;
}

interface VideoEncoderInit {
    output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => void;
    error: (error: DOMException) => void;
}

declare class VideoEncoder {
    constructor(init: VideoEncoderInit);
    readonly state: 'unconfigured' | 'configured' | 'closed';
    readonly encodeQueueSize: number;
    configure(config: VideoEncoderConfig): void;
    encode(frame: VideoFrame, options?: VideoEncoderEncodeOptions): void;
    flush(): Promise<void>;
    close(): void;
    addEventListener(type: 'dequeue', listener: () => void): void;
    removeEventListener(type: 'dequeue', listener: () => void): void;
    static isConfigSupported(config: VideoEncoderConfig): Promise<VideoEncoderSupport>;
}

interface VideoFrameInit {
    format?: 'RGBA' | 'RGBX' | 'BGRA' | 'BGRX' | 'I420';
    codedWidth?: number;
    codedHeight?: number;
    timestamp: number;
    duration?: number;
    colorSpace?: {
        primaries?: string;
        transfer?: string;
        matrix?: string;
        fullRange?: boolean;
    };
}

declare class VideoFrame {
    constructor(data: BufferSource, init: VideoFrameInit & { format: string; codedWidth: number; codedHeight: number });
    constructor(source: CanvasImageSource, init: { timestamp: number; duration?: number });
    readonly codedWidth: number;
    readonly codedHeight: number;
    close(): void;
}
