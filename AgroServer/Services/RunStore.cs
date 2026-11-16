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
    /// <summary>Refuses new work when durable storage is unavailable or below its reserve.</summary>
    void EnsureWritable();

    /// <summary>Registers a durable new run, or throws <see cref="StorageException"/>.</summary>
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

    /// <summary>
    /// Drains the run's writer and records how it ended. <c>reason</c> says why an aborted run
    /// stopped, as a clause that reads after "stopped: "; when the store itself asked for the stop
    /// (<see cref="TryGetStopReason"/>), its own reason stands in for a null one.
    /// </summary>
    RunCompletion CompleteRun(string runId, bool aborted, string? reason = null);

    /// <summary>
    /// Why this run can no longer be stored, once that is so: its volume reached the free-space
    /// reserve, or a frame or checkpoint could not be written.
    ///
    /// The run loop asks after every step and stops the run cleanly when this is true. Carrying on
    /// would leave a run whose stored frames have a hole in them that nothing can ever fill, and
    /// whose checkpoints stop being written - while stopping now keeps the frames contiguous and
    /// the final checkpoint, written into the reserve, makes the run resumable once there is room.
    /// Two dictionary and field reads: cheap enough for the simulation thread.
    /// </summary>
    bool TryGetStopReason(string runId, out string reason);

    /// <summary>What the store did and failed to do - every deletion, every storage error - kept
    /// across restarts and shown in the Runs tab.</summary>
    RunStoreLog Log { get; }

    /// <summary>
    /// The store's state for the Runs tab: whether it works at all and why not, where it writes and
    /// how much room is left there, configuration that will lose data, and the newest events.
    /// </summary>
    Models.RunStoreStatusResponse Status(int events);

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
    /// For an active run, the caller transfers ownership of the snapshot to the bounded
    /// writer. CompleteRun drains both frames and checkpoints before returning.
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
    /// <returns>False when the run is still live, which is the one state a resume must not enter,
    /// or when its files could not be reconciled; <c>reason</c> then says which, for the user.</returns>
    bool ResumeRun(string runId, uint checkpointStep, uint totalSteps, int totalHours, string requestJson, out string reason);

    /// <summary>The serialized soil as gzipped bytes, for serving with Content-Encoding: gzip.</summary>
    bool TryReadTerrain(string runId, out byte[] gzipped);

    /// <summary>
    /// Files the run's complete leaf-area-index document, gzipped.
    ///
    /// The copy sent over the socket drops its per-obstacle weekly series above the wire budget,
    /// because hundreds of trays with a series each is more JSON than a browser should receive in
    /// one message. That budget is a property of the WIRE, not of the measurement - so the whole
    /// document is written here and fetched over REST, where size is the browser's own business
    /// and it never has to parse it at all.
    /// </summary>
    void WriteObstacleLight(string runId, byte[] utf8Json);

    /// <summary>The stored measurement as gzipped bytes, for serving with Content-Encoding: gzip.</summary>
    bool TryReadObstacleLight(string runId, out byte[] gzipped);

    /// <summary>
    /// Files an uploaded field model and returns its content hash, or throws when it cannot be
    /// stored. Identical models are stored once however many runs use them.
    /// </summary>
    string StoreFieldModel(string objSource);

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

    /// <summary>Changes automatic-eviction protection, including while a run is computing.</summary>
    RunRecord SetProtection(string runId, bool protect);

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
    /// <summary>Exempt from automatic eviction; explicit deletion remains possible.</summary>
    public bool Protected { get; set; }
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
    /// Why an aborted run stopped, as a clause that reads after "stopped: ".
    ///
    /// "aborted" alone sent people looking in the wrong place: a run stops because the user pressed
    /// stop, because nobody had been watching it for five minutes, because the server restarted,
    /// because the simulation threw - or because its volume reached the reserve and the store asked
    /// for it to stop. The first three need nothing, the last two need someone to act. Null for a
    /// run that finished, and for rows written before the reason was recorded.
    /// </summary>
    public string? StopReason { get; set; }

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
    public required BlockingCollection<(uint Step, byte[] Scene, bool Checkpoint)> Queue { get; init; }
    public required FileStream Index { get; init; }
    public Task? Worker;
    /// <summary>Written by the committer thread only, read for the periodic metadata flush.</summary>
    public uint StoredFrames;
    public uint LastStep;
    public long BytesOnDisk;
    public uint AccountedIndexEntries;
    public DateTime LastMetadataFlush = DateTime.UtcNow;
    /// <summary>
    /// Why this run can no longer be stored. Set once - the first thread to hit a problem wins,
    /// whether the simulation thread finding the reserve reached or a compressor failing a write -
    /// and read by the run loop after every step, which then stops the run.
    /// </summary>
    public string? StopReason;
    /// <summary>Frames and checkpoints that should have been stored and were not, for the summary
    /// at the end of the run. Incremented from every thread that refuses or fails one.</summary>
    public int Unstored;
    /// <summary>Whether the last progress flush to the database failed. Committer thread only; it
    /// is what makes "progress cannot be recorded" one warning and not one every two seconds.</summary>
    public bool MetadataFailing;
    /// <summary>
    /// The first step whose frame was lost, or <c>uint.MaxValue</c> while none has been.
    ///
    /// Nothing at or after it is committed, and no checkpoint after it is written, because the
    /// stored run must stay contiguous: a checkpoint describes the world after every frame before
    /// it, and resuming from one that skips a lost frame would leave a hole in the run that nothing
    /// could ever fill. Resuming from the last checkpoint before the gap recomputes those few steps
    /// instead, and the run carries on whole.
    /// </summary>
    public uint GapFromStep = uint.MaxValue;
    /// <summary>The step of the last checkpoint committed, for the summary: where a resume starts.</summary>
    public uint CheckpointStep;
}

public class RunStore : IRunStore, IDisposable
{
    readonly string Root;
    readonly LiteDatabase Db = null!;
    readonly ILiteCollection<RunRecord> Runs = null!;
    readonly ConcurrentDictionary<string, RunWriter> Writers = new();

    public RunStoreLog Log { get; }

    /// <summary>
    /// Outcomes <see cref="CompleteRun"/> could not write to the database, kept until a maintenance
    /// pass manages to. Without them a run that finished while the disk was full would be listed -
    /// and later recorded - as aborted, because a row still saying "running" with no writer behind
    /// it can only be read as a run that was interrupted.
    /// </summary>
    readonly ConcurrentDictionary<string, (string Status, string? Reason)> UnrecordedOutcomes = new();

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
    // Protection is overlaid independently: adding it must not rescan historical frame files.
    const int CurrentMetadataVersion = 4; // reconcile actual tick duration on historical rows

    /// <summary>
    /// Frames deflated concurrently per run.
    ///
    /// Deflate was the largest single term of storing a frame and it overtook the simulation step
    /// itself between 400 and 1,600 plants, at which point the single writer's full queue stalled
    /// the simulation thread. The compressions are independent, so several run at once; the
    /// commits - file move, index entry, counts - still happen in frame order on one thread, so
    /// the index stays monotonic. A quarter of the cores, because the simulation wants the rest.
    /// </summary>
    public int Compressors { get; set; } = DefaultCompressors;
    static readonly int DefaultCompressors = Math.Clamp(Environment.ProcessorCount / 4, 2, 8);
    /// <summary>
    /// Frames that may sit between the simulation and the disk.
    ///
    /// Small on purpose. Each slot holds a whole exported frame - tens of megabytes at production
    /// plant count - so this is a memory budget, not a throughput knob: enough to keep every
    /// compressor busy and one frame in hand, no more. If the compressors cannot keep up, the
    /// simulation waits, which is the correct failure.
    /// </summary>
    public int QueueDepth { get; set; } = 2 * DefaultCompressors + 2;
    /// <summary>Global raw-frame budget, including frames currently being compressed. A single
    /// frame or checkpoint larger than the budget is allowed only while no other snapshot is pending.
    /// Scaled with the compressors, or a deeper queue would simply block on the budget instead.</summary>
    public long PendingFrameByteBudget { get; set; } = (128L << 20) * DefaultCompressors;
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
    /// Total run footprint above which unprotected, inactive runs are evicted, oldest first.
    /// Protected runs remain stored even above this budget; the free-space reserve stops new work.
    /// </summary>
    public long RetentionBytes { get; set; } = 64L * 1024 * 1024 * 1024;

    /// <summary>
    /// Free space kept on the store's volume. A run that would eat into it is stopped cleanly.
    ///
    /// The retention budget alone cannot protect the disk: it is a number somebody typed, and the
    /// deployed one was a terabyte on a volume that filled at 56 GB. Past that point every write
    /// failed - frames, the metadata database, Docker's own log of the errors - and the store
    /// disabled itself at the next start. So the store watches the volume itself. Reaching the
    /// reserve stops the run with a final checkpoint, which may use up to three quarters of the
    /// reserve; the rest is what keeps the database and the event log writable, so the stop can be
    /// recorded and the run resumed once there is room. Zero turns the guard off.
    /// </summary>
    public long MinFreeBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// True when the store could not be opened. Reads report it unavailable and new work is refused.
    ///
    /// The constructor used to let its exceptions escape, and it is called from a top-level
    /// statement before the host is built - so an unwritable root took the entire server down
    /// before Kestrel ever listened. That is the deployed case, not a hypothetical: the image
    /// copies a root-owned /app and then drops to an unprivileged user, which cannot create
    /// /app/runstore. Reads and diagnosis must remain available even when new work is refused.
    /// </summary>
    public bool Disabled { get; }

