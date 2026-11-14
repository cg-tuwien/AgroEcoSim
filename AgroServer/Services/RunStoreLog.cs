using System.Text.Json;

namespace AgroServer.Services;

/// <summary>
/// One thing the run store did, or failed to do: a deletion, an eviction, a run that could not be
/// stored, a volume that filled up.
///
/// Immutable, and numbered. The number orders events across restarts - the log is reloaded from
/// disk at startup and continues from the highest one - so "what happened before the server came
/// back" reads in the order it happened even when two events share a timestamp.
///
/// <c>Level</c> is <c>info</c>, <c>warning</c> or <c>error</c>. <c>Kind</c> is a short fixed name
/// for what happened (<c>deleted</c>, <c>evicted</c>, <c>frame-failed</c>, ...) that the panel
/// groups by; the sentence is in <c>Message</c>. <c>Bytes</c> is what the event freed or concerned
/// when it is about a size, and zero otherwise.
/// </summary>
public sealed record RunStoreEvent(long Seq, DateTime Utc, string Level, string Kind, string? RunId, long Bytes, string Message);

/// <summary>
/// What the run store has done and failed to do, kept where the Runs tab can show it.
///
/// It exists because the store's failures were all silent in the one place anyone looks. A full
/// volume froze a 56 GB run's stored size without a word, the next start disabled the store, and a
/// disabled store answered the listing with an empty array - which the panel printed as "No stored
/// runs". Every line that would have explained it went to stderr, and Docker keeps stderr on the
/// same full disk.
///
/// Three properties are what make it useful rather than decorative:
///
///   - It never throws and never needs the store. Events are kept in memory first and written to
///     disk second, so a store that cannot open, or a volume with no space left, still produces a
///     log - and that is exactly the case the log is for.
///   - It survives restarts. The tail of the file is read back at startup, so an eviction last night
///     or the disk filling up yesterday is still on screen after the redeploy that followed it.
///   - It says the same thing twice. Every event is also printed to the console in the same words,
///     so `docker logs` and the panel never disagree about what happened.
///
/// Repeating states - low space, eviction refused, a model budget that cannot be met - are logged by
/// <see cref="Condition"/> when they start and when they clear, not once per maintenance pass: a
/// warning repeated every thirty minutes for a week is how a log stops being read.
/// </summary>
public sealed class RunStoreLog
{
    /// <summary>Events kept in memory and served. A few weeks of an ordinary store's history.</summary>
    public const int Capacity = 1000;

    /// <summary>The file is rotated past this, keeping one previous generation.</summary>
    const long RotateBytes = 1L << 20;

    /// <summary>
    /// How long persistence rests after a failed write.
    ///
    /// The usual cause is a full volume, and retrying every event against it costs a failed open per
    /// event while changing nothing. Events are still recorded in memory meanwhile.
    /// </summary>
    static readonly TimeSpan PersistRetry = TimeSpan.FromMinutes(1);

    /// <summary>Guards the ring, the sequence and the condition states. A leaf lock: nothing else is
    /// taken inside it, and neither the console nor the file is written while it is held.</summary>
    readonly object Gate = new();
    readonly RunStoreEvent[] Ring = new RunStoreEvent[Capacity];
    int Next, Count;
    long NextSeq = 1;
    readonly HashSet<string> ActiveConditions = new(StringComparer.Ordinal);

    /// <summary>Serialises appends and rotation. Also a leaf lock.</summary>
    readonly object FileGate = new();
    readonly string? FilePath;
    DateTime PersistBlockedUntil;
    bool PersistFailing;

    /// <param name="filePath">Where events are kept across restarts, or null for memory only.</param>
    public RunStoreLog(string? filePath)
    {
        FilePath = filePath;
        Load();
    }

    string RotatedPath => Path.ChangeExtension(FilePath!, ".1.jsonl");

    public RunStoreEvent Info(string kind, string? runId, string message, long bytes = 0) =>
        Record("info", kind, runId, message, bytes);

    public RunStoreEvent Warning(string kind, string? runId, string message, long bytes = 0) =>
        Record("warning", kind, runId, message, bytes);

    public RunStoreEvent Error(string kind, string? runId, string message, long bytes = 0) =>
        Record("error", kind, runId, message, bytes);

