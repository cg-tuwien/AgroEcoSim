using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotFrontend.Core.History;

namespace GodotFrontend.Core.Persist;

/// <summary>
/// helpers/RunArchive.ts: the .agro session file the web app saves and loads.
/// <code>"AGROAR01" | u32 metaLen | UTF-8 JSON metadata | frame payloads | index | footer</code>
/// The index has 24 bytes per entry (u32 step, f64 offset, u32 length, u32 flag 0 zlib / 1 raw / 2 gzipped
/// leaf-area-index document with step 0xFFFFFFFF, always last, 4 bytes padding); the footer is f64 index offset,
/// u32 count, "AGROEND1". Offsets must be contiguous and steps strictly increasing.
/// </summary>
public static class RunArchive
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("AGROAR01");
    public static readonly byte[] FooterMagic = Encoding.ASCII.GetBytes("AGROEND1");
    public const int EntrySize = 24;
    public const uint MeasurementStep = 0xffffffff;

    public static bool IsArchive(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[8];
            return f.Read(head, 0, 8) == 8 && head.AsSpan().SequenceEqual(Magic);
        }
        catch { return false; }
    }
}

/// <summary>One frame to write: the bytes and whether they are a raw scene (true) or a zlib stream (false).</summary>
public readonly record struct ArchiveFrame(uint Step, byte[] Bytes, bool Plain);

public sealed class RunArchiveWriter
{
    /// <summary>
    /// Streams an archive: metadata straight into the file (the length is patched afterwards), frames one at a time
    /// as the source yields them (raw ones are deflated and the smaller kept, as the web does), then index and footer.
    /// Written to a temporary file and renamed, so a failed save never leaves a truncated archive in place.
    /// </summary>
    public static async Task WriteAsync(string path, JsonNode metadata, int frameCount, Func<int, Task<ArchiveFrame>> frame,
        Func<Task<Stream?>>? measurement = null, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var tmp = path + ".tmp";
        try
        {
            await using (var f = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20, useAsync: true))
            {
                await f.WriteAsync(RunArchive.Magic, ct).ConfigureAwait(false);
                await f.WriteAsync(new byte[4], ct).ConfigureAwait(false);
                await using (var w = new Utf8JsonWriter(f, new JsonWriterOptions { SkipValidation = false }))
                {
                    metadata.WriteTo(w);
                }
                var metaEnd = f.Position;
                var metaLen = checked((uint)(metaEnd - 12));
                var lenBytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(lenBytes, metaLen);
                f.Position = 8;
                await f.WriteAsync(lenBytes, ct).ConfigureAwait(false);
                f.Position = metaEnd;

                var stream = measurement != null ? await measurement().ConfigureAwait(false) : null;
                var count = frameCount + (stream != null ? 1 : 0);
                var index = new byte[count * RunArchive.EntrySize];
                long offset = metaEnd;
                long previous = -1;
                for (var i = 0; i < frameCount; ++i)
                {
                    ct.ThrowIfCancellationRequested();
                    var fr = await frame(i).ConfigureAwait(false);
                    if (fr.Step <= previous) throw new InvalidDataException($"frame steps must increase ({fr.Step} after {previous})");
                    previous = fr.Step;
                    var bytes = fr.Bytes;
                    var plain = fr.Plain;
                    if (plain)
                    {
                        var packed = Compressor.Deflate(bytes);
                        if (packed.Length < bytes.Length) { bytes = packed; plain = false; }
                    }
                    var at = i * RunArchive.EntrySize;
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at), fr.Step);
                    BinaryPrimitives.WriteDoubleLittleEndian(index.AsSpan(at + 4), offset);
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at + 12), checked((uint)bytes.Length));
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at + 16), plain ? 1u : 0u);
                    await f.WriteAsync(bytes, ct).ConfigureAwait(false);
                    offset += bytes.Length;
                    progress?.Report(i + 1);
                }
                if (stream != null)
                {
                    var start = offset;
                    await using (stream) await stream.CopyToAsync(f, ct).ConfigureAwait(false);
                    offset = f.Position;
                    var at = frameCount * RunArchive.EntrySize;
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at), RunArchive.MeasurementStep);
                    BinaryPrimitives.WriteDoubleLittleEndian(index.AsSpan(at + 4), start);
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at + 12), checked((uint)(offset - start)));
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(at + 16), 2u);
                }
                await f.WriteAsync(index, ct).ConfigureAwait(false);
                var footer = new byte[20];
                BinaryPrimitives.WriteDoubleLittleEndian(footer, offset);
                BinaryPrimitives.WriteUInt32LittleEndian(footer.AsSpan(8), (uint)count);
                RunArchive.FooterMagic.CopyTo(footer, 12);
                await f.WriteAsync(footer, ct).ConfigureAwait(false);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }
}

