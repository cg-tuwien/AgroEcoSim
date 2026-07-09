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

        // Carex has long, narrow, grass-like leaves that arch outward
        LeafLength = 0.24f,
        LeafLengthVar = 0.08f,
        LeafRadius = 0.003f,       // ~3mm half-width = 6mm wide leaf
        LeafRadiusVar = 0.0008f,
        LeafPitchVar = 8f * DegToRad,
        LeafPitch = 12f * DegToRad,  // Small pitch - leaves grow mostly horizontal then arch down

        LeafGrowthTime = 24f * 18f * 2f,
        LeafGrowthTimeVar = 24f * 6f,
        MaxLeaveAge = 24f * 400f,

        PetioleLength = 0.015f,
        PetioleLengthVar = 0.004f,
        PetioleRadius = 0.0012f,
        PetioleRadiusVar = 0.0002f,

        pNewCrown = 0.70f,
        crownPitch = 0.50f,
        growthFactor = 0.15f,
        MaxRadius = 0.0025f,

        LateralPitch = 10f * DegToRad,
        LateralPitchVar = 8f * DegToRad,
        LateralRoll = 60f * DegToRad,
        LateralRollVar = 8f * DegToRad,

        LateralsPerNode = 2,
        NodeDistance = 0f,
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.80f,

        // Carex has short, creeping rhizomes (not thick)
        RizomeMaxDepth = 2,
        RizomeLength = 0.025f,
        RizomeRadius = 0.0015f,      // ~1.5mm - much thinner for a sedge
        pExpandRizome = 0.0008f,
        SympodialStartAgeHours = 24f * 50f,
        FloweringStartAgeHours = 24f * 80f,
        FloweringEndAgeHours = 24f * 180f,
        pChaningSeaonns = [0.008f, 0.002f, 0.003f, 0.0f],
        pFloweringSeaonns = [0.001f, 0.025f, 0.001f, 0.0f],
        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 3e6f,
        DepthFactor = 0.01f,
        FlowerElasticModulus = 3e7f,

        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.003f,
            PedalRadius = 0.001f,
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
            BudRadius = 0.0015f,
            BudBloomAge = 96u,
            FlowerMaxAge = 400u,

            PetiolLength = 0.004f,
            PetiolRadius = 0.00025f,
            PetiolLengthVar = 0.0015f,
            PetiolRadiusVar = 0.00008f,
            petiolSegments = 1,

            bStemLength = 0.005f,
            bStemLengthVar = 0.002f,
            bStemRadius = 0.0003f,
            bStemRadiusVar = 0.00008f,

            stemLength = 0.006f,
            stemLengthVar = 0.0015f,
            fStemRadius = 0.00025f,
            fStemRadiusVar = 0.00008f,

            flowerBaseDebth = 2,
            flowerDebth = 1,
            LateralsPerNode = 1,
            FlowersPerInternode = 1,
            clusterSize = 2,
            clusterAngle = 0.12f,
            deterministic = true,
            continous = false,
            internodeFlower = true,
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 0.4f,

            LateralAngle = 15,
            LateralRoll = 8,
            BaseLaterals = 1,
            BaseLateralAngle = 50,
            BaseLateralRoll = 6,
            pFlowerBaseDebth = 0.4f,

            LeafLength = 0.006f,
            LeafRadius = 0.003f,
            LeafLengthVar = 0.0015f,
            LeafRadiusVar = 0.0008f,
            LeavePetioleLength = 0.0015f,
            LeavePetioleRadius = 0.00015f,

            growthTime = 60f,
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
