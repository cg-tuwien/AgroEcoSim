using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Agro;
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
///     &lt;root&gt;/index.litedb                    run metadata, nothing large
///     &lt;root&gt;/models/&lt;sha256&gt;.obj.gz          field model sources, SHARED between runs
///     &lt;root&gt;/runs/&lt;runId&gt;/request.json        so a resumed run can rebuild itself
///     &lt;root&gt;/runs/&lt;runId&gt;/index.bin           8 bytes per frame, appended
///     &lt;root&gt;/runs/&lt;runId&gt;/terrain.bin.gz      the serialized soil, so a stored run can be VIEWED
///     &lt;root&gt;/runs/&lt;runId&gt;/frames/&lt;step&gt;.g
///
/// Frames are stored already deflated and served that way, with Content-Encoding: deflate, so
/// the browser inflates them natively and neither side spends anything on it.
///
/// The frame extension is <c>.g</c> ("geometry"). Runs written by an earlier build used
/// <c>.z</c> and are still read - <c>TryReadFrameDeflated</c> falls back to that name. Nothing
/// renames them: a rename pass over the 42,000 frame files of an existing store is a large,
/// destructive startup operation in exchange for nothing a fallback read does not already give.
///
/// Two files beyond the frames are what make a stored run re-runnable and viewable rather than
/// merely replayable:
///
///   - <c>request.json</c> alone is NOT enough to rebuild a run that used an imported facade. The
///     model reaches the simulation through <c>POST /terrain</c> and the per-connection terrain
///     cache, which is single-use and expires in five minutes, and <c>FieldModelKey</c> is null in
///     every request ever stored. So the OBJ source is filed here too, content-addressed and
///     shared between the runs that use it - a 94 MB facade compresses to a fraction of that and
///     is nothing against a 16 GB run, but storing it once per run rather than once per model
///     would not be.
///   - <c>terrain.bin.gz</c> is the soil exactly as the socket sends it to a viewport. Rebuilding
///     it from the OBJ costs seconds and a few hundred megabytes; serving the bytes costs neither,
///     and viewing a past run is the common case.
/// </summary>
public interface IRunStore
{
    /// <summary>Registers a new run and returns its id. Never throws; storage is best-effort.</summary>
    string BeginRun(RunSpec spec);

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

    /// <summary>
    /// Files the soil as the viewport receives it, so a stored run can be drawn later.
    ///
    /// Without it a past run loads as plants floating in nothing: the trays and the facade reach
    /// the browser over the socket at upload time and are never part of a frame.
    /// </summary>
    void WriteTerrain(string runId, byte[] serializedTerrain);

    void CompleteRun(string runId, bool aborted);

    /// <summary>The deflated bytes as stored, for serving with Content-Encoding: deflate.</summary>
    bool TryReadFrameDeflated(string runId, uint step, out byte[] deflated);

    /// <summary>One (step, storedBytes) pair per stored frame, in step order.</summary>
    bool TryReadIndex(string runId, out byte[] indexBytes);
    bool TryReadIndexSnapshot(string runId, out byte[] indexBytes, out uint revision, out uint restartStep);

    /// <summary>The request this run was started from, as stored. The client forks from it.</summary>
    bool TryReadRequest(string runId, out byte[] utf8Json);

    /// <summary>
    /// Files the simulation's live state so the run can be RESUMED rather than recomputed.
    ///
    /// Only the latest is kept: a checkpoint is a place to carry on from, not a history - the
    /// history is the frames. It is overwritten in place, so it costs its own size once however
    /// long the run turns out to be.
    /// </summary>
    void WriteCheckpoint(string runId, uint step, byte[] state);

    /// <summary>The stored state and the step it was taken at. False when the run has none.</summary>
    bool TryReadCheckpoint(string runId, out uint step, out byte[] state);

    /// <summary>Whether this run can be resumed rather than recomputed.</summary>
    bool HasCheckpoint(string runId);

    /// <summary>
    /// Re-opens a finished run that is being resumed, and records its new horizon.
    ///
    /// The run goes back to "running" and its request.json is rewritten with the new total, so a
    /// later reader - the panel, an adoption pass, a second resume - sees the horizon the run is
    /// actually working towards rather than the one it was first given.
    /// </summary>
    /// <returns>False when the run is still live, which is the one state a resume must not enter.</returns>
    bool ResumeRun(string runId, uint checkpointStep, uint totalSteps, int totalHours, string requestJson);

    /// <summary>The serialized soil as gzipped bytes, for serving with Content-Encoding: gzip.</summary>
    bool TryReadTerrain(string runId, out byte[] gzipped);

    /// <summary>
    /// Files the run's complete leaf-area-index measurement, gzipped.
    ///
    /// The copy sent over the socket has its per-obstacle daily series dropped above
    /// `DailyRecordBudget`, because sixteen hundred trays with a series each is a hundred
    /// megabytes of JSON that no browser should receive in one message. That budget is a property
    /// of the WIRE, not of the measurement - so the whole thing is written here and fetched over
    /// REST, where size is the browser's own business and it never has to parse it at all.
    /// </summary>
    void WriteObstacleLight(string runId, byte[] utf8Json);

    /// <summary>The stored measurement as gzipped bytes, for serving with Content-Encoding: gzip.</summary>
    bool TryReadObstacleLight(string runId, out byte[] gzipped);

    /// <summary>
    /// Files an uploaded field model and returns its content hash, or null when it could not be
    /// stored. Identical models are stored once however many runs use them.
    /// </summary>
    string? StoreFieldModel(string objSource);

    /// <summary>The OBJ source behind a hash, for rebuilding a stored run's terrain.</summary>
    bool TryReadFieldModel(string hash, out string objSource);

    /// <summary>
    /// Whether a run's optional files are present, without reading them.
    ///
    /// A listing has to say whether a run can be re-run, drawn with its trays, or have its
    /// measurement downloaded, and the answer is a stat() - the files themselves run to tens of
    /// megabytes each and a listing of fifty runs must not touch a byte of them.
    /// </summary>
    bool HasTerrain(string runId);
    bool HasObstacleLight(string runId);
    bool HasFieldModel(string? hash);

    RunRecord? GetRun(string runId);
    IReadOnlyList<RunRecord> ListRuns(int limit);

    /// <summary>
    /// Stops scheduling maintenance, leaving the database open for requests still in flight.
    /// Call on ApplicationStopping; call <see cref="IDisposable.Dispose"/> on ApplicationStopped.
    /// </summary>
    void BeginShutdown();

    /// <summary>
    /// Begins periodic maintenance: eviction and field model collection, on a timer.
    ///
    /// Called by the host AFTER the budgets are configured, because the first sweep enforces them.
    /// </summary>
    void StartMaintenance();

    /// <summary>
    /// Deletes a run and everything under it, reporting why when it will not.
    ///
    /// A typed outcome rather than a message the caller parses: the HTTP layer has to map "no such
    /// run" to 404 and "still computing" to 409, and keying that off the prose means the day
    /// someone improves the wording, a delete starts answering the wrong status.
    /// </summary>
    DeleteOutcome DeleteRun(string runId, out string reason);
}

/// <summary>
/// Everything the store records about a run at the moment it starts.
///
/// A parameter object rather than a dozen positional arguments: what a listing has to show grew
/// from five values to fifteen, and at that width a positional call is a place for two same-typed
/// neighbours to be silently swapped.
/// </summary>
public enum DeleteOutcome
{
    Deleted,
    NoSuchRun,
    /// <summary>Refused: the run is still computing, or the store is disabled, or the disk said no.</summary>
    Refused,
}

public sealed class RunSpec
{
    public required string RequestJson { get; init; }
    public required ushort HoursPerTick { get; init; }
    public required uint TotalSteps { get; init; }
    public bool Analytics { get; init; }
    public bool Deterministic { get; init; }

    public int TotalHours { get; init; }
    /// <summary>The simulated start date as a calendar day, <c>yyyy-MM-dd</c>.</summary>
    public string InitialDay { get; init; } = "";
    public ulong Seed { get; init; }
    public byte RenderMode { get; init; }

