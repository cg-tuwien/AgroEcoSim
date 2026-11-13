namespace GodotFrontend.Core.Packing;

/// <summary>
/// One MultiMesh bucket's instance data: <see cref="Stride"/> floats per instance in Godot's MultiMesh buffer layout -
/// the 3x4 transform as three rows of (basis row, origin component), then RGBA colour, then 4 floats of custom data.
/// Arrays are kept at capacity and reused frame to frame; <see cref="Count"/> is the number of live instances.
/// </summary>
public sealed class Bucket
{
    public const int Stride = 20;

    public readonly BucketKey Key;
    public float[] Data;
    public int Count;
    /// <summary>Primitive index of each slot, for diagnostics and highlight passes.</summary>
    public int[] PrimitiveOf;

    public Bucket(BucketKey key, int capacity)
    {
        Key = key;
        Data = new float[capacity * Stride];
        PrimitiveOf = new int[capacity];
    }

    public int Capacity => PrimitiveOf.Length;

    public void EnsureCapacity(int needed)
    {
        if (needed <= Capacity) return;
        var cap = System.Math.Max(needed, Capacity + Capacity / 2);
        Array.Resize(ref Data, cap * Stride);
        Array.Resize(ref PrimitiveOf, cap);
    }
}

/// <summary>A decoded frame packed into buckets, plus the counters the HUD shows.</summary>
public sealed class PackedFrame
{
    public readonly Dictionary<int, Bucket> Buckets = new();
    public int InstanceCount;
    public long TriangleCount;
    public float[] SceneMin = new float[3];
    public float[] SceneMax = new float[3];
    /// <summary>Geometry id each primitive was drawn with (indexed by primitive), for the picker.</summary>
    public int[] GeometryOf = Array.Empty<int>();
    /// <summary>Identity of the decoded frame this packing came from (a step number or a session index).</summary>
    public long FrameId = -1;

    public Bucket GetOrAdd(BucketKey key, int capacityHint)
    {
        var packed = key.Packed;
        if (!Buckets.TryGetValue(packed, out var b))
        {
            b = new Bucket(key, System.Math.Max(16, capacityHint));
            Buckets[packed] = b;
        }
        return b;
    }

    public void ResetCounts()
    {
        foreach (var b in Buckets.Values) b.Count = 0;
        TriangleCount = 0;
        InstanceCount = 0;
        TriangleCount = 0;
    }
}
