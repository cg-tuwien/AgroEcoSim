namespace GodotFrontend.Core.Geometry;

/// <summary>render/core/GeometryIds.ts: the ten static geometries, then runtime ids from FirstDynamic.</summary>
public static class Geom
{
    public const int BoxCentered = 0;
    public const int BoxCorner = 1;
    public const int BoxBottomCentered = 2;
    public const int Cylinder = 3;
    public const int Sphere = 4;
    public const int Plane = 5;
    public const int CircleHalf = 6;
    public const int CircleUnit = 7;
    public const int Dodecahedron = 8;
    public const int DebugBox = 9;
    public const int StaticCount = 10;
    public const int FirstDynamic = 10;

    public static string LeafKey(string species, int slice) => $"leaf:{species}:{slice}";
    public static string PetalKey(string species) => $"petal:{species}";
    public static string RecordedLeafKey(string id, int slice) => $"recorded:{id}:leaf:{slice}";
    public static string RecordedPetalKey(string id) => $"recorded:{id}:petal";
    public static string RecordedFruitKey(string id) => $"recorded:{id}:fruit";
    public static string RecordedFruitWingKey(string id) => $"recorded:{id}:fruit-wing";
}
