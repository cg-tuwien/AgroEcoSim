using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Small water-pepper: a non-clonal, branching annual with narrow alternate leaves.
/// Botanical bounds: Illustrated Flora of British Columbia (UBC E-Flora),
/// https://linnet.geog.ubc.ca/Atlas/Atlas.aspx?sciname=Persicaria+minor;
/// BfN FloraWeb, https://www.floraweb.de/php/artenhome.php?name-use-id=4428.
/// Growth rates, mechanics and colour samples below are model assumptions, not measurements.
/// </summary>
public static class Persicaria_Minor
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Persicaria minor",
        Aka = "Small water-pepper",
        Behavior = Behavior.Herbaceous,
        Monocarpic = true,
        Height = 0.35f,
        // Flora BC: alternate blades 3–10 cm long, 5–8 mm wide; short petioles.
        LeafLength = 0.06f,
        LeafLengthVar = 0.015f,
        LeafRadius = 0.0035f,
        LeafRadiusVar = 0.0007f,
        LeafPitch = 30f * DegToRad,
        LeafPitchVar = 8f * DegToRad,
        LeafGrowthTime = 24f * 12f,
        LeafGrowthTimeVar = 24f * 3f,
        LeafMaximumAgeHours = 24f * 90f,
        PetioleLength = 0.003f,
        PetioleLengthVar = 0.001f,
        PetioleRadius = 0.00055f,
        PetioleRadiusVar = 0.0001f,
        LateralsPerNode = 1,
        LateralPitch = 55f * DegToRad,
        LateralPitchVar = 10f * DegToRad,
        LateralRoll = 180f * DegToRad,
        LateralRollVar = 8f * DegToRad,
        NodeDistance = 0.025f,
        NodeDistanceVar = 0.006f,
        CrownPitch = 65f * DegToRad,
        CrownMaximumNodes = 28,
        CrownNodeHeightAboveSoil = 0.004f,
        ShootMaximumRadius = 0.0015f,
        HerbaceousGrowthRateScale = 0.55f,
        HerbaceousBranchProbability = 0.20f,
        HerbaceousBranchMaximumOrder = 2,
        DominanceFactor = 0.75f,
        ShootsGravitaxis = 0.8f,
        ShootsGravitropicSetPointAngle = 25f * DegToRad,
        ShootsGravitropicRatePerHour = 0.01f,
        // Seed reproduction only. A branching aerial shoot is not a spreading rhizome.
        InitialBasalRhizomeBuds = 0,
        RhizomeAxillaryBudsPerSegment = 0,
        RhizomeMaximumSegmentDepth = 0,
        CrownBudActivationProbabilityPerSeason = 0f,
        RootsDensity = 0.6f,
        // A fibrous network must not be a succession of equally thick rods.
        // Geometric approximation; not a measured species allometric coefficient.
        RootRadiusTaper = 0.6f,
        RootsGravitaxis = 0.3f,
        // Central European July–October flowering; season zero is 1 March.
        FloweringStartAgeHours = 24f * 50f,
        FloweringSeasonStart = 0.334f,
        FloweringSeasonEnd = 0.62f,
        FloweringPhaseDurationHours = 24f * 110f,
        SeasonalSenescence = 0.67f,
        MaximumFlowersPerCrown = 5,
        // A flowering shoot terminates in its raceme; branching is the leafy-axis rule.
        ContinueCrownAfterFlowerCommitment = false,
        PreFloweringCrownContinuationProbabilityPerHour = 0.018f,
        FloweringCrownContinuationProbabilityPerHour = 0.008f,
        PostFloweringCrownContinuationProbabilityPerHour = 0f,
        PreFloweringCommitmentProbabilityPerHour = 0f,
        FloweringCommitmentProbabilityPerHour = 0.008f,
        PostFloweringCommitmentProbabilityPerHour = 0f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,
        GreenElasticModulus = 4e7f,
        WoodElasticModulus = 1e8f,
        FlowerScapeElasticModulus = 1.5e8f,
        FlowerOrganElasticModulus = 3e7f,
        FlowerSettings = new()
        {
            // A loose narrow raceme, about 36 mm long, bearing tiny five-part tepals.
            // Each base lateral terminates immediately: no exponential panicle fan.
            FlowerBaseSegmentCount = 6,
            FlowerBaseSegmentLength = 0.006f,
            FlowerBaseSegmentLengthVariance = 0.001f,
            FlowerBaseSegmentRadius = 0.0005f,
            FlowerStemSegmentGrowthDurationHours = 24f,
            FlowerBaseLateralCount = 2,
            FlowerBaseLateralProbability = 1f,
            FlowerBaseLateralActiveTopFraction = 1f,
            FlowerBaseLateralInclinationDegrees = 45f,
            FlowerBaseLateralAzimuthOffsetDegrees = 137.5f,
            FlowerBranchMaximumDepth = 0,
            FlowerBranchLateralsPerNode = 0,
            FlowerBranchSegmentLength = 0.0008f,
            FlowerBranchSegmentRadius = 0.00025f,
            CreateFlowersAtInternodes = false,
            CreateLeavesOnFlowerBase = false,
            FlowersPerCluster = 1,
            FlowerPedicelLength = 0.0008f,
            FlowerPedicelRadius = 0.0002f,
            FlowerPedicelSegmentCount = 1,
            BudLength = 0.0015f,
            BudRadius = 0.0006f,
            BudBloomDurationHours = 24f * 3f,
            PetalLength = 0.0016f,
            PetalRadius = 0.00055f,
            PetalCount = 5,
            PetalPitchDegrees = 35f,
            PetalPitchVarianceDegrees = 5f,
            PetalAndPedicelGrowthDurationHours = 24f,
            FlowerMaximumAgeHours = 24f * 24f,
            PetalSenescenceDurationHours = 24f * 4f,
            // Polygonaceae tepals do not fall: they persist around the achene, fading to a pinkish tan.
            PetalAbscissionSenescence = 1f,
            FlowerCentreLength = 0.0005f,
            FlowerCentreRadius = 0.00035f,
            FlowerCentreColor = new(0.94f, 0.69f, 0.72f),
            ScapeColor = new(0.40f, 0.49f, 0.14f),
            // The bud IS the closed perianth, so it shows the tepals' pink and not a calyx green.
            BudColor = new(0.84f, 0.35f, 0.47f),
            ScapeGravitropicSetPointAngle = 15f * DegToRad,
            ScapeGravitropicRatePerHour = 0.01f,
            PetalMorphology = new()
            {
                Shape = new OvateLeafShape(),
                Margin = LeafMarginType.Entire,
                Apex = LeafApexType.Rounded,
                Base = LeafBaseType.Cuneate,
                Venation = LeafVenationType.Parallel,
                Dimensions = LeafDimensions.Create(0.0016f, 0.0011f, 0.00006f, 0f, 0.12f),
            },
            FlowerPhenology = new(24f * 24f, new ColorModel(
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromCieLab(55f, 53f, 10f)) // opening: deep pink
                    .Add(0.50f, ColorUtils.OkLabFromCieLab(58f, 50f, 8f)) // open tepals: pink
                    .Add(1f, ColorUtils.OkLabFromCieLab(60f, 48f, 7f)),
                ColorModel.DefaultStress,
                new ColorCurve()
                    .Add(0f, ColorUtils.OkLabFromCieLab(58f, 41f, 14f)) // tepals persisting round the fruit
                    .Add(1f, ColorUtils.OkLabFromCieLab(55f, 22f, 25f)))), // pinkish tan
        },
        LeafMorphology = VolunteerLeafMorphologies.PersicariaMinor,
        // Green shoots that redden as they harden, darkest at the swollen nodes; the dead
        // annual stem is a reddish straw.
        StemColors = new StemColorModel(
            young: new(122, 171, 45), mature: new(172, 105, 84),
            old: new(146, 85, 64), dead: new(159, 112, 70),
            matureAgeDays: 25f, oldAgeDays: 140f),
        LeafPhenology = new(24f * 90f, new ColorModel(
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(52f, -27f, 49f)) // young
                .Add(0.50f, ColorUtils.OkLabFromCieLab(40f, -27f, 37f)) // mature
                .Add(1f, ColorUtils.OkLabFromCieLab(40f, -27f, 37f)),
            ColorModel.DefaultStress,
            // Knotweed leaves yellow and then flush red before they brown.
            new ColorCurve()
                .Add(0f, ColorUtils.OkLabFromCieLab(66f, 2f, 63f)) // yellow
                .Add(0.40f, ColorUtils.OkLabFromCieLab(48f, 37f, 42f)) // red flush
                .Add(0.75f, ColorUtils.OkLabFromCieLab(44f, 27f, 35f)) // red-brown
                .Add(1f, ColorUtils.OkLabFromCieLab(45f, 17f, 31f)), // brown
            ColorModel.DefaultStressSenescence)),
    };
}
