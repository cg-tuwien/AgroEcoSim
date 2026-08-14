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
        MaxLeaveAge = 24 * 400,

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
        RhizomeOffshootProbabilityPerHour = 0.0008f,
        FloweringStartAgeHours = 24f * 80f,
        FloweringDurationAgeHours = 24 * 30,
        FloweringStartSeason = 0f,
        FloweringEndSeason = 1f / 11,

        ProbabilityPreSeasonChaining = 0.008f,
        ProbabilitySeasonChaining = 0.002f,
        ProbabilityPostSeasonChaining = 0.003f,
        ProbabilityRestSeasonChaining = 0f,

        ProbabilityPreSeasonFlowering = 0.001f,
        ProbabilitySeasonFlowering = 0.025f,
        ProbabilityPostSeasonFlowering = 0.001f,
        ProbabilityRestSeasonFlowering = 0f,

        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 3e6f,
        DepthFactor = 0.01f,
        FlowerElasticModulus = 3e7f,

        FlowerSettings = new FlowerSettings
        {
            // Carex flowers grow on tall upright stems (culms) that rise above foliage
            PedalLength = 0.002f,        // Very small - Carex flowers are inconspicuous
            PedalRadius = 0.0008f,       // Tiny spikelet scales
            PedalLengthVar = 0.0005f,
            pedalGrowthTime = 72f,
            pedalGrowthTimeVar = 18f,
            pedalCount = 1,              // Single spikelet structure, not radial petals

            HasFlowerBaseLeaves = false,
            FlowerPhenology = new LeafPhenology(
                expectedAge: 30f * 24f,
                colorModel: new ColorModel(
                    age: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(140, 135, 110))  // young greenish-brown
                        .Add(0.25f, ColorUtils.OkLabFromSrgb8(130, 125, 100))  // developing
                        .Add(0.70f, ColorUtils.OkLabFromSrgb8(120, 115, 90))   // mature brown
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(110, 105, 80)),  // senescing
                    stress: new ColorCurve()
                        .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                        .Add(1.00f, new Vector3(-0.04f, 0.82f, 0.01f)),
                    senescenceSeason: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(135, 130, 105))
                        .Add(0.40f, ColorUtils.OkLabFromSrgb8(125, 120, 95))
                        .Add(0.75f, ColorUtils.OkLabFromSrgb8(115, 110, 85))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(100, 95, 70))
                )
            ),

            PetalSenescenceDurationH = 240f,

            BudLength = 0.004f,
            BudRadius = 0.002f,
            BudBloomAge = 120u,
            FlowerMaxAge = 600u,

            // Flower stem (culm) settings - tall and upright
            PetiolLength = 0.003f,       // Minimal pedicel
            PetiolRadius = 0.0003f,
            PetiolLengthVar = 0.001f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            // Base stem that elevates the flower - this is the main culm
            bStemLength = 0.18f,         // ~18cm tall flowering stem (reaches leaf height)
            bStemLengthVar = 0.04f,
            bStemRadius = 0.0012f,       // Thin but sturdy
            bStemRadiusVar = 0.0002f,

            stemLength = 0.004f,         // Minimal additional stem segments
            stemLengthVar = 0.001f,
            fStemRadius = 0.0004f,
            fStemRadiusVar = 0.0001f,

            flowerBaseDebth = 1,         // Single flowering stem from base
            flowerDebth = 1,
            LateralsPerNode = 0,         // No lateral branches on flower stem
            FlowersPerInternode = 1,
            clusterSize = 1,             // Single terminal spikelet
            clusterAngle = 0.05f,
            deterministic = true,
            continous = false,
            internodeFlower = false,     // Terminal flower only
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 0.3f,

            LateralAngle = 5,            // Nearly vertical
            LateralRoll = 3,
            BaseLaterals = 0,            // No basal laterals on flower stem
            BaseLateralAngle = 5,
            BaseLateralRoll = 3,
            pFlowerBaseDebth = 0.3f,

            // Spikelet bracts (reduced leaves on flower stem)
            LeafLength = 0.015f,
            LeafRadius = 0.0015f,
            LeafLengthVar = 0.004f,
            LeafRadiusVar = 0.0004f,
            LeavePetioleLength = 0.002f,
            LeavePetioleRadius = 0.00015f,

            growthTime = 96f,
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
