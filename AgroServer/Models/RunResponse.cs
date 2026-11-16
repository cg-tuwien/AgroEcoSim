namespace AgroServer.Models;

// Named wire types let the JSON source generator cover the stored-run endpoints.

/// <summary>
/// One stored run, as both the listing and the single-run endpoint report it.
///
/// One type rather than a listing summary and a detail record. They used to differ, and the
/// listing carried five fields - which meant a panel that wanted to show anything about a run had
/// to fetch every run individually, a hundred requests to draw one table. The whole record is a
/// few hundred bytes against the gigabytes of frames it describes, so there is nothing to save by
/// sending less of it.
///
/// Field notes, where the name is not the whole story:
///   StoredFrames  frames actually on disk; below LastStep whenever a frame stride was used
///   LastStep      the step the run reached, plus one - the honest progress figure
///   Trays         soil fields: 1 for a plain box field, one per tray for an imported facade.
///                 0 means NOT RECORDED rather than none: the number comes out of the model, so
///                 runs stored before it was counted cannot report one after the fact
///   BytesOnDisk   frame bytes only; AuxBytesOnDisk is the request, index, terrain and measurement
///   HasFieldModel whether the model behind this run is still stored, and so whether it can be
///                 re-run or drawn with its trays without the user uploading the OBJ again
///   StopReason    why an aborted run stopped, in words; a non-live row still marked running is
///                 reported as aborted, because nothing is computing it any more
/// </summary>
public sealed record RunResponse(
    string Id,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    ushort HoursPerTick,
    uint TotalSteps,
    uint StoredFrames,
    uint LastStep,
    int TotalHours,
    string InitialDay,
    [property: System.Text.Json.Serialization.JsonNumberHandling(
        System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString |
        System.Text.Json.Serialization.JsonNumberHandling.WriteAsString)] ulong Seed,
    byte RenderMode,
    uint Plants,
    uint Trays,
    uint Obstacles,
    uint Species,
    bool Analytics,
    bool Deterministic,
    long BytesOnDisk,
    long AuxBytesOnDisk,
    string? FieldModelName,
    bool HasFieldModel,
    bool HasTerrain,
    bool HasObstacleLight,
    /// <summary>Whether this run can be RESUMED - carried on from where it stopped - rather than
    /// recomputed from its request.</summary>
    bool HasCheckpoint,
    string? ParentRunId,
    uint? BranchStep,
    //Why the run stopped before its horizon, as a clause that reads after "stopped: " - the volume
    //reached its reserve, a frame could not be written, nobody was watching. Null for a run that
    //finished, and for runs stored before the reason was recorded.
    string? StopReason,
    bool Protected);

/// <summary>Null rejects an omitted flag instead of accidentally unprotecting a run.</summary>
public sealed record RunProtectionRequest(bool? Protected);

/// <summary>
/// The run store as the Runs tab needs to see it: whether it works at all, where it writes, how much
/// room is left there, what is wrong with that, and the newest events.
///
/// The listing alone cannot say any of this. A store that could not open answered it with an empty
/// array, and an empty array is what an empty store returns too - so the panel printed "No stored
/// runs" over a disabled store with 56 GB on its disk, and new runs went unstored without a word.
///
/// Field notes:
///   DisabledReason  why the store could not open, with what to do about it; empty when it works
///   MountPoint      the mount containing Root; VolumeName the Docker volume behind it, if any
///   FreeBytes       statvfs on Root; -1 when unknown. Runs stop cleanly at MinFreeBytes
///   StoredBytes     the runs' footprint (frames and the rest); ModelBytes the field models
///   Warnings        configuration that will lose data, worked out now - not a log
/// </summary>
public sealed record RunStoreStatusResponse(
    bool Enabled,
    string Root,
    string DisabledReason,
    string MountPoint,
    string FileSystem,
    string VolumeName,
    bool AnonymousVolume,
    long TotalBytes,
    long FreeBytes,
    long MinFreeBytes,
    long RetentionBytes,
    long ModelRetentionBytes,
    long StoredBytes,
    long ModelBytes,
    int Runs,
    int LiveRuns,
    double SweepMinutes,
    string[] Warnings,
    Services.RunStoreEvent[] Events);

/// <summary>What a refused delete tells the user. A 409 with no body reads as a bug.</summary>
public sealed record RunDeleteResponse(bool Deleted, string Reason);

/// <summary>
/// How a run's terrain was built, filed beside the run.
///
/// None of it is recoverable from request.json. The model is named there only by its FILE NAME,
/// never its content hash, and the regex and resolution in the request are the ones in force when
/// the run STARTED - the trays were built from whatever was on screen when the OBJ was uploaded,
/// which is not the same thing. So the values that actually produced this run are written down
/// where the run is, and a store whose database is lost can recover them with the frames.
/// </summary>
public sealed record StoredTerrainMeta(
    string? ModelHash, string? ModelName, string? Regex, bool RegexForMaterials, float Resolution);