    /// <summary>Plant formations in the built world, not seeds in the request - they differ when
    /// a seed falls outside every soil field.</summary>
    public uint Plants { get; init; }
    /// <summary>Soil fields. One for a plain box field, one per tray for an imported facade.</summary>
    public uint Trays { get; init; }
    public uint Obstacles { get; init; }
    public uint Species { get; init; }

    public string? FieldModelName { get; init; }
    public string? FieldModelHash { get; init; }
    public string? FieldItemRegex { get; init; }
    public bool FieldItemRegexMaterial { get; init; }
    public float FieldResolution { get; init; }

    public string? ParentRunId { get; init; }
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
    /// <summary>How many frames are actually on disk. With a frame stride this is NOT the step
    /// the run reached - see <see cref="LastStep"/>, which is what a progress figure wants.</summary>
    public uint StoredFrames { get; set; }
    /// <summary>The highest step stored, plus one. The run's progress.</summary>
    public uint LastStep { get; set; }
    public bool Analytics { get; set; }
    /// <summary>Whether this run is reproducible, and therefore whether a branch from it can be
    /// compared against it. Recorded because it is not recoverable after the fact.</summary>
    public bool Deterministic { get; set; }
    /// <summary>Frame bytes only. The retention budget is spent almost entirely on these.</summary>
    public long BytesOnDisk { get; set; }
    /// <summary>Everything else in the run directory: the request, the index, the terrain, the
    /// measurement. Tracked apart from the frames so the budget keeps meaning what it meant.</summary>
    public long AuxBytesOnDisk { get; set; }
    /// <summary>Changes when resume replaces a tail beyond its checkpoint.</summary>
    public uint FrameRevision { get; set; }
    public uint FrameRestartStep { get; set; }
    /// <summary>Set when this run was continued or forked from another; null otherwise.</summary>
    public string? ParentRunId { get; set; }
    /// <summary>
    /// The parent step a child picked up from. Always null, and honestly so.
    ///
    /// It would mean something if a child resumed its parent. None does: there is no checkpoint of
    /// live world state anywhere in this project, so a continued or forked run is computed again
    /// from the recorded request, from step zero. Kept because it is on the wire and because a
    /// checkpoint is the one change that would give it a value.
    /// </summary>
    public uint? BranchStep { get; set; }

    //--- what a listing shows -----------------------------------------------------------------
    public int TotalHours { get; set; }
    /// <summary>
    /// The simulated start date, as the calendar day it is - <c>yyyy-MM-dd</c>, no time, no zone.
    ///
    /// A string and not a DateTime, because it is not an instant. Held as one it acquires a zone
    /// somewhere between the request (Kind=Unspecified), LiteDB and the JSON serializer, and the
    /// day a reader gets back then depends on the server's TZ and the browser's: a run that starts
    /// on 1 April reads as 31 March for half the world. The client wants the day the user picked
    /// and nothing else, and this is the only representation that cannot be shifted.
    /// </summary>
    public string InitialDay { get; set; } = "";
    public ulong Seed { get; set; }
    public byte RenderMode { get; set; }
    public uint Plants { get; set; }
    /// <summary>Soil fields, or 0 when a run predates the field being recorded. Not derivable from
    /// the request - the trays come out of the imported model - so an old run reports "unknown"
    /// rather than a wrong number.</summary>
    public uint Trays { get; set; }
    public uint Obstacles { get; set; }
    public uint Species { get; set; }
    public string? FieldModelName { get; set; }
    /// <summary>Content hash of the OBJ under &lt;root&gt;/models. Null when the run used the
    /// default box field, or when it predates field models being stored.</summary>
    public string? FieldModelHash { get; set; }
    public string? FieldItemRegex { get; set; }
    public bool FieldItemRegexMaterial { get; set; }
    public float FieldResolution { get; set; }

    /// <summary>
    /// Which shape of metadata this row was written with.
    ///
    /// Rows written by an older build simply have no value for the fields above - LiteDB returns
    /// the CLR default for a property its document does not carry - and a listing that showed
    /// "0 plants" for every run that predates this change would be reporting a measurement it
    /// never made. <see cref="RunStore.Reconcile"/> rebuilds what it can from request.json and
    /// the directory, once, and stamps the row.
    /// </summary>
    public int MetadataVersion { get; set; }
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
    public uint LastStep;
    public long BytesOnDisk;
    public uint AccountedIndexEntries;
    public DateTime LastMetadataFlush = DateTime.UtcNow;
}

public class RunStore : IRunStore, IDisposable
{
    readonly string Root;
    readonly LiteDatabase Db;
    readonly ILiteCollection<RunRecord> Runs;
    readonly ConcurrentDictionary<string, RunWriter> Writers = new();

    /// <summary>
    /// Serialises every read-modify-write of a run's metadata row.
    ///
    /// Each of them fetches the whole record, changes two fields and writes it back, and they run
    /// on different threads: the frame writer flushes progress from its own long-running task
    /// while the hub thread books the terrain it has just written and the run loop books the
    /// measurement. Interleave a fetch and a store and the loser's fields are silently reverted -
    /// a run whose recorded size is missing its terrain, or whose progress jumps backwards. The
    /// contention is nil: at most one of these every couple of seconds per run.
    /// </summary>
    readonly object MetadataLock = new();

    /// <summary>Drives periodic maintenance. Null until <see cref="StartMaintenance"/>.</summary>
    Timer? Maintenance;
    /// <summary>Non-zero while a sweep is running, so a slow one cannot overlap the next tick.</summary>
    int Sweeping;
    /// <summary>Non-zero while model collection is running. Eviction, delete and the sweep all reach it.</summary>
    int Collecting;
    /// <summary>Set on the way down, so an in-flight sweep stops touching a closing database.</summary>
    volatile bool Stopping;

    /// <summary>The shape <see cref="Reconcile"/> brings older rows up to.</summary>
    const int CurrentMetadataVersion = 3;

    /// <summary>
    /// Frames that may sit between the simulation and the disk.
    ///
    /// Small on purpose. Each slot holds a whole exported frame - tens of megabytes at production
    /// plant count - so this is a memory budget, not a throughput knob. One worker deflating at
    /// ~10 ms a frame keeps up with a simulation step of similar cost; if it ever does not, the
    /// simulation waits, which is the correct failure.
    /// </summary>
    public int QueueDepth { get; set; } = 3;
    /// <summary>Global raw-frame budget, including frames currently being compressed. A single
    /// frame larger than the budget is allowed only while no other frame is pending.</summary>
    public long PendingFrameByteBudget { get; set; } = 128L << 20;
    readonly object PendingFramesLock = new();
    long PendingFrameBytes;

    bool ReserveFrameBytes(RunWriter writer, long bytes)
    {
        lock (PendingFramesLock)
        {
            while (!Stopping && !writer.Queue.IsAddingCompleted && PendingFrameBytes > 0
                && bytes > PendingFrameByteBudget - PendingFrameBytes)
                Monitor.Wait(PendingFramesLock, 250);
            if (Stopping || writer.Queue.IsAddingCompleted) return false;
            PendingFrameBytes += bytes;
            return true;
        }
    }

    void ReleaseFrameBytes(long bytes)
    {
        lock (PendingFramesLock)
        {
            PendingFrameBytes -= bytes;
            Monitor.PulseAll(PendingFramesLock);
        }
    }

