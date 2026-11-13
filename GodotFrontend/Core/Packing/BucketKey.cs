namespace GodotFrontend.Core.Packing;

/// <summary>Which material a bucket is drawn with.</summary>
public enum BucketVariant : byte
{
    /// <summary>Leaves and petals: double-sided leaf BSDF.</summary>
    Foliage = 0,
    /// <summary>Leaves with a non-zero V8 fold: foliage plus the fold texture and flat shading (reserved; pass 1 draws them flat).</summary>
    FoliageFolded = 1,
    /// <summary>Fruit wings: double-sided, no leaf BSDF.</summary>
    LaminaOpaque = 2,
    /// <summary>Fruits: mirrored matrices, so drawn cull-disabled.</summary>
    Fruit = 3,
    /// <summary>Cylinders, joints, roots: back-face culled Lambert.</summary>
    Opaque = 4,
}

/// <summary>What the visibility toggles switch; one MultiMeshInstance3D visibility flag per category.</summary>
public enum Category : byte { Leaves = 0, FruitWings = 1, Fruits = 2, Stems = 3, Roots = 4 }

/// <summary>One MultiMesh bucket identity: geometry x material x toggle category, packed into an int for dictionary keys.</summary>
public readonly record struct BucketKey(int GeometryId, BucketVariant Variant, Category Category)
{
    public int Packed => (GeometryId << 8) | ((int)Variant << 4) | (int)Category;
    public static BucketKey Unpack(int packed) => new(packed >> 8, (BucketVariant)((packed >> 4) & 15), (Category)(packed & 15));
}
