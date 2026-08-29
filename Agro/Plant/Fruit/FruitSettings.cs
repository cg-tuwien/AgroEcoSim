using System.Numerics;

namespace Agro;

/// <summary>
/// Fruit settings for Fragaria vesca 'Rügen'
/// Alpine strawberries produce small aggregate accessory fruits continuously
/// through the growing season once established.
/// </summary>
public class FruitSettings
{
    public FruitType FruitType { get; init; } = FruitType.Accessory;

    public float InitialRadius { get; init; } = 0.002f;
    public float MatureRadius { get; init; } = 0.006f;
    public float InitialLength { get; init; } = 0.003f;
    public float MatureLength { get; init; } = 0.008f;

    public float GrowthTimeHours { get; init; } = 24f * 21f;
    public float RipeningTimeHours { get; init; } = 24f * 7f;

    public Vector3 UnripeColor { get; init; }
    public Vector3 RipeningColor { get; init; }
    public Vector3 MatureColor { get; init; }
    public Vector3 OverripeColor { get; init; }

    public int SeedCount { get; init; } = 150;
    public float SeedRadius { get; init; } = 0.0004f;
    public Vector3 SeedColor { get; init; }

    public float FruitPerFlowerProbability { get; init; } = 0.85f;
    public bool PollinationRequired { get; init; } = true;
    public float PollinationWindowHours { get; init; } = 48f;
    public float FruitRetentionDays { get; init; } = 14f;
}