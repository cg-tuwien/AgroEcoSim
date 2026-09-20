import { canDeflate, deflateBytes } from './deflate';
import { downloadBlob } from './download';

export interface ArchiveEntry { t: number; s: Uint8Array | undefined; plain: boolean }
export interface PackedFrame { bytes: Uint8Array; plain: boolean }
const sources = new WeakMap<ArchiveEntry, () => Promise<PackedFrame>>();
const magic = new TextEncoder().encode('AGROAR01');
const footerMagic = new TextEncoder().encode('AGROEND1');
const entrySize = 24;

export function localArchiveFrame(entry: ArchiveEntry): Promise<PackedFrame> | undefined {
    return sources.get(entry)?.();
}

interface Sink { write(data: Uint8Array): Promise<void>; close(): Promise<void>; abort?(): Promise<void> }
interface ArchiveFileHandle { createWritable(): Promise<Sink>; getFile(): Promise<File> }

/** Version 1: magic + metadata length + UTF8 JSON, frame payloads, 24-byte index entries,
 * then index offset (f64 exact integer), frame count (u32), and footer magic. Payloads are
 * unchanged raw V7 or zlib streams. Optional final index entry (step UINT32_MAX, flag 2)
 * holds the complete gzipped measurement. All offsets must fit JS's exact-integer range. */
export async function writeRunArchive(metadata: unknown, history: readonly ArchiveEntry[],
    read: (entry: ArchiveEntry) => Promise<PackedFrame>, sink: Sink,
    fullMeasurement?: () => Promise<ReadableStream<Uint8Array>>): Promise<void> {
    const settings = new TextEncoder().encode(JSON.stringify(metadata));
    const header = new Uint8Array(12);
    header.set(magic); new DataView(header.buffer).setUint32(8, settings.length, true);
    await sink.write(header); await sink.write(settings);
    let offset = header.length + settings.length;
    const count = history.length + (fullMeasurement ? 1 : 0);
    const index = new Uint8Array(count * entrySize);
    const view = new DataView(index.buffer);
    for (let i = 0; i < history.length; ++i) {
        let frame = await read(history[i]);
        if (frame.plain && canDeflate()) {
            const compressed = await deflateBytes(frame.bytes);
            if (compressed.length < frame.bytes.length) frame = { bytes: compressed, plain: false };
        }
        const at = i * entrySize;
        view.setUint32(at, history[i].t, true);
        view.setFloat64(at + 4, offset, true);
        view.setUint32(at + 12, frame.bytes.length, true);
        view.setUint32(at + 16, frame.plain ? 1 : 0, true);
        await sink.write(frame.bytes);
        offset += frame.bytes.length;
        if (!Number.isSafeInteger(offset)) throw new Error('The archive exceeds the supported file size.');
    }
    if (fullMeasurement) {
        const start = offset;
        const reader = (await fullMeasurement()).getReader();
        try {
            for (;;) {
                const chunk = await reader.read();
                if (chunk.done) break;
                await sink.write(chunk.value); offset += chunk.value.length;
            }
        } finally { await reader.cancel(); reader.releaseLock(); }
        if (offset - start > 0xffffffff || !Number.isSafeInteger(offset)) throw new Error('Measurement attachment is too large.');
        const at = history.length * entrySize;
        view.setUint32(at, 0xffffffff, true); view.setFloat64(at + 4, start, true);
        view.setUint32(at + 12, offset - start, true); view.setUint32(at + 16, 2, true);
    }
    await sink.write(index);
    const footer = new Uint8Array(20); const tail = new DataView(footer.buffer);
    tail.setFloat64(0, offset, true); tail.setUint32(8, count, true); footer.set(footerMagic, 12);
    await sink.write(footer);
}

