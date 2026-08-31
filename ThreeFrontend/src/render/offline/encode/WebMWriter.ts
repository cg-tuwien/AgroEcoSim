//A minimal WebM (Matroska/EBML) muxer for a single video track.
//
//WebCodecs hands back raw encoded chunks with no container, and wrapping them is the
//caller's problem. This writes just enough Matroska for a playable single-track file:
//EBML header, Segment, Info, Tracks, and Clusters of SimpleBlocks. It is deliberately not a
//general Matroska implementation - no audio, no seeking index, no lacing.

const ELEMENT = {
    EBML: 0x1A45DFA3,
    EBMLVersion: 0x4286,
    EBMLReadVersion: 0x42F7,
    EBMLMaxIDLength: 0x42F2,
    EBMLMaxSizeLength: 0x42F3,
    DocType: 0x4282,
    DocTypeVersion: 0x4287,
    DocTypeReadVersion: 0x4285,

    Segment: 0x18538067,
    Info: 0x1549A966,
    TimestampScale: 0x2AD7B1,
    MuxingApp: 0x4D80,
    WritingApp: 0x5741,
    Duration: 0x4489,

    Tracks: 0x1654AE6B,
    TrackEntry: 0xAE,
    TrackNumber: 0xD7,
    TrackUID: 0x73C5,
    TrackType: 0x83,
    CodecID: 0x86,
    Video: 0xE0,
    PixelWidth: 0xB0,
    PixelHeight: 0xBA,

    Cluster: 0x1F43B675,
    Timestamp: 0xE7,
    SimpleBlock: 0xA3,
};

function idBytes(id: number): Uint8Array {
    const out: number[] = [];
    for (let shift = 24; shift >= 0; shift -= 8) {
        const b = (id >>> shift) & 0xFF;
        if (out.length || b) out.push(b);
    }
    return new Uint8Array(out.length ? out : [0]);
}

/**
 * EBML variable-length size. The leading bits mark the width, so the value must fit in
 * 7*width bits; `unknown` emits the all-ones form used for a live-sized Segment.
 */