    RunStoreEvent Record(string level, string kind, string? runId, string message, long bytes, bool persist = true)
    {
        RunStoreEvent e;
        lock (Gate)
        {
            e = new(NextSeq++, DateTime.UtcNow, level, kind, runId, bytes, message);
            Append(e);
        }
        //Outside the lock, so a slow console or a slow disk delays only the thread that has
        //something to say - never one that only wanted to read the log.
        var line = $"RunStore: {message}";
        if (level == "info") Console.WriteLine(line); else Console.Error.WriteLine(line);
        if (persist) Persist(e);
        return e;
    }

    /// <summary>
    /// Logs a repeating state when it starts and, if <c>cleared</c> is given, when it ends - and
    /// says nothing in between, however often it is checked. <c>message</c> and <c>cleared</c> are
    /// evaluated only when the state changes, so they may measure something.
    /// </summary>
    public void Condition(string key, bool active, string kind, Func<string> message,
                          string level = "warning", string? runId = null, Func<string>? cleared = null)
    {
        bool changed;
        lock (Gate) changed = active ? ActiveConditions.Add(key) : ActiveConditions.Remove(key);
        if (!changed) return;
        if (active) Record(level, kind, runId, message(), 0);
        else if (cleared != null) Record("info", kind, runId, cleared(), 0);
    }

    /// <summary>The newest events first, at most <paramref name="limit"/> of them.</summary>
    public RunStoreEvent[] Snapshot(int limit)
    {
        lock (Gate)
        {
            var n = Math.Clamp(limit, 0, Count);
            var result = new RunStoreEvent[n];
            for (int i = 0; i < n; ++i)
                result[i] = Ring[(Next - 1 - i + Capacity) % Capacity];
            return result;
        }
    }

    void Append(RunStoreEvent e)
    {
        Ring[Next] = e;
        Next = (Next + 1) % Capacity;
        if (Count < Capacity) ++Count;
    }

    /// <summary>
    /// One JSON line per event, appended. Never throws.
    ///
    /// Through the same source-generated context as everything else, so the file is PascalCase; it
    /// is only ever read back by <see cref="Load"/>, and the HTTP endpoint camelCases its own copy.
    /// </summary>
    void Persist(RunStoreEvent e)
    {
        if (FilePath == null) return;
        string? note = null, recovered = null;
        lock (FileGate)
        {
            var now = DateTime.UtcNow;
            if (now < PersistBlockedUntil) return;
            try
            {
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > RotateBytes) File.Move(FilePath, RotatedPath, overwrite: true);
                File.AppendAllText(FilePath,
                    JsonSerializer.Serialize(e, Models.AppJsonSerializerContext.Default.RunStoreEvent) + "\n");
                if (PersistFailing)
                {
                    PersistFailing = false;
                    recovered = $"Events are being saved to {FilePath} again. Those recorded while it failed " +
                                $"are shown here until the next restart, but are not in the file.";
                }
            }
            catch (Exception ex)
            {
                PersistBlockedUntil = now + PersistRetry;
                if (!PersistFailing)
                {
                    PersistFailing = true;
                    note = $"Events cannot be saved to {FilePath} ({ex.Message}). They are kept in memory " +
                           $"and shown here, but will be lost at the next restart.";
                }
            }
        }
        //Recorded after the file lock is released: the note about the file must not go through the
        //file, and the recovery note may.
        if (note != null) Record("warning", "log-unsaved", null, note, 0, persist: false);
        if (recovered != null) Record("info", "log-saved", null, recovered, 0);
    }

    /// <summary>
    /// Reads back what earlier processes recorded, oldest first, so the ring holds the newest
    /// <see cref="Capacity"/> events across restarts.
    ///
    /// A line cut short - by a crash, or by the very disk-full condition the log exists to report -
    /// is skipped rather than failing the whole read.
    /// </summary>
    void Load()
    {
        if (FilePath == null) return;
        try
        {
            var loaded = new List<RunStoreEvent>();
            foreach (var file in new[] { RotatedPath, FilePath })
            {
                if (!File.Exists(file)) continue;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Length < 2 || line[0] != '{') continue;
                    try
                    {
                        if (JsonSerializer.Deserialize(line, Models.AppJsonSerializerContext.Default.RunStoreEvent) is { } e)
                            loaded.Add(e);
                    }
                    catch (JsonException) { /* a partial line */ }
                }
            }
            lock (Gate)
            {
                foreach (var e in loaded.Skip(Math.Max(0, loaded.Count - Capacity))) Append(e);
                if (loaded.Count > 0) NextSeq = loaded.Max(x => x.Seq) + 1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"RunStore: could not read the event log {FilePath}: {e.Message}");
        }
    }
}