export async function readRunArchive(file: Blob): Promise<{ metadata: any; history: ArchiveEntry[]; fullMeasurement?: Blob } | undefined> {
    const head = new Uint8Array(await file.slice(0, 12).arrayBuffer());
    if (!magic.every((v, i) => head[i] === v)) return undefined;
    if (file.size < 32 || head.length !== 12) throw new Error('Truncated run archive.');
    const settingsLength = new DataView(head.buffer).getUint32(8, true);
    const tail = new Uint8Array(await file.slice(-20).arrayBuffer());
    if (!footerMagic.every((v, i) => tail[i + 12] === v)) throw new Error('Incomplete run archive.');
    const footer = new DataView(tail.buffer); const offset = footer.getFloat64(0, true);
    const count = footer.getUint32(8, true);
    if (!Number.isSafeInteger(offset) || offset < 12 + settingsLength || offset + count * entrySize !== file.size - 20)
        throw new Error('Invalid run archive index.');
    const metadata = JSON.parse(await file.slice(12, 12 + settingsLength).text());
    const bytes = await file.slice(offset, file.size - 20).arrayBuffer(); const index = new DataView(bytes);
    const history: ArchiveEntry[] = []; let end = 12 + settingsLength; let previousStep = -1;
    let fullMeasurement: Blob | undefined;
    for (let i = 0; i < count; ++i) {
        const at = i * entrySize; const t = index.getUint32(at, true);
        const start = index.getFloat64(at + 4, true); const length = index.getUint32(at + 12, true);
        const flag = index.getUint32(at + 16, true);
        if (t <= previousStep || !Number.isSafeInteger(start) || start !== end || start + length > offset || flag > 2)
            throw new Error('Invalid run archive frame.');
        if (flag === 2) {
            if (i !== count - 1 || t !== 0xffffffff) throw new Error('Invalid measurement attachment.');
            fullMeasurement = file.slice(start, start + length, 'application/gzip'); end = start + length;
            continue;
        }
        const entry: ArchiveEntry = { t, s: undefined, plain: flag === 1 };
        sources.set(entry, async () => ({ bytes: new Uint8Array(await file.slice(start, start + length).arrayBuffer()), plain: flag === 1 }));
        history.push(entry); previousStep = t; end = start + length;
    }
    if (end !== offset) throw new Error('Unindexed data in run archive.');
    return { metadata, history, fullMeasurement };
}

interface ArchiveContents {
    metadata: unknown;
    history: readonly ArchiveEntry[];
    read(entry: ArchiveEntry): Promise<PackedFrame>;
    fullMeasurement?: () => Promise<ReadableStream<Uint8Array>>;
    verify?(): Promise<void>;
}

/** Open the picker while the click still has user activation, then prepare and stream the run. */
export async function saveRunArchive(prepare: () => Promise<ArchiveContents>): Promise<void> {
    const picker = (window as unknown as { showSaveFilePicker?: (options: unknown) => Promise<ArchiveFileHandle> }).showSaveFilePicker;
    const filename = `AgroEco-${new Date().toISOString().replace(/[:.]/g, '-')}.agro`;
    let sink: Sink | undefined;
    let directory: { removeEntry(name: string): Promise<void> } | undefined;
    let handle: ArchiveFileHandle;
    try {
        if (picker) handle = await picker({ suggestedName: filename, types: [{ description: 'Agro simulation', accept: { 'application/octet-stream': ['.agro'] } }] });
        else {
            const storage = navigator.storage as unknown as { getDirectory(): Promise<{ getFileHandle(name: string, options: { create: boolean }): Promise<ArchiveFileHandle>; removeEntry(name: string): Promise<void> }> };
            if (!storage.getDirectory) throw new Error('This browser does not support streaming file storage.');
            const root = await storage.getDirectory(); directory = root;
            handle = await root.getFileHandle(filename, { create: true });
        }
        sink = await handle.createWritable();
        const { metadata, history, read, fullMeasurement, verify } = await prepare();
        await writeRunArchive(metadata, history, read, sink, fullMeasurement);
        await verify?.();
        await sink.close(); sink = undefined;
        if (directory) {
            downloadBlob(await handle.getFile(), '', 'agro');
            const temporary = directory;
            setTimeout(() => { void temporary.removeEntry(filename).catch(() => {}); }, 60000);
            directory = undefined;
        }
    } finally {
        if (sink?.abort) await sink.abort();
        if (directory) await directory.removeEntry(filename).catch(() => {});
    }
}
