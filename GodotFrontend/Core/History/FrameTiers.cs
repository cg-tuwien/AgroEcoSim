using System.IO.Compression;
using GodotFrontend.Core.Threading;

namespace GodotFrontend.Core.History;

/// <summary>Bytes resolved for a frame: raw scene bytes or a zlib stream the decode worker inflates.</summary>
public readonly record struct ResolvedFrame(byte[] Bytes, bool Deflated);

/// <summary>Reads a frame's bytes out of an open .agro archive (implemented by the archive reader).</summary>
public interface IArchiveFrames
{
    byte[] Read(long offset, int length);
}

/// <summary>
/// Deflates frames that fell out of the hot window (the 8 newest) on a background worker, keeping the smaller of
/// the two representations; applied on the main thread only if the entry still holds the raw buffer it compressed.
/// </summary>
public sealed class Compressor
{
    public const int HotFrames = 8;
    private readonly History history;
    private readonly IMainThread main;
    private int running;

    public Compressor(History history, IMainThread main)
    {
        this.history = history;
        this.main = main;
    }

    /// <summary>Starts a pass if none runs; cheap to call after every received frame.</summary>
    public void Kick()
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return;
        var work = new List<(HistoryFrame frame, byte[] raw)>();
        var frontier = history.Count - HotFrames;
        for (var i = 0; i < frontier; ++i)
        {
            var e = history[i];
            if (e.Kind == PayloadKind.Raw && e.Bytes != null) work.Add((e, e.Bytes));
            if (work.Count >= 64) break;
        }
        if (work.Count == 0) { running = 0; return; }
        Task.Run(() =>
        {
            foreach (var (frame, raw) in work)
            {
                byte[] packed;
                try { packed = Deflate(raw); }
                catch { continue; }
                if (packed.Length >= raw.Length) continue;
                main.Post(() =>
                {
                    if (frame.Kind == PayloadKind.Raw && ReferenceEquals(frame.Bytes, raw)) history.ApplyCompressed(frame, packed);
                });
            }
            running = 0;
            main.Post(Kick); // more may have fallen out of the hot window meanwhile
        });
    }

    public static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream(raw.Length / 2 + 64);
        using (var z = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw, 0, raw.Length);
        return output.ToArray();
    }

    public static byte[] Inflate(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(packed.Length * 4);
        z.CopyTo(output);
        return output.ToArray();
    }
}

/// <summary>
/// A size-bounded on-disk LRU of packed frames per run (the browser's HTTP cache equivalent), so a frame evicted from
/// memory and scrubbed onto again costs a local read rather than a round trip.
/// </summary>
public sealed class DiskFrameCache
{
    private readonly string root;
    private readonly long capBytes;
    private readonly object gate = new();

    public DiskFrameCache(string root, long capBytes)
    {
        this.root = root;
        this.capBytes = capBytes;
    }

    private string Dir(string runId) => Path.Combine(root, runId);
    private string PathOf(string runId, uint step) => Path.Combine(Dir(runId), $"{step}.z");

    public byte[]? TryRead(string runId, uint step)
    {
        try
        {
            var p = PathOf(runId, step);
            if (!File.Exists(p)) return null;
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow);
            return File.ReadAllBytes(p);
        }
        catch { return null; }
    }

    public void Write(string runId, uint step, byte[] packed)
    {
        try
        {
            Directory.CreateDirectory(Dir(runId));
            var p = PathOf(runId, step);
            var tmp = p + ".tmp";
            File.WriteAllBytes(tmp, packed);
            File.Move(tmp, p, overwrite: true);
            Prune();
        }
        catch { /* best effort */ }
    }

    public void Forget(string runId)
    {
        try { if (Directory.Exists(Dir(runId))) Directory.Delete(Dir(runId), recursive: true); } catch { }
    }

    private void Prune()
    {
        lock (gate)
        {
            if (!Directory.Exists(root)) return;
            var files = new DirectoryInfo(root).EnumerateFiles("*.z", SearchOption.AllDirectories).ToList();
            var total = files.Sum(f => f.Length);
            if (total <= capBytes) return;
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                try { total -= f.Length; f.Delete(); } catch { }
                if (total <= capBytes) break;
            }
        }
    }
}

/// <summary>
/// Resolves a timeline entry to bytes: resident raw, resident zlib, the archive slice, the disk cache, and finally
/// the server's /packed endpoint (stored to the disk cache, not rehydrated into memory, like the browser cache).
/// </summary>
public sealed class FrameSource
{
    private readonly DiskFrameCache? disk;
    public IArchiveFrames? Archive { get; set; }
    public Func<string, uint, CancellationToken, Task<byte[]?>>? FetchPacked { get; set; }

    public FrameSource(DiskFrameCache? disk)
    {
        this.disk = disk;
    }

    public async Task<ResolvedFrame?> ResolveAsync(HistoryFrame e, string? runId, CancellationToken ct)
    {
        switch (e.Kind)
        {
            case PayloadKind.Raw when e.Bytes != null:
                return new ResolvedFrame(e.Bytes, false);
            case PayloadKind.Zlib when e.Bytes != null:
                return new ResolvedFrame(e.Bytes, true);
            case PayloadKind.Archive when Archive != null:
            {
                var bytes = await Task.Run(() => Archive.Read(e.ArchiveOffset, e.ArchiveLength), ct).ConfigureAwait(false);
                return new ResolvedFrame(bytes, !e.ArchivePlain);
            }
        }
        if (string.IsNullOrEmpty(runId)) return null;
        var cached = disk?.TryRead(runId, e.Step);
        if (cached != null) return new ResolvedFrame(cached, true);
        if (FetchPacked == null) return null;
        var packed = await FetchPacked(runId, e.Step, ct).ConfigureAwait(false);
        if (packed == null) return null;
        disk?.Write(runId, e.Step, packed);
        return new ResolvedFrame(packed, true);
    }
}