    /// <summary>How often the run's metadata record is brought up to date while it computes.</summary>
    static readonly TimeSpan MetadataInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long an unreferenced field model is kept before it is collected.
    ///
    /// A model is stored when it is UPLOADED, which is before any run references it - the user
    /// picks the file, looks at the trays, adjusts the planting and starts the run some minutes
    /// later. Collecting unreferenced models immediately would delete the one that is about to be
    /// used, every time.
    /// </summary>
    public TimeSpan ModelGrace { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How often the store enforces its own budget.
    ///
    /// Eviction used to happen only when a simulation STARTED, and model collection only from
    /// eviction or an explicit delete. On a server that is uploading and browsing but not running
    /// anything - which is most of a working day - nothing swept at all: a store already over its
    /// budget stayed over it, and uploaded models accumulated with nothing to collect them, at up
    /// to 256 MB per upload and before any run refers to them.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Total bytes of stored field models to keep.
    ///
    /// Its own cap rather than a share of <see cref="RetentionBytes"/>, because the two cannot be
    /// spent against each other: evicting a run does not release its model - the model outlives it
    /// and may be shared - so a single budget would have the eviction loop deleting every
    /// completed run trying to reach a total that only model collection can reduce. Smaller than
    /// the run budget by an order of magnitude, because a model is an OBJ and a run is a film.
    /// </summary>
    public long ModelRetentionBytes { get; set; } = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// The age below which a field model is never collected, whatever the budget says.
    ///
    /// Above the upload buffer's own five-minute lifetime, deliberately. A model younger than this
    /// may be one an upload has just claimed - <see cref="StoreFieldModel"/> returns the hash of an
    /// existing file without rewriting it - and deleting it leaves the run that is starting with a
    /// hash pointing at nothing, permanently.
    /// </summary>
    public TimeSpan ModelMinGrace { get; set; } = TimeSpan.FromMinutes(15);

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
            Directory.CreateDirectory(ModelsDir);
            //Said out loud at startup. The root is a fallback off AppContext.BaseDirectory unless
            //something sets AGRO_RunStoreRoot, which means it moves when the server is run from a
            //different output directory - and "where did my runs go" should be a two-second
            //question, not an archaeology exercise.
            Console.WriteLine($"RunStore root: {Path.GetFullPath(Root)}");
            //Shared connection: the simulation loop writes from a background thread while HTTP
            //requests read, and LiteDB's default Direct mode allows a single process handle only.
            Db = new LiteDatabase(new ConnectionString { Filename = Path.Combine(Root, "index.litedb"), Connection = ConnectionType.Shared });
            Runs = Db.GetCollection<RunRecord>("runs");
            Runs.EnsureIndex(x => x.CreatedUtc);
            SweepStaleRuns();
            AdoptOrphanedRuns();
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

    /// <summary>
    /// Takes ownership of run directories the metadata knows nothing about.
    ///
    /// The frames are the run; index.litedb is a description of them. A store whose database was
    /// lost, replaced or restored separately from its files - or one assembled by copying run
    /// directories between machines, which is how these are moved around - would otherwise have
    /// tens of gigabytes on disk and an empty listing, and eviction would never reclaim any of it
    /// because eviction only knows what the database told it.
    /// </summary>
    void AdoptOrphanedRuns()
    {
        try
        {
            var known = Runs.FindAll().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            var adopted = 0;
            foreach (var dir in Directory.EnumerateDirectories(RunsDir))
            {
                var id = Path.GetFileName(dir);
                if (!IsSafeId(id) || known.Contains(id)) continue;

                var created = Directory.GetCreationTimeUtc(dir);
                var run = new RunRecord
                {
                    Id = id,
                    CreatedUtc = created,
                    UpdatedUtc = Directory.GetLastWriteTimeUtc(dir),
                    //Nothing can be running in a process that has just started, and an adopted
                    //directory is by definition not this process's work.
                    Status = "complete",
                };
                Reconcile(run, insert: true);
                ++adopted;
            }
            if (adopted > 0)
                Console.WriteLine($"RunStore: adopted {adopted} run director{(adopted == 1 ? "y" : "ies")} that the metadata did not know about.");
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not scan for orphaned runs: {e.Message}"); }
    }

    /// <summary>
    /// Rebuilds a row's descriptive fields from what is actually on disk.
    ///
    /// Runs the listing has to describe outlive the shape of the record that describes them. This
    /// reads the counts back out of request.json - which every run has written since the store
    /// existed - and measures the directory, then stamps the row so it is done once per run rather
    /// than once per listing. Trays are the exception and stay 0: they come out of the imported
    /// model, not the request, and a guess would be indistinguishable from a measurement.
    /// </summary>
    void Reconcile(RunRecord run, bool insert = false)
    {
        try
        {
            var dir = RunDir(run.Id);
            var framesDir = FramesDir(run.Id);

            long frameBytes = 0;
            uint frames = 0, lastStep = 0;
            var firstFrame = "";
            var firstStep = uint.MaxValue;
            var newestWrite = DateTime.MinValue;
            if (Directory.Exists(framesDir))
                foreach (var file in Directory.EnumerateFiles(framesDir))
                {
                    var info = new FileInfo(file);
                    frameBytes += info.Length;
                    ++frames;
                    if (info.LastWriteTimeUtc > newestWrite) newestWrite = info.LastWriteTimeUtc;
                    if (!uint.TryParse(Path.GetFileNameWithoutExtension(file), out var step)) continue;
                    if (step + 1 > lastStep) lastStep = step + 1;
                    if (step < firstStep) { firstStep = step; firstFrame = file; }
                }

            //What the run exported is not in its request - the flags are computed at Create - but
            //every frame carries them in its second byte. One frame, six bytes, one inflate of the
            //first block: the 418 MB frames in this store cost exactly what the 14-byte ones do.
            if (firstFrame.Length > 0 && TryReadExportFlags(firstFrame, out var flags))
                run.Analytics = (flags & 2) != 0;

            if (newestWrite > DateTime.MinValue) run.UpdatedUtc = newestWrite;

            long auxBytes = 0;
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir))
                    auxBytes += new FileInfo(file).Length;

            run.BytesOnDisk = frameBytes;
            run.AuxBytesOnDisk = auxBytes;
            //The directory wins for a run that has finished: it is the only count that is a count.
            //An older build wrote `step + 1` into StoredFrames, which over-reports by the frame
            //stride - a strided run that stored 135 frames claimed 540. A run still being written
            //is the exception, where a scan is only a moment in time and the writer knows better.
            var live = Writers.ContainsKey(run.Id);
            if (!live || run.StoredFrames == 0) run.StoredFrames = frames;
            if (!live || run.LastStep == 0) run.LastStep = lastStep;

            var requestPath = Path.Combine(dir, "request.json");
            if (File.Exists(requestPath))
            {
                var request = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(requestPath),
                    Models.AppJsonSerializerContext.Default.SimulationRequest);
                if (request != null)
                {
                    if (run.HoursPerTick == 0) run.HoursPerTick = (ushort)(request.HoursPerTick ?? 1);
                    run.TotalHours = request.TotalHours ?? 0;
                    //Sliced, not formatted: the stored request holds a C# DateTime literal
                    //"2026-02-01T00:00:00", and its first ten characters ARE the day.
                    run.InitialDay = request.InitialDate?.ToString("yyyy-MM-dd") ?? "";
                    run.Seed = request.Seed ?? 0;
                    run.RenderMode = request.RenderMode ?? 0;
                    //`?? 1` because a request with no Plants array plants exactly one centred
                    //plant - see Initialize.SecondStageInit - so zero would be wrong, not unknown.
                    run.Plants = (uint)(request.Plants?.Length ?? 1);
                    run.Species = (uint)(request.Species?.Length ?? 0);
                    run.FieldModelName = string.IsNullOrEmpty(request.FieldModelPath) ? null : request.FieldModelPath;
                    //The request's obstacle array is the true count only when there is no imported
                    //model. With one it holds a SINGLE 'mesh' entry standing for the entire facade
                    //- SoilFormationsList merges every imported obstacle into one mesh before it
                    //reaches the browser, and the browser sends that one entry back. Reporting "1
                    //obstacle" for a model with thousands would be a wrong number wearing the
                    //clothes of a measurement, so such a run reports nothing instead.
                    run.Obstacles = run.FieldModelName == null ? (uint)(request.Obstacles?.Length ?? 0) : 0;
                    run.FieldItemRegex = request.FieldItemRegex;
                    run.FieldItemRegexMaterial = request.FieldItemRegexMaterial ?? false;
                    run.FieldResolution = request.FieldResolution ?? 0;
                    run.Deterministic = request.Deterministic ?? run.Deterministic;
                    //Lineage survives in the request because the client sends it there. Without
                    //this an adopted child stops pinning its parent against eviction.
                    run.ParentRunId = string.IsNullOrEmpty(request.ParentRunId) ? null : request.ParentRunId;
                    if (run.TotalSteps == 0 && run.HoursPerTick > 0)
                    {
                        var ticks = Math.DivRem(run.TotalHours, run.HoursPerTick, out var rem);
                        run.TotalSteps = (uint)(ticks + (rem == 0 ? 0 : 1));
                    }
                }
            }

            //The request is written once, at BeginRun, so its timestamp is when the run STARTED -
            //which the directory's own creation time is only incidentally, and stops being the
            //moment a store is copied from one machine to another.
            if (File.Exists(requestPath))
            {
                var started = File.GetLastWriteTimeUtc(requestPath);
                if (started < run.CreatedUtc) run.CreatedUtc = started;
            }
            if (run.UpdatedUtc < run.CreatedUtc) run.UpdatedUtc = run.CreatedUtc;

            //The terrain sidecar wins over the request wherever they disagree: it records what the
            //trays were BUILT with, while the request records what was in force when the run was
            //started. It is also the only place the model's content hash survives.
            var metaPath = TerrainMetaPath(run.Id);
            if (File.Exists(metaPath))
            {
                var meta = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(metaPath),
                    Models.AppJsonSerializerContext.Default.StoredTerrainMeta);
                if (meta != null)
                {
                    run.FieldModelHash = meta.ModelHash;
                    run.FieldModelName = meta.ModelName ?? run.FieldModelName;
                    run.FieldItemRegex = meta.Regex ?? run.FieldItemRegex;
                    run.FieldItemRegexMaterial = meta.RegexForMaterials;
                    run.FieldResolution = meta.Resolution;
                }
            }

