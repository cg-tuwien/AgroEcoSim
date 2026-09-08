using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using LiteDB;

namespace AgroServer.Services;

/// <summary>
/// Durable storage for a simulation run's exported frames.
///
/// The browser used to keep every timestep it had ever received, which is what killed a
/// 4,134-plant run around simulated month 2: one mature frame is tens of megabytes and nothing
/// ever evicted. The server, meanwhile, produced each frame and threw it away immediately - so
/// the data existed exactly once, in the one place that could not afford it.
///
/// Bulk goes to files and only metadata goes to LiteDB. That split is deliberate: a two-month
/// run at this plant count is on the order of 17 GB of compressed frames, one frame already
/// exceeds LiteDB's 16 MB document limit, and a LiteDB file that size is well outside where it
/// behaves well. Keeping frames as opaque files also sidesteps LiteDB's reflection-based mapper
/// being at odds with this project's CreateSlimBuilder plus trim analyzer.
///
///     &lt;root&gt;/index.litedb              run metadata, nothing large
///     &lt;root&gt;/runs/&lt;runId&gt;/request.json  so a resumed run can rebuild itself
///     &lt;root&gt;/runs/&lt;runId&gt;/index.bin     8 bytes per frame, appended
///     &lt;root&gt;/runs/&lt;runId&gt;/frames/&lt;step&gt;.z
///
/// Frames are stored already deflated and served that way, with Content-Encoding: deflate, so
/// the browser inflates them natively and neither side spends anything on it.
/// </summary>
public interface IRunStore
{
    /// <summary>Registers a new run and returns its id. Never throws; storage is best-effort.</summary>
    string BeginRun(string requestJson, ushort hoursPerTick, uint totalSteps, bool analytics, bool deterministic);

    /// <summary>
    /// Hands one exported frame to the store. Returns as soon as it is queued.
    ///
    /// The compression, the disk write, the index append and the metadata update all happen on a
    /// background writer, because this is called from inside the simulation loop and everything
    /// it does there is added to every step of every run. Only the export itself has to happen on
    /// the simulation thread - it reads live world state - and that cost is irreducible.
    ///
    /// <paramref name="step"/> is the frame's ADDRESS: it is what the index records, what the
    /// client reads back out of the index, and what it passes to <c>/frame/{step}</c>. This used
    /// to be two parameters - a step and a "simulated timestep t" - which the only caller passed
    /// the same value for. Two names for one number is an invitation: the day they diverge, the
    /// client fetches by the one it kept and silently gets the wrong frame or a 404, and nothing
    /// on either side would say so. One number, one meaning.
    ///
    /// The clock multiplies it by HoursPerTick, so a branched run must renumber from its parent
    /// rather than continue its parent's count.
    /// </summary>
    void WriteFrame(string runId, uint step, byte[] scene);

    void CompleteRun(string runId, bool aborted);

    /// <summary>The deflated bytes as stored, for serving with Content-Encoding: deflate.</summary>
    bool TryReadFrameDeflated(string runId, uint step, out byte[] deflated);

    /// <summary>One (step, storedBytes) pair per stored frame, in step order.</summary>
    bool TryReadIndex(string runId, out byte[] indexBytes);

    RunRecord? GetRun(string runId);
    IReadOnlyList<RunRecord> ListRuns(int limit);
}

public class RunRecord
{
    [BsonId] public string Id { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    /// <summary>running | complete | aborted</summary>
    public string Status { get; set; } = "running";
    public ushort HoursPerTick { get; set; }
    public uint TotalSteps { get; set; }
    public uint StoredFrames { get; set; }
    public bool Analytics { get; set; }
    /// <summary>Whether this run is reproducible, and therefore whether a branch from it can be
    /// compared against it. Recorded because it is not recoverable after the fact.</summary>
    public bool Deterministic { get; set; }
    public long BytesOnDisk { get; set; }
    /// <summary>Set when this run was branched from another; null otherwise.</summary>
    public string? ParentRunId { get; set; }
    public uint? BranchStep { get; set; }
}

/// <summary>
/// The background half of one run's storage.
///
/// A bounded queue, deliberately: if the writer cannot keep up, the simulation is made to wait
/// rather than the frames piling up in memory. At production frame sizes an unbounded queue is
/// just a slower way to run the server out of memory, which is the failure Stage 2 exists to
/// prevent - moving it from the browser to the server would not be a fix.
/// </summary>
sealed class RunWriter
{
    public required string RunId { get; init; }
    public required BlockingCollection<(uint Step, byte[] Scene)> Queue { get; init; }
    public required FileStream Index { get; init; }
    public Task? Worker;
    /// <summary>Written by the worker thread only, read for the periodic metadata flush.</summary>
    public uint StoredFrames;
    public long BytesOnDisk;
    public DateTime LastMetadataFlush = DateTime.UtcNow;
}

public class RunStore : IRunStore, IDisposable
{
    readonly string Root;
    readonly LiteDatabase Db;
    readonly ILiteCollection<RunRecord> Runs;
    readonly ConcurrentDictionary<string, RunWriter> Writers = new();