public sealed record ArchiveIndexEntry(uint Step, long Offset, int Length, bool Plain);

/// <summary>Opens an archive, validates it exactly as the web reader does, and serves frames lazily from the open file.</summary>
public sealed class RunArchiveReader : IArchiveFrames, IDisposable
{
    private readonly FileStream file;
    private readonly object gate = new();
    public JsonNode Metadata { get; }
    public IReadOnlyList<ArchiveIndexEntry> Frames { get; }
    public (long Offset, int Length)? Measurement { get; }
    public string Path { get; }

    private RunArchiveReader(string path, FileStream file, JsonNode metadata, List<ArchiveIndexEntry> frames, (long, int)? measurement)
    {
        Path = path;
        this.file = file;
        Metadata = metadata;
        Frames = frames;
        Measurement = measurement;
    }

    public static RunArchiveReader Open(string path)
    {
        var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        try
        {
            var size = f.Length;
            var head = new byte[12];
            ReadExact(f, 0, head);
            if (!head.AsSpan(0, 8).SequenceEqual(RunArchive.Magic)) throw new InvalidDataException("not a run archive");
            if (size < 32) throw new InvalidDataException("Truncated run archive.");
            var settingsLength = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
            var tail = new byte[20];
            ReadExact(f, size - 20, tail);
            if (!tail.AsSpan(12, 8).SequenceEqual(RunArchive.FooterMagic)) throw new InvalidDataException("Incomplete run archive.");
            var indexOffsetD = BinaryPrimitives.ReadDoubleLittleEndian(tail);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(8));
            var indexOffset = (long)indexOffsetD;
            if (indexOffset != indexOffsetD || indexOffset < 12 + settingsLength || indexOffset + (long)count * RunArchive.EntrySize != size - 20)
                throw new InvalidDataException("Invalid run archive index.");
            var meta = new byte[settingsLength];
            ReadExact(f, 12, meta);
            var metadata = JsonNode.Parse(meta) ?? new JsonObject();
            var index = new byte[count * RunArchive.EntrySize];
            ReadExact(f, indexOffset, index);
            var frames = new List<ArchiveIndexEntry>((int)count);
            long end = 12 + settingsLength;
            long previousStep = -1;
            (long, int)? measurement = null;
            for (var i = 0; i < count; ++i)
            {
                var at = i * RunArchive.EntrySize;
                var t = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at));
                var startD = BinaryPrimitives.ReadDoubleLittleEndian(index.AsSpan(at + 4));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at + 12));
                var flag = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(at + 16));
                var start = (long)startD;
                if (t <= previousStep || start != startD || start != end || start + length > indexOffset || flag > 2)
                    throw new InvalidDataException("Invalid run archive frame.");
                if (flag == 2)
                {
                    if (i != count - 1 || t != RunArchive.MeasurementStep) throw new InvalidDataException("Invalid measurement attachment.");
                    measurement = (start, checked((int)length));
                    end = start + length;
                    continue;
                }
                frames.Add(new ArchiveIndexEntry(t, start, checked((int)length), flag == 1));
                previousStep = t;
                end = start + length;
            }
            if (end != indexOffset) throw new InvalidDataException("Unindexed data in run archive.");
            return new RunArchiveReader(path, f, metadata, frames, measurement);
        }
        catch
        {
            f.Dispose();
            throw;
        }
    }

    private static void ReadExact(FileStream f, long at, byte[] dst)
    {
        f.Position = at;
        var read = 0;
        while (read < dst.Length)
        {
            var n = f.Read(dst, read, dst.Length - read);
            if (n <= 0) throw new InvalidDataException("Truncated run archive.");
            read += n;
        }
    }

    public byte[] Read(long offset, int length)
    {
        var b = new byte[length];
        lock (gate) ReadExact(file, offset, b);
        return b;
    }

    public byte[]? ReadMeasurement() => Measurement is { } m ? Read(m.Offset, m.Length) : null;

    public void Dispose() => file.Dispose();
}

/// <summary>The legacy session format: the same metadata as a JSON object plus history:[{t, s: base64}].</summary>
public static class LegacySessionJson
{
    public static (JsonNode Metadata, List<(uint Step, byte[] Bytes)> Frames) Read(string path)
    {
        var node = JsonNode.Parse(File.ReadAllBytes(path)) ?? throw new InvalidDataException("empty session file");
        var frames = new List<(uint, byte[])>();
        if (node["history"] is JsonArray history)
        {
            foreach (var e in history)
            {
                if (e is null) continue;
                var t = e["t"]!.GetValue<uint>();
                var s = e["s"]?.GetValue<string>();
                frames.Add((t, string.IsNullOrEmpty(s) ? Array.Empty<byte>() : Convert.FromBase64String(s)));
            }
            node.AsObject().Remove("history");
        }
        return (node, frames);
    }
}
