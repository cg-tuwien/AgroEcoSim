using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Common dandelion aggregate: persistent taproot and compressed basal rosette.
/// Aggregate-level morphology, not an identification of a particular microspecies.
/// Sources: https://gobotany.nativeplanttrust.org/species/taraxacum/officinale/;
/// https://ipm.ucanr.edu/PMG/WEEDS/dandelion.html.
/// Development rates, mechanical constants and colours are explicit model assumptions.
/// </summary>
public static class Taraxacum_Officinale
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Taraxacum officinale aggregate",
        Aka = "Common dandelion",
        Behavior = Behavior.Herbaceous,
        Taproot = true,
        RosetteScapes = true,
        Height = 0.30f,
        LeafLength = 0.18f,
        LeafLengthVar = 0.05f,
        LeafRadius = 0.025f,
        LeafRadiusVar = 0.006f,
        LeafPitch = 15f * DegToRad,
        LeafPitchVar = 8f * DegToRad,
        LeafGrowthTime = 24f * 18f,
        LeafGrowthTimeVar = 24f * 4f,
        LeafMaximumAgeHours = 24f * 110f,
        PetioleLength = 0.012f,
        PetioleLengthVar = 0.004f,
        PetioleRadius = 0.0016f,
        PetioleRadiusVar = 0.0003f,
        LateralsPerNode = 1,
        LateralPitch = 60f * DegToRad,
        LateralPitchVar = 12f * DegToRad,
        LateralRoll = 137.5f * DegToRad,
        LateralRollVar = 8f * DegToRad,
        NodeDistance = 0.002f,
        NodeDistanceVar = 0.0004f,
        CrownPitch = 90f * DegToRad,
        CrownMaximumNodes = 12,
        CrownNodeHeightAboveSoil = 0.004f,
        ShootMaximumRadius = 0.004f,
        HerbaceousGrowthRateScale = 0.5f,
        ShootsGravitaxis = 1f,
        // The living crown renews leaves locally; no ordinary stolons/rhizome spread.
        InitialBasalRhizomeBuds = 0,
        RhizomeAxillaryBudsPerSegment = 0,
        RhizomeMaximumSegmentDepth = 0,
        CrownBudActivationProbabilityPerSeason = 0f,
        RetainDormantRhizomes = true,
        RootsDensity = 0.45f,
        RootRadiusTaper = 0.55f,
        RootsGravitaxis = 0.8f,
        FloweringStartAgeHours = 24f * 60f,
        FloweringSeasonStart = 0.085f,
        FloweringSeasonEnd = 0.59f,
        FloweringPhaseDurationHours = 24f * 190f,
        SeasonalSenescence = 0.73f,
        MaximumFlowersPerCrown = 4,
        PreFloweringCrownContinuationProbabilityPerHour = 0.012f,
        FloweringCrownContinuationProbabilityPerHour = 0.006f,
        PostFloweringCrownContinuationProbabilityPerHour = 0.003f,
        PreFloweringCommitmentProbabilityPerHour = 0f,
        FloweringCommitmentProbabilityPerHour = 0.006f,
        PostFloweringCommitmentProbabilityPerHour = 0f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,
        GreenElasticModulus = 5e7f,
        WoodElasticModulus = 1e8f,
        FlowerScapeElasticModulus = 1.5e8f,
        FlowerOrganElasticModulus = 3e7f,
        FlowerSettings = new()
        {
            // Every leafless, unbranched scape terminates in exactly one capitulum.
            FlowerBaseSegmentCount = 3,
            FlowerBaseSegmentLength = 0.075f,
            FlowerBaseSegmentLengthVariance = 0.015f,
            FlowerBaseSegmentRadius = 0.002f,
            FlowerStemSegmentGrowthDurationHours = 48f,
            FlowerBaseLateralCount = 0,
            FlowerBranchMaximumDepth = 0,
            FlowerBranchLateralsPerNode = 0,
            CreateFlowersAtInternodes = false,
            CreateLeavesOnFlowerBase = false,
            FlowersPerCluster = 1,
            FlowerPedicelLength = 0.002f,
            FlowerPedicelRadius = 0.0015f,
            FlowerPedicelSegmentCount = 1,
            BudLength = 0.010f,
            BudRadius = 0.003f,
            BudBloomDurationHours = 24f * 3f,
            // Ligules represent whole ray florets, not the petals of a single flower.
            PetalLength = 0.017f,
            PetalRadius = 0.0011f,
            PetalCount = 54,
            PetalRingCount = 3,
            InnerPetalLengthRatio = 0.35f,
            PetalPitchDegrees = 82f,
            PetalPitchVarianceDegrees = 5f,
            PetalAndPedicelGrowthDurationHours = 36f,
            FlowerMaximumAgeHours = 24f * 18f,
            PetalSenescenceDurationHours = 24f * 3f,
            FlowerCentreLength = 0.002f,
            FlowerCentreRadius = 0.003f,
            FlowerCentreColor = new(0.96f, 0.70f, 0.06f),
            ScapeColor = new(0.49f, 0.62f, 0.25f),
            ScapeGravitropicRatePerHour = 0.012f,
            PetalMorphology = FlowerMorphologies.Ligule(0.017f, 0.0022f),
            FlowerPhenology = new(24f * 18f, new ColorModel(
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(251, 201, 24))
                    .Add(0.5f, ColorUtils.OkLabFromSrgb8(255, 212, 35))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(244, 191, 28)),
                ColorModel.DefaultStress,
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(216, 175, 65))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(143, 115, 66)))),
        },
        LeafMorphology = VolunteerLeafMorphologies.TaraxacumOfficinale,
        LeafPhenology = new(24f * 110f, new ColorModel(
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromSrgb8(143, 185, 88))
                .Add(0.5f, ColorUtils.OkLabFromSrgb8(72, 137, 47))
                .Add(1f, ColorUtils.OkLabFromSrgb8(59, 120, 42)),
            ColorModel.DefaultStress, ColorModel.DefaultSeasonalSenescence,
            ColorModel.DefaultStressSenescence)),
    };
}
