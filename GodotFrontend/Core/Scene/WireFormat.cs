namespace GodotFrontend.Core.Scene;

/// <summary>Agro/Organs.cs OrganTypes, as written to the scene wire (one byte per organ).</summary>
public enum OrganType : byte
{
    Unspecified = 0, Seed = 1, Bud = 2, Root = 3, Stem = 4, Leaf = 5, Petiole = 6, Fruit = 7, Meristem = 8,
    RizomeMeristem = 9, FlowerBud = 10, FlowerStem = 11, FlowerPadel = 12, FlowerPedicel = 13, FlowerMeristem = 14,
    FlowerBaseBud = 15, FruitWing = 16,
}

/// <summary>helpers/Primitives.ts Primitives: how a decoded record is drawn.</summary>
public enum PrimitiveType : byte { Disk = 0, Rectangle = 1, Cylinder = 2, Sphere = 3, Box = 4, Ellipsoid = 5 }

/// <summary>V8 contact deformation record kinds.</summary>
public enum DeformKind : byte { None = 0, LeafFold = 1, PetioleBend = 2 }

[Flags]
public enum ExportFlags : byte { None = 0, Roots = 1, Analytics = 2 }

/// <summary>Slot layout of the 9-float stats record every drawn primitive carries (PlantColors.ts).</summary>
public static class Stat
{
    public const int Water = 0, Energy = 1, Auxins = 2, Cytokinins = 3, IrradianceOrWood = 4,
        DailyResource = 5, DailyProduction = 6, RelResource = 7, RelProduction = 8, Count = 9;
}

public static class WireFormat
{
    public static bool IsLamina(OrganType o) => o is OrganType.Leaf or OrganType.FlowerPadel or OrganType.FruitWing;
    public static bool IsMeshRecord(OrganType o) => IsLamina(o) || o == OrganType.Fruit;
    public static bool IsCylinderRecord(OrganType o) => o is OrganType.Stem or OrganType.Petiole or OrganType.Meristem
        or OrganType.RizomeMeristem or OrganType.FlowerBud or OrganType.FlowerStem or OrganType.FlowerPedicel or OrganType.FlowerMeristem;
}
