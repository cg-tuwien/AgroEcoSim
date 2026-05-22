using System.Numerics;
using System.Text.Json.Serialization;

namespace Agro;

public enum LeafComplexity { Simple, Compound }

public enum CompoundLeafType { Pinnate, Bipinnate, Palmate, Trifoliate }

public enum LeafMarginType { Entire, Serrate, Dentate, Crenate, Lobed, Undulate, Spiny }

public enum LeafVenationType { Pinnate, Palmate, Parallel, Dichotomous, Reticulate }

public enum LeafApexType { Acute, Acuminate, Obtuse, Rounded, Emarginate, Mucronate }

public enum LeafBaseType { Cuneate, Rounded, Cordate, Truncate, Oblique }

public readonly record struct LeafDimensions(
    float LengthM,
    float MaxWidthM,
    float ThicknessM,
    float PetioleLengthM,
    float Curvature01 // 0 flat; 1 strongly curved
)
{
    public static LeafDimensions Create(
        float lengthM = 0.08f,
        float widthM = 0.04f,
        float thicknessM = 0.00025f,
        float petioleM = 0.01f,
        float curvature01 = 0.15f)
        => new(lengthM, widthM, thicknessM, petioleM, curvature01);
}

public sealed record LeafMorphology
{
    public LeafComplexity Complexity { get; init; } = LeafComplexity.Simple;

    public LeafDimensions Dimensions { get; init; } = LeafDimensions.Create();

    public ILeafShape Shape { get; init; } = new EllipticLeafShape();

    public LeafMarginType Margin { get; init; } = LeafMarginType.Entire;
    public LeafVenationType Venation { get; init; } = LeafVenationType.Pinnate;
    public LeafApexType Apex { get; init; } = LeafApexType.Acute;
    public LeafBaseType Base { get; init; } = LeafBaseType.Cuneate;

    // Lobes / leaflets
    public int LobeCount { get; init; } = 0;

    // Compound config only used when Complexity == Compound
    public CompoundLeafDefinition? Compound { get; init; } = null;

    //Direct mesh
    public List<Vector3> LowResVertices { get; init; } = null;
    public List<Vector3> HighResVertices { get; init; } = null;
    public List<int> LowResIndices { get; init; } = null;
    public List<int> HighResIndices { get; init; } = null;

    public static LeafMorphology Default => new();

    public float BaseAreaM2() => Shape.AreaM2(Dimensions);


    internal void WriteMesh(BinaryWriter writer, int lod)
    {
        int segments = Math.Clamp(Shape.RecommendedSegments / Math.Max(1, lod), 12, 256);

        var vertices = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var indices = new int[segments * 3];

        vertices[0] = Vector3.Zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments;
            var p2 = Shape.OutlinePoint(Dimensions, t);         // local 2D
            var v3 = new Vector3(p2.X, 0f, p2.Y);         // XZ plane

            // Apply simple curvature as a function of y (length axis -> Z)
            var curvature = Dimensions.Curvature01;
            v3.Y = curvature * 0.1f * (1f - (p2.Y / (Dimensions.LengthM * 0.5f)) * (p2.Y / (Dimensions.LengthM * 0.5f)));

            vertices[i + 1] = v3;

            // Rough UVs normalized by dims
            uvs[i + 1] = new Vector2(
                0.5f + (p2.X / Math.Max(1e-6f, Dimensions.MaxWidthM)),
                0.5f + (p2.Y / Math.Max(1e-6f, Dimensions.LengthM))
            );
        }

        for (int i = 0; i < segments; i++)
        {
            int a = 0;
            int b = i + 1;
            int c = (i + 1) % segments + 1;

            indices[i * 3 + 0] = a;
            indices[i * 3 + 1] = b;
            indices[i * 3 + 2] = c;
        }
    }

    public bool TryGetDefaultLeafMesh(RenderLeafLod lod, out MeshData mesh)
    {
        if (lod == RenderLeafLod.CoarseOutline)
        {
            if (LowResVertices?.Count > 2 && LowResIndices?.Count > 2)
            {
                mesh = new MeshData()
                {
                    Positions = LowResVertices,
                    Indices = LowResIndices,
                };
                return true;
            }
            else
            {
                mesh = null;
                return false;
            }
        }
        else
        {
            if (HighResVertices?.Count > 2 && HighResIndices?.Count > 2)
            {
                mesh = new MeshData()
                {
                  Positions = HighResVertices,
                  Indices = HighResIndices,
                };
                return true;
            }
            else
            {
                mesh = null;
                return false;
            }
        }
    }
}

public enum LeafletAttachment { Sessile, Petiolulate }