    /// <summary>
    /// Why the store is disabled, and what to do about it. Empty while it works.
    ///
    /// A disabled store used to be indistinguishable from an empty one: the listing answered an
    /// empty array, the panel printed "No stored runs", and the one line that said why went to a
    /// log on the disk that had just filled up.
    /// </summary>
    public string DisabledReason { get; } = "";

    // LiteDB 5 discovers this model through reflection. Direct calls to its constructor and
    // accessors do not keep their reflection metadata when publishing with trimming/AOT.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties, typeof(RunRecord))]
    public RunStore(string root)
    {
        Root = root;
        //First, and outside every try: the log must exist whatever else fails, because a store that
        //cannot open is precisely what it has to report.
        Log = new RunStoreLog(Path.Combine(root, "events.jsonl"));

        //Only OPENING is fatal. Everything after it is housekeeping that writes, and on a full disk
        //it fails - which used to disable the whole store, listing and deleting included, so a user
        //could not even delete a run to make room.
        var openingDatabase = false;
        try
        {
            Directory.CreateDirectory(RunsDir);
            Directory.CreateDirectory(ModelsDir);
            openingDatabase = true;
            //Shared connection: the simulation loop writes from a background thread while HTTP
            //requests read, and LiteDB's default Direct mode allows a single process handle only.
            //
            //AutoRebuild because LiteDB marks a datafile whose last write failed - a full disk, a
            //killed process - as being in an invalid state, and refuses to open it again until it
            //is rebuilt. Without this the store stayed disabled after the space was freed.
            Db = new LiteDatabase(new ConnectionString
            {
                Filename = Path.Combine(Root, "index.litedb"),
                Connection = ConnectionType.Shared,
                AutoRebuild = true,
            });
            Runs = Db.GetCollection<RunRecord>("runs");
            //A read, so that a database which cannot be opened fails HERE, as a disabled store with
            //a reason, rather than later as an exception inside some request.
            _ = Runs.Count();
        }
        catch (Exception e)
        {
            Disabled = true;
            DisabledReason = $"Cannot use '{Path.GetFullPath(root)}': {Explain(e, openingDatabase)}";
            Log.Error("store-disabled", null,
                $"The run store is disabled. {DisabledReason} New simulations and uploads are refused; " +
                $"stored runs cannot be listed, and the server must be restarted once " +
                $"this is fixed.");
            return;
        }

        //Partial files first: on a full volume they are what is holding the last free blocks, and
        //removing them may be what lets the writes below succeed.
        RemoveStaleTemporaries();
        try { Runs.EnsureIndex(x => x.CreatedUtc); }
        catch (Exception e) { Log.Warning("index-failed", null, $"Could not index the runs by date ({e.Message}). Listings still work, more slowly."); }
        SweepStaleRuns(atStartup: true);
        AdoptOrphanedRuns();
        // Legacy descriptions are maintained by the server, never committed as a side effect of
        // an anonymous GET. Current records need no frame-directory scan on upgrade.
        try
        {
            foreach (var run in Runs.Query().Where(x => x.MetadataVersion < CurrentMetadataVersion).ToList())
                Reconcile(run);
        }
        catch (Exception e) { Log.Error("reconcile-failed", null, $"Could not update legacy run descriptions at startup: {e.Message}"); }
    }

    /// <summary>
    /// A storage exception in words, with what to do about it. Used for the reason a store is
    /// disabled, which is the first thing anybody reads when runs have gone missing.
    ///
    /// <c>openingDatabase</c> says the failure came out of LiteDB rather than the directories, where
    /// anything that is not an I/O or permission error means the file itself is unreadable - and
    /// LiteDB does not always say so with a LiteException: a datafile of garbage was measured to
    /// throw an ArgumentOutOfRangeException about DateTime ticks.
    /// </summary>
    static string Explain(Exception e, bool openingDatabase)
    {
        var message = e.Message.TrimEnd('.', ' ');
        //28 is ENOSPC and 122 EDQUOT: .NET puts the errno in HResult for a plain IOException.
        if (e is IOException && (e.HResult is 28 or 122
            || message.Contains("No space", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)))
            return $"{message}. The volume is full: free space on it - the stale *.tmp files under " +
                   $"runs/ first - or move the store to a larger volume.";
        if (e is UnauthorizedAccessException || e.HResult is 13 or 30
            || message.Contains("read-only", StringComparison.OrdinalIgnoreCase))
            return $"{message}. The server cannot write there: the directory must be writable by the " +
                   $"server's user (uid 61000 in the image, which docker-compose also gives group 1001).";
        if (e is LiteException || (openingDatabase && e is not IOException))
            return $"{message}. The run database is damaged. Stop the server, move index.litedb (and " +
                   $"index-log.litedb, if present) aside and start it again: every run directory is " +
                   $"adopted back from disk.";
        return message;
    }

