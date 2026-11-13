namespace GodotFrontend.Core.History;

public enum PayloadKind : byte
{
    /// <summary>No bytes held; the server (or the disk cache) can serve the step again.</summary>
    None,
    Raw,
    Zlib,
    /// <summary>Served lazily from an open .agro archive.</summary>
    Archive,
}

/// <summary>One timeline entry: the step is always known, the payload may be held, compressed, archived or evicted.</summary>
public sealed class HistoryFrame
{
    public uint Step;
    public byte[]? Bytes;
    public PayloadKind Kind;
    public long ArchiveOffset;
    public int ArchiveLength;
    public bool ArchivePlain;

    public bool HasPayload => Kind != PayloadKind.None;
    public int ResidentBytes => Bytes?.Length ?? 0;
}

public sealed record MergeOutcome(bool Changed, bool Replaced, int Count);

/// <summary>
/// The timeline of a run: entries sorted by step, filed by step (never by position), with byte accounting that
/// matches the web's historySize (payload bytes plus a 136-byte entry overhead), eviction to a budget that spares
/// the newest frame and a small window around the playhead, and a union merge with the server's frame index.
/// </summary>
public sealed class History
{
    public const int EntryOverhead = 136;
    public const int NearPlayhead = 4;

    private readonly List<HistoryFrame> frames = new();

    public int Count => frames.Count;
    public long ResidentBytes { get; private set; }
    public int EvictedCount { get; private set; }
    public HistoryFrame this[int index] => frames[index];
    public IReadOnlyList<HistoryFrame> Frames => frames;

    public int IndexOfStep(uint step)
    {
        int lo = 0, hi = frames.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var s = frames[mid].Step;
            if (s == step) return mid;
            if (s < step) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public uint StepAt(int index)
    {
        if (frames.Count == 0) return 0;
        return frames[System.Math.Clamp(index, 0, frames.Count - 1)].Step;
    }

    public int ClampIndex(int index) => frames.Count == 0 ? 0 : System.Math.Clamp(index, 0, frames.Count - 1);

    /// <summary>Files a received raw frame by step; a known step is replaced and its bytes counted once. Returns the index.</summary>
    public int Upsert(uint step, byte[] raw)
    {
        var at = IndexOfStep(step);
        if (at >= 0)
        {
            var e = frames[at];
            if (e.HasPayload) ResidentBytes -= e.ResidentBytes + EntryOverhead;
            else EvictedCount--;
            e.Bytes = raw;
            e.Kind = PayloadKind.Raw;
            ResidentBytes += raw.Length + EntryOverhead;
            return at;
        }
        var ins = ~at;
        frames.Insert(ins, new HistoryFrame { Step = step, Bytes = raw, Kind = PayloadKind.Raw });
        ResidentBytes += raw.Length + EntryOverhead;
        return ins;
    }

    /// <summary>Adds an entry served from an archive (no resident bytes).</summary>
    public int AddArchived(uint step, long offset, int length, bool plain)
    {
        var at = IndexOfStep(step);
        var e = new HistoryFrame { Step = step, Kind = PayloadKind.Archive, ArchiveOffset = offset, ArchiveLength = length, ArchivePlain = plain };
        if (at >= 0) { Drop(frames[at]); frames[at] = e; return at; }
        frames.Insert(~at, e);
        return ~at;
    }

    /// <summary>Replaces a frame's payload with its compressed form; the caller checked it is still the same raw buffer.</summary>
    public void ApplyCompressed(HistoryFrame e, byte[] zlib)
    {
        if (e.Kind != PayloadKind.Raw) return;
        ResidentBytes += zlib.Length - e.ResidentBytes;
        e.Bytes = zlib;
        e.Kind = PayloadKind.Zlib;
    }

    private void Drop(HistoryFrame e)
    {
        if (!e.HasPayload) return;
        if (e.Kind != PayloadKind.Archive) ResidentBytes -= e.ResidentBytes + EntryOverhead;
        e.Bytes = null;
        e.Kind = PayloadKind.None;
        EvictedCount++;
    }

    /// <summary>
    /// The web's evictOldestPayloads: drop payloads oldest first until under 0.75 x budget, sparing the newest frame and
    /// the playhead window; if still over budget and the frames are recoverable, drop everything except the shown frame.
    /// Returns the number dropped; <paramref name="stillOver"/> says whether the caller must stop the run.
    /// </summary>
    public int EvictToBudget(int pointer, long budget, bool recoverable, out bool stillOver)
    {
        var target = (long)(budget * 0.75);
        var last = frames.Count - 1;
        int Evict(bool spareWindow)
        {
            var dropped = 0;
            for (var i = 0; i <= last && ResidentBytes > target; ++i)
            {
                if (spareWindow && (i == last || System.Math.Abs(i - pointer) <= NearPlayhead)) continue;
                if (!spareWindow && i == pointer) continue;
                var e = frames[i];
                if (e.Kind is PayloadKind.None or PayloadKind.Archive) continue;
                Drop(e);
                dropped++;
            }
            return dropped;
        }
        var n = Evict(true);
        if (ResidentBytes > budget && recoverable) n += Evict(false);
        stillOver = ResidentBytes > budget;
        return n;
    }

    /// <summary>Hands every resident payload back (before an export), keeping only the window around the pointer.</summary>
    public int Release(int pointer, int keepAround)
    {
        var n = 0;
        for (var i = 0; i < frames.Count; ++i)
        {
            if (System.Math.Abs(i - pointer) <= keepAround) continue;
            var e = frames[i];
            if (e.Kind is PayloadKind.Raw or PayloadKind.Zlib) { Drop(e); n++; }
        }
        return n;
    }

    /// <summary>
    /// Union with the server's index by step. A changed revision (a resume replaced the tail at <paramref name="restart"/>)
    /// drops the held entries at or past the restart step. Entries the client never held arrive with no payload.
    /// </summary>
    public MergeOutcome MergeIndex(uint[] steps, bool replaced, uint restart)
    {
        var changed = false;
        if (replaced)
        {
            for (var i = frames.Count - 1; i >= 0; --i)
                if (frames[i].Step >= restart)
                {
                    Drop(frames[i]);
                    if (frames[i].Kind == PayloadKind.None) EvictedCount--; // an entry removed outright is not an eviction
                    frames.RemoveAt(i);
                    changed = true;
                }
        }
        foreach (var s in steps)
        {
            var at = IndexOfStep(s);
            if (at >= 0) continue;
            frames.Insert(~at, new HistoryFrame { Step = s, Kind = PayloadKind.None });
            EvictedCount++;
            changed = true;
        }
        return new MergeOutcome(changed, replaced, frames.Count);
    }

    public void Reset()
    {
        frames.Clear();
        ResidentBytes = 0;
        EvictedCount = 0;
    }

    /// <summary>Recounts bytes after a bulk load.</summary>
    public void Remeasure()
    {
        ResidentBytes = 0;
        EvictedCount = 0;
        foreach (var e in frames)
        {
            if (e.Kind == PayloadKind.None) EvictedCount++;
            else if (e.Kind != PayloadKind.Archive) ResidentBytes += e.ResidentBytes + EntryOverhead;
        }
    }
}