    /// <summary>
    /// Frames that may sit between the simulation and the disk.
    ///
    /// Small on purpose. Each slot holds a whole exported frame - tens of megabytes at production
    /// plant count - so this is a memory budget, not a throughput knob. One worker deflating at
    /// ~10 ms a frame keeps up with a simulation step of similar cost; if it ever does not, the
    /// simulation waits, which is the correct failure.
    /// </summary>
    public int QueueDepth { get; set; } = 3;

    /// <summary>How often the run's metadata record is brought up to date while it computes.</summary>
    static readonly TimeSpan MetadataInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Total bytes of stored frames to keep before evicting whole runs, oldest first.
    ///
    /// A budget is not optional here. One two-month run at production plant count is around
    /// 17 GB compressed, so without eviction the disk fills and the failure lands on whatever
    /// writes next rather than on the thing that caused it.
    /// </summary>
    public long RetentionBytes { get; set; } = 64L * 1024 * 1024 * 1024;

    /// <summary>
    /// True when the store could not be opened. Every operation then becomes a no-op.
    ///
    /// The constructor used to let its exceptions escape, and it is called from a top-level
    /// statement before the host is built - so an unwritable root took the entire server down
    /// before Kestrel ever listened. That is the deployed case, not a hypothetical: the image
    /// copies a root-owned /app and then drops to an unprivileged user, which cannot create
    /// /app/runstore. A best-effort store must never be able to do that.
    /// </summary>
    public bool Disabled { get; }

    // LiteDB 5 discovers this model through reflection. Direct calls to its constructor and
    // accessors do not keep their reflection metadata when publishing with trimming/AOT.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties, typeof(RunRecord))]
    public RunStore(string root)
    {
        Root = root;
        try
        {
            Directory.CreateDirectory(RunsDir);
            //Shared connection: the simulation loop writes from a background thread while HTTP
            //requests read, and LiteDB's default Direct mode allows a single process handle only.
            Db = new LiteDatabase(new ConnectionString { Filename = Path.Combine(Root, "index.litedb"), Connection = ConnectionType.Shared });
            Runs = Db.GetCollection<RunRecord>("runs");
            Runs.EnsureIndex(x => x.CreatedUtc);
            SweepStaleRuns();
        }
        catch (Exception e)
        {
            Disabled = true;
            Console.Error.WriteLine(
                $"RunStore disabled: cannot use '{root}' ({e.Message}). Simulations will run and stream " +
                $"normally, but frames will not be persisted and cannot be fetched back. " +
                $"Set RunStoreRoot to a writable path to enable persistence.");
        }
    }

    /// <summary>
    /// Nothing can still be running in a freshly started process.
    ///
    /// A record left at "running" by a crash or a redeploy is otherwise never evictable - Evict
    /// skips running rows while still counting their bytes - so a few interrupted long runs make
    /// the budget permanently unreachable and every later BeginRun deletes all the completed runs
    /// for nothing.
    /// </summary>
    void SweepStaleRuns()
    {
        var stale = Runs.Find(x => x.Status == "running").ToList();
        foreach (var run in stale)
        {
            run.Status = "aborted";
            run.UpdatedUtc = DateTime.UtcNow;
            Runs.Update(run);
        }
        if (stale.Count > 0)
            Console.WriteLine($"RunStore: marked {stale.Count} run(s) left 'running' by a previous process as aborted.");
    }

    string RunsDir => Path.Combine(Root, "runs");
    string RunDir(string runId) => Path.Combine(RunsDir, runId);
    string FramesDir(string runId) => Path.Combine(RunDir(runId), "frames");
    string IndexPath(string runId) => Path.Combine(RunDir(runId), "index.bin");
    string FramePath(string runId, uint step) => Path.Combine(FramesDir(runId), $"{step:D6}.z");