    /// <summary>
    /// Records "aborted" for rows still saying "running" that nothing is writing.
    ///
    /// At startup that is every such row: nothing can still be running in a fresh process, and a
    /// record left at "running" by a crash or a redeploy is otherwise never evictable - Evict skips
    /// running rows while still counting their bytes - so a few interrupted long runs make the
    /// budget permanently unreachable. Afterwards it is the rows whose outcome
    /// <see cref="CompleteRun"/> could not write, which a maintenance pass records late.
    ///
    /// Never fatal. On a full disk the write fails, and the listing reports those rows as aborted
    /// anyway (<see cref="Overlay"/>), which is what lets the user delete one to make room.
    /// </summary>
    void SweepStaleRuns(bool atStartup)
    {
        try
        {
            var swept = new List<RunRecord>();
            lock (MetadataLock)
            {
                foreach (var run in Runs.Find(x => x.Status == "running").ToList())
                {
                    if (IsLive(run.Id)) continue;
                    if (UnrecordedOutcomes.TryRemove(run.Id, out var outcome))
                    {
                        run.Status = outcome.Status;
                        run.StopReason = outcome.Reason;
                    }
                    else
                    {
                        run.Status = "aborted";
                        run.StopReason ??= atStartup
                            ? "the server stopped while it was computing (a restart, a redeploy or a crash)"
                            : "its outcome was never recorded";
                    }
                    run.UpdatedUtc = DateTime.UtcNow;
                    Runs.Update(run);
                    swept.Add(run);
                }
            }
            foreach (var run in swept)
                Log.Warning("stale-aborted", run.Id,
                    $"Run {run.Id} was still marked running with nothing computing it; recorded as {run.Status}" +
                    (run.Status == "aborted" ? $": {run.StopReason}." : "."),
                    Footprint(run));
            Log.Condition("stale-sweep", false, "stale-sweep-failed", () => "",
                cleared: () => "Runs left marked 'running' are recorded as aborted again.");
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (Exception e)
        {
            Log.Condition("stale-sweep", true, "stale-sweep-failed", () =>
                $"Could not record runs left marked 'running' as aborted ({e.Message}). They are listed " +
                $"as aborted and can be deleted anyway; each maintenance pass tries again.");
        }
    }

    /// <summary>
    /// Deletes the partial files interrupted writes leave behind. Called at startup only, when
    /// nothing can be writing.
    ///
    /// Every frame is deflated into a <c>.tmp</c> beside its final name, and a write that fails -
    /// above all on a full disk - used to leave it there: one per frame the run tried to store,
    /// each as large as the space it found, with several compressors at it at once. That debris is
    /// what held the last free blocks of the deployed volume. Nothing else ever removed it, and
    /// Reconcile counted it as frames.
    /// </summary>
    void RemoveStaleTemporaries()
    {
        long bytes = 0;
        var count = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(RunsDir))
                foreach (var where in new[] { dir, Path.Combine(dir, "frames") })
                {
                    if (!Directory.Exists(where)) continue;
                    foreach (var file in Directory.EnumerateFiles(where, "*.tmp"))
                        try
                        {
                            var length = new FileInfo(file).Length;
                            File.Delete(file);
                            bytes += length;
                            ++count;
                        }
                        catch { /* the next start tries again */ }
                }
        }
        catch (Exception e) { Log.Error("temp-cleanup-failed", null, $"Could not look for partial files left by interrupted writes: {e.Message}"); }
        if (count > 0)
            Log.Info("temp-removed", null,
                $"Removed {count} partial file{(count == 1 ? "" : "s")} ({Size(bytes)}) left by writes that were " +
                $"interrupted - by a full disk or a crash.", bytes);
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
                //One event per run: adoption is how a store moved between volumes by hand comes
                //back, so each run that reappears is worth seeing by name and size.
                Log.Info("adopted", id,
                    $"Adopted run {id} ({Size(Footprint(run))}, {run.StoredFrames} frames): its directory was on " +
                    $"disk but not in the database.", Footprint(run));
            }
        }
        catch (Exception e) { Log.Error("adopt-failed", null, $"Could not scan for run directories the database does not know: {e.Message}"); }
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
    RunRecord Reconcile(RunRecord run, bool insert = false)
    {
        lock (MetadataLock)
        {
        //Listings can have fetched this row before a protection change or writer flush. Re-read
        //under the same lock as those updates before replacing the complete document.
        if (!insert)
        {
            var current = Runs.FindById(run.Id);
            if (current == null) return run;
            run = current;
        }
        run.Protected = ReadProtection(run.Id);
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
                    //Frames only. A partial .tmp left by a failed write is not a frame, and counting
                    //it made a run that had filled its disk look larger than what it had stored.
                    if (Path.GetExtension(file) is not (".g" or ".z")) continue;
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
                    // Historical requests were narrowed before snapping. Preserve what actually
                    // ran (including old overflowing inputs); new requests store the resolved tick.
                    var legacyHours = unchecked((ushort)Math.Max(0, request.HoursPerTick ?? 1));
                    run.HoursPerTick = Agro.AgroWorld.SnapHoursPerTick(Math.Max(1, (int)legacyHours));
                    run.TotalHours = request.TotalHours ?? 0;
                    //Sliced, not formatted: the stored request holds a C# DateTime literal
                    //"2026-02-01T00:00:00", and its first ten characters ARE the day.
                    run.InitialDay = request.InitialDate?.ToString("yyyy-MM-dd") ?? "";
                    run.Seed = request.Seed ?? 42UL;
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
            Log.Error("reconcile-failed", run.Id, $"Could not read back the description of run {run.Id}: {e.Message}");
            //Stamped anyway. A request.json that cannot be parsed will not parse on the next
            //listing either, and retrying it for every listing forever costs more than the blanks.
            try { run.MetadataVersion = CurrentMetadataVersion; if (insert) Runs.Insert(run); else Runs.Update(run); }
            catch { /* best effort */ }
        }
        return run;
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
    string ProtectionPath(string runId) => Path.Combine(RunDir(runId), "protection.json");
    string ModelPath(string hash) => Path.Combine(ModelsDir, $"{hash}.obj.gz");

    /// <summary>The sidecar is authoritative even if the database is rebuilt or a cache update fails.</summary>
    bool ReadProtection(string runId)
    {
        try
        {
            //Read directly: File.Exists also returns false on permission errors, which must never
            //silently turn a protected run into an eviction candidate.
            var value = File.ReadAllText(ProtectionPath(runId)).Trim();
            var protect = value switch
            {
                "true" => true,
                "false" => false,
                _ => throw new InvalidDataException("Expected a JSON boolean in protection.json."),
            };
            Log.Condition("protection-" + runId, false, "protection-unreadable", () => "");
            return protect;
        }
        catch (FileNotFoundException) { return false; } // runs created before this feature
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception e)
        {
            Log.Condition("protection-" + runId, true, "protection-unreadable",
                () => $"Run {runId}: its protection setting cannot be read ({e.Message}). Keeping it protected from automatic deletion.",
                runId: runId);
            return true;
        }
    }

    long WriteProtection(string runId, bool protect) =>
        WriteAtomically(ProtectionPath(runId), output => output.Write(protect ? "true\n"u8 : "false\n"u8));

    public RunRecord SetProtection(string runId, bool protect)
    {
        if (Disabled) throw new StorageException($"The run store is disabled. {DisabledReason}");
        if (Stopping) throw new StorageException("The run store is shutting down.");
        if (!IsSafeId(runId)) throw new KeyNotFoundException("No such run.");
        lock (MetadataLock)
        {
            var run = Runs.FindById(runId);
            if (run == null || !Directory.Exists(RunDir(runId))) throw new KeyNotFoundException("No such run.");
            try
            {
                var path = ProtectionPath(runId);
                var previous = File.Exists(path) ? new FileInfo(path).Length : 0;
                var written = WriteProtection(runId, protect);
                run.Protected = protect;
                run.AuxBytesOnDisk += written - previous;
                // Keep simulation duration intact: changing retention is not another tick.
            }
            catch (Exception e)
            {
                throw StorageException.From($"Run {runId}: its protection could not be saved; the previous setting remains in effect. {e.Message}", e, runId);
            }

            //The sidecar has committed, so the requested state is durable and effective even when
            //LiteDB is full. ReadProtection supplies it to every listing and eviction decision.
            try { Runs.Update(run); }
            catch (Exception e) { Log.Warning("protection-cache-failed", runId, $"Run {runId}: protection was saved, but its database cache could not be updated ({e.Message})."); }
            Log.Info(protect ? "protected" : "unprotected", runId,
                $"Run {runId} is now {(protect ? "protected from" : "eligible for")} automatic deletion.");
            return Overlay(run);
        }
    }

    /// <summary>Files how this run's terrain was built, beside the run.</summary>
    void WriteTerrainMeta(string runId, RunSpec spec)
    {
        if (string.IsNullOrEmpty(spec.FieldModelHash) && string.IsNullOrEmpty(spec.FieldModelName)) return;
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Models.StoredTerrainMeta(spec.FieldModelHash, spec.FieldModelName, spec.FieldItemRegex,
                                         spec.FieldItemRegexMaterial, spec.FieldResolution),
            Models.AppJsonSerializerContext.Default.StoredTerrainMeta);
        WriteAtomically(TerrainMetaPath(runId), output => output.Write(bytes));
    }

    /// <summary>
    /// Writes beside the target and moves into place, and returns the bytes written.
    ///
    /// A file overwritten in place is truncated first and filled second, so a full disk or a crash
    /// in between leaves it empty or cut short under its real name - and for request.json that is
    /// permanent: a run whose request does not parse can never be resumed or forked again. The
    /// temporary is removed when the write fails, so a failure costs no space either.
    /// </summary>
    static long WriteAtomically(string path, Action<Stream> write)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            long length;
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(output);
                output.Flush(flushToDisk: true);
                length = output.Length;
            }
            File.Move(temporary, path, overwrite: true);
            return length;
        }
        catch
        {
            try { File.Delete(temporary); } catch { /* the original failure is the one to report */ }
            throw;
        }
    }

    /// <summary>Rejects anything that is not a plain id, so a path cannot escape the store.</summary>
    static bool IsSafeId(string id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>
    /// Whether something is still writing this run - the one test of liveness this store trusts.
    ///
    /// The row's status cannot be it. A row says "running" from the moment it is inserted until
    /// <see cref="CompleteRun"/> manages to write the outcome, and on a full disk that write fails:
    /// the run that filled the volume then stayed "running" forever, which made it undeletable from
    /// the panel and unevictable by the budget - the two ways of getting the space back. A writer
    /// exists for exactly as long as frames can arrive, so the row is inserted after the writer
    /// starts, and the writer is removed under the same lock that records the outcome.
    /// </summary>
    bool IsLive(string runId) => Writers.ContainsKey(runId);

    /// <summary>Everything a run occupies: frames, then the request, index, terrain and checkpoint.</summary>
    static long Footprint(RunRecord run) => run.BytesOnDisk + run.AuxBytesOnDisk;

    public void EnsureWritable()
    {
        if (Disabled) throw new StorageException($"The run store is disabled. {DisabledReason}");
        if (Stopping) throw new StorageException("The run store is shutting down.");
        var free = StoreVolume.FreeSpace(Root);
        if (MinFreeBytes > 0 && free >= 0 && free - Volatile.Read(ref PendingFrameBytes) < MinFreeBytes)
            throw new StorageException($"Cannot accept new work: {LowSpaceReason(free)}.", insufficientSpace: true);
        try
        {
            //Check actual permissions as well as free space. DeleteOnClose leaves no preflight
            //files behind if the process terminates between the write and close.
            using var probe = new FileStream(Path.Combine(Root, $".writable-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            probe.Flush(flushToDisk: true);
            _ = Runs.Count();
        }
        catch (Exception e) { throw StorageException.From($"The run store cannot accept new work: {e.Message}", e); }
    }

    public string BeginRun(RunSpec spec)
    {
        EnsureWritable();
        var id = Guid.NewGuid().ToString("N");
        var inserted = false;
        try
        {
            lock (MetadataLock)
            {
            if (!string.IsNullOrEmpty(spec.FieldModelHash) && !HasFieldModel(spec.FieldModelHash))
                throw new StorageException("The original field model is not stored; upload it again before starting this run.");
            Directory.CreateDirectory(FramesDir(id));
            //The request is what a resumed or branched run needs to rebuild its world, and the
            //upload and terrain caches that hold it today expire after five minutes.
            WriteAtomically(RequestPath(id), output => output.Write(Encoding.UTF8.GetBytes(spec.RequestJson)));
            //And beside it, how the terrain was actually built. The database is not the only copy
            //of this: a store whose index.litedb is lost is adopted from its directories, and a run
            //adopted without its model hash contributes nothing to the reference set that decides
            //which models to keep - so the next eviction would delete every model the store owns.
            WriteTerrainMeta(id, spec);
            WriteProtection(id, protect: true);

            //The writer BEFORE the row: liveness is the writer (IsLive), so a row that says
            //"running" must never exist without one - Evict, just below, would take it for a run
            //that is already over.
            StartWriter(id);
            Runs.Insert(new RunRecord
            {
                Id = id,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                Protected = true,
                Status = "running",
                HoursPerTick = spec.HoursPerTick,
                TotalSteps = spec.TotalSteps,
                Analytics = spec.Analytics,
                Deterministic = spec.Deterministic,
                AuxBytesOnDisk = Directory.EnumerateFiles(RunDir(id)).Sum(path => new FileInfo(path).Length),
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
            inserted = true;
            Log.Info("run-started", id,
                $"Run {id} started: {spec.Plants} plant{(spec.Plants == 1 ? "" : "s")}, {spec.TotalSteps} steps of " +
                $"{spec.HoursPerTick} h{(spec.ParentRunId != null ? $", derived from {spec.ParentRunId}" : "")}.");
            }
            Evict();
        }
        catch (Exception e)
        {
            Log.Error("begin-failed", id, $"Run {id} was not started: it could not be stored ({e.Message}).");
            AbandonBegin(id, inserted);
            throw StorageException.From($"The run was not started because it could not be stored: {e.Message}", e, id);
        }
        return id;
    }

    /// <summary>
    /// Undoes a <see cref="BeginRun"/> that failed half way, so nothing it made outlives it.
    ///
    /// A writer left behind would keep the run "live" forever; a directory left behind is adopted at
    /// the next start as a run with no frames; a row left behind is a run that never ran.
    /// </summary>
    void AbandonBegin(string id, bool inserted)
    {
        AbandonWriter(id);
        if (inserted) try { Runs.Delete(id); } catch { /* the next start sweeps it to aborted */ }
        try { if (Directory.Exists(RunDir(id))) Directory.Delete(RunDir(id), true); } catch { /* adopted later, empty */ }
    }

    public void WriteFrame(string runId, uint step, byte[] scene)
    {
        if (Disabled || !IsSafeId(runId)) return;
        if (!Writers.TryGetValue(runId, out var writer)) return;
        //Once storing has had to stop, nothing more is queued. The run loop reads the reason right
        //after this call and stops the run; a frame queued now would only be refused at the floor.
        if (Volatile.Read(ref writer.StopReason) != null) { LoseFrame(writer, step); return; }
        if (!HasRoomFor(scene.LongLength, out var free))
        {
            StopStoring(writer, LowSpaceReason(free), "low-space-stop", step, "warning");
            //The frame that reached the reserve is still kept if it fits above the floor. It is the
            //state the run stops in, and the final checkpoint taken right after it describes exactly
            //that state - so a resume continues with no gap in the stored frames.
            if (free - scene.LongLength < MinFreeBytes / 4) { LoseFrame(writer, step); return; }
        }
        if (!ReserveFrameBytes(writer, scene.LongLength)) return;
        var queued = false;
        try
        {
            //Blocks when the queue is full, which is the backpressure. Everything expensive -
            //the deflate above all - happens on the worker, so the simulation thread pays only
            //for handing over a reference.
            writer.Queue.Add((step, scene, false));
            queued = true;
        }
        catch (InvalidOperationException) { /* the run finished; nothing more is accepted */ }
        finally { if (!queued) ReleaseFrameBytes(scene.LongLength); }
    }

    public bool TryGetStopReason(string runId, out string reason)
    {
        reason = "";
        if (!Writers.TryGetValue(runId, out var writer) || Volatile.Read(ref writer.StopReason) is not { } why) return false;
        reason = why;
        return true;
    }

    /// <summary>
    /// Counts a frame that will not be stored and moves the gap back to it if it is the earliest:
    /// from here on nothing later is committed and no later checkpoint written (see
    /// <see cref="RunWriter.GapFromStep"/>).
    /// </summary>
    static void LoseFrame(RunWriter writer, uint step)
    {
        Interlocked.Increment(ref writer.Unstored);
        lock (writer)
            if (step < writer.GapFromStep) writer.GapFromStep = step;
    }

    /// <summary>
    /// Records that this run can no longer be stored and why, once - whichever thread gets there
    /// first. The run loop picks it up after the current step and stops the run.
    /// </summary>
    void StopStoring(RunWriter writer, string reason, string kind, uint step, string level)
    {
        if (Interlocked.CompareExchange(ref writer.StopReason, reason, null) != null) return;
        var message = $"Run {writer.RunId}: storing stopped at step {step} because {reason}. The run is being " +
                      $"stopped with a final checkpoint, and can be resumed from it once this is fixed.";
        if (level == "error") Log.Error(kind, writer.RunId, message);
        else Log.Warning(kind, writer.RunId, message);
    }

    /// <summary>Why the reserve was reached, as a clause, with what to do about it.</summary>
    string LowSpaceReason(long free) =>
        $"the run store's volume has only {Size(Math.Max(0, free))} free, below the {Size(MinFreeBytes)} reserve " +
        $"(delete runs or free space on {Root}, then resume)";

    /// <summary>Free space, read at most once a second. The simulation thread asks every frame.</summary>
    long FreeBytesCached()
    {
        var now = Environment.TickCount64;
        var at = Volatile.Read(ref FreeMeasuredAt);
        if (at != 0 && now - at < 1000) return Volatile.Read(ref MeasuredFree);
        var free = StoreVolume.FreeSpace(Root);
        Volatile.Write(ref MeasuredFree, free);
        Volatile.Write(ref FreeMeasuredAt, now);
        return free;
    }
    long MeasuredFree = -1, FreeMeasuredAt;

    /// <summary>
    /// Whether a frame of this many raw bytes fits above the reserve, counting what is already
    /// queued or being compressed. Raw sizes are an upper bound on what deflate writes.
    /// </summary>
    bool HasRoomFor(long bytes, out long free)
    {
        free = -1;
        if (MinFreeBytes <= 0) return true;
        free = FreeBytesCached();
        //Unknown is not full. A volume that will not say how much space it has is not refused.
        if (free < 0) return true;
        return free - Volatile.Read(ref PendingFrameBytes) - bytes >= MinFreeBytes;
    }

    /// <summary>Raw bytes compressors are writing right now, which statvfs does not see yet.</summary>
    long WritingBytes;

    /// <summary>
    /// The exact check, taken by a compressor just before it writes: a fresh statvfs against a
    /// quarter of the reserve, counting every write in flight. The simulation thread's check is
    /// a second old and cannot see several compressors writing at once; this one can, and it is
    /// what keeps the last part of the reserve for the database and the event log.
    /// </summary>
    bool BeginWrite(long bytes, out long free)
    {
        free = -1;
        var writing = Interlocked.Add(ref WritingBytes, bytes);
        if (MinFreeBytes <= 0) return true;
        free = StoreVolume.FreeSpace(Root);
        if (free < 0 || free - writing >= MinFreeBytes / 4) return true;
        Interlocked.Add(ref WritingBytes, -bytes);
        return false;
    }

    void EndWrite(long bytes) => Interlocked.Add(ref WritingBytes, -bytes);

    public void WriteTerrain(string runId, byte[] serializedTerrain)
    {
        if (Disabled) throw new StorageException($"The run store is disabled. {DisabledReason}", runId: runId);
        if (!IsSafeId(runId) || serializedTerrain == null || serializedTerrain.Length == 0)
            throw new StorageException("No valid terrain was supplied to the run store.", runId: runId);
        if (!BeginWrite(serializedTerrain.LongLength, out var free))
            throw new StorageException($"The terrain could not be stored: {LowSpaceReason(free)}.", insufficientSpace: true, runId: runId);
        try
        {
            var stored = WriteAtomically(TerrainPath(runId), output =>
            {
                using var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
                gz.Write(serializedTerrain, 0, serializedTerrain.Length);
            });
            AddAuxBytes(runId, stored);
        }
        catch (Exception e)
        {
            Log.Error("terrain-failed", runId, $"Run {runId}: could not store its terrain ({e.Message}).");
            throw StorageException.From($"Run {runId}: its terrain could not be stored: {e.Message}", e, runId);
        }
        finally { EndWrite(serializedTerrain.LongLength); }
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
            Queue = new BlockingCollection<(uint, byte[], bool)>(QueueDepth),
            //Opened ONCE for the run. Reopening it per frame was a file open, append and close on
            //the simulation thread for eight bytes.
            Index = new FileStream(IndexPath(runId), FileMode.Append, FileAccess.Write, FileShare.Read),
        };
        Writers[runId] = writer;
        writer.Worker = Task.Factory.StartNew(() => Drain(writer), TaskCreationOptions.LongRunning);
    }

    /// <summary>A frame deflated into its temporary file, waiting for its turn to be committed.</summary>
    readonly record struct PackedFrame(uint Step, string Path, string Temporary, long StoredBytes, bool Checkpoint = false);

    /// <summary>
    /// Deflates one queued snapshot on a pool thread into a temporary file next
    /// to its final name and is moved into place by the committer, in order - checkpoints too, so
    /// an earlier checkpoint can never overwrite a later one. The raw bytes are released as soon
    /// as the compression is done, which is when they stop being needed.
    /// </summary>
    PackedFrame? Pack(RunWriter writer, uint step, byte[] scene, bool checkpoint)
    {
        try
        {
            if (checkpoint) return PackCheckpoint(writer.RunId, step, scene, writer) is { } packedCheckpoint ? packedCheckpoint with { Checkpoint = true } : null;
            if (!BeginWrite(scene.LongLength, out var free))
            {
                LoseFrame(writer, step);
                StopStoring(writer, LowSpaceReason(free), "low-space-stop", step, "warning");
                return null;
            }
            var path = FramePath(writer.RunId, step);
            var temporary = path + ".tmp";
            try
            {
                long storedBytes;
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
                        zlib.Write(scene);
                    storedBytes = output.Length;
                }
                return new(step, path, temporary, storedBytes);
            }
            catch
            {
                //Never left behind. On a full disk each failed frame used to leave its partial
                //temporary in place - as large as the space it found - and with several compressors
                //failing at once that debris is what held the volume full.
                try { File.Delete(temporary); } catch { /* reported below; swept at the next start */ }
                throw;
            }
            finally { EndWrite(scene.LongLength); }
        }
        catch (Exception e)
        {
            //Frames only: PackCheckpoint reports its own failures and never throws.
            LoseFrame(writer, step);
            StopStoring(writer, $"frame {step} could not be written ({e.Message})", "frame-failed", step, "error");
            return null;
        }
        finally { ReleaseFrameBytes(scene.LongLength); }
    }

    void Drain(RunWriter writer)
    {
        //Compressions fan out over the pool, bounded by the gate; the commits queue keeps their
        //order, and one committer applies them in that order.
        using var gate = new SemaphoreSlim(Math.Max(1, Compressors));
        using var commits = new BlockingCollection<Task<PackedFrame?>>(Math.Max(1, QueueDepth));
        var committer = Task.Factory.StartNew(() => Commit(writer, commits), TaskCreationOptions.LongRunning);
        try
        {
            foreach (var (step, scene, checkpoint) in writer.Queue.GetConsumingEnumerable())
            {
                gate.Wait();
                var packing = Task.Run(() =>
                {
                    try { return Pack(writer, step, scene, checkpoint); }
                    finally { gate.Release(); }
                });
                commits.Add(packing);
            }
        }
        catch (Exception e)
        {
            StopStoring(writer, $"its frame writer stopped ({e.Message})", "writer-failed", writer.LastStep, "error");
            Console.Error.WriteLine($"RunStore: writer for {writer.RunId} stopped: {e}");
        }
        finally
        {
            writer.Queue.CompleteAdding();
            while (writer.Queue.TryTake(out var abandoned)) ReleaseFrameBytes(abandoned.Scene.LongLength);
            commits.CompleteAdding();
            try { committer.Wait(); }
            catch (Exception e)
            {
                StopStoring(writer, $"its frame committer failed ({e.Message})", "writer-failed", writer.LastStep, "error");
                Log.Error("writer-failed", writer.RunId, $"Run {writer.RunId}: its frame committer failed: {e.Message}");
                Console.Error.WriteLine($"RunStore: committer for {writer.RunId} failed: {e}");
            }
            TryFlushMetadata(writer);
            try { writer.Index.Dispose(); } catch { /* best effort */ }
        }
    }

    /// <summary>Moves each packed frame into place and appends its index entry, in queue order.</summary>
    void Commit(RunWriter writer, BlockingCollection<Task<PackedFrame?>> commits)
    {
        foreach (var packing in commits.GetConsumingEnumerable())
        {
            CommitOne(writer, packing);
            //Metadata on a timer rather than per frame. It is only a progress figure, and LiteDB in
            //Shared mode takes a cross-process mutex for each update - 0.7 ms a frame measured, which
            //is small but entirely pointless a thousand times over. On the cadence whatever happened
            //to the item, so a run whose frames are failing still reports what it did store.
            if (DateTime.UtcNow - writer.LastMetadataFlush > MetadataInterval)
                TryFlushMetadata(writer);
        }
    }

    void CommitOne(RunWriter writer, Task<PackedFrame?> packing)
    {
        PackedFrame? packed;
        try { packed = packing.Result; }
        catch (Exception e)
        {
            //Pack catches everything it can throw; reaching this means the task itself failed, and
            //the frame it carried is gone. Its step is unknown, so the gap starts at the frame after
            //the last one committed - which loses nothing that was stored.
            LoseFrame(writer, writer.LastStep);
            StopStoring(writer, $"a frame could not be compressed ({e.Message})", "frame-failed", writer.LastStep, "error");
            return;
        }
        if (packed is not { } frame) return; // refused or failed, and already reported

        //Past the gap nothing is committed: frames after a lost one, and a checkpoint that would
        //claim the lost frame was stored. Resuming from the last checkpoint before it recomputes the
        //missing steps instead, and keeps the stored run whole.
        var gap = Volatile.Read(ref writer.GapFromStep);
        if (frame.Checkpoint ? frame.Step > gap : frame.Step >= gap)
        {
            try { File.Delete(frame.Temporary); } catch { /* swept at the next start */ }
            if (!frame.Checkpoint) Interlocked.Increment(ref writer.Unstored);
            return;
        }

        if (frame.Checkpoint) { CommitCheckpoint(writer.RunId, frame, writer); return; }
        var step = frame.Step;
        try
        {
            File.Move(frame.Temporary, frame.Path, overwrite: true);
            var storedBytes = frame.StoredBytes;

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
            //Written for the listing's thread, which reads the live figure (Overlay).
            Volatile.Write(ref writer.BytesOnDisk, writer.BytesOnDisk + storedBytes);
        }
        catch (Exception e)
        {
            try { File.Delete(frame.Temporary); } catch { /* moved already, or swept at the next start */ }
            LoseFrame(writer, step);
            StopStoring(writer, $"frame {step} could not be committed ({e.Message})", "frame-failed", step, "error");
        }
    }

    /// <summary>
    /// <see cref="FlushMetadata"/> with its failure reported once while it lasts, not every two
    /// seconds. Frames are still stored meanwhile, and the listing reads the live writer's figures,
    /// so a database that cannot be written costs the record of progress and nothing else.
    /// </summary>
    void TryFlushMetadata(RunWriter writer)
    {
        try
        {
            FlushMetadata(writer);
            if (writer.MetadataFailing)
            {
                writer.MetadataFailing = false;
                Log.Info("metadata-failed", writer.RunId, $"Run {writer.RunId}: its progress is being recorded in the database again.");
            }
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (Exception e)
        {
            if (writer.MetadataFailing) return;
            writer.MetadataFailing = true;
            Log.Error("metadata-failed", writer.RunId,
                $"Run {writer.RunId}: its progress could not be recorded in the database ({e.Message}). Its frames are " +
                $"still being stored, and the listing shows the live figures.");
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

    public RunCompletion CompleteRun(string runId, bool aborted, string? reason = null)
    {
        if (Disabled) throw new StorageException($"The run store is disabled. {DisabledReason}", runId: runId);
        if (!IsSafeId(runId)) throw new StorageException("Not a run id.", runId: runId);
        RunWriter? writer = null;
        var removed = false;
        try
        {
            //Drain before recording the outcome, so a completed run's metadata describes every
            //frame it actually stored rather than whatever the last periodic flush had seen.
            if (Writers.TryGetValue(runId, out writer))
            {
                writer.Queue.CompleteAdding();
                // The final checkpoint shares the bounded writer. Completion must not
                // publish a resumable run before that snapshot has reached disk.
                writer.Worker?.GetAwaiter().GetResult();
            }
            //A compressor can fail after the simulation has queued its final frame/checkpoint.
            //Only the drained writer knows whether that apparently complete run was kept.
            if (writer?.StopReason is { } storageReason)
            {
                aborted = true;
                reason ??= storageReason;
            }

            //Taken AFTER the writer has drained, never across the wait: the writer's own final
            //flush takes this same lock on its way out.
            lock (MetadataLock)
            {
                //Deregistered inside the lock that records the outcome, so nothing that takes it -
                //delete, eviction, the stale sweep - can see a row still saying "running" with no
                //writer behind it and take it for an interrupted run. Until here the live writer
                //also protects the run against eviction.
                removed = writer != null && Writers.TryRemove(runId, out _);
                //Logged by the one call that removed the writer, and before the write below that may
                //fail: the outcome is worth knowing even when the database cannot record it.
                if (removed) LogOutcome(writer!, aborted, reason);

                var run = Runs.FindById(runId);
                if (run == null) throw new StorageException("The run's metadata is missing.", runId: runId);
                //The FIRST outcome wins. This is called twice whenever delivering the result
                //throws - the loop completes the run, `await to.Result(...)` fails at a client
                //that has gone, and the catch completes it again as aborted. The run had finished
                //every step it was asked for; saying otherwise sends a user to continue a run that
                //has nothing left to compute.
                if (run.Status == "running")
                {
                    run.Status = aborted ? "aborted" : "complete";
                    run.StopReason = aborted ? reason : null;
                }
                //Booked once, here, rather than eight bytes at a time from the writer thread: the
                //index is the one file that grows with the run, and adding to the metadata row per
                //frame is the cross-process mutex this store already refuses to take per frame.
                if (writer != null)
                {
                    run.StoredFrames = writer.StoredFrames;
                    run.LastStep = writer.LastStep;
                    run.BytesOnDisk = writer.BytesOnDisk;
                    run.AuxBytesOnDisk += (writer.StoredFrames - writer.AccountedIndexEntries) * 8L;
                }
                run.UpdatedUtc = DateTime.UtcNow;
                Runs.Update(run);
                if (writer != null) writer.AccountedIndexEntries = writer.StoredFrames;
                UnrecordedOutcomes.TryRemove(runId, out _);
                return new RunCompletion(run.Status == "aborted", run.StopReason, writer?.StopReason != null);
            }
        }
        catch (Exception e)
        {
            //Kept, so the listing reports the run as it ended and the next maintenance pass records
            //it - rather than both reading the stale "running" as an interruption.
            lock (MetadataLock)
            {
                removed |= writer != null && Writers.TryRemove(runId, out _);
                if (removed) UnrecordedOutcomes[runId] = (aborted ? "aborted" : "complete", aborted ? reason : null);
            }
            Log.Error("complete-failed", runId,
                $"Run {runId}: how it ended could not be recorded ({e.Message}). It is listed as " +
                $"{(aborted ? "aborted" : "complete")} anyway, and recorded at the next maintenance pass.");
            throw StorageException.From($"Run {runId}: its final outcome could not be saved: {e.Message}", e, runId);
        }
    }

    /// <summary>
    /// The one event that says how a stored run ended: what it stored, what it could not, why it
    /// stopped and where a resume would start.
    /// </summary>
    void LogOutcome(RunWriter writer, bool aborted, string? reason)
    {
        var bytes = Volatile.Read(ref writer.BytesOnDisk);
        var stored = $"{writer.StoredFrames} frame{(writer.StoredFrames == 1 ? "" : "s")} ({Size(bytes)})";
        var unstored = Volatile.Read(ref writer.Unstored);
        var lost = unstored > 0 ? $" {unstored} frame{(unstored == 1 ? "" : "s")} could not be stored." : "";
        if (!aborted && unstored == 0)
        {
            Log.Info("run-completed", writer.RunId, $"Run {writer.RunId} completed and stored {stored}.", bytes);
            return;
        }
        var resume = writer.CheckpointStep > 0 ? $" It can be resumed from step {writer.CheckpointStep}." : "";
        var last = writer.StoredFrames > 0 ? $", the last at step {writer.LastStep - 1}" : "";
        var message = aborted
            ? $"Run {writer.RunId} stopped because {reason ?? "it was stopped"}. It stored {stored}{last}.{lost}{resume}"
            : $"Run {writer.RunId} completed and stored {stored}.{lost}{resume}";
        Log.Warning(aborted ? "run-aborted" : "run-completed", writer.RunId, message, bytes);
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
        if (!Writers.TryGetValue(runId, out var writer))
        { WriteCheckpointCore(runId, step, state); return; }
        //A checkpoint describes the world after every frame before it. Past a lost frame it would
        //claim one the store does not have, so the last checkpoint before the gap stands instead.
        //No reserve check of its own: the final checkpoint is what makes a stopped run resumable,
        //and it may use the reserve down to the floor PackCheckpoint enforces.
        if (step > Volatile.Read(ref writer.GapFromStep)) return;
        // SerializeState runs between ticks and returns an owned snapshot. Only its
        // compression and file I/O move to the writer; it never reads the live world.
        if (!ReserveFrameBytes(writer, state.LongLength)) return;
        var queued = false;
        try { writer.Queue.Add((step, state, true)); queued = true; }
        catch (InvalidOperationException) { /* completion stopped accepting snapshots */ }
        finally { if (!queued) ReleaseFrameBytes(state.LongLength); }
    }

    void WriteCheckpointCore(string runId, uint step, byte[] state)
    {
        var packed = PackCheckpoint(runId, step, state, null);
        if (packed is { } frame) CommitCheckpoint(runId, frame, null);
    }

    /// <summary>
    /// Written beside and moved into place by <see cref="CommitCheckpoint"/>. A checkpoint
    /// truncated by a crash is worse than no checkpoint: it is the one file a resume trusts
    /// completely, and a half-written one reads back as a world with fewer organs rather than as a
    /// failure. A unique suffix also distinguishes repeated preparation snapshots at recorded
    /// step zero, which the bounded writer can pack concurrently.
    /// </summary>
    PackedFrame? PackCheckpoint(string runId, uint step, byte[] state, RunWriter? writer)
    {
        var path = CheckpointPath(runId);
        var temp = $"{path}.{step}.{Guid.NewGuid():N}.tmp";
        if (!BeginWrite(state.LongLength, out var free))
        {
            //The previous checkpoint stays, and with it a place to resume from.
            if (writer != null) StopStoring(writer, LowSpaceReason(free), "low-space-stop", step, "warning");
            Log.Warning("checkpoint-skipped", runId,
                $"Run {runId}: the checkpoint at step {step} was not written: {LowSpaceReason(free)}.");
            return null;
        }
        try
        {
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
            return new(step, path, temp, size);
        }
        catch (Exception e)
        {
            try { File.Delete(temp); } catch { /* swept at the next start */ }
            if (writer != null) StopStoring(writer, $"the checkpoint at step {step} could not be written ({e.Message})", "checkpoint-failed", step, "error");
            else Log.Error("checkpoint-failed", runId, $"Run {runId}: the checkpoint at step {step} could not be written: {e.Message}");
            return null;
        }
        finally { EndWrite(state.LongLength); }
    }

    void CommitCheckpoint(string runId, PackedFrame packed, RunWriter? writer)
    {
        try
        {
            var previous = File.Exists(packed.Path) ? new FileInfo(packed.Path).Length : 0;
            File.Move(packed.Temporary, packed.Path, overwrite: true);
            if (writer != null) writer.CheckpointStep = packed.Step;
            //Only the difference: the file replaces itself rather than accumulating.
            if (packed.StoredBytes != previous) AddAuxBytes(runId, packed.StoredBytes - previous);
        }
        catch (Exception e)
        {
            try { File.Delete(packed.Temporary); } catch { /* swept at the next start */ }
            if (writer != null) StopStoring(writer, $"the checkpoint at step {packed.Step} could not be committed ({e.Message})", "checkpoint-failed", packed.Step, "error");
            else Log.Error("checkpoint-failed", runId, $"Run {runId}: the checkpoint at step {packed.Step} could not be committed: {e.Message}");
        }
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
            Log.Error("checkpoint-unreadable", runId, $"Run {runId}: its checkpoint could not be read ({e.Message}), so it cannot be resumed - only forked.");
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
        //A trailing partial record is an append the disk could not finish - the volume filling up
        //is the usual way. Every whole entry before it is good, and readers already ignore the
        //remainder, so it is dropped rather than refused: refusing made exactly the run that had
        //filled its disk impossible to resume. The original index is kept in the recovery
        //directory below, since the record count no longer matches its length.
        var whole = index.Length / 8 * 8;
        var prefix = new SortedDictionary<uint, uint>();
        var tail = new List<string>();
        for (int offset = 0; offset < whole; offset += 8)
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

    public bool ResumeRun(string runId, uint checkpointStep, uint totalSteps, int totalHours, string requestJson, out string reason)
    {
        reason = "";
        if (Disabled) { reason = $"The run store is disabled. {DisabledReason}"; return false; }
        if (!IsSafeId(runId)) { reason = "Not a run id."; return false; }
        var startedWriter = false;
        try
        {
            EnsureWritable();
            lock (MetadataLock)
            {
                var run = Runs.FindById(runId);
                if (run == null) { reason = "No such run."; return false; }

                //The same test DeleteRun makes, for the same reason. A run whose writer is still
                //draining has a thread appending frames and holding the index handle: starting a
                //second writer on it leaves two workers writing the same frame paths and
                //interleaving eight-byte index entries, after which the index no longer parses in
                //step order. The writer, not the status: a row left "running" by a server that
                //stopped - or by a disk too full to record the outcome - is exactly the run that
                //most needs resuming.
                if (IsLive(runId))
                {
                    reason = "That run is still being written. Wait for it to settle and try again.";
                    return false;
                }

                ReconcileCheckpointHistory(run, checkpointStep);
                ++run.FrameRevision;
                run.FrameRestartStep = checkpointStep;

                //Written inside the lock, so a refusal cannot have already replaced the request of
                //a run it then declines to reopen - and atomically, because a request truncated by
                //a full disk would make the run impossible to resume or fork ever again.
                var oldRequestBytes = new FileInfo(RequestPath(runId)).Length;
                var newRequestBytes = WriteAtomically(RequestPath(runId), output => output.Write(Encoding.UTF8.GetBytes(requestJson)));
                run.AuxBytesOnDisk += newRequestBytes - oldRequestBytes;
                run.Status = "running";
                run.StopReason = null;
                run.TotalSteps = totalSteps;
                run.TotalHours = totalHours;
                run.UpdatedUtc = DateTime.UtcNow;
                //Seeded from what the run already holds, so the resumed writer adds to the tally
                //rather than replacing it. Started before the row says "running", as BeginRun does.
                StartWriter(runId, run.StoredFrames, run.BytesOnDisk, run.LastStep);
                startedWriter = true;
                Runs.Update(run);
                UnrecordedOutcomes.TryRemove(runId, out _);
            }
            Log.Info("resumed", runId, $"Run {runId} resumed from its checkpoint at step {checkpointStep}, now running to step {totalSteps}.");
            return true;
        }
        catch (Exception e)
        {
            //The writer's final metadata flush takes MetadataLock too; never wait for it while
            //holding that lock after a failed registration.
            if (startedWriter) AbandonWriter(runId);
            reason = $"That run could not be reopened: {e.Message}";
            Log.Error("resume-failed", runId, $"Run {runId} could not be resumed: {e.Message}");
            return false;
        }
    }

    /// <summary>Stops a writer that was started for a run that is not going ahead after all.</summary>
    void AbandonWriter(string runId)
    {
        if (!Writers.TryRemove(runId, out var writer)) return;
        writer.Queue.CompleteAdding();
        try { writer.Worker?.Wait(TimeSpan.FromSeconds(30)); } catch { /* it had nothing to write */ }
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
        if (Disabled) throw new StorageException($"The run store is disabled. {DisabledReason}", runId: runId);
        if (!IsSafeId(runId) || utf8Json == null || utf8Json.Length == 0)
            throw new StorageException("No valid measurement was supplied to the run store.", runId: runId);
        if (!BeginWrite(utf8Json.LongLength, out var free))
            throw new StorageException($"The measurement could not be stored: {LowSpaceReason(free)}.", insufficientSpace: true, runId: runId);
        try
        {
            //gzip rather than the frames' zlib: this one is served to a browser as a file to save,
            //and Content-Encoding: gzip is the form every client handles without ambiguity.
            var stored = WriteAtomically(ObstacleLightPath(runId), output =>
            {
                using var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
                gz.Write(utf8Json, 0, utf8Json.Length);
            });
            AddAuxBytes(runId, stored);
            //Console only: it happens once per run, and the run's own outcome is the event.
            Console.WriteLine($"RunStore: stored the leaf area index document of {runId} " +
                              $"({Size(utf8Json.Length)} of JSON, {Size(stored)} on disk).");
        }
        catch (Exception e)
        {
            Log.Error("measurement-failed", runId, $"Run {runId}: could not store its leaf area index document: {e.Message}");
            throw StorageException.From($"Run {runId}: its measurement could not be stored: {e.Message}", e, runId);
        }
        finally { EndWrite(utf8Json.LongLength); }
    }

    public string StoreFieldModel(string objSource)
    {
        EnsureWritable();
        if (string.IsNullOrWhiteSpace(objSource)) throw new ArgumentException("The field model is empty.", nameof(objSource));
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
            if (!BeginWrite(bytes.LongLength, out var free))
                throw new StorageException($"The field model could not be stored: {LowSpaceReason(free)}.", insufficientSpace: true);
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gz = new GZipStream(output, CompressionLevel.Optimal))
                    gz.Write(bytes, 0, bytes.Length);
                lock (MetadataLock) File.Move(temp, path, overwrite: true);
            }
            catch
            {
                //Up to a few hundred megabytes, and nothing would ever reference it.
                try { File.Delete(temp); } catch { /* CollectTempModels removes it within the hour */ }
                throw;
            }
            finally { EndWrite(bytes.LongLength); }

            var stored = new FileInfo(path).Length;
            Log.Info("model-stored", null, $"Stored field model {hash[..12]} ({Size(bytes.Length)} of OBJ, {Size(stored)} on disk).", stored);
            return hash;
        }
        catch (Exception e)
        {
            Log.Error("model-store-failed", null,
                $"The field model upload was refused because its original source could not be stored ({e.Message}).");
            throw StorageException.From($"The field model could not be stored: {e.Message}", e);
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
            Log.Error("model-unreadable", null, $"Could not read field model {hash}: {e.Message}");
            return false;
        }
    }

    public RunRecord? GetRun(string runId)
    {
        if (Disabled || !IsSafeId(runId)) return null;
        lock (MetadataLock)
        {
            var run = Runs.FindById(runId);
            if (run == null) return null;
            return Overlay(run);
        }
    }

    public IReadOnlyList<RunRecord> ListRuns(int limit)
    {
        if (Disabled) return [];
        lock (MetadataLock)
        {
        var runs = Runs.Query().OrderByDescending(x => x.CreatedUtc).Limit(limit).ToList();
        for (var i = 0; i < runs.Count; ++i)
        {
            var run = runs[i];
            runs[i] = Overlay(run);
        }
        return runs;
        }
    }

    /// <summary>
    /// What a row says, corrected by what is known in memory. The returned copy only - nothing is
    /// written back.
    ///
    /// A live run's figures come from its writer, so the listing keeps moving even while the
    /// database cannot be written. A row still saying "running" that nothing is writing is reported
    /// the way it ended, or as aborted: that row is how a full disk left the run that filled it -
    /// undeletable in the panel, because the panel will not delete a running run, and unevictable,
    /// because eviction will not either.
    /// </summary>
    RunRecord Overlay(RunRecord run)
    {
        run.Protected = ReadProtection(run.Id);
        if (Writers.TryGetValue(run.Id, out var writer))
        {
            run.StoredFrames = writer.StoredFrames;
            run.LastStep = writer.LastStep;
            run.BytesOnDisk = Volatile.Read(ref writer.BytesOnDisk);
        }
        else if (run.Status == "running")
        {
            if (UnrecordedOutcomes.TryGetValue(run.Id, out var outcome))
            {
                run.Status = outcome.Status;
                run.StopReason = outcome.Reason;
            }
            else
            {
                run.Status = "aborted";
                run.StopReason ??= "nothing is computing it any more; its outcome was never recorded";
            }
        }
        return run;
    }

    public void StartMaintenance()
    {
        //Said once at startup, and here rather than in the constructor: the budgets it compares the
        //volume against are configured after the store is built. Also when the store is disabled -
        //where it writes, and how full that is, is half of explaining why.
        ReportSetup();
        if (Disabled || Maintenance != null) return;
        if (SweepInterval <= TimeSpan.Zero)
        {
            Log.Warning("maintenance-off", null,
                "Periodic maintenance is off. The budget is then enforced only when a run starts or is deleted, " +
                "and uploaded field models are never collected.");
            return;
        }
        //One-shot and re-armed at the end of each pass, not a fixed period: recursively deleting a
        //17 GB run can outlive the interval, and a tick queued behind a pass that is already
        //evicting would start a second one on top of it.
        Maintenance = new Timer(_ => Sweep(), null, SweepInterval, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Says where the store writes, how much room is left there and what it will keep - and warns
    /// about every configuration that will lose data. Logged once at startup.
    /// </summary>
    void ReportSetup()
    {
        var volume = StoreVolume.Probe(Root);
        var stored = StoredBytes(out _);
        var where = volume.MountPoint.Length == 0 ? "" :
            $" on {volume.MountPoint}" +
            (volume.FileSystem.Length > 0 ? $" ({volume.FileSystem}" : " (") +
            (volume.AnonymousVolume ? ", an anonymous Docker volume)" :
             volume.VolumeName.Length > 0 ? $", Docker volume {volume.VolumeName})" : ")");
        var room = volume.FreeBytes >= 0 ? $", {Size(volume.FreeBytes)} free of {Size(volume.TotalBytes)}" : "";
        var maintenance = SweepInterval > TimeSpan.Zero ? $"maintenance every {SweepInterval.TotalMinutes:0} min" : "no periodic maintenance";
        Log.Info(Disabled ? "store-status" : "store-opened", null,
            $"{(Disabled ? "The disabled run store" : "Run store")} at {volume.Root}{where}{room}. Automatic eviction of unprotected runs above " +
            $"{Size(RetentionBytes)} (holding {Size(stored)}); new runs are protected. Field models up to {Size(ModelRetentionBytes)}, " +
            $"{Size(MinFreeBytes)} reserved free; {maintenance}.");
        foreach (var warning in Warnings(volume, stored))
            Log.Warning("config", null, warning);
    }

    /// <summary>What the runs occupy, from the database. Zero when it cannot be read.</summary>
    long StoredBytes(out int runs)
    {
        runs = 0;
        if (Disabled) return 0;
        try
        {
            var all = Runs.FindAll().ToList();
            runs = all.Count;
            return all.Sum(Footprint);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Configuration that will lose data, in words, each with what to do about it. Computed from
    /// the volume as it is now, so the Runs tab shows the current state and not a startup snapshot.
    /// </summary>
    List<string> Warnings(StoreVolume volume, long stored)
    {
        var warnings = new List<string>();
        if (volume.AnonymousVolume)
            warnings.Add($"The run store is on an anonymous Docker volume ({volume.MountPoint}). A redeploy that " +
                         $"recreates the container starts with an empty store, and `docker volume prune` deletes " +
                         $"every run in it. Mount a named volume and point AGRO_RunStoreRoot inside it.");
        else if (volume.InContainer && volume.MountPoint == "/")
            warnings.Add($"The run store is in the container's own filesystem, not on a volume, so it is lost when " +
                         $"the container is recreated. Mount a volume and point AGRO_RunStoreRoot inside it.");
        foreach (var mount in volume.NestedMounts)
            warnings.Add($"A volume is mounted at {mount}, but the run store writes to {volume.Root}, outside it" +
                         $"{(volume.MountPoint.Length > 0 ? $", on {volume.MountPoint}" : "")}. If that volume is meant " +
                         $"to hold the runs, set AGRO_RunStoreRoot to a directory inside it.");
        if (volume.FreeBytes >= 0)
        {
            var capacity = stored + volume.FreeBytes - MinFreeBytes;
            //Not when there is no room at all: the reserve warning below says that, and "set the
            //budget below 0 B" is not advice.
            if (capacity > 0 && RetentionBytes > capacity)
                warnings.Add($"The retention budget ({Size(RetentionBytes)}) is more than this volume can hold for " +
                             $"runs (about {Size(Math.Max(0, capacity))}, keeping the {Size(MinFreeBytes)} reserve free). " +
                             $"Eviction will never start; runs stop at the reserve instead, and only deleting runs " +
                             $"makes room. Set AGRO_RunStoreRetentionBytes below {Size(Math.Max(0, capacity))}.");
            if (MinFreeBytes > 0 && volume.FreeBytes < MinFreeBytes)
                warnings.Add($"Only {Size(volume.FreeBytes)} is free on {(volume.MountPoint.Length > 0 ? volume.MountPoint : volume.Root)}, " +
                             $"below the {Size(MinFreeBytes)} reserve: new work is refused and active writing stops. " +
                             $"Delete runs or free space on the volume.");
        }
        return warnings;
    }

    public Models.RunStoreStatusResponse Status(int events)
    {
        var volume = StoreVolume.Probe(Root);
        var stored = StoredBytes(out var runs);
        return new(
            Enabled: !Disabled,
            Root: volume.Root,
            DisabledReason: DisabledReason,
            MountPoint: volume.MountPoint,
            FileSystem: volume.FileSystem,
            VolumeName: volume.VolumeName,
            AnonymousVolume: volume.AnonymousVolume,
            TotalBytes: volume.TotalBytes,
            FreeBytes: volume.FreeBytes,
            MinFreeBytes: MinFreeBytes,
            RetentionBytes: RetentionBytes,
            ModelRetentionBytes: ModelRetentionBytes,
            StoredBytes: stored,
            ModelBytes: Disabled ? 0 : ModelBytes(),
            Runs: runs,
            LiveRuns: Writers.Count,
            SweepMinutes: SweepInterval.TotalMinutes,
            Warnings: Warnings(volume, stored).ToArray(),
            Events: Log.Snapshot(events));
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
            //Outcomes CompleteRun could not write - typically on the full disk that stopped the run.
            SweepStaleRuns(atStartup: false);
            CollectTempModels();
            CollectModels();
            Evict();
            var free = StoreVolume.FreeSpace(Root);
            Log.Condition("disk-low", MinFreeBytes > 0 && free >= 0 && free < MinFreeBytes, "disk-low",
                () => $"Only {Size(free)} is free on the run store's volume, below the {Size(MinFreeBytes)} reserve. " +
                      $"Runs stop as soon as they try to store a frame; delete runs or free space on the volume.",
                cleared: () => $"Free space on the run store's volume is back above the {Size(MinFreeBytes)} reserve " +
                               $"({Size(StoreVolume.FreeSpace(Root))} free).");
        }
        catch (ObjectDisposedException) { /* the database went away: the store is shutting down */ }
        catch (Exception e) { Log.Error("maintenance-failed", null, $"A maintenance pass failed: {e.Message}"); }
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
                //The writer and not the row's status: a row the full disk left saying "running" is
                //the run a user most needs to delete, to make room.
                if (IsLive(runId))
                {
                    reason = "This run is still computing. Stop it first, then delete it.";
                    Log.Info("delete-refused", runId, $"Refused to delete run {runId}: it is still computing.");
                    return DeleteOutcome.Refused;
                }

                //Measured before it goes, from the row or - for a directory the database never knew -
                //from the disk, so the event says what was given back.
                var dir = RunDir(runId);
                var size = run != null ? Footprint(run) : DirectorySize(dir);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (run != null) Runs.Delete(runId);
                UnrecordedOutcomes.TryRemove(runId, out _);
                Log.Info("deleted", runId,
                    $"Deleted run {runId} ({Size(size)}{(run != null ? $", started {run.CreatedUtc:yyyy-MM-dd HH:mm} UTC" : "")}) " +
                    $"on request.", size);
            }
            CollectModels();
            return DeleteOutcome.Deleted;
        }
        catch (Exception e)
        {
            reason = e.Message;
            Log.Error("delete-failed", runId, $"Could not delete run {runId}: {e.Message}");
            return DeleteOutcome.Refused;
        }
    }

    /// <summary>Bytes under a directory, or zero when it cannot be measured.</summary>
    static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
                : 0;
        }
        catch { return 0; }
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
            var parents = all.Where(x => x.ParentRunId != null).Select(x => x.ParentRunId!)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var run in all) run.Protected = ReadProtection(run.Id);
            //Runs only. Field models have their own cap (ModelRetentionBytes) because evicting a
            //run does not release its model - it outlives the run and may be shared - so counting
            //them here would have this loop delete every completed run in the store chasing a
            //total that only model collection can bring down.
            var total = all.Sum(Footprint);

            //If what cannot be evicted already exceeds the budget, deleting the rest frees nobody
            //and costs the user their whole history. Say so - once, when it starts - and stop.
            //Liveness is the writer, not the status: a row the server left saying "running" after
            //it stopped is over, and holding the budget for it is how that run's space was lost.
            var pinned = all.Where(r => r.Protected || IsLive(r.Id) || parents.Contains(r.Id)).Sum(Footprint);
            Log.Condition("eviction-refused", pinned > RetentionBytes, "eviction-refused",
                () => $"{Size(pinned)} is held by protected, running or branched-from runs, which is already over the " +
                      $"{Size(RetentionBytes)} budget. Not evicting - deleting completed runs would not help.",
                cleared: () => "Protected, running and branched-from runs fit inside the retention budget again; eviction resumes.");
            if (pinned > RetentionBytes) return;

            foreach (var run in all)
            {
                if (total <= RetentionBytes) break;
                //Checked per run, not only at the pass entry: the loop is the long part, and every
                //iteration is a recursive delete plus a row removal that must not straddle shutdown.
                if (Stopping) break;
                //Never evict a run something else was branched from - its request and its stored
                //frames are the only thing making those children comparable to it.
                if (run.Protected || IsLive(run.Id) || parents.Contains(run.Id)) continue;

                //Per run, because one undeletable directory used to abort the whole pass - which on
                //a timer means aborting it again every interval, forever, with the store stuck over
                //budget and only the first failure in the log.
                try
                {
                    var dir = RunDir(run.Id);
                    var before = total;
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Runs.Delete(run.Id);
                    UnrecordedOutcomes.TryRemove(run.Id, out _);
                    total -= Footprint(run);
                    //A warning, because it deletes results nobody asked to delete - the one deletion
                    //a user will come looking for, so it says what, when it was made, and why.
                    Log.Warning("evicted", run.Id,
                        $"Evicted run {run.Id} ({Size(Footprint(run))}, started {run.CreatedUtc:yyyy-MM-dd HH:mm} UTC): " +
                        $"the stored runs held {Size(before)} against a retention budget of {Size(RetentionBytes)}, " +
                        $"and this was the oldest that could go.", Footprint(run));
                }
                catch (ObjectDisposedException)
                {
                    //The database is closing under us. Stop: continuing deletes directories whose
                    //rows can no longer be removed, which is worse than stopping mid-budget.
                    Log.Warning("eviction-failed", run.Id, $"Eviction stopped at run {run.Id}: the store is shutting down.");
                    break;
                }
                catch (Exception e)
                {
                    Log.Error("eviction-failed", run.Id, $"Could not evict run {run.Id}: {e.Message}");
                }
            }
            }

            //After eviction, because a run that has just gone may have been the last reference to
            //its model. The list is passed through so the pass reads the table once.
            CollectModels(Runs.FindAll().ToList());
        }
        catch (Exception e) { Log.Error("eviction-failed", null, $"Eviction failed: {e.Message}"); }
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
            Log.Condition("models-unaccounted", unaccounted > 0, "models-unaccounted",
                () => $"{unaccounted} run(s) name a field model without recording which one. They cannot use a stored " +
                      $"model and do not hold one back; collecting anyway.", level: "info");

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
                if (!TryDeleteModel(model.Path, model.Hash, model.Size,
                        $"no stored run uses it, and it was last uploaded over {ModelGrace.TotalHours:0} h ago")) survivors.Add(model);
            }

            //Stage B: still over the cap, so the youngest dead models go too - oldest touched
            //first, and never one young enough to be an upload in progress.
            var total = referencedBytes + survivors.Sum(x => x.Size);
            if (total > ModelRetentionBytes)
            {
                var floor = now - ModelMinGrace;
                foreach (var model in survivors.Where(x => x.Touched <= floor).OrderBy(x => x.Touched))
                {
                    if (total <= ModelRetentionBytes || Stopping) break;
                    if (TryDeleteModel(model.Path, model.Hash, model.Size,
                            "no stored run uses it, and the field models were over their budget")) total -= model.Size;
                }
            }

            Log.Condition("model-budget", total > ModelRetentionBytes, "model-budget",
                () => $"Field models occupy {Size(total)}, over the {Size(ModelRetentionBytes)} budget, and " +
                      $"{Size(referencedBytes)} of that is referenced by stored runs. Nothing further can be collected " +
                      $"without costing those runs their terrain - delete runs to release their models.",
                cleared: () => $"Field models fit inside the {Size(ModelRetentionBytes)} budget again.");
        }
        catch (ObjectDisposedException) { /* the database went away: the store is shutting down */ }
        catch (Exception e) { Log.Error("model-collect-failed", null, $"Field model collection failed: {e.Message}"); }
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
            Log.Info("model-collected", null, $"Deleted field model {hash[..Math.Min(12, hash.Length)]} ({Size(size)}): {why}.", size);
            return true;
        }
        catch (Exception e)
        {
            Log.Error("model-collect-failed", null, $"Could not delete field model {hash[..Math.Min(12, hash.Length)]}: {e.Message}");
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
                        Log.Info("model-temp-removed", null, $"Removed an abandoned partial field model upload ({Size(size)}).", size);
                    }
                }
                catch { /* the next pass will try again */ }
        }
        catch (Exception e) { Log.Error("model-collect-failed", null, $"Could not look for abandoned partial field model uploads: {e.Message}"); }
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
    /// V7, V8 and V9 frames open with a version byte and a flags byte. The frames are
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
            if (header[0] is not (7 or 8 or 9)) return false;
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
