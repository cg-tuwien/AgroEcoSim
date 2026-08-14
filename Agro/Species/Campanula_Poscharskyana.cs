using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Campanula poscharskyana (Serbian Bellflower, Trailing Bellflower)
/// Sources:
/// - Missouri Botanical Garden Plant Finder (native to Croatia; trailing perennial;
///   rounded to heart-shaped leaves 2-5 cm wide; star-shaped blue-violet flowers 2-3 cm;
///   flower panicles 15-30 cm tall summer to early autumn)
/// - RHS Plant Finder (semi-evergreen; mat-forming; excellent for walls/rockeries;
///   flowers lavender-blue to violet, sometimes white cultivars)
/// - Gardenia.net (vigorous ground cover; leaves coarsely toothed;
///   flower spikes 20-30 cm; blooms June-September)
/// </summary>
public static class Campanula_Poscharskyana
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Campanula poscharskyana",
        Aka = "Serbian Bellflower",
        Behavior = Behavior.Herbaceous,

        Height = 0.3f,

        LeafLength = 0.04f,
        LeafLengthVar = 0.008f,
        LeafRadius = 0.02f,
        LeafRadiusVar = 0.006f,
        LeafPitchVar = 5f * (MathF.PI / 180f),
        LeafPitch = 85 * (MathF.PI / 180f),

        LeafGrowthTime = 24f * 7f * 2f,
        LeafGrowthTimeVar = 24f * 3f,
        MaxLeaveAge = 24 * 150,

        PetioleLength = 0.02f,
        PetioleLengthVar = 0.010f,
        PetioleRadius = 0.0015f,
        PetioleRadiusVar = 0.0002f,

        pNewCrown = 0.850f,
        crownPitch = 0.55f,
        growthFactor = 0.8f,
        MaxRadius = 0.004f,

        LateralPitch = 15 * (MathF.PI / 180f),
        LateralPitchVar = 10 * (MathF.PI / 180f),
        LateralRoll = 45f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        LateralsPerNode = 2,
        NodeDistance = 0.01f,
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.7f,

        RizomeMaxDepth = 3,
        RizomeLength = 0.04f,
        RizomeRadius = 0.0025f,
        RhizomeOffshootProbabilityPerHour = 0.00016f,
        FloweringStartAgeHours = 24f * 45f,
        FloweringDurationAgeHours = 24f * 55f,

        ProbabilityPreSeasonChaining = 0.015f,
        ProbabilitySeasonChaining = 0.004f,
        ProbabilityPostSeasonChaining = 0.008f,
        ProbabilityRestSeasonChaining = 0f,

        ProbabilityPreSeasonFlowering = 0.004f,
        ProbabilitySeasonFlowering = 0.05f,
        ProbabilityPostSeasonFlowering = 0.004f,
        ProbabilityRestSeasonFlowering = 0f,


        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 4e6f,
        DepthFactor = 0.01f,
        FlowerElasticModulus = 4e7f,

        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.010f,
            PedalRadius = 0.004f,
            PedalLengthVar = 0.002f,
            pedalGrowthTime = 48f,
            pedalGrowthTimeVar = 12f,

            HasFlowerBaseLeaves = false,
            FlowerPhenology = new LeafPhenology(
                expectedAge: 21f * 24f,
                colorModel: new ColorModel(
                    age: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(130, 110, 200))
                        .Add(0.25f, ColorUtils.OkLabFromSrgb8(120, 100, 190))
                        .Add(0.70f, ColorUtils.OkLabFromSrgb8(110, 95, 180))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(100, 88, 165)),
                    stress: new ColorCurve()
                        .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                        .Add(1.00f, new Vector3(-0.04f, 0.80f, 0.01f)),
                    senescenceSeason: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(140, 120, 160))
                        .Add(0.40f, ColorUtils.OkLabFromSrgb8(125, 105, 140))
                        .Add(0.75f, ColorUtils.OkLabFromSrgb8(105, 88, 115))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(85, 70, 90))
                )
            ),

            PetalSenescenceDurationH = 120f,

            BudLength = 0.006f,
            BudRadius = 0.0035f,
            BudBloomAge = 100u,
            FlowerMaxAge = 500u,

            PetiolLength = 0.006f,
            PetiolRadius = 0.0004f,
            PetiolLengthVar = 0.002f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            bStemLength = 0.006f,
            bStemLengthVar = 0.003f,
            bStemRadius = 0.0005f,
            bStemRadiusVar = 0.0001f,

            stemLength = 0.010f,
            stemLengthVar = 0.002f,
            fStemRadius = 0.0004f,
            fStemRadiusVar = 0.0001f,

            flowerBaseDebth = 4,
            flowerDebth = 2,
            LateralsPerNode = 2,
            FlowersPerInternode = 2,
            clusterSize = 3,
            clusterAngle = 0.20f,
            deterministic = true,
            continous = false,
            internodeFlower = true,
            internodeFlowerWithStem = true,
            floralDepthFactor = 1,
            pFlowerDebth = 0.5f,

            LateralAngle = 30,
            LateralRoll = 15,
            BaseLaterals = 1,
            BaseLateralAngle = 60,
            BaseLateralRoll = 10,
            pFlowerBaseDebth = 0.5f,

            LeafLength = 0.010f,
            LeafRadius = 0.005f,
            LeafLengthVar = 0.002f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.003f,
            LeavePetioleRadius = 0.0003f,

            growthTime = 80f,
        },

        LeafPhenology = new LeafPhenology(
            expectedAge: 1.5f * 365f * 24f,
            colorModel: new ColorModel(
                CampanulaPoscharskyanaPreset.Lifecycle,
                CampanulaPoscharskyanaPreset.Stress,
                CampanulaPoscharskyanaPreset.SeasonalSenescence,
                CampanulaPoscharskyanaPreset.StressSenescence
            )
            {
                HealthySeasonCurve = CampanulaPoscharskyanaPreset.HealthySeason,
                HealthySeasonDirection = CampanulaPoscharskyanaPreset.HealthySeasonDirection,
            }
        ),

        // Placeholder for leaf shape - to be replaced with actual Campanula leaf mesh later
        // Leaves are rounded to heart-shaped with coarsely toothed margins
        LeafMorphology = new() {
            LowResVertices = null,
            LowResIndices = null,
            HighResVertices = null,
            HighResIndices = null,
        },
    };
}

