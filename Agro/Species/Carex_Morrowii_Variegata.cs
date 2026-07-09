using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Carex morrowii 'Variegata' (Japanese Sedge, Variegated Sedge)
/// Sources:
/// - RHS Plant Finder (evergreen/semi-evergreen sedge; arching linear leaves
///   with cream-white margins and green center; forms clumps 30-45cm tall)
/// - Missouri Botanical Garden Plant Finder (native to Japan; variegated foliage
///   green with white/cream edges; excellent for shade gardens; flowers insignificant)
/// - Gardenia.net (clump-forming perennial; leaves 15-40cm long, 6-8mm wide;
///   variegation most prominent in cool weather; may fade in hot sun)
/// </summary>
public static class Carex_Morrowii_Variegata
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Carex morrowii 'Variegata'",
        Aka = "Japanese Sedge",
        Behavior = Behavior.Herbaceous,

        Height = 0.40f,

        LeafLength = 0.25f,
        LeafLengthVar = 0.05f,
        LeafRadius = 0.004f,
        LeafRadiusVar = 0.001f,
        LeafPitchVar = 3f * (MathF.PI / 180f),
        LeafPitch = 80 * (MathF.PI / 180f),

        LeafGrowthTime = 24f * 14f * 2f,
        LeafGrowthTimeVar = 24f * 5f,
        MaxLeaveAge = 24f * 400f,

        PetioleLength = 0.02f,
        PetioleLengthVar = 0.005f,
        PetioleRadius = 0.0015f,
        PetioleRadiusVar = 0.0002f,

        pNewCrown = 0.65f,
        crownPitch = 0.55f,
        growthFactor = 0.18f,
        MaxRadius = 0.003f,

        LateralPitch = 12 * (MathF.PI / 180f),
        LateralPitchVar = 8 * (MathF.PI / 180f),
        LateralRoll = 50f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        LateralsPerNode = 2,
        NodeDistance = 0f,
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.75f,

        RizomeMaxDepth = 3,
        RizomeLength = 0.035f,
        RizomeRadius = 0.0035f,
        pExpandRizome = 0.0012f,
        SympodialStartAgeHours = 24f * 45f,
        FloweringStartAgeHours = 24f * 60f,
        FloweringEndAgeHours = 24f * 150f,
        pChaningSeaonns = [0.010f, 0.002f, 0.004f, 0.0f],
        pFloweringSeaonns = [0.002f, 0.035f, 0.002f, 0.0f],
        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 4e6f,
        DepthFactor = 0.01f,
        FlowerElasticModulus = 4e7f,

        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.003f,
            PedalRadius = 0.0015f,
            PedalLengthVar = 0.0005f,
            pedalGrowthTime = 48f,
            pedalGrowthTimeVar = 12f,

            HasFlowerBaseLeaves = false,
            FlowerPhenology = new LeafPhenology(
                expectedAge: 20f * 24f,
                colorModel: new ColorModel(
                    age: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(200, 195, 185))
                        .Add(0.25f, ColorUtils.OkLabFromSrgb8(190, 185, 175))
                        .Add(0.70f, ColorUtils.OkLabFromSrgb8(180, 175, 165))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(170, 165, 155)),
                    stress: new ColorCurve()
                        .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                        .Add(1.00f, new Vector3(-0.04f, 0.82f, 0.01f)),
                    senescenceSeason: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(185, 175, 150))
                        .Add(0.40f, ColorUtils.OkLabFromSrgb8(165, 150, 120))
                        .Add(0.75f, ColorUtils.OkLabFromSrgb8(140, 125, 95))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(110, 95, 70))
                )
            ),

            PetalSenescenceDurationH = 120f,

            BudLength = 0.003f,
            BudRadius = 0.002f,
            BudBloomAge = 96u,
            FlowerMaxAge = 400u,

            PetiolLength = 0.005f,
            PetiolRadius = 0.0003f,
            PetiolLengthVar = 0.002f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            bStemLength = 0.006f,
            bStemLengthVar = 0.003f,
            bStemRadius = 0.0004f,
            bStemRadiusVar = 0.0001f,

            stemLength = 0.008f,
            stemLengthVar = 0.002f,
            fStemRadius = 0.0003f,
            fStemRadiusVar = 0.0001f,

            flowerBaseDebth = 3,
            flowerDebth = 1,
            LateralsPerNode = 1,
            FlowersPerInternode = 1,
            clusterSize = 2,
            clusterAngle = 0.15f,
            deterministic = true,
            continous = false,
            internodeFlower = true,
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 0.5f,

            LateralAngle = 20,
            LateralRoll = 10,
            BaseLaterals = 1,
            BaseLateralAngle = 55,
            BaseLateralRoll = 8,
            pFlowerBaseDebth = 0.5f,

            LeafLength = 0.008f,
            LeafRadius = 0.004f,
            LeafLengthVar = 0.002f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.002f,
            LeavePetioleRadius = 0.0002f,

            growthTime = 72f,
        },

        LeafPhenology = new LeafPhenology(
            expectedAge: 1.5f * 365f * 24f,
            colorModel: new ColorModel(
                CarexMorrowiiVariegataCenterPreset.Lifecycle,
                CarexMorrowiiVariegataCenterPreset.Stress,
                CarexMorrowiiVariegataCenterPreset.SeasonalSenescence,
                CarexMorrowiiVariegataCenterPreset.StressSenescence
            )
        ),

        // Placeholder for leaf shape - to be replaced with actual Carex linear leaf mesh later
        // Leaves are linear/grass-like with parallel venation and cream-white margins
        LeafMorphology = new() {
            LowResVertices = null,
            LowResIndices = null,
            HighResVertices = null,
            HighResIndices = null,
        },

        // Note: Carex morrowii 'Variegata' has variegated foliage with green center
        // and cream/white margins. This requires dual-color rendering which is not
        // yet fully supported. The current settings use the center green color.
        // Future enhancement: implement margin stripe rendering.
    };
}
