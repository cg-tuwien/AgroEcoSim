namespace GodotFrontend.Core.Scene;

/// <summary>
/// One decoded frame as a structure of arrays: no per-primitive objects, so 1.26M primitives cost about 100 bytes
/// each and the packer and the picker walk flat arrays. Buds are decoded and dropped, exactly as the web app does.
/// The transform is kept in WIRE order (row-major 3x4, cylinders not yet scaled or recentred): the packer needs
/// exactly those floats because they are Godot's MultiMesh layout, and the picker needs the base and the axes.
/// </summary>
public sealed class DecodedScene
{
    public byte Version;
    public bool RootsDownloaded, Analytics, HasRootGeometry, VirtualRoots;

    // ---- plants ----
    public int PlantCount;
    public int[] PlantSpecies = Array.Empty<int>();      // index into Species
    public int[] PlantAsset = Array.Empty<int>();        // index into Assets, -1 for V7
    public uint[] PlantSourceIndex = Array.Empty<uint>(); // V9 request order; uint.MaxValue otherwise
    public int[] PlantFirst = Array.Empty<int>();        // first drawn primitive of the plant
    public int[] PlantDrawCount = Array.Empty<int>();
    public float[] PlantBoundsMin = Array.Empty<float>(); // 3 per plant
    public float[] PlantBoundsMax = Array.Empty<float>();
    public List<string> Species = new();
    public List<string> Assets = new();                  // recorded asset ids referenced by this frame

    // ---- drawn primitives ----
    public int Count;
    public float[] Transform = Array.Empty<float>();     // 12 per primitive, wire order
    public PrimitiveType[] Kind = Array.Empty<PrimitiveType>();
    public OrganType[] Organ = Array.Empty<OrganType>();
    public float[] Length = Array.Empty<float>();        // cylinders and roots; 0 otherwise
    public float[] Radius = Array.Empty<float>();
    public byte[] Rgb = Array.Empty<byte>();             // 3 per primitive, sRGB bytes (roots: none, see HasColor)
    public byte[] Flags = Array.Empty<byte>();           // PrimFlags
    public float[]? Stats;                               // 9 per primitive; null when the frame carries no analytics
    public ushort[] LaminaSlice = Array.Empty<ushort>(); // chain position of a leaf, 0 otherwise
    public int[] Plant = Array.Empty<int>();             // primitive -> plant
    public int[] DeformIndex = Array.Empty<int>();       // -1 or index into Deform (4 floats each)
    public List<float> Deform = new();
    public byte[] DeformKinds = Array.Empty<byte>();     // parallel to Deform entries (DeformKind)

    public float[] SceneMin = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
    public float[] SceneMax = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };

    public ReadOnlySpan<float> TransformOf(int i) => Transform.AsSpan(i * 12, 12);
    public ReadOnlySpan<float> StatsOf(int i) => Stats is null ? ReadOnlySpan<float>.Empty : Stats.AsSpan(i * Stat.Count, Stat.Count);
    public bool HasColor(int i) => (Flags[i] & PrimFlags.HasColor) != 0;
    public bool IsDeformed(int i) => (Flags[i] & PrimFlags.Deformed) != 0;

    /// <summary>Index of the primitive within its plant (what BuildScene's `j` is), used by the legacy green palette.</summary>
    public int IndexInPlant(int i) => i - PlantFirst[Plant[i]];
}

public static class PrimFlags
{
    public const byte HasColor = 1;
    public const byte Deformed = 2;
}
