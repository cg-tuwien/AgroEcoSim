//Deflate and inflate over the browser's own CompressionStream, with no dependency.
//
//Used for two quite different jobs: PNG's IDAT payload, and compressing stored simulation
//frames in memory. Both want the zlib wrapper ('deflate', not 'deflate-raw') - PNG requires it,
//and for stored frames it costs six bytes and buys a checksum.
//
//Real V7 scene data compresses about 1.7x. That is unremarkable for float-heavy payloads and it
//is worth knowing before anyone expects more: the affine transforms and analytics blocks are
//nearly incompressible, and it is the record headers and repeated colours that give back what
//little there is.

/** True when this browser can compress for us; without it callers must hold data uncompressed. */
export function canDeflate(): boolean {
    return typeof CompressionStream !== 'undefined' && typeof DecompressionStream !== 'undefined';
}

async function drain(stream: ReadableStream<Uint8Array>): Promise<Uint8Array> {
    const reader = stream.getReader();
    const parts: Uint8Array[] = [];
    let total = 0;
    for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        parts.push(value);
        total += value.length;
    }

    const out = new Uint8Array(total);
    let at = 0;
    for (const part of parts) { out.set(part, at); at += part.length; }
    return out;
}

function feed(raw: Uint8Array): ReadableStream<Uint8Array> {
    //Fed from a ReadableStream and drained by hand rather than going through Blob.stream() and
    //Response: those are two more browser surfaces to depend on for no benefit, and jsdom has
    //neither, so the tests would need a mock for something this module does not need.
    return new ReadableStream<Uint8Array>({
        start(controller) { controller.enqueue(raw); controller.close(); },
    });
}

export async function deflateBytes(raw: Uint8Array): Promise<Uint8Array> {
    //TypeScript 4.9's lib.dom types the transform's output as unknown, so the cast is the
    //cheapest honest way to say what the spec already guarantees.
    return drain(feed(raw).pipeThrough(new CompressionStream('deflate')) as ReadableStream<Uint8Array>);
}

export async function inflateBytes(packed: Uint8Array): Promise<Uint8Array> {
    return drain(feed(packed).pipeThrough(new DecompressionStream('deflate')) as ReadableStream<Uint8Array>);
}
