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
        LeafMaximumAgeHours = 24 * 400,

        PetioleLength = 0.015f,
        PetioleLengthVar = 0.004f,
        PetioleRadius = 0.0012f,
        PetioleRadiusVar = 0.0002f,

        CrownBudActivationProbabilityPerSeason = 0.70f,
        CrownPitch = 90f * DegToRad,
        HerbaceousGrowthRateScale = 0.15f,
        ShootMaximumRadius = 0.0025f,

        LateralPitch = 10f * DegToRad,
        LateralPitchVar = 8f * DegToRad,
        LateralRoll = 60f * DegToRad,
        LateralRollVar = 8f * DegToRad,

        LateralsPerNode = 2,
        NodeDistance = 0f,
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.80f,

        // Carex has short, creeping rhizomes (not thick)
        RhizomeMaximumSegmentDepth = 2,
        RhizomeSegmentLength = 0.025f,
        RhizomeSegmentRadius = 0.0015f,      // ~1.5mm - much thinner for a sedge
        RhizomeOffshootProbabilityPerHour = 0.0008f,
        FloweringStartAgeHours = 24f * 80f,
        FloweringPhaseDurationHours = 24 * 30,
        FloweringSeasonStart = 0f,
        FloweringSeasonEnd = 1f / 11f,

        PreFloweringCrownContinuationProbabilityPerHour = 0.008f,
        FloweringCrownContinuationProbabilityPerHour = 0.002f,
        PostFloweringCrownContinuationProbabilityPerHour = 0.003f,
        DormantCrownContinuationProbabilityPerHour = 0f,

        PreFloweringCommitmentProbabilityPerHour = 0.001f,
        FloweringCommitmentProbabilityPerHour = 0.025f,
        PostFloweringCommitmentProbabilityPerHour = 0.001f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,

        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 3e6f,
        FlowerOrganElasticModulus = 3e7f,

        FlowerSettings = new FlowerSettings
        {
            // Carex flowers grow on tall upright stems (culms) that rise above foliage
            PetalLength = 0.002f,        // Very small - Carex flowers are inconspicuous
            PetalRadius = 0.0008f,       // Tiny spikelet scales
            PetalLengthVariance = 0.0005f,
            PetalAndPedicelGrowthDurationHours = 72f,
            PetalAndPedicelGrowthDurationVarianceHours = 18f,
            PetalCount = 1,              // Single spikelet structure, not radial petals

            CreateLeavesOnFlowerBase = false,
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

            PetalSenescenceDurationHours = 240f,

            BudLength = 0.004f,
            BudRadius = 0.002f,
            BudBloomDurationHours = 120u,
            FlowerMaximumAgeHours = 600u,

            // Flower stem (culm) settings - tall and upright
            FlowerPedicelLength = 0.003f,       // Minimal pedicel
            FlowerPedicelRadius = 0.0003f,
            FlowerPedicelLengthVariance = 0.001f,
            FlowerPedicelRadiusVariance = 0.0001f,
            FlowerPedicelSegmentCount = 1,

            // Base stem that elevates the flower - this is the main culm
            FlowerBaseSegmentLength = 0.18f,         // ~18cm tall flowering stem (reaches leaf height)
            FlowerBaseSegmentLengthVariance = 0.04f,
            FlowerBaseSegmentRadius = 0.0012f,       // Thin but sturdy
            FlowerBaseSegmentRadiusVariance = 0.0002f,

            FlowerBranchSegmentLength = 0.004f,         // Minimal additional stem segments
            FlowerBranchSegmentLengthVariance = 0.001f,
            FlowerBranchSegmentRadius = 0.0004f,
            FlowerBranchSegmentRadiusVariance = 0.0001f,

            FlowerBaseSegmentCount = 1,         // Single flowering stem from base
            FlowerBranchMaximumDepth = 1,
            FlowerBranchLateralsPerNode = 0,         // No lateral branches on flower stem
            FlowersPerCluster = 1,             // Single terminal spikelet
            FlowerClusterMaximumSpreadDegrees = 9f,
            UseDeterministicBranching = true,
            CreateFlowersAtInternodes = false,     // Terminal flower only
            InternodeFlowersHaveStems = false,
            TerminalFlowerProbabilityAtMaximumBranchDepth = 0.3f,

            FlowerLateralInclinationDegrees = 5,            // Nearly vertical
            FlowerLateralAzimuthOffsetDegrees = 3,
            FlowerBaseLateralCount = 0,            // No basal laterals on flower stem
            FlowerBaseLateralInclinationDegrees = 5,
            FlowerBaseLateralAzimuthOffsetDegrees = 3,
            FlowerBaseLateralProbability = 0.3f,

            // Spikelet bracts (reduced leaves on flower stem)
            FlowerBaseLeafLength = 0.015f,
            FlowerBaseLeafRadius = 0.0015f,
            FlowerBaseLeafPetioleLength = 0.002f,
            FlowerBaseLeafPetioleRadius = 0.00015f,

            FlowerStemSegmentGrowthDurationHours = 96f,
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
