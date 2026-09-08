namespace AgroServer.Models;

// Named wire types let the JSON source generator cover the stored-run endpoints.
public sealed record RunSummaryResponse(
    string Id, string Status, DateTime CreatedUtc, uint StoredFrames, long BytesOnDisk);

public sealed record RunResponse(
    string Id, string Status, DateTime CreatedUtc, ushort HoursPerTick, uint TotalSteps,
    uint StoredFrames, bool Analytics, bool Deterministic, long BytesOnDisk,
    string? ParentRunId, uint? BranchStep);
