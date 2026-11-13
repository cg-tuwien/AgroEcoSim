using System.IO.Compression;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Packing;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Threading;

namespace GodotFrontend.App.Render;

/// <summary>The decoded and packed form of one frame, ready for the main thread to upload.</summary>
public sealed record FrameReady(long FrameId, DecodedScene Scene, PackedFrame Packed, double InflateMs, double DecodeMs, double PackMs);

/// <summary>
/// The decode worker: inflates, decodes and packs frames on a dedicated thread and posts the result to the main
/// thread. Requests coalesce latest-wins (a scrub issues many, only the newest matters), and packed frames are
/// recycled through a small pool so a long playback allocates nothing per frame.
/// </summary>
public sealed class SceneDecodeService : IDisposable
{
    private readonly IMainThread main;
    private readonly SceneDecoder decoder;
    private readonly FramePacker packer;
    private readonly Thread thread;
    private readonly object gate = new();
    private readonly System.Collections.Concurrent.ConcurrentBag<PackedFrame> pool = new();
    private (long id, byte[] bytes, bool deflated, Action<FrameReady> onReady, Action<long, Exception> onError)? pending;
    private bool disposed;

    public SceneDecodeService(IMainThread main, GeometryLibrary library)
    {
        this.main = main;
        decoder = new SceneDecoder(library);
        packer = new FramePacker(library);
        thread = new Thread(Run) { Name = "scene-decode", IsBackground = true };
        thread.Start();
    }

    /// <summary>Queues a frame; a frame queued before this one started decoding is dropped.</summary>
    public void Request(long frameId, byte[] bytes, bool deflated, Action<FrameReady> onReady, Action<long, Exception> onError)
    {
        lock (gate)
        {
            pending = (frameId, bytes, deflated, onReady, onError);
            Monitor.Pulse(gate);
        }
    }

    /// <summary>Hands a packed frame back for reuse once its buffers have been uploaded.</summary>
    public void Recycle(PackedFrame frame)
    {
        if (pool.Count < 3) pool.Add(frame);
    }

    private void Run()
    {
        while (true)
        {
            (long id, byte[] bytes, bool deflated, Action<FrameReady> onReady, Action<long, Exception> onError) job;
            lock (gate)
            {
                while (pending is null && !disposed) Monitor.Wait(gate);
                if (disposed) return;
                job = pending!.Value;
                pending = null;
            }
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var raw = job.deflated ? Inflate(job.bytes) : job.bytes;
                var inflateMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var scene = decoder.Decode(raw);
                var decodeMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                pool.TryTake(out var reuse);
                var packed = packer.Pack(scene, reuse);
                packed.FrameId = job.id;
                var packMs = sw.Elapsed.TotalMilliseconds;
                var ready = new FrameReady(job.id, scene, packed, inflateMs, decodeMs, packMs);
                main.Post(() => job.onReady(ready));
            }
            catch (Exception ex)
            {
                main.Post(() => job.onError(job.id, ex));
            }
        }
    }

    public static byte[] Inflate(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(packed.Length * 4);
        z.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Whether bytes look like a zlib stream (0x78 then a valid FCHECK) rather than a raw scene (version byte 7..9).</summary>
    public static bool LooksDeflated(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[0] == 0x78 && ((bytes[0] << 8) | bytes[1]) % 31 == 0;

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Monitor.PulseAll(gate);
        }
    }
}