    /// <summary>Rejects anything that is not a plain id, so a path cannot escape the store.</summary>
    static bool IsSafeId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public string BeginRun(string requestJson, ushort hoursPerTick, uint totalSteps, bool analytics, bool deterministic)
    {
        var id = Guid.NewGuid().ToString("N");
        if (Disabled) return id;
        try
        {
            Directory.CreateDirectory(FramesDir(id));
            //The request is what a resumed or branched run needs to rebuild its world, and the
            //upload and terrain caches that hold it today expire after five minutes.
            File.WriteAllText(Path.Combine(RunDir(id), "request.json"), requestJson);

            Runs.Insert(new RunRecord
            {
                Id = id,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Status = "running",
                HoursPerTick = hoursPerTick,
                TotalSteps = totalSteps,
                Analytics = analytics,
                Deterministic = deterministic,
            });
            Evict();
            StartWriter(id);
        }
        catch (Exception e)
        {
            //Persistence must never take the simulation down with it. A run with no store still
            //streams to the client exactly as it did before.
            Console.Error.WriteLine($"RunStore: could not begin run {id}: {e.Message}");
        }
        return id;
    }

    public void WriteFrame(string runId, uint step, byte[] scene)
    {
        if (Disabled || !IsSafeId(runId)) return;
        if (!Writers.TryGetValue(runId, out var writer)) return;
        try
        {
            //Blocks when the queue is full, which is the backpressure. Everything expensive -
            //the deflate above all - happens on the worker, so the simulation thread pays only
            //for handing over a reference.
            writer.Queue.Add((step, scene));
        }
        catch (InvalidOperationException) { /* the run finished; nothing more is accepted */ }
    }

    /// <summary>Opens the run's index file and starts the single worker that owns it.</summary>
    void StartWriter(string runId)
    {
        var writer = new RunWriter
        {
            RunId = runId,
            Queue = new BlockingCollection<(uint, byte[])>(QueueDepth),
            //Opened ONCE for the run. Reopening it per frame was a file open, append and close on
            //the simulation thread for eight bytes.
            Index = new FileStream(IndexPath(runId), FileMode.Append, FileAccess.Write, FileShare.Read),
        };
        Writers[runId] = writer;
        writer.Worker = Task.Factory.StartNew(() => Drain(writer), TaskCreationOptions.LongRunning);
    }

    void Drain(RunWriter writer)
    {
        try
        {
            foreach (var (step, scene) in writer.Queue.GetConsumingEnumerable())
            {
                try
                {
                    var deflated = Deflate(scene);
                    File.WriteAllBytes(FramePath(writer.RunId, step), deflated);

                    Span<byte> entry = stackalloc byte[8];
                    BitConverter.TryWriteBytes(entry[..4], step);
                    BitConverter.TryWriteBytes(entry[4..], (uint)deflated.Length);
                    writer.Index.Write(entry);
                    //Flushed per frame so a reader - the client rebuilding its timeline while the
                    //run computes - sees entries for frames that are actually on disk, and never
                    //an entry for one that is not.
                    writer.Index.Flush();

                    writer.StoredFrames = step + 1;
                    writer.BytesOnDisk += deflated.Length;

                    //Metadata on a timer rather than per frame. It is only a progress figure, and
                    //LiteDB in Shared mode takes a cross-process mutex for each update - 0.7 ms a
                    //frame measured, which is small but entirely pointless a thousand times over.
                    if (DateTime.UtcNow - writer.LastMetadataFlush > MetadataInterval)
                        FlushMetadata(writer);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"RunStore: could not store frame {step} of {writer.RunId}: {e.Message}");
                }
            }
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: writer for {writer.RunId} stopped: {e}"); }
        finally
        {
            try { FlushMetadata(writer); } catch { /* best effort */ }
            try { writer.Index.Dispose(); } catch { /* best effort */ }
        }
    }

    void FlushMetadata(RunWriter writer)
    {
        writer.LastMetadataFlush = DateTime.UtcNow;
        var run = Runs.FindById(writer.RunId);
        if (run == null) return;
        run.StoredFrames = writer.StoredFrames;
        run.BytesOnDisk = writer.BytesOnDisk;
        run.UpdatedUtc = DateTime.UtcNow;
        Runs.Update(run);
    }

