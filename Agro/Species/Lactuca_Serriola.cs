using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Prickly lettuce: an annual/biennial taprooted herb, initially a rosette, then leafy and bolting.
/// Sources: Flora of New Zealand, https://biotanz.landcareresearch.co.nz/scientific-names/C37782D6-3F38-4215-A58C-31C9A65C3CAC;
/// Flora Zambesiaca / Flora of Iraq (Kew), https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:228255-1/general-information.
/// This preset selects a first-season flowering cohort; bolting age is configurable, not vernalisation.
/// Rates, mechanical constants and RGB colours are provisional model parameters.
/// </summary>
public static class Lactuca_Serriola
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Lactuca serriola",
        Aka = "Prickly lettuce",
        Behavior = Behavior.Herbaceous,
        Monocarpic = true,
        Taproot = true,
        BoltingAgeHours = 24f * 45f,
        RosetteNodeDistance = 0.002f,
        RosetteMaximumNodes = 8,
        Height = 1.1f,
        LeafLength = 0.18f,
        LeafLengthVar = 0.04f,
        LeafRadius = 0.03f,
        LeafRadiusVar = 0.008f,
        LeafPitch = 30f * DegToRad,
        LeafPitchVar = 10f * DegToRad,
        LeafGrowthTime = 24f * 18f,
        LeafGrowthTimeVar = 24f * 4f,
        LeafMaximumAgeHours = 24f * 100f,
        // Upper leaves are sessile/clasping; this tiny axis is the attachment proxy.
        PetioleLength = 0.003f,
        PetioleLengthVar = 0.001f,
        PetioleRadius = 0.0012f,
        PetioleRadiusVar = 0.0002f,
        LateralsPerNode = 1,
        LateralPitch = 55f * DegToRad,
        LateralPitchVar = 12f * DegToRad,
        LateralRoll = 137.5f * DegToRad,
        LateralRollVar = 8f * DegToRad,
        NodeDistance = 0.045f,
        NodeDistanceVar = 0.01f,
        CrownPitch = 90f * DegToRad,
        CrownMaximumNodes = 24,
        CrownNodeHeightAboveSoil = 0.004f,
        ShootMaximumRadius = 0.0035f,
        HerbaceousGrowthRateScale = 0.8f,
        DominanceFactor = 0.85f,
        ShootsGravitaxis = 0.9f,
        ShootsGravitropicRatePerHour = 0.012f,
        InitialBasalRhizomeBuds = 0,
        RhizomeAxillaryBudsPerSegment = 0,
        RhizomeMaximumSegmentDepth = 0,
        CrownBudActivationProbabilityPerSeason = 0f,
        RootsDensity = 0.5f,
        RootRadiusTaper = 0.55f,
        RootsGravitaxis = 0.7f,
        FloweringStartAgeHours = 24f * 70f,
        FloweringSeasonStart = 0.334f,
        FloweringSeasonEnd = 0.59f,
        FloweringPhaseDurationHours = 24f * 95f,
        SeasonalSenescence = 0.67f,
        MaximumFlowersPerCrown = 2,
        ContinueCrownAfterFlowerCommitment = false,
        PreFloweringCrownContinuationProbabilityPerHour = 0.014f,
        FloweringCrownContinuationProbabilityPerHour = 0.006f,
        PostFloweringCrownContinuationProbabilityPerHour = 0f,
        PreFloweringCommitmentProbabilityPerHour = 0f,
        FloweringCommitmentProbabilityPerHour = 0.008f,
        PostFloweringCommitmentProbabilityPerHour = 0f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,
        GreenElasticModulus = 2e8f,
        WoodElasticModulus = 5e8f,
        FlowerScapeElasticModulus = 5e8f,
        FlowerOrganElasticModulus = 5e7f,
        FlowerSettings = new()
        {
            // Open upper panicle with small yellow all-ligulate heads.
            FlowerBaseSegmentCount = 4,
            FlowerBaseSegmentLength = 0.055f,
            FlowerBaseSegmentLengthVariance = 0.01f,
            FlowerBaseSegmentRadius = 0.0015f,
            FlowerStemSegmentGrowthDurationHours = 48f,
            FlowerBaseLateralCount = 2,
            FlowerBaseLateralProbability = 1f,
            FlowerBaseLateralActiveTopFraction = 0.75f,
            // Inclinations compose along the panicle; 55 + 40 + 40 turned terminal
            // heads down before mechanics. Ascending laterals keep the heads exposed.
            FlowerBaseLateralInclinationDegrees = 45f,
            FlowerBaseLateralAzimuthOffsetDegrees = 137.5f,
            FlowerBranchMaximumDepth = 2,
            FlowerBranchLateralsPerNode = 1,
            FlowerBranchSegmentLength = 0.07f,
            FlowerBranchSegmentLengthVariance = 0.008f,
            FlowerBranchSegmentRadius = 0.0007f,
            FlowerLateralInclinationDegrees = 20f,
            CreateFlowersAtInternodes = false,
            FlowersPerCluster = 1,
            CreateLeavesOnFlowerBase = true,
            FlowerBaseLeafLength = 0.04f,
            FlowerBaseLeafRadius = 0.006f,
            FlowerBaseLeafPetioleLength = 0.002f,
            FlowerBaseLeafPetioleRadius = 0.0005f,
            FlowerPedicelLength = 0.008f,
            FlowerPedicelRadius = 0.00045f,
            FlowerPedicelSegmentCount = 1,
            BudLength = 0.008f,
            BudRadius = 0.0015f,
            BudBloomDurationHours = 24f * 3f,
            PetalLength = 0.005f,
            PetalRadius = 0.0007f,
            PetalCount = 14,
            PetalRingCount = 2,
            InnerPetalLengthRatio = 0.65f,
            PetalPitchDegrees = 65f,
            PetalPitchVarianceDegrees = 5f,
            PetalAndPedicelGrowthDurationHours = 36f,
            FlowerMaximumAgeHours = 24f * 30f,
            PetalSenescenceDurationHours = 24f * 3f,
            // Ligules wilt within the day and the involucre closes over them: pale tan-yellow.
            PetalAbscissionSenescence = 0.5f,
            FlowerCentreLength = 0.002f,
            FlowerCentreRadius = 0.0015f,
            FlowerCentreColor = new(0.91f, 0.74f, 0.15f),
            ScapeColor = new(0.59f, 0.66f, 0.42f),
            ScapeGravitropicRatePerHour = 0.012f,
            PetalMorphology = FlowerMorphologies.Ligule(0.005f, 0.0014f),
            FlowerPhenology = new(24f * 30f, new ColorModel(
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(242, 215, 78))
                    .Add(0.5f, ColorUtils.OkLabFromSrgb8(249, 220, 77))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(237, 201, 63)),
                ColorModel.DefaultStress,
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(225, 200, 110))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(205, 180, 120)))),
        },
        LeafMorphology = VolunteerLeafMorphologies.LactucaSerriola,
        // Glaucous healthy foliage; whitish disease deposits are not a species trait.
        // The stem of prickly lettuce is pale from the start and whitish once it has
        // bolted (Kew: "whitish, shining"); it ripens and dies straw-coloured.
        StemColors = new StemColorModel(
            young: new(175, 200, 150), mature: new(205, 210, 180),
            old: new(200, 190, 150), dead: new(190, 170, 125),
            matureAgeDays: 40f, oldAgeDays: 110f),
        LeafPhenology = new(24f * 100f, new ColorModel(
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(60f, -15f, 24f)) // young
                .Add(0.50f, ColorUtils.OkLabFromCieLab(50f, -14f, 16f)) // mature: glaucous blue-green
                .Add(1f, ColorUtils.OkLabFromCieLab(50f, -14f, 16f)),
            ColorModel.DefaultStress,
            // The lower leaves yellow and dry to a papery light brown on the bolting stem.
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(68f, -2f, 46f)) // yellow
                .Add(0.40f, ColorUtils.OkLabFromCieLab(62f, 4f, 38f)) // straw-yellow
                .Add(0.75f, ColorUtils.OkLabFromCieLab(56f, 7f, 30f)) // straw-brown
                .Add(1f, ColorUtils.OkLabFromCieLab(52f, 8f, 26f)), // papery light brown
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(50f, -4f, 24f)) // dull olive
                .Add(0.50f, ColorUtils.OkLabFromCieLab(51f, 4f, 24f)) // khaki
                .Add(1f, ColorUtils.OkLabFromCieLab(52f, 7f, 24f)))), // papery light brown
    };
}
