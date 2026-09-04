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

        // naturadb gives 30-40 cm and RHS 0.1-0.5 m; the clump was reading short.
        Height = 0.50f,

        // Carex has long, narrow, grass-like leaves that arch outward
        // Gardenia.net: leaves 15-40 cm long, 6-8 mm wide.
        LeafLength = 0.30f,
        LeafLengthVar = 0.09f,
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

        CrownBudActivationProbabilityPerSeason = 0.45f,
        CrownPitch = 90f * DegToRad,
        HerbaceousGrowthRateScale = 0.15f,
        ShootMaximumRadius = 0.0025f,

        LateralPitch = 10f * DegToRad,
        LateralPitchVar = 8f * DegToRad,
        LateralRoll = 60f * DegToRad,
        LateralRollVar = 8f * DegToRad,

        LateralsPerNode = 2,
        // A sedge tiller carries six to ten leaves and then flowers or rests. Uncapped,
        // each tiller kept adding nodes and the clump reached 906 leaves in two years
        // against the 100-200 a 17-tiller tussock actually carries.
        CrownMaximumNodes = 5,
        // Tiller internodes are very short but non-zero. They must still exceed the
        // shoot radius: a segment wider than it is long has an ill-conditioned axis,
        // and the bending solver can then flip it over.
        NodeDistance = 0.003f,
        NodeDistanceVar = 0.0008f,
        DominanceFactor = 0.80f,

        // Carex has short, creeping rhizomes (not thick)
        // Caespitose sedge: tillers break inside the leaf sheath a few mm from the
        // parent shoot, so the tussock stays dense instead of spacing itself out.
        // With such short segments the depth cap is what bounds the tiller count
        // rather than the reach, so it has to stay low: each level roughly doubles
        // the number of shoots and four levels give a plausible young clump.
        // Four saturated the tussock at 41 tillers in year one; a sedge keeps adding
        // tillers each spring.
        RhizomeMaximumSegmentDepth = 5,
        RhizomeSegmentLength = 0.005f,
        RhizomeSegmentRadius = 0.0015f,      // ~1.5mm - much thinner for a sedge
        RhizomeGrowthRateScale = 0.05f,
        RhizomeBasalBudActivationProbabilityPerHour = 1f,
        RhizomeAxillaryBudActivationProbabilityPerSeason = 0.3f,
        RhizomeAxillaryBudsPerSegment = 1,
        // The tussock persists through winter; its dormant tiller buds must not be
        // deleted when their reserve runs down.
        RetainDormantRhizomes = true,
        // A sedge raised from seed does not raise culms in its first summer. At 80 days
        // the plant matured on 20 May, inside that year's window, and flowered in June;
        // 300 days moves the first culms to the second spring, where they belong.
        FloweringStartAgeHours = 24f * 300f,
        FloweringPhaseDurationHours = 24 * 40,
        // April to May (naturadb "Maerz bis Mai", RHS "spring", MOBOT "April to July").
        // Season 0 is 1 March, so 1 April = (91-59)/365 = 0.088 and
        // 31 May = (151-59)/365 = 0.252.
        //
        // FloweringSeasonStart must stay above zero. The reset branch of
        // GetFloweringPhase is guarded by "season < FloweringSeasonStart", which a
        // value of zero makes unreachable; the annual re-arm in PlantSubFormation now
        // covers that, but a window that opens on the very first day of the season year
        // is not what this sedge does anyway.
        FloweringSeasonStart = 0.088f,
        FloweringSeasonEnd = 0.252f,

        PreFloweringCrownContinuationProbabilityPerHour = 0.008f,
        FloweringCrownContinuationProbabilityPerHour = 0.002f,
        PostFloweringCrownContinuationProbabilityPerHour = 0.003f,
        DormantCrownContinuationProbabilityPerHour = 0f,

        // The PreFlower phase runs from 1 March to the start of the flowering window, and
        // its own commitment rate is what puts flowers on the plant in March. Keep it at
        // zero unless the species really does bloom before its main window.
        PreFloweringCommitmentProbabilityPerHour = 0f,
        FloweringCommitmentProbabilityPerHour = 0.025f,
        // A sedge raises its culms once, in spring. A non-zero post-flowering rate put
        // spikes on the clump from May to December.
        PostFloweringCommitmentProbabilityPerHour = 0f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,

        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 3e6f,
        // Effective culm stiffness includes the reinforcing leaf sheath that
        // the simplified solid-axis geometry does not model explicitly.
        FlowerOrganElasticModulus = 8e8f,
        // A sedge culm is stiff and erect - "sedges have edges", a solid trigonous stem.
        // Stated explicitly rather than inherited, because the scape and the spikes it
        // carries are no longer the same tissue as far as bending is concerned.
        FlowerScapeElasticModulus = 8e8f,

        FlowerSettings = new FlowerSettings
        {
            // Carex flowers grow on tall upright stems (culms) that rise above foliage

            PetalAndPedicelGrowthDurationHours = 72f,
            PetalAndPedicelGrowthDurationVarianceHours = 18f,

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
            // A SEDGE HAS NO PETALS AND NO PERIANTH AT ALL. Flora of North America on
            // Carex: "staminate flowers without scales; pistillate flowers with 1 scale
            // with fused margins (perigynium) enclosing flower ... perianth absent".
            // MOBOT for this very species: "Brownish flowers (lack petals and sepals)".
            // It is wind-pollinated, so there is nothing showy to render.
            //
            // What a photograph shows is a spike-like raceme on a trigonous culm: one
            // terminal staminate spike 3-4 cm long and 3-5 mm thick, dark chestnut, its
            // cream-white filaments and pale anthers exserted at 40-70 degrees so the
            // whole thing reads as a narrow bottle-brush, and below it two to five
            // slender pistillate spikes 1.5-3.5 cm long, erect to ascending, spaced
            // further apart down the axis.
            //
            // So the mapping is: FlowerBud is the SPIKE AXIS, and the "petals" are the
            // exserted anthers and glumes around it - many, tiny, narrow and pale, not
            // five broad coloured blades. That is what the previous single 12 mm petal
            // on a 3 mm bud looked wrong as.
            FlowerPedicelLength = 0.003f,       // Minimal pedicel
            FlowerPedicelRadius = 0.0003f,
            FlowerPedicelLengthVariance = 0.001f,
            FlowerPedicelRadiusVariance = 0.0001f,
            FlowerPedicelSegmentCount = 1,

            // The culm: unbranched, trigonous, and standing clear of the arching
            // foliage. Four segments of 11 cm reach 44 cm against a 30-40 cm leaf mound,
            // which is what the photographs show - the culms rise above and beyond the
            // leaves. One 18 cm segment left the spikes below the foliage.
            // Four segments of 8.5 cm put the spikes a few centimetres above the
            // 40 cm leaf mound. At 11 cm they stood 15 cm clear of it, which is more
            // than "slightly taller than the leaves".
            FlowerBaseSegmentLength = 0.085f,
            FlowerBaseSegmentLengthVariance = 0.02f,
            FlowerBaseSegmentRadius = 0.0011f,
            FlowerBaseSegmentRadiusVariance = 0.0002f,

            // Short peduncles carrying the lateral pistillate spikes.
            FlowerBranchSegmentLength = 0.012f,
            FlowerBranchSegmentLengthVariance = 0.004f,
            FlowerBranchSegmentRadius = 0.0005f,
            FlowerBranchSegmentRadiusVariance = 0.0001f,

            FlowerBaseSegmentCount = 4,
            FlowerBranchMaximumDepth = 1,
            FlowerBranchLateralsPerNode = 0,
            FlowersPerCluster = 1,             // one spike per site
            FlowerClusterMaximumSpreadDegrees = 6f,
            UseDeterministicBranching = true,
            CreateFlowersAtInternodes = false,
            InternodeFlowersHaveStems = false,
            TerminalFlowerProbabilityAtMaximumBranchDepth = 1.0f,

            // Erect to slightly ascending, never nodding.
            FlowerLateralInclinationDegrees = 12,
            FlowerLateralAzimuthOffsetDegrees = 40,
            // Two to five lateral spikes, on the upper half of the culm only.
            FlowerBaseLateralCount = 1,
            FlowerBaseLateralInclinationDegrees = 20,
            FlowerBaseLateralAzimuthOffsetDegrees = 55,
            FlowerBaseLateralProbability = 1.0f,
            FlowerBaseLateralActiveTopFraction = 0.6f,

            // Spikelet bracts (reduced leaves on flower stem)
            FlowerBaseLeafLength = 0.015f,
            FlowerBaseLeafRadius = 0.0015f,
            FlowerBaseLeafPetioleLength = 0.002f,
            FlowerBaseLeafPetioleRadius = 0.00015f,

            FlowerStemSegmentGrowthDurationHours = 24f,

            // The spike axis, dark chestnut, 3 cm long and 2 mm thick.
            FlowerCentreLength = 0.030f,
            FlowerCentreRadius = 0.0020f,
            FlowerCentreColor = new Vector3(0.36f, 0.19f, 0.11f),
            // The culm is pale green flushed red-brown below the spikes.
            ScapeColor = new Vector3(0.38f, 0.46f, 0.22f),

            // Glumes and exserted anthers rather than petals: many, 4 mm long and only
            // 0.6 mm wide, standing off the axis at about 55 degrees, cream-white.
            PetalCount = 14,
            PetalLength = 0.0045f,
            PetalLengthVariance = 0.001f,
            PetalRadius = 0.0003f,
            PetalPitchDegrees = 55f,
            PetalPitchVarianceDegrees = 12f,
            PetalRollVarianceDegrees = 20f,
            PetalRimFraction = 0.6f,
            PetalMorphology = new LeafMorphology
            {
                Shape = new LanceolateLeafShape(),
                Margin = LeafMarginType.Entire,
                Venation = LeafVenationType.Parallel,
                Apex = LeafApexType.Acuminate,
                Base = LeafBaseType.Truncate,
                Dimensions = LeafDimensions.Create(lengthM: 0.0045f, widthM: 0.0006f,
                    thicknessM: 0.00008f, petioleM: 0f, curvature01: 0.1f),
            },
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
