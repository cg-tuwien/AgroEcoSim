//Codec selection for WebCodecs video export.

export type CodecChoice = {
    codec: string;
    /** Track codec id as the container needs it. */
    container: 'webm';
    muxerCodec: 'V_VP9' | 'V_VP8' | 'V_AV1';
    label: string;
};

/**
 * WebM candidates, best first.
 *
 * Only WebM is offered. WebCodecs emits raw encoded chunks and something must wrap them in a
 * container; a single-track WebM needs an EBML header, Info, Tracks and Clusters of
 * SimpleBlocks, whereas MP4 needs moov/stbl/avcC/stss/ctts and is an order of magnitude more
 * work to get subtly right. With no new dependencies permitted, WebM is the honest choice -
 * and where it is unavailable the PNG/JPEG sequence is a complete fallback.
 */
const WEBM_CANDIDATES: CodecChoice[] = [
    { codec: 'vp09.00.10.08', container: 'webm', muxerCodec: 'V_VP9', label: 'VP9' },
    { codec: 'vp8', container: 'webm', muxerCodec: 'V_VP8', label: 'VP8' },
    { codec: 'av01.0.04M.08', container: 'webm', muxerCodec: 'V_AV1', label: 'AV1' },
];

export function webCodecsAvailable(): boolean {
    return typeof VideoEncoder !== 'undefined'
        && typeof VideoFrame !== 'undefined'
        && typeof window !== 'undefined'
        && window.isSecureContext !== false;
}

/**
 * Returns the first candidate this browser can actually encode, or null.
 *
 * Null is a first-class outcome, not an error: Safari has no VP9 encoder and Firefox's
 * support varies, so the caller must be prepared to fall back to an image sequence.
 */
export async function pickCodec(width: number, height: number, fps: number, bitrate: number): Promise<CodecChoice | null> {
    if (!webCodecsAvailable()) return null;

    for (const candidate of WEBM_CANDIDATES) {
        const config = {
            codec: candidate.codec,
            width, height, bitrate,
            framerate: fps,
            latencyMode: 'quality' as const,
            hardwareAcceleration: 'no-preference' as const,
        };
        try {
            const { supported } = await VideoEncoder.isConfigSupported(config);
            if (supported) return candidate;
        } catch {
            //An unparseable codec string rejects rather than returning false; try the next.
        }
    }
    return null;
}

/** Bits per pixel per frame. Multiply by width*height*fps for a bitrate. */
export const QUALITY_BPP = { low: 0.05, medium: 0.10, high: 0.20 };
export type QualityLevel = keyof typeof QUALITY_BPP;

export function bitrateFor(width: number, height: number, fps: number, quality: QualityLevel): number {
    return Math.round(width * height * fps * QUALITY_BPP[quality]);
}

/** H.264-style 4:2:0 chroma subsampling needs even dimensions; VP9/AV1 are happier too. */
export function evenDimension(n: number): number {
    return n % 2 === 0 ? n : n + 1;
}
