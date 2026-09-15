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
    ulong Seed,
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
    string? ParentRunId,
    uint? BranchStep);

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