function vint(value: number, unknown = false): Uint8Array {
    if (unknown) return new Uint8Array([0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

    let width = 1;
    while (width <= 8 && value >= 2 ** (7 * width) - 1) width++;
    const out = new Uint8Array(width);
    let v = value;
    for (let i = width - 1; i >= 0; --i) { out[i] = v & 0xFF; v = Math.floor(v / 256); }
    out[0] |= 1 << (8 - width);
    return out;
}

function uintBytes(value: number): Uint8Array {
    if (value === 0) return new Uint8Array([0]);
    const out: number[] = [];
    let v = value;
    while (v > 0) { out.unshift(v & 0xFF); v = Math.floor(v / 256); }
    return new Uint8Array(out);
}

function floatBytes(value: number): Uint8Array {
    const b = new Uint8Array(8);
    new DataView(b.buffer).setFloat64(0, value, false);
    return b;
}

function concat(parts: Uint8Array[]): Uint8Array {
    const total = parts.reduce((a, p) => a + p.length, 0);
    const out = new Uint8Array(total);
    let at = 0;
    for (const p of parts) { out.set(p, at); at += p.length; }
    return out;
}

function element(id: number, payload: Uint8Array): Uint8Array {
    return concat([idBytes(id), vint(payload.length), payload]);
}

function uintElement(id: number, value: number): Uint8Array {
    return element(id, uintBytes(value));
}

function stringElement(id: number, value: string): Uint8Array {
    return element(id, new TextEncoder().encode(value));
}

export type WebMTrack = {
    codecId: string;      // e.g. V_VP9
    width: number;
    height: number;
};

export class WebMWriter {
    private chunks: Uint8Array[] = [];
    private clusterBlocks: Uint8Array[] = [];
    private clusterTimestamp = 0;
    private lastTimestampMs = 0;
    private frameIntervalMs = 0;
    private open = false;
    private track: WebMTrack;
    /**
     * The Info element's bytes, kept so the duration can be written once it is known.
     * Duration is deliberately the LAST child of Info, which puts its 8-byte float at the
     * very end of this array and makes the patch a single set() with no offset arithmetic.
     */
    private infoBytes: Uint8Array | undefined;

    /** Matroska timestamps are in units of TimestampScale; 1 ms is the conventional choice. */
    private static readonly TIMESTAMP_SCALE_NS = 1_000_000;
    /** A new cluster every ~2s keeps SimpleBlock's signed 16-bit relative timestamp in range. */
    private static readonly CLUSTER_MS = 2000;

    constructor(track: WebMTrack) {
        this.track = track;
        this.writeHeader();
    }

    private writeHeader() {
        this.chunks.push(element(ELEMENT.EBML, concat([
            uintElement(ELEMENT.EBMLVersion, 1),
            uintElement(ELEMENT.EBMLReadVersion, 1),
            uintElement(ELEMENT.EBMLMaxIDLength, 4),
            uintElement(ELEMENT.EBMLMaxSizeLength, 8),
            stringElement(ELEMENT.DocType, 'webm'),
            uintElement(ELEMENT.DocTypeVersion, 2),
            uintElement(ELEMENT.DocTypeReadVersion, 2),
        ])));

        //Unknown-length Segment: the total size is not known until every frame is encoded,
        //and streaming players accept the live form.
        this.chunks.push(idBytes(ELEMENT.Segment));
        this.chunks.push(vint(0, true));

        this.infoBytes = element(ELEMENT.Info, concat([
            uintElement(ELEMENT.TimestampScale, WebMWriter.TIMESTAMP_SCALE_NS),
            stringElement(ELEMENT.MuxingApp, 'AgroEcoSim'),
            stringElement(ELEMENT.WritingApp, 'AgroEcoSim'),
            //Placeholder, overwritten in finish(). Left at zero the Segment is unknown-length
            //AND undurated, so players report an infinite duration and cannot scrub.
            element(ELEMENT.Duration, floatBytes(0)),
        ]));
        this.chunks.push(this.infoBytes);

        this.chunks.push(element(ELEMENT.Tracks, element(ELEMENT.TrackEntry, concat([
            uintElement(ELEMENT.TrackNumber, 1),
            uintElement(ELEMENT.TrackUID, 1),
            uintElement(ELEMENT.TrackType, 1),   // 1 = video
            stringElement(ELEMENT.CodecID, this.track.codecId),
            element(ELEMENT.Video, concat([
                uintElement(ELEMENT.PixelWidth, this.track.width),
                uintElement(ELEMENT.PixelHeight, this.track.height),
            ])),
        ]))));

        this.open = true;
    }

    /** `timestampMs` must be non-decreasing. */
    addFrame(data: Uint8Array, timestampMs: number, isKeyFrame: boolean) {
        if (!this.open) throw new Error('WebMWriter has already been finalised');

        //A cluster must start on a keyframe, and SimpleBlock stores its timestamp as a signed
        //16-bit offset from the cluster, so clusters cannot span more than ~32s regardless.
        if (this.clusterBlocks.length === 0
            || (isKeyFrame && timestampMs - this.clusterTimestamp >= WebMWriter.CLUSTER_MS)) {
            this.flushCluster();
            this.clusterTimestamp = timestampMs;
        }

        const relative = Math.max(-32768, Math.min(32767, Math.round(timestampMs - this.clusterTimestamp)));
        const header = new Uint8Array(4);
        header[0] = 0x81;                                  // track number 1, as a vint
        new DataView(header.buffer).setInt16(1, relative, false);
        header[3] = isKeyFrame ? 0x80 : 0x00;              // keyframe flag

        this.clusterBlocks.push(element(ELEMENT.SimpleBlock, concat([header, data])));
        this.lastTimestampMs = timestampMs;
    }

    private flushCluster() {
        if (this.clusterBlocks.length === 0) return;
        this.chunks.push(element(ELEMENT.Cluster, concat([
            uintElement(ELEMENT.Timestamp, Math.round(this.clusterTimestamp)),
            ...this.clusterBlocks,
        ])));
        this.clusterBlocks = [];
    }

    finish(): Blob {
        this.flushCluster();

        //Duration is in TimestampScale units, i.e. milliseconds here. One frame interval is
        //added so the last frame has a presentation window rather than ending on its own
        //timestamp.
        if (this.infoBytes) {
            const duration = this.lastTimestampMs + this.frameIntervalMs;
            this.infoBytes.set(floatBytes(duration), this.infoBytes.length - 8);
        }

        this.open = false;
        return new Blob(this.chunks as BlobPart[], { type: 'video/webm' });
    }

    /** Declares the frame interval so the final frame contributes to the duration. */
    setFrameInterval(ms: number) {
        this.frameIntervalMs = ms;
    }

    get durationMs() { return this.lastTimestampMs; }
}
