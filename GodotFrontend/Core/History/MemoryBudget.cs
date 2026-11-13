namespace GodotFrontend.Core.History;

/// <summary>
/// How many bytes of frame payloads the client keeps. The web uses 45 % of the JS heap without a store and 15 % with
/// one; here the decoded scene and the MultiMesh buffers live in the same process, so the no-store share is lower.
/// </summary>
public static class MemoryBudget
{
    public const double FractionWithStore = 0.15;
    public const double FractionWithoutStore = 0.35;
    public const long Min = 512L << 20;
    public const long Max = 16L << 30;

    public static long Compute(bool hasStore, long? overrideBytes = null)
    {
        if (overrideBytes is > 0) return overrideBytes.Value;
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (total <= 0) total = 8L << 30;
        var fraction = hasStore ? FractionWithStore : FractionWithoutStore;
        return System.Math.Clamp((long)(total * fraction), Min, Max);
    }
}
