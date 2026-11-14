using System.Numerics;

namespace Agro.Species;
/// <summary>Shared conservative woody defaults. Rates and elastic constants are model parameters.</summary>
internal static class TreeSpecies
{
    internal static SpeciesSettings Create(string name, string common) => new()
    {
        Name = name,
        Aka = common,
        Behavior = Behavior.Woody,
        NodeDistance = .08f,
        NodeDistanceVar = .015f,
        MonopodialFactor = 1f,
        DominanceFactor = .72f,
        LateralsPerNode = 1,
        LateralRoll = MathF.PI,
        LateralRollVar = .04f,
        LateralPitch = .85f,
        LateralPitchVar = .1f,
        WoodGrowthTime = 24f * 70f,
        WoodGrowthTimeVar = 24f * 10f,
        LeafGrowthTime = 24f * 20f,
        LeafGrowthTimeVar = 24f * 3f,
        LeafSenescenceSeasonalDuration = 24f * 28f,
        LeafSenescenceSeasonalDurationVar = 0f,
        SeasonalSenescence = .64f,
        LeafKeelThickness = .001f,
        LeafElasticModulus = 8e7f,
        GreenElasticModulus = 4e8f,
        WoodElasticModulus = 8e9f,
        BasalGreenElasticModulus = 8e8f,
        FlowerOrganElasticModulus = 5e7f,
        FlowerScapeElasticModulus = 2e8f,
        ShootMaximumRadius = .5f,
        RootsDensity = .5f,
        RootRadiusTaper = .65f,
        VegetativeTemperatureResponseEnabled = true,
        VegetativeGrowthBaseTemperatureC = 5f,
        VegetativeGrowthFullTemperatureC = 18f,
        VegetativeGrowthLightReferenceWhM2 = 1800f,
        TreeShortShootShadeDiebackDays = 180f,
        LeafPhenology = Phenology(new(130, 175, 85), new(65, 125, 52), new(219, 181, 55)),
    };
    internal static LeafPhenology Phenology(Vector3 young, Vector3 mature, Vector3 autumn) => new(24f * 160f, new ColorModel(new ColorCurve().Add(0, ColorUtils.OkLabFromSrgb8((byte)young.X, (byte)young.Y, (byte)young.Z)).Add(.25f, ColorUtils.OkLabFromSrgb8((byte)mature.X, (byte)mature.Y, (byte)mature.Z)).Add(1, ColorUtils.OkLabFromSrgb8((byte)mature.X, (byte)mature.Y, (byte)mature.Z)), ColorModel.DefaultStress, new ColorCurve().Add(0, ColorUtils.OkLabFromSrgb8((byte)autumn.X, (byte)autumn.Y, (byte)autumn.Z)).Add(1, ColorUtils.OkLabFromSrgb8(137, 97, 50))));
    internal static FlowerSettings Flowers(float petalLength, float petalWidth, Vector3 color, float pedicelLength = .015f) => new()
    {
        FlowerPedicelLength = pedicelLength,
        FlowerPedicelRadius = .00065f,
        BudLength = .003f,
        BudRadius = .0015f,
        BudBloomDurationHours = 24f * 6f,
        PetalLength = petalLength,
        PetalRadius = petalWidth * .5f,
        PetalCount = 5,
        PetalAndPedicelGrowthDurationHours = 24f * 4f,
        FlowerMaximumAgeHours = 24f * 18f,
        PetalSenescenceDurationHours = 24f * 3f,
        FlowerCentreLength = .002f,
        FlowerCentreRadius = .0012f,
        PetalMorphology = LeafMorphology.Petal,
        FlowerPhenology = new(24f * 18f, new ColorModel(new ColorCurve().Add(0, ColorUtils.OkLabFromSrgb8((byte)color.X, (byte)color.Y, (byte)color.Z)).Add(1, ColorUtils.OkLabFromSrgb8((byte)color.X, (byte)color.Y, (byte)color.Z)), ColorModel.DefaultStress, ColorModel.DefaultSeasonalSenescence)),
    };
}