    public void CompleteRun(string runId, bool aborted)
    {
        if (Disabled || !IsSafeId(runId)) return;
        try
        {
            //Drain before recording the outcome, so a completed run's metadata describes every
            //frame it actually stored rather than whatever the last periodic flush had seen.
            if (Writers.TryRemove(runId, out var writer))
            {
                writer.Queue.CompleteAdding();
                writer.Worker?.Wait(TimeSpan.FromMinutes(2));
            }

            var run = Runs.FindById(runId);
            if (run == null) return;
            run.Status = aborted ? "aborted" : "complete";
            run.UpdatedUtc = DateTime.UtcNow;
            Runs.Update(run);
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not complete {runId}: {e.Message}"); }
    }

    public bool TryReadFrameDeflated(string runId, uint step, out byte[] deflated)
    {
        deflated = [];
        if (Disabled || !IsSafeId(runId)) return false;
        var path = FramePath(runId, step);
        if (!File.Exists(path)) return false;
        try { deflated = File.ReadAllBytes(path); return true; }
        catch { return false; }
    }

    public bool TryReadIndex(string runId, out byte[] indexBytes)
    {
        indexBytes = [];
        if (Disabled || !IsSafeId(runId)) return false;
        var path = IndexPath(runId);
        if (!File.Exists(path)) return false;
        try { indexBytes = File.ReadAllBytes(path); return true; }
        catch { return false; }
    }

    public RunRecord? GetRun(string runId) => !Disabled && IsSafeId(runId) ? Runs.FindById(runId) : null;

    public IReadOnlyList<RunRecord> ListRuns(int limit) =>
        Disabled ? [] : Runs.Query().OrderByDescending(x => x.CreatedUtc).Limit(limit).ToList();

    /// <summary>
    /// A byte count a person can read. Integer megabytes alone printed "0 MB" for every run
    /// under a megabyte, which is most of them during development and all of them in a log that
    /// is being read precisely because something unexpected was deleted.
    /// </summary>
    static string Size(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB"
                                    : bytes >= 1024 ? $"{bytes / 1024.0:0.0} KB"
                                    : $"{bytes} B";

    /// <summary>Drops whole runs, oldest first, until the store is back inside its budget.</summary>
    void Evict()
    {
        try
        {
            var all = Runs.Query().OrderBy(x => x.CreatedUtc).ToList();
            var total = all.Sum(r => r.BytesOnDisk);

            //If what cannot be evicted already exceeds the budget, deleting the rest frees nobody
            //and costs the user their whole history. Say so and stop.
            var pinned = all.Where(r => r.Status == "running" || all.Any(x => x.ParentRunId == r.Id)).Sum(r => r.BytesOnDisk);
            if (pinned > RetentionBytes)
            {
                Console.Error.WriteLine(
                    $"RunStore: {Size(pinned)} is held by running or branched-from runs, which is already over " +
                    $"the {Size(RetentionBytes)} budget. Not evicting - deleting completed runs would not help.");
                return;
            }

            foreach (var run in all)
            {
                if (total <= RetentionBytes) break;
                //Never evict a run something else was branched from - its checkpoints are the
                //only thing making those children reproducible.
                if (run.Status == "running" || all.Any(x => x.ParentRunId == run.Id)) continue;

                var dir = RunDir(run.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Runs.Delete(run.Id);
                total -= run.BytesOnDisk;
                Console.WriteLine($"RunStore: evicted run {run.Id} ({Size(run.BytesOnDisk)}) to stay inside the retention budget.");
            }
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: eviction failed: {e.Message}"); }
    }

    /// <summary>
    /// zlib-wrapped deflate, matching what Content-Encoding: deflate means to a browser.
    ///
    /// Real V7 scene data gives about 1.7x - unremarkable, because the affine transforms and
    /// analytics blocks are nearly incompressible and it is the record headers and repeated
    /// colours that give back what little there is.
    /// </summary>
    static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream(raw.Length / 2 + 64);
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        return output.ToArray();
    }

    public void Dispose()
    {
        foreach (var writer in Writers.Values)
        {
            writer.Queue.CompleteAdding();
            try { writer.Worker?.Wait(TimeSpan.FromSeconds(30)); } catch { /* shutting down */ }
        }
        Writers.Clear();
        Db?.Dispose();
    }
}
