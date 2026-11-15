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
            ScapeColor = new(0.56f, 0.67f, 0.31f),
            ScapeGravitropicRatePerHour = 0.012f,
            PetalMorphology = FlowerMorphologies.Ligule(0.005f, 0.0014f),
            FlowerPhenology = new(24f * 30f, new ColorModel(
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(243, 215, 67))
                    .Add(0.5f, ColorUtils.OkLabFromSrgb8(249, 220, 68))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(238, 201, 52)),
                ColorModel.DefaultStress,
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromSrgb8(227, 200, 92))
                    .Add(1f, ColorUtils.OkLabFromSrgb8(210, 179, 98)))),
        },
        LeafMorphology = VolunteerLeafMorphologies.LactucaSerriola,
        // Glaucous healthy foliage; whitish disease deposits are not a species trait.
        // The stem of prickly lettuce is pale from the start and whitish once it has
        // bolted (Kew: "whitish, shining"); it ripens and dies straw-coloured.
        StemColors = new StemColorModel(
            young: new(151, 190, 115), mature: new(203, 212, 167),
            old: new(203, 190, 134), dead: new(195, 170, 106),
            matureAgeDays: 40f, oldAgeDays: 110f),
        LeafPhenology = new(24f * 100f, new ColorModel(
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(60f, -22f, 36f)) // young
                .Add(0.50f, ColorUtils.OkLabFromCieLab(50f, -21f, 24f)) // mature: glaucous blue-green
                .Add(1f, ColorUtils.OkLabFromCieLab(50f, -21f, 24f)),
            ColorModel.DefaultStress,
            // The lower leaves yellow and dry to a papery light brown on the bolting stem.
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(68f, -2f, 55f)) // yellow
                .Add(0.40f, ColorUtils.OkLabFromCieLab(62f, 5f, 49f)) // straw-yellow
                .Add(0.75f, ColorUtils.OkLabFromCieLab(56f, 10f, 41f)) // straw-brown
                .Add(1f, ColorUtils.OkLabFromCieLab(52f, 11f, 36f)), // papery light brown
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(50f, -6f, 34f)) // dull olive
                .Add(0.50f, ColorUtils.OkLabFromCieLab(51f, 6f, 34f)) // khaki
                .Add(1f, ColorUtils.OkLabFromCieLab(52f, 10f, 34f)))), // papery light brown
    };
}