            run.MetadataVersion = CurrentMetadataVersion;
            if (insert) Runs.Insert(run); else Runs.Update(run);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not reconcile {run.Id}: {e.Message}");
            //Stamped anyway. A request.json that cannot be parsed will not parse on the next
            //listing either, and retrying it for every listing forever costs more than the blanks.
            try { run.MetadataVersion = CurrentMetadataVersion; if (insert) Runs.Insert(run); else Runs.Update(run); }
            catch { /* best effort */ }
        }
    }

    string RunsDir => Path.Combine(Root, "runs");
    string ModelsDir => Path.Combine(Root, "models");
    string RunDir(string runId) => Path.Combine(RunsDir, runId);
    string FramesDir(string runId) => Path.Combine(RunDir(runId), "frames");
    string IndexPath(string runId) => Path.Combine(RunDir(runId), "index.bin");
    string RequestPath(string runId) => Path.Combine(RunDir(runId), "request.json");
    string TerrainPath(string runId) => Path.Combine(RunDir(runId), "terrain.bin.gz");
    string FramePath(string runId, uint step) => Path.Combine(FramesDir(runId), $"{step:D6}.g");
    /// <summary>What frames were called before the extension became <c>.g</c>. Read, never written.</summary>
    string LegacyFramePath(string runId, uint step) => Path.Combine(FramesDir(runId), $"{step:D6}.z");
    string ObstacleLightPath(string runId) => Path.Combine(RunDir(runId), "obstaclelight.json.gz");
    string TerrainMetaPath(string runId) => Path.Combine(RunDir(runId), "terrain.json");
    string CheckpointPath(string runId) => Path.Combine(RunDir(runId), "checkpoint.bin.gz");
    string ModelPath(string hash) => Path.Combine(ModelsDir, $"{hash}.obj.gz");

    /// <summary>Files how this run's terrain was built, beside the run. Best-effort.</summary>
    void WriteTerrainMeta(string runId, RunSpec spec)
    {
        if (string.IsNullOrEmpty(spec.FieldModelHash) && string.IsNullOrEmpty(spec.FieldModelName)) return;
        try
        {
            File.WriteAllBytes(TerrainMetaPath(runId), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new Models.StoredTerrainMeta(spec.FieldModelHash, spec.FieldModelName, spec.FieldItemRegex,
                                             spec.FieldItemRegexMaterial, spec.FieldResolution),
                Models.AppJsonSerializerContext.Default.StoredTerrainMeta));
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not record the terrain of {runId}: {e.Message}"); }
    }

    /// <summary>Rejects anything that is not a plain id, so a path cannot escape the store.</summary>
    static bool IsSafeId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public string BeginRun(RunSpec spec)
    {
        var id = Guid.NewGuid().ToString("N");
        if (Disabled) return id;
        try
        {
            Directory.CreateDirectory(FramesDir(id));
            //The request is what a resumed or branched run needs to rebuild its world, and the
            //upload and terrain caches that hold it today expire after five minutes.
            File.WriteAllText(RequestPath(id), spec.RequestJson);
            //And beside it, how the terrain was actually built. The database is not the only copy
            //of this: a store whose index.litedb is lost is adopted from its directories, and a run
            //adopted without its model hash contributes nothing to the reference set that decides
            //which models to keep - so the next eviction would delete every model the store owns.
            WriteTerrainMeta(id, spec);

            Runs.Insert(new RunRecord
            {
                Id = id,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Status = "running",
                HoursPerTick = spec.HoursPerTick,
                TotalSteps = spec.TotalSteps,
                Analytics = spec.Analytics,
                Deterministic = spec.Deterministic,
                AuxBytesOnDisk = Encoding.UTF8.GetByteCount(spec.RequestJson),
                TotalHours = spec.TotalHours,
                InitialDay = spec.InitialDay,
                Seed = spec.Seed,
                RenderMode = spec.RenderMode,
                Plants = spec.Plants,
                Trays = spec.Trays,
                Obstacles = spec.Obstacles,
                Species = spec.Species,
                FieldModelName = spec.FieldModelName,
                FieldModelHash = spec.FieldModelHash,
                FieldItemRegex = spec.FieldItemRegex,
                FieldItemRegexMaterial = spec.FieldItemRegexMaterial,
                FieldResolution = spec.FieldResolution,
                    ParentRunId = spec.ParentRunId,
                MetadataVersion = CurrentMetadataVersion,
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
        if (!ReserveFrameBytes(writer, scene.LongLength)) return;
        var queued = false;
        try
        {
            //Blocks when the queue is full, which is the backpressure. Everything expensive -
            //the deflate above all - happens on the worker, so the simulation thread pays only
            //for handing over a reference.
            writer.Queue.Add((step, scene));
            queued = true;
        }
        catch (InvalidOperationException) { /* the run finished; nothing more is accepted */ }
        finally { if (!queued) ReleaseFrameBytes(scene.LongLength); }
    }

    public void WriteTerrain(string runId, byte[] serializedTerrain)
    {
        if (Disabled || !IsSafeId(runId) || serializedTerrain == null || serializedTerrain.Length == 0) return;
        try
        {
            using var output = new FileStream(TerrainPath(runId), FileMode.Create, FileAccess.Write, FileShare.Read);
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(serializedTerrain, 0, serializedTerrain.Length);
            AddAuxBytes(runId, output.Length);
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not store the terrain of {runId}: {e.Message}"); }
    }

    /// <summary>
    /// Opens the run's index file and starts the single worker that owns it.
    ///
    /// The counts are carried in rather than started at zero, because a RESUMED run already has
    /// frames on disk: its writer continues an existing tally, and a fresh one would flush its own
    /// count over the top and report a forty-frame run as holding twenty.
    /// </summary>
    void StartWriter(string runId, uint storedFrames = 0, long bytesOnDisk = 0, uint lastStep = 0)
    {
        var writer = new RunWriter
        {
            RunId = runId,
            StoredFrames = storedFrames,
            LastStep = lastStep,
            AccountedIndexEntries = storedFrames,
            BytesOnDisk = bytesOnDisk,
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
                    var path = FramePath(writer.RunId, step);
                    var temporary = path + ".tmp";
                    long storedBytes;
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
                            zlib.Write(scene);
                        storedBytes = output.Length;
                    }
                    File.Move(temporary, path, overwrite: true);

                    Span<byte> entry = stackalloc byte[8];
                    BitConverter.TryWriteBytes(entry[..4], step);
                    BitConverter.TryWriteBytes(entry[4..], checked((uint)storedBytes));
                    writer.Index.Write(entry);
                    //Flushed per frame so a reader - the client rebuilding its timeline while the
                    //run computes - sees entries for frames that are actually on disk, and never
                    //an entry for one that is not.
                    writer.Index.Flush();

                    //A count, not the step reached: with a frame stride those differ, and a
                    //listing that says "540 frames" for a run that stored 135 is describing
                    //storage it does not have.
                    ++writer.StoredFrames;
                    writer.LastStep = step + 1;
                    writer.BytesOnDisk += storedBytes;

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
                finally { ReleaseFrameBytes(scene.LongLength); }
            }
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: writer for {writer.RunId} stopped: {e}"); }
        finally
        {
            writer.Queue.CompleteAdding();
            while (writer.Queue.TryTake(out var abandoned)) ReleaseFrameBytes(abandoned.Scene.LongLength);
            try { FlushMetadata(writer); } catch { /* best effort */ }
            try { writer.Index.Dispose(); } catch { /* best effort */ }
        }
    }

    void FlushMetadata(RunWriter writer)
    {
        writer.LastMetadataFlush = DateTime.UtcNow;
        lock (MetadataLock)
        {
            var run = Runs.FindById(writer.RunId);
            if (run == null) return;
            run.StoredFrames = writer.StoredFrames;
            run.LastStep = writer.LastStep;
            run.BytesOnDisk = writer.BytesOnDisk;
            run.AuxBytesOnDisk += (writer.StoredFrames - writer.AccountedIndexEntries) * 8L;
            run.UpdatedUtc = DateTime.UtcNow;
            Runs.Update(run);
            writer.AccountedIndexEntries = writer.StoredFrames;
        }
    }

    /// <summary>Books a file written outside the frame writer against the run's footprint.</summary>
    void AddAuxBytes(string runId, long bytes)
    {
        try
        {
            lock (MetadataLock)
            {
                var run = Runs.FindById(runId);
                if (run == null) return;
                run.AuxBytesOnDisk += bytes;
                run.UpdatedUtc = DateTime.UtcNow;
                Runs.Update(run);
            }
        }
        catch { /* a size that is a little stale is not worth failing a write for */ }
    }

    public void CompleteRun(string runId, bool aborted)
    {
        if (Disabled || !IsSafeId(runId)) return;
        try
        {
            //Drain before recording the outcome, so a completed run's metadata describes every
            //frame it actually stored rather than whatever the last periodic flush had seen.
            if (Writers.TryGetValue(runId, out var writer))
            {
                writer.Queue.CompleteAdding();
                var drained = writer.Worker?.Wait(TimeSpan.FromMinutes(2)) ?? true;
                //Deregistered only once the worker has actually stopped. It used to be removed
                //BEFORE the wait, so a drain that timed out left a thread still appending frames
                //and still holding the index handle - while the row said "complete" and the
                //dictionary said there was no writer, which is exactly the pair of conditions
                //eviction looks for before deleting a run's directory.
                if (drained) Writers.TryRemove(runId, out _);
                else Console.Error.WriteLine(
                    $"RunStore: the writer for {runId} did not drain within two minutes. The run stays " +
                    $"pinned against eviction until this process exits; its frames are still being written.");
            }

            //Taken AFTER the writer has drained, never across the wait: the writer's own final
            //flush takes this same lock on its way out.
            lock (MetadataLock)
            {
                var run = Runs.FindById(runId);
                if (run == null) return;
                //The FIRST outcome wins. This is called twice whenever delivering the result
                //throws - the loop completes the run, `await to.Result(...)` fails at a client
                //that has gone, and the catch completes it again as aborted. The run had finished
                //every step it was asked for; saying otherwise sends a user to continue a run that
                //has nothing left to compute.
                if (run.Status == "running")
                    run.Status = aborted ? "aborted" : "complete";
                //Booked once, here, rather than eight bytes at a time from the writer thread: the
                //index is the one file that grows with the run, and adding to the metadata row per
                //frame is the cross-process mutex this store already refuses to take per frame.
                if (writer != null)
                {
                    run.AuxBytesOnDisk += (writer.StoredFrames - writer.AccountedIndexEntries) * 8L;
                    writer.AccountedIndexEntries = writer.StoredFrames;
                }
                run.UpdatedUtc = DateTime.UtcNow;
                Runs.Update(run);
            }
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not complete {runId}: {e.Message}"); }
    }

    public bool TryReadFrameDeflated(string runId, uint step, out byte[] deflated)
    {
        deflated = [];
        if (Disabled || !IsSafeId(runId)) return false;
        var path = FramePath(runId, step);
        //Runs written before the extension changed still have their frames, and they are the same
        //zlib stream under a different name.
        if (!File.Exists(path)) path = LegacyFramePath(runId, step);
        if (!File.Exists(path)) return false;
        try { deflated = File.ReadAllBytes(path); return true; }
        catch { return false; }
    }

    public bool TryReadIndex(string runId, out byte[] indexBytes) =>
        TryReadFile(runId, IndexPath, out indexBytes);

    public bool TryReadIndexSnapshot(string runId, out byte[] indexBytes, out uint revision, out uint restartStep)
    {
        lock (MetadataLock)
        {
            revision = restartStep = 0;
            if (!TryReadIndex(runId, out indexBytes)) return false;
            var run = Runs.FindById(runId);
            revision = run?.FrameRevision ?? 0;
            restartStep = run?.FrameRestartStep ?? 0;
            // The writer may be appending the last eight-byte entry during this read.
            if (indexBytes.Length % 8 != 0) Array.Resize(ref indexBytes, indexBytes.Length / 8 * 8);
            return true;
        }
    }

    public bool TryReadRequest(string runId, out byte[] utf8Json) =>
        TryReadFile(runId, RequestPath, out utf8Json);

    public bool TryReadTerrain(string runId, out byte[] gzipped) =>
        TryReadFile(runId, TerrainPath, out gzipped);

    public bool TryReadObstacleLight(string runId, out byte[] gzipped) =>
        TryReadFile(runId, ObstacleLightPath, out gzipped);

    bool TryReadFile(string runId, Func<string, string> pathOf, out byte[] bytes)
    {
        bytes = [];
        if (Disabled || !IsSafeId(runId)) return false;
        var path = pathOf(runId);
        if (!File.Exists(path)) return false;
        try { bytes = File.ReadAllBytes(path); return true; }
        catch { return false; }
    }

    public void WriteCheckpoint(string runId, uint step, byte[] state)
    {
        if (Disabled || !IsSafeId(runId) || state == null || state.Length == 0) return;
        try
        {
            //Written beside and moved into place. A checkpoint truncated by a crash is worse than
            //no checkpoint: it is the one file a resume trusts completely, and a half-written one
            //reads back as a world with fewer organs rather than as a failure.
            var path = CheckpointPath(runId);
            var temp = $"{path}.tmp";
            long size;
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                //The step goes in first and uncompressed, so reading it back costs four bytes
                //rather than inflating hundreds of megabytes to find out where the run had got to.
                output.Write(BitConverter.GetBytes(step));
                using (var gz = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    gz.Write(state, 0, state.Length);
                size = output.Length;
            }

            var previous = File.Exists(path) ? new FileInfo(path).Length : 0;
            File.Move(temp, path, overwrite: true);
            //Only the difference: the file replaces itself rather than accumulating.
            if (size != previous) AddAuxBytes(runId, size - previous);
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not checkpoint {runId}: {e.Message}"); }
    }

    public bool TryReadCheckpoint(string runId, out uint step, out byte[] state)
    {
        step = 0;
        state = [];
        if (Disabled || !IsSafeId(runId)) return false;
        var path = CheckpointPath(runId);
        if (!File.Exists(path)) return false;
        try
        {
            using var input = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            if (input.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4) return false;
            step = BitConverter.ToUInt32(header);

            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            gz.CopyTo(buffer);
            state = buffer.ToArray();
            return state.Length > 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not read the checkpoint of {runId}: {e.Message}");
            return false;
        }
    }

    /// <summary>A checkpoint at N contains frames through N-1. Keep that prefix exactly once;
    /// preserve any later files in a recovery directory before allowing their addresses to be
    /// generated again. Index replacement is atomic and precedes moving the superseded files.</summary>
    void ReconcileCheckpointHistory(RunRecord run, uint checkpointStep)
    {
        var indexPath = IndexPath(run.Id);
        var index = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : [];
        if (index.Length % 8 != 0) throw new InvalidDataException("The frame index has a partial record.");
        var prefix = new SortedDictionary<uint, uint>();
        var tail = new List<string>();
        for (int offset = 0; offset < index.Length; offset += 8)
        {
            var step = BitConverter.ToUInt32(index, offset);
            if (step >= checkpointStep) continue;
            var path = FramePath(run.Id, step);
            if (!File.Exists(path)) path = LegacyFramePath(run.Id, step);
            if (!File.Exists(path)) throw new InvalidDataException($"Missing frame {step} before the checkpoint.");
            prefix[step] = checked((uint)new FileInfo(path).Length);
        }
        // Include orphaned tail files left by an interrupted index append or earlier resume.
        foreach (var path in Directory.EnumerateFiles(FramesDir(run.Id)))
            if ((Path.GetExtension(path) is ".g" or ".z")
                && uint.TryParse(Path.GetFileNameWithoutExtension(path), out var step) && step >= checkpointStep)
                tail.Add(path);
        string? recovery = null;
        if (tail.Count > 0 || prefix.Count * 8 != index.Length)
        {
            recovery = Path.Combine(RunDir(run.Id), "recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(recovery);
            File.WriteAllBytes(Path.Combine(recovery, "index.bin"), index);
            File.Copy(RequestPath(run.Id), Path.Combine(recovery, "request.json"));
        }
        var temporary = indexPath + ".tmp";
        using (var output = new BinaryWriter(File.Create(temporary)))
            foreach (var (step, length) in prefix) { output.Write(step); output.Write(length); }
        File.Move(temporary, indexPath, overwrite: true);
        foreach (var path in tail) File.Move(path, Path.Combine(recovery!, Path.GetFileName(path)));
        run.StoredFrames = checked((uint)prefix.Count);
        run.LastStep = prefix.Count > 0 ? prefix.Last().Key + 1 : 0;
        run.BytesOnDisk = prefix.Values.Sum(x => (long)x);
        run.AuxBytesOnDisk = Directory.EnumerateFiles(RunDir(run.Id), "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length) - run.BytesOnDisk;
    }

    public bool ResumeRun(string runId, uint checkpointStep, uint totalSteps, int totalHours, string requestJson)
    {
        if (Disabled || !IsSafeId(runId)) return false;
        try
        {
            lock (MetadataLock)
            {
                var run = Runs.FindById(runId);
                if (run == null) return false;

                //The same test DeleteRun makes, for the same reason. A run whose writer is still
                //draining has a thread appending frames and holding the index handle: starting a
                //second writer on it leaves two workers writing the same frame paths and
                //interleaving eight-byte index entries, after which the index no longer parses in
                //step order. The status alone is not enough - a drain that timed out leaves the
                //writer registered and the row already stamped.
                if (Writers.ContainsKey(runId) || run.Status == "running") return false;

                ReconcileCheckpointHistory(run, checkpointStep);
                ++run.FrameRevision;
                run.FrameRestartStep = checkpointStep;

                //Written inside the lock, so a refusal cannot have already replaced the request of
                //a run it then declines to reopen.
                var oldRequestBytes = new FileInfo(RequestPath(runId)).Length;
                File.WriteAllText(RequestPath(runId), requestJson);
                run.AuxBytesOnDisk += new FileInfo(RequestPath(runId)).Length - oldRequestBytes;
                run.Status = "running";
                run.TotalSteps = totalSteps;
                run.TotalHours = totalHours;
                run.UpdatedUtc = DateTime.UtcNow;
                Runs.Update(run);
                //Seeded from what the run already holds, so the resumed writer adds to the tally
                //rather than replacing it.
                StartWriter(runId, run.StoredFrames, run.BytesOnDisk, run.LastStep);
                return true;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not reopen {runId}: {e.Message}");
            return false;
        }
    }

    public bool HasCheckpoint(string runId) =>
        !Disabled && IsSafeId(runId) && File.Exists(CheckpointPath(runId));

    public bool HasTerrain(string runId) =>
        !Disabled && IsSafeId(runId) && File.Exists(TerrainPath(runId));

    public bool HasObstacleLight(string runId) =>
        !Disabled && IsSafeId(runId) && File.Exists(ObstacleLightPath(runId));

    public bool HasFieldModel(string? hash) =>
        !Disabled && !string.IsNullOrEmpty(hash) && IsSafeId(hash) && File.Exists(ModelPath(hash));

    public void WriteObstacleLight(string runId, byte[] utf8Json)
    {
        if (Disabled || !IsSafeId(runId) || utf8Json == null || utf8Json.Length == 0) return;
        try
        {
            //gzip rather than the frames' zlib: this one is served to a browser as a file to save,
            //and Content-Encoding: gzip is the form every client handles without ambiguity.
            using var output = new FileStream(ObstacleLightPath(runId), FileMode.Create, FileAccess.Write, FileShare.Read);
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(utf8Json, 0, utf8Json.Length);
            AddAuxBytes(runId, output.Length);
            Console.WriteLine($"RunStore: stored the full leaf area index measurement of {runId} " +
                              $"({Size(utf8Json.Length)} of JSON, {Size(output.Length)} on disk).");
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: could not store the measurement of {runId}: {e.Message}"); }
    }

    public string? StoreFieldModel(string objSource)
    {
        if (Disabled || string.IsNullOrEmpty(objSource)) return null;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(objSource);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var path = ModelPath(hash);
            //Content-addressed, so the same facade uploaded for the tenth run costs nothing. The
            //existence check is the dedup - and it has to be atomic against the collector, which
            //is deciding whether to delete this very file on another thread.
            lock (MetadataLock)
            {
                if (File.Exists(path))
                {
                    //Touched so the grace period is measured from the last time it mattered, not
                    //from the first upload - otherwise a model in daily use is collected six hours
                    //in. NOT best-effort: if the touch fails because the file has just been
                    //collected, returning its hash anyway hands the caller a reference to nothing.
                    //Falling through rewrites it, which is the correct repair.
                    try
                    {
                        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                        return hash;
                    }
                    catch (FileNotFoundException) { /* collected underneath us; write it again */ }
                    catch (DirectoryNotFoundException) { /* ditto */ }
                }
            }

            //Written beside and moved into place, so a crash mid-write cannot leave a truncated
            //file under a name that claims to be the hash of its contents.
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gz = new GZipStream(output, CompressionLevel.Optimal))
                gz.Write(bytes, 0, bytes.Length);
            lock (MetadataLock) File.Move(temp, path, overwrite: true);

            Console.WriteLine($"RunStore: stored field model {hash[..12]} ({Size(bytes.Length)} of OBJ, {Size(new FileInfo(path).Length)} on disk).");
            return hash;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not store the field model: {e.Message}");
            return null;
        }
    }

    public bool TryReadFieldModel(string hash, out string objSource)
    {
        objSource = "";
        if (Disabled || !IsSafeId(hash)) return false;
        var path = ModelPath(hash);
        if (!File.Exists(path)) return false;
        try
        {
            using var input = File.OpenRead(path);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            objSource = reader.ReadToEnd();
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not read field model {hash}: {e.Message}");
            return false;
        }
    }

    public RunRecord? GetRun(string runId)
    {
        if (Disabled || !IsSafeId(runId)) return null;
        var run = Runs.FindById(runId);
        if (run != null && run.MetadataVersion < CurrentMetadataVersion) Reconcile(run);
        return run;
    }

    public IReadOnlyList<RunRecord> ListRuns(int limit)
    {
        if (Disabled) return [];
        var runs = Runs.Query().OrderByDescending(x => x.CreatedUtc).Limit(limit).ToList();
        //Once per run, not once per listing: Reconcile stamps the row it repairs.
        foreach (var run in runs)
            if (run.MetadataVersion < CurrentMetadataVersion) Reconcile(run);
        return runs;
    }

    public void StartMaintenance()
    {
        if (Disabled || Maintenance != null) return;
        if (SweepInterval <= TimeSpan.Zero)
        {
            Console.WriteLine("RunStore: periodic maintenance is off. The budget is then enforced only when a " +
                              "run starts or is deleted, and uploaded field models are never collected.");
            return;
        }
        //One-shot and re-armed at the end of each pass, not a fixed period: recursively deleting a
        //17 GB run can outlive the interval, and a tick queued behind a pass that is already
        //evicting would start a second one on top of it.
        Maintenance = new Timer(_ => Sweep(), null, SweepInterval, Timeout.InfiniteTimeSpan);
        Console.WriteLine($"RunStore: maintenance every {SweepInterval.TotalMinutes:0} min (runs {Size(RetentionBytes)}, " +
                          $"field models {Size(ModelRetentionBytes)}, unreferenced models kept {ModelGrace.TotalHours:0} h).");
    }

    /// <summary>
    /// One maintenance pass: collect dead models, then enforce the run budget.
    ///
    /// Collection first, so eviction measures a store that has already given up what it can give
    /// up for free. Never throws - and, because the timer is one-shot, the re-arm is in the finally
    /// where a failed pass cannot silently end maintenance for the life of the process.
    /// </summary>
    void Sweep()
    {
        if (Stopping || Disabled) return;
        if (Interlocked.Exchange(ref Sweeping, 1) != 0) return;
        try
        {
            CollectTempModels();
            CollectModels();
            Evict();
        }
        catch (ObjectDisposedException) { /* the database went away: the store is shutting down */ }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: maintenance sweep failed: {e.Message}"); }
        finally
        {
            Interlocked.Exchange(ref Sweeping, 0);
            if (!Stopping) try { Maintenance?.Change(SweepInterval, Timeout.InfiniteTimeSpan); } catch { /* disposed */ }
        }
    }

    public DeleteOutcome DeleteRun(string runId, out string reason)
    {
        reason = "";
        if (Disabled) { reason = "The run store is disabled on this server."; return DeleteOutcome.Refused; }
        if (!IsSafeId(runId)) { reason = "Not a run id."; return DeleteOutcome.NoSuchRun; }
        try
        {
            //Under the same lock as eviction, so the two cannot both decide to remove the same
            //directory - the loser then fails on a path that no longer exists and reports a disk
            //error for a deletion that did exactly what the user asked.
            lock (MetadataLock)
            {
                var run = Runs.FindById(runId);
                //A directory with no row is still deletable - AdoptOrphanedRuns may not have run
                //since it appeared - but a row with no directory must still clear the row.
                if (run == null && !Directory.Exists(RunDir(runId)))
                {
                    reason = "No such run.";
                    return DeleteOutcome.NoSuchRun;
                }

                //Refused rather than silently stopped. Deleting the frames out from under a live
                //writer leaves it appending to an index file in a directory that no longer exists,
                //and the user who asked has a simulation on screen they did not ask to lose.
                if (Writers.ContainsKey(runId) || run?.Status == "running")
                {
                    reason = "This run is still computing. Stop it first, then delete it.";
                    return DeleteOutcome.Refused;
                }

                var dir = RunDir(runId);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (run != null) Runs.Delete(runId);
                Console.WriteLine($"RunStore: deleted run {runId} ({Size((run?.BytesOnDisk ?? 0) + (run?.AuxBytesOnDisk ?? 0))}).");
            }
            CollectModels();
            return DeleteOutcome.Deleted;
        }
        catch (Exception e)
        {
            reason = e.Message;
            Console.Error.WriteLine($"RunStore: could not delete {runId}: {e.Message}");
            return DeleteOutcome.Refused;
        }
    }

    /// <summary>
    /// A byte count a person can read. Integer megabytes alone printed "0 MB" for every run
    /// under a megabyte, which is most of them during development and all of them in a log that
    /// is being read precisely because something unexpected was deleted.
    /// </summary>
    static string Size(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.00} GB"
                                    : bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB"
                                    : bytes >= 1024 ? $"{bytes / 1024.0:0.0} KB"
                                    : $"{bytes} B";

    /// <summary>Drops whole runs, oldest first, until the store is back inside its budget.</summary>
    void Evict()
    {
        if (Disabled) return;
        try
        {
            //Serialised against delete and against another eviction. Two passes that each read the
            //total before either deletes anything both believe the store is over budget by the
            //whole excess, and between them they delete twice what was needed - history the user
            //was inside their budget to keep.
            lock (MetadataLock)
            {
            var all = Runs.Query().OrderBy(x => x.CreatedUtc).ToList();
            static long Footprint(RunRecord r) => r.BytesOnDisk + r.AuxBytesOnDisk;
            //Runs only. Field models have their own cap (ModelRetentionBytes) because evicting a
            //run does not release its model - it outlives the run and may be shared - so counting
            //them here would have this loop delete every completed run in the store chasing a
            //total that only model collection can bring down.
            var total = all.Sum(Footprint);

            //If what cannot be evicted already exceeds the budget, deleting the rest frees nobody
            //and costs the user their whole history. Say so and stop.
            var pinned = all.Where(r => r.Status == "running" || all.Any(x => x.ParentRunId == r.Id)).Sum(Footprint);
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
                //Checked per run, not only at the pass entry: the loop is the long part, and every
                //iteration is a recursive delete plus a row removal that must not straddle shutdown.
                if (Stopping) break;
                //Never evict a run something else was branched from - its request and its stored
                //frames are the only thing making those children comparable to it.
                //Writers too, not only the status: the row is inserted as "running" before the
                //writer exists and cleared after it is gone, so the two disagree at the edges - and
                //on a timer this runs at arbitrary moments rather than only at BeginRun.
                if (run.Status == "running" || Writers.ContainsKey(run.Id) || all.Any(x => x.ParentRunId == run.Id)) continue;

                //Per run, because one undeletable directory used to abort the whole pass - which on
                //a timer means aborting it again every interval, forever, with the store stuck over
                //budget and only the first failure in the log.
                try
                {
                    var dir = RunDir(run.Id);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Runs.Delete(run.Id);
                    total -= Footprint(run);
                    Console.WriteLine($"RunStore: evicted run {run.Id} ({Size(Footprint(run))}) to stay inside the retention budget.");
                }
                catch (ObjectDisposedException)
                {
                    //The database is closing under us. Stop: continuing deletes directories whose
                    //rows can no longer be removed, which is worse than stopping mid-budget.
                    Console.Error.WriteLine($"RunStore: eviction stopped at {run.Id}; the store is shutting down.");
                    break;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"RunStore: could not evict {run.Id}: {e.Message}");
                }
            }
            }

            //After eviction, because a run that has just gone may have been the last reference to
            //its model. The list is passed through so the pass reads the table once.
            CollectModels(Runs.FindAll().ToList());
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: eviction failed: {e.Message}"); }
    }

    /// <summary>
    /// Deletes field models no surviving run refers to.
    ///
    /// Models are shared, so they cannot be deleted with the run that used them - and they are
    /// stored at upload time, before any run refers to them at all, which is why an unreferenced
    /// model is only collected once it is older than <see cref="ModelGrace"/>.
    /// </summary>
    /// <summary>What the shared field models occupy. Small in count, large in bytes.</summary>
    long ModelBytes()
    {
        try
        {
            if (!Directory.Exists(ModelsDir)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(ModelsDir)) total += new FileInfo(file).Length;
            return total;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Deletes field models no surviving run refers to, and keeps the model store inside its cap.
    ///
    /// Two stages, because "unreferenced" and "over budget" are different problems:
    ///
    ///   A. An unreferenced model older than <see cref="ModelGrace"/> is simply dead. It is stored
    ///      at UPLOAD, before any run refers to it, which is what the grace is for - collecting
    ///      immediately would delete the one about to be used, every time.
    ///   B. Over <see cref="ModelRetentionBytes"/>, unreferenced models go oldest-touched first
    ///      regardless of age - but never one touched within <see cref="ModelMinGrace"/>, which
    ///      must stay above the upload buffer's own five-minute lifetime: a model whose buffer
    ///      entry is still live is definitely about to be used.
    ///
    /// A REFERENCED model is never deleted, at any size. It is the only copy of the OBJ that makes
    /// its run re-runnable and drawable, so deleting it silently turns a listed capability into a
    /// failure - the same argument that pins a run something was branched from.
    /// </summary>
    void CollectModels(List<RunRecord>? known = null)
    {
        if (Disabled) return;
        //Single-flight. Eviction and an explicit delete both collect, and two passes enumerating
        //the same directory race each other into File.Delete - where the loser's exception used to
        //abandon every remaining model in that pass.
        if (Interlocked.Exchange(ref Collecting, 1) != 0) return;
        try
        {
            if (!Directory.Exists(ModelsDir)) return;
            var all = known ?? Runs.FindAll().ToList();

            //A run that used a model but cannot say WHICH is reported, not obeyed.
            //
            //This used to refuse collection outright, which was a mistake of exactly the shape the
            //budget exists to prevent: the condition has no writer that can ever clear it - nothing
            //can back-fill a hash a run never recorded - so one such row disabled collection AND the
            //model cap store-wide, permanently, while uploads kept arriving at up to 256 MB each.
            //
            //And protecting those runs protects nothing. A run with no recorded hash cannot be
            //linked to any file here: it already reports hasFieldModel false, it cannot be
            //continued or forked from its model, and its terrain cannot be rebuilt. Keeping every
            //model in the store alive on its behalf keeps bytes that run can never use. The rows
            //that genuinely matter - adopted ones - recover their hash from terrain.json in
            //Reconcile, which is the mechanism that actually protects them.
            var unaccounted = all.Count(x => !string.IsNullOrEmpty(x.FieldModelName) && string.IsNullOrEmpty(x.FieldModelHash));
            if (unaccounted > 0)
                Console.WriteLine($"RunStore: {unaccounted} run(s) name a field model without recording which one. " +
                                  $"They cannot use a stored model and do not hold one back; collecting anyway.");

            var referenced = all
                .Select(x => x.FieldModelHash)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var now = DateTime.UtcNow;
            var loose = new List<(string Path, string Hash, long Size, DateTime Touched)>();
            long referencedBytes = 0;
            foreach (var file in Directory.EnumerateFiles(ModelsDir, "*.obj.gz"))
            {
                var hash = Path.GetFileName(file);
                hash = hash[..^".obj.gz".Length];
                var info = new FileInfo(file);
                if (referenced.Contains(hash)) { referencedBytes += info.Length; continue; }
                loose.Add((file, hash, info.Length, info.LastWriteTimeUtc));
            }

            //Stage A: dead and past the grace.
            var aged = now - ModelGrace;
            var survivors = new List<(string Path, string Hash, long Size, DateTime Touched)>();
            foreach (var model in loose)
            {
                if (Stopping) return;
                if (model.Touched > aged) { survivors.Add(model); continue; }
                if (!TryDeleteModel(model.Path, model.Hash, model.Size, "unreferenced")) survivors.Add(model);
            }

            //Stage B: still over the cap, so the youngest dead models go too - oldest touched
            //first, and never one young enough to be an upload in progress.
            var total = referencedBytes + survivors.Sum(x => x.Size);
            if (total <= ModelRetentionBytes) return;

            var floor = now - ModelMinGrace;
            foreach (var model in survivors.Where(x => x.Touched <= floor).OrderBy(x => x.Touched))
            {
                if (total <= ModelRetentionBytes || Stopping) break;
                if (TryDeleteModel(model.Path, model.Hash, model.Size, "over the model budget")) total -= model.Size;
            }

            if (total > ModelRetentionBytes)
                Console.Error.WriteLine(
                    $"RunStore: field models occupy {Size(total)}, over the {Size(ModelRetentionBytes)} budget, " +
                    $"and {Size(referencedBytes)} of that is referenced by stored runs. Nothing further can be " +
                    $"collected without costing those runs their terrain - delete runs to release their models.");
        }
        catch (ObjectDisposedException) { /* the database went away: the store is shutting down */ }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: model collection failed: {e.Message}"); }
        finally { Interlocked.Exchange(ref Collecting, 0); }
    }

    /// <summary>
    /// Deletes one model, re-checking under the lock that it has not just been claimed.
    ///
    /// The re-stat is the whole point. Between building the reference set and reaching this file,
    /// an upload of the same OBJ can have found it already present and returned its hash without
    /// rewriting it - so the run about to start refers to a file this pass is about to delete, and
    /// the only evidence is the timestamp that upload touched.
    /// </summary>
    bool TryDeleteModel(string path, string hash, long size, string why)
    {
        try
        {
            lock (MetadataLock)
            {
                if (!File.Exists(path)) return true;
                if (File.GetLastWriteTimeUtc(path) > DateTime.UtcNow - ModelMinGrace) return false;
                File.Delete(path);
            }
            Console.WriteLine($"RunStore: collected field model {hash[..Math.Min(12, hash.Length)]} ({Size(size)}), {why}.");
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not collect field model {hash[..Math.Min(12, hash.Length)]}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clears half-written models left by a crash.
    ///
    /// <see cref="StoreFieldModel"/> writes beside the target and moves into place, so an
    /// interrupted upload leaves a <c>.tmp</c> that nothing references and that the collector never
    /// sees - it globs <c>*.obj.gz</c> - while <see cref="ModelBytes"/> counts it. A permanent,
    /// invisible charge against the budget.
    /// </summary>
    void CollectTempModels()
    {
        try
        {
            if (!Directory.Exists(ModelsDir)) return;
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
            foreach (var file in Directory.EnumerateFiles(ModelsDir, "*.tmp"))
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    //Under the lock and re-checked, like every other deletion here. A finished
                    //temp file waits on this same lock to be moved into place, so without the
                    //re-read the sweep can delete an upload in the instant between its last byte
                    //and its rename.
                    lock (MetadataLock)
                    {
                        if (!File.Exists(file)) continue;
                        if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        Console.WriteLine($"RunStore: removed an abandoned partial field model ({Size(size)}).");
                    }
                }
                catch { /* the next pass will try again */ }
        }
        catch (Exception e) { Console.Error.WriteLine($"RunStore: temp model sweep failed: {e.Message}"); }
    }

    /// <summary>
    /// zlib-wrapped deflate, matching what Content-Encoding: deflate means to a browser.
    ///
    /// Real V7 scene data gives about 1.7x - unremarkable, because the affine transforms and
    /// analytics blocks are nearly incompressible and it is the record headers and repeated
    /// colours that give back what little there is.
    /// </summary>
    /// <summary>
    /// The export flags out of a stored frame's header, without inflating the frame.
    ///
    /// A V7 frame opens with version, flags and plant count - six bytes - and the frames are
    /// zlib streams, so decompressing the first block is enough however large the frame is.
    /// </summary>
    static bool TryReadExportFlags(string framePath, out byte flags)
    {
        flags = 0;
        try
        {
            using var input = File.OpenRead(framePath);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            Span<byte> header = stackalloc byte[2];
            if (zlib.ReadAtLeast(header, 2, throwOnEndOfStream: false) < 2) return false;
            if (header[0] != 7) return false;
            flags = header[1];
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Stops scheduling work, without closing anything a request might still be using.
    ///
    /// Split from <see cref="Dispose"/> because of WHEN the host raises its two shutdown events.
    /// ApplicationStopping fires BEFORE the web server drains: handlers are still running, and
    /// several of them - the listing, a single run, a delete - are inside LiteDB. Disposing the
    /// database there answers those requests with an ObjectDisposedException. So Stopping does the
    /// half that is safe at that moment, and Dispose, on ApplicationStopped, does the rest.
    /// </summary>
    public void BeginShutdown()
    {
        //Flagged first, so a pass that is between steps notices and stops early.
        Stopping = true;
        //Then WAITED FOR. Timer.Dispose() only stops future callbacks; one already executing runs on
        //regardless, and the flag alone cannot help it - it is checked between steps, not inside a
        //LiteDB call. Disposing the database under that pass throws ObjectDisposedException in the
        //middle of the eviction loop, which then goes on deleting run directories whose rows it can
        //no longer remove: frames gone, rows intact, and the listing describing storage that is not
        //there. The overload that takes a wait handle signals once every callback has returned.
        var timer = Maintenance;
        Maintenance = null;
        if (timer != null)
        {
            using var finished = new ManualResetEvent(false);
            if (timer.Dispose(finished))
                //Bounded: a pass recursively deleting a 17 GB run can legitimately take a while, and
                //a shutdown that hangs on it is worse than one that leaves a directory half-removed
                //for the next startup's adoption pass to reconcile.
                finished.WaitOne(TimeSpan.FromSeconds(30));
        }
    }

    public void Dispose()
    {
        //Idempotent, and safe to reach without BeginShutdown - a test or a host that raises only
        //one of the two events still gets a clean teardown.
        BeginShutdown();

        foreach (var writer in Writers.Values)
        {
            writer.Queue.CompleteAdding();
            try { writer.Worker?.Wait(TimeSpan.FromSeconds(30)); } catch { /* shutting down */ }
        }
        Writers.Clear();
        Db?.Dispose();
    }
}