/// <summary>
/// Why these colors: Campanula poscharskyana foliage is semi-evergreen, medium green
/// with a slightly gray-green cast. Leaves may develop subtle bronze/purple tints in
/// cool weather. The lifecycle stays mostly green with mild seasonal variation,
/// while senescence follows a green→olive→brown path typical of semi-evergreen perennials.
/// Sources:
/// - Missouri Botanical Garden: "leaves are rounded to heart-shaped, coarsely toothed,
///   medium green"
/// - RHS: "foliage semi-evergreen, may tint bronze-purple in winter"
/// - Gardenia.net: "gray-green foliage, excellent ground cover"
/// </summary>
public static class CampanulaPoscharskyanaPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(170, 215, 150)) // fresh light green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(110, 170, 105)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 95, 155,  95)) // mature green (slightly gray-green)
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 88, 145,  90)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 82, 138,  85)); // full adult, stable

    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.000f, 1.00f, 0.000f)) // early spring neutral
        .Add(0.25f, new Vector3( 0.000f, 1.00f, 0.000f)) // spring/summer unchanged
        .Add(0.60f, new Vector3( 0.000f, 1.00f, 0.000f)) // still neutral
        .Add(0.75f, new Vector3(-0.006f, 1.01f, 0.006f)) // early autumn: hint of warmth
        .Add(0.88f, new Vector3(-0.014f, 1.03f, 0.012f)) // late autumn: subtle bronze/purple tint
        .Add(1.00f, new Vector3(-0.020f, 1.04f, 0.015f)); // winter: deeper tone, semi-evergreen

    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(0.60f, -0.80f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(165, 150,  85)) // olive-gold
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(150, 115,  65)) // warm bronze
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(130,  90,  60)) // reddish-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90,  65,  40)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(120, 120,  70)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(110,  85,  50)) // stress brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 80,  58,  35)); // dark brown
}
