using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Heuchera micrantha (Creeping Alumroot)
/// Sources:
/// - Missouri Botanical Garden Plant Finder (native to western North America;
///   basal rosettes of rounded/heart-shaped leaves 5-10 cm wide; flower scapes 30-60 cm;
///   small bell-shaped flowers white to pink)
/// - RHS Plant Finder (semi-evergreen; mottled/variegated foliage common;
///   flowers on airy panicles summer to early autumn)
/// - Gardenia.net (clump-forming perennial; leaves often with silver/purple markings;
///   flower spikes 40-60 cm; flowers creamy-white to pale pink)
/// </summary>
public static class Heuchera_Micrantha_Palace_Purple
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Heuchera micrantha",
        Aka = "Creeping Alumroot",
        Behavior = Behavior.Herbaceous,

        Height = 0.50f,

        LeafLength = 0.07f,
        LeafLengthVar = 0.015f,
        LeafRadius = 0.055f,
        LeafRadiusVar = 0.010f,
        LeafPitchVar = 5f * (MathF.PI / 180f),
        LeafPitch = 85 * (MathF.PI / 180f),

        LeafGrowthTime = 24f * 10f * 2f,
        LeafGrowthTimeVar = 24f * 4f,
        LeafMaximumAgeHours = 24 * 200,

        PetioleLength = 0.08f,
        PetioleLengthVar = 0.015f,
        PetioleRadius = 0.002f,
        PetioleRadiusVar = 0.0003f,

        CrownBudActivationProbabilityPerSeason = 0.55f,
        CrownPitch = 117f * DegToRad,
        HerbaceousGrowthRateScale = 0.22f,
        ShootMaximumRadius = 0.0035f,

        LateralPitch = 15 * (MathF.PI / 180f),
        LateralPitchVar = 10 * (MathF.PI / 180f),
        LateralRoll = 45f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        LateralsPerNode = 2,
        NodeDistance = 0f,
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.75f,

        RhizomeMaximumSegmentDepth = 3,
        RhizomeSegmentLength = 0.03f,
        RhizomeSegmentRadius = 0.003f,
        RhizomeOffshootProbabilityPerHour = 0.0001f,
        FloweringStartAgeHours = 24f * 50f,
        FloweringPhaseDurationHours = 24 * 60,

        PreFloweringCrownContinuationProbabilityPerHour = 0.012f,
        FloweringCrownContinuationProbabilityPerHour = 0.003f,
        PostFloweringCrownContinuationProbabilityPerHour = 0.005f,
        DormantCrownContinuationProbabilityPerHour = 0f,

        PreFloweringCommitmentProbabilityPerHour = 0.003f,
        FloweringCommitmentProbabilityPerHour = 0.045f,
        PostFloweringCommitmentProbabilityPerHour = 0.003f,
        DormantFloweringCommitmentProbabilityPerHour = 0f,

        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 4e6f,
        FlowerOrganElasticModulus = 4e7f,

        FlowerSettings = new FlowerSettings
        {
            PetalLength = 0.006f,
            PetalRadius = 0.0025f,
            PetalLengthVariance = 0.001f,
            PetalAndPedicelGrowthDurationHours = 60f,
            PetalAndPedicelGrowthDurationVarianceHours = 12f,

            CreateLeavesOnFlowerBase = false,
            FlowerPhenology = new LeafPhenology(
                expectedAge: 28f * 24f,
                colorModel: new ColorModel(
                    age: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(245, 240, 235))
                        .Add(0.25f, ColorUtils.OkLabFromSrgb8(240, 235, 230))
                        .Add(0.70f, ColorUtils.OkLabFromSrgb8(235, 230, 225))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(228, 222, 215)),
                    stress: new ColorCurve()
                        .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                        .Add(1.00f, new Vector3(-0.04f, 0.82f, 0.01f)),
                    senescenceSeason: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(220, 210, 200))
                        .Add(0.40f, ColorUtils.OkLabFromSrgb8(195, 180, 165))
                        .Add(0.75f, ColorUtils.OkLabFromSrgb8(165, 150, 130))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(130, 115, 95))
                )
            ),

            PetalSenescenceDurationHours = 144f,

            BudLength = 0.004f,
            BudRadius = 0.0025f,
            BudBloomDurationHours = 120u,
            FlowerMaximumAgeHours = 600u,

            FlowerPedicelLength = 0.008f,
            FlowerPedicelRadius = 0.0004f,
            FlowerPedicelLengthVariance = 0.003f,
            FlowerPedicelRadiusVariance = 0.0001f,
            FlowerPedicelSegmentCount = 1,

            FlowerBaseSegmentLength = 0.008f,
            FlowerBaseSegmentLengthVariance = 0.004f,
            FlowerBaseSegmentRadius = 0.0006f,
            FlowerBaseSegmentRadiusVariance = 0.0001f,

            FlowerBranchSegmentLength = 0.012f,
            FlowerBranchSegmentLengthVariance = 0.002f,
            FlowerBranchSegmentRadius = 0.0005f,
            FlowerBranchSegmentRadiusVariance = 0.0001f,

            FlowerBaseSegmentCount = 4,
            FlowerBranchMaximumDepth = 2,
            FlowerBranchLateralsPerNode = 2,
            FlowersPerCluster = 3,
            FlowerClusterMaximumSpreadDegrees = 32.4f,
            UseDeterministicBranching = true,
            CreateFlowersAtInternodes = true,
            InternodeFlowersHaveStems = true,
            TerminalFlowerProbabilityAtMaximumBranchDepth = 0.5f,

            FlowerLateralInclinationDegrees = 25,
            FlowerLateralAzimuthOffsetDegrees = 12,
            FlowerBaseLateralCount = 1,
            FlowerBaseLateralInclinationDegrees = 65,
            FlowerBaseLateralAzimuthOffsetDegrees = 8,
            FlowerBaseLateralProbability = 0.6f,

            FlowerBaseLeafLength = 0.012f,
            FlowerBaseLeafRadius = 0.006f,
            FlowerBaseLeafPetioleLength = 0.003f,
            FlowerBaseLeafPetioleRadius = 0.0003f,

            FlowerStemSegmentGrowthDurationHours = 90f,
        },

        LeafPhenology = new LeafPhenology(
            expectedAge: 1.5f * 365f * 24f,
            colorModel: new ColorModel(
                HeucheraMicranthaPalacePurplePreset.Lifecycle,
                HeucheraMicranthaPalacePurplePreset.Stress,
                HeucheraMicranthaPalacePurplePreset.SeasonalSenescence,
                HeucheraMicranthaPalacePurplePreset.StressSenescence
            )
            {
                HealthySeasonCurve = HeucheraMicranthaPalacePurplePreset.HealthySeason,
                HealthySeasonDirection = HeucheraMicranthaPalacePurplePreset.HealthySeasonDirection,
            }
        ),

        // Heuchera micrantha 'Palace Purple'
        // Stylized 3D blade based on the cultivar's broad, maple-/ivy-like,
        // palmately lobed foliage with a crenate margin.
        //
        // Coordinate convention follows the supplied Geranium preset:
        //   +X = distal / central lobe
        //   -X = petiole-side basal sinus / attachment
        //   Y  = leaf width
        //   Z  = out-of-plane leaf relief
        //
        // The petiole attachment vertex (-0.99, 0.0) stays exactly at Z = 0.
        // The blade uses a shallow upward sweep, transverse cupping, a subtle
        // five-fold palmate undulation, and fine margin corrugation. X/Y and all
        // triangle indices are unchanged from the planar version.
        //
        // Approximate Z range:
        //   Low-res:  -0.018 .. +0.116
        //   High-res: -0.020 .. +0.119
        //
        // All triangles remain plain indexed triangles with clockwise winding.
        //
        // Low-res:  84 vertices, 82 triangles
        // High-res: 336 vertices, 334 triangles
        LeafMorphology = new()
        {
            LowResVertices =
            [
                new(-0.990000f, 0.000000f, 0.000000f),
                new(-1.140254f, 0.048806f, 0.002134f),
                new(-1.271866f, 0.118992f, 0.005650f),
                new(-1.408363f, 0.212901f, 0.010601f),
                new(-1.493220f, 0.315208f, 0.014335f),
                new(-1.505130f, 0.405967f, 0.014672f),
                new(-1.527673f, 0.509533f, 0.015242f),
                new(-1.570602f, 0.636893f, 0.015019f),
                new(-1.565488f, 0.748442f, 0.010106f),
                new(-1.567323f, 0.876973f, 0.002563f),
                new(-1.553134f, 1.006538f, -0.005179f),
                new(-1.477473f, 1.083506f, -0.008849f),
                new(-1.359550f, 1.105401f, -0.009655f),
                new(-1.255593f, 1.132862f, -0.011454f),
                new(-1.169316f, 1.180880f, -0.010844f),
                new(-1.061121f, 1.179833f, -0.002893f),
                new(-0.983493f, 1.240008f, 0.001654f),
                new(-0.924958f, 1.378483f, 0.001259f),
                new(-0.845572f, 1.497747f, 0.000323f),
                new(-0.738980f, 1.525479f, 0.002507f),
                new(-0.627789f, 1.510260f, 0.007957f),
                new(-0.520000f, 1.489090f, 0.014892f),
                new(-0.420001f, 1.401112f, 0.020395f),
                new(-0.322468f, 1.376063f, 0.018207f),
                new(-0.207063f, 1.439621f, 0.012375f),
                new(-0.082241f, 1.490140f, 0.005227f),
                new(0.024100f, 1.455662f, 0.001510f),
                new(0.116084f, 1.386884f, 0.002119f),
                new(0.202135f, 1.313312f, 0.002979f),
                new(0.243693f, 1.176138f, 0.002106f),
                new(0.298202f, 1.077293f, -0.000745f),
                new(0.401473f, 1.042768f, -0.001986f),
                new(0.541605f, 1.034277f, 0.000478f),
                new(0.664652f, 0.991965f, 0.009840f),
                new(0.763170f, 0.918593f, 0.023993f),
                new(0.827826f, 0.817076f, 0.037533f),
                new(0.804148f, 0.669560f, 0.045954f),
                new(0.787952f, 0.539000f, 0.052873f),
                new(0.811909f, 0.431382f, 0.063702f),
                new(0.851536f, 0.328696f, 0.078368f),
                new(0.886887f, 0.222657f, 0.092598f),
                new(0.974704f, 0.117613f, 0.105989f),
                new(1.107170f, 0.000000f, 0.115864f),
                new(0.977575f, -0.117839f, 0.096892f),
                new(0.886718f, -0.222630f, 0.082634f),
                new(0.846850f, -0.327573f, 0.074621f),
                new(0.816502f, -0.432869f, 0.067743f),
                new(0.788110f, -0.539065f, 0.057823f),
                new(0.801553f, -0.668248f, 0.043368f),
                new(0.827826f, -0.817076f, 0.024181f),
                new(0.765549f, -0.920296f, 0.008529f),
                new(0.664519f, -0.991854f, 0.000965f),
                new(0.538082f, -1.030845f, -0.000881f),
                new(0.404742f, -1.046467f, -0.001155f),
                new(0.298308f, -1.077432f, -0.002858f),
                new(0.242070f, -1.173640f, -0.008010f),
                new(0.202135f, -1.313312f, -0.013175f),
                new(0.117333f, -1.389608f, -0.010104f),
                new(0.024038f, -1.455496f, -0.003041f),
                new(-0.083657f, -1.485318f, 0.003325f),
                new(-0.205993f, -1.444540f, 0.007181f),
                new(-0.322443f, -1.376240f, 0.008877f),
                new(-0.420216f, -1.398097f, 0.006664f),
                new(-0.520000f, -1.489090f, 0.003193f),
                new(-0.628004f, -1.513276f, 0.002643f),
                new(-0.738955f, -1.525302f, -0.001166f),
                new(-0.844503f, -1.492827f, -0.007803f),
                new(-0.926374f, -1.383305f, -0.010401f),
                new(-0.983555f, -1.240175f, -0.009330f),
                new(-1.059872f, -1.177108f, -0.011270f),
                new(-1.169316f, -1.180880f, -0.014394f),
                new(-1.257215f, -1.135361f, -0.015424f),
                new(-1.359444f, -1.105261f, -0.018429f),
                new(-1.474204f, -1.079807f, -0.017260f),
                new(-1.556657f, -1.009971f, -0.008336f),
                new(-1.567456f, -0.877084f, 0.000371f),
                new(-1.563109f, -0.746739f, 0.005522f),
                new(-1.570602f, -0.636893f, 0.009218f),
                new(-1.530268f, -0.510845f, 0.011993f),
                new(-1.504972f, -0.405901f, 0.013883f),
                new(-1.488640f, -0.313725f, 0.012712f),
                new(-1.411867f, -0.213741f, 0.008718f),
                new(-1.271950f, -0.119005f, 0.004894f),
                new(-1.139538f, -0.048749f, 0.002037f),
            ],
            LowResIndices =
            [
                10, 11, 9, 11, 12, 9, 18, 19, 17, 19, 20, 17,
                25, 26, 24, 26, 27, 24, 33, 36, 32, 33, 34, 36,
                34, 35, 36, 42, 43, 41, 50, 48, 49, 50, 51, 48,
                57, 55, 56, 57, 60, 55, 57, 58, 60, 58, 59, 60,
                65, 67, 64, 65, 66, 67, 73, 75, 72, 73, 74, 75,
                75, 76, 72, 77, 78, 76, 7, 8, 6, 9, 12, 8,
                12, 13, 8, 14, 15, 13, 17, 22, 16, 17, 20, 22,
                20, 21, 22, 24, 29, 23, 24, 27, 29, 27, 28, 29,
                32, 36, 31, 36, 37, 31, 39, 0, 38, 39, 45, 0,
                39, 40, 45, 41, 44, 40, 41, 43, 44, 48, 53, 47,
                48, 52, 53, 48, 51, 52, 55, 61, 54, 55, 60, 61,
                63, 64, 62, 64, 68, 62, 64, 67, 68, 72, 76, 71,
                76, 78, 71, 78, 69, 71, 78, 82, 69, 78, 79, 82,
                79, 81, 82, 79, 80, 81, 4, 5, 3, 5, 2, 3,
                5, 6, 2, 6, 15, 2, 6, 13, 15, 6, 8, 13,
                15, 1, 2, 15, 0, 1, 15, 16, 0, 16, 23, 0,
                16, 22, 23, 23, 30, 0, 23, 29, 30, 31, 37, 30,
                37, 38, 30, 38, 0, 30, 0, 69, 83, 0, 68, 69,
                0, 61, 68, 0, 54, 61, 0, 46, 54, 0, 45, 46,
                46, 47, 54, 47, 53, 54, 61, 62, 68, 70, 71, 69,
                69, 82, 83, 45, 40, 44,
            ],
            HighResVertices =
            [
                new(-0.990000f, 0.000000f, 0.000000f),
                new(-1.031627f, 0.010047f, 0.000425f),
                new(-1.070797f, 0.021640f, 0.000919f),
                new(-1.106597f, 0.034590f, 0.001466f),
                new(-1.139771f, 0.048768f, 0.002085f),
                new(-1.171772f, 0.064175f, 0.002803f),
                new(-1.203534f, 0.080867f, 0.003633f),
                new(-1.236138f, 0.098995f, 0.004586f),
                new(-1.268839f, 0.118513f, 0.005655f),
                new(-1.301604f, 0.139440f, 0.006831f),
                new(-1.335934f, 0.162102f, 0.008109f),
                new(-1.373426f, 0.186971f, 0.009497f),
                new(-1.413411f, 0.214111f, 0.010981f),
                new(-1.451698f, 0.242618f, 0.012462f),
                new(-1.481870f, 0.270619f, 0.013734f),
                new(-1.498680f, 0.296051f, 0.014532f),
                new(-1.502469f, 0.318204f, 0.014730f),
                new(-1.498087f, 0.337941f, 0.014462f),
                new(-1.494384f, 0.357999f, 0.014132f),
                new(-1.495019f, 0.379865f, 0.013972f),
                new(-1.499506f, 0.403649f, 0.014015f),
                new(-1.505152f, 0.428466f, 0.014194f),
                new(-1.510193f, 0.453617f, 0.014456f),
                new(-1.515643f, 0.479579f, 0.014782f),
                new(-1.524423f, 0.507890f, 0.015167f),
                new(-1.538515f, 0.539879f, 0.015596f),
                new(-1.556486f, 0.575189f, 0.016002f),
                new(-1.573460f, 0.611338f, 0.016243f),
                new(-1.584224f, 0.645152f, 0.016134f),
                new(-1.586035f, 0.674466f, 0.015499f),
                new(-1.580017f, 0.699355f, 0.014252f),
                new(-1.570921f, 0.722482f, 0.012503f),
                new(-1.563885f, 0.747294f, 0.010495f),
                new(-1.561331f, 0.775807f, 0.008409f),
                new(-1.562107f, 0.807565f, 0.006270f),
                new(-1.563210f, 0.840507f, 0.004038f),
                new(-1.562523f, 0.872953f, 0.001740f),
                new(-1.560358f, 0.905069f, -0.000515f),
                new(-1.558646f, 0.938518f, -0.002641f),
                new(-1.558504f, 0.974467f, -0.004636f),
                new(-1.558209f, 1.011483f, -0.006493f),
                new(-1.553280f, 1.045107f, -0.008054f),
                new(-1.539586f, 1.070565f, -0.009033f),
                new(-1.515929f, 1.085586f, -0.009239f),
                new(-1.484074f, 1.090976f, -0.008777f),
                new(-1.448613f, 1.091074f, -0.008098f),
                new(-1.414275f, 1.091125f, -0.007696f),
                new(-1.383486f, 1.094297f, -0.007799f),
                new(-1.355784f, 1.100442f, -0.008359f),
                new(-1.329269f, 1.107393f, -0.009205f),
                new(-1.302760f, 1.113633f, -0.010158f),
                new(-1.276922f, 1.120120f, -0.011075f),
                new(-1.253561f, 1.129733f, -0.011891f),
                new(-1.233774f, 1.144676f, -0.012624f),
                new(-1.216503f, 1.163902f, -0.013245f),
                new(-1.198720f, 1.182716f, -0.013527f),
                new(-1.177181f, 1.195184f, -0.013051f),
                new(-1.150492f, 1.197828f, -0.011459f),
                new(-1.119994f, 1.192005f, -0.008782f),
                new(-1.088887f, 1.183248f, -0.005558f),
                new(-1.060280f, 1.177997f, -0.002509f),
                new(-1.035647f, 1.180252f, -0.000093f),
                new(-1.015345f, 1.192110f, 0.001533f),
                new(-0.997700f, 1.210934f, 0.002587f),
                new(-0.981249f, 1.234007f, 0.003342f),
                new(-0.965770f, 1.261460f, 0.003799f),
                new(-0.951746f, 1.295553f, 0.003805f),
                new(-0.939212f, 1.337669f, 0.003317f),
                new(-0.926998f, 1.385429f, 0.002448f),
                new(-0.913008f, 1.432370f, 0.001480f),
                new(-0.895321f, 1.470751f, 0.000747f),
                new(-0.873273f, 1.495685f, 0.000431f),
                new(-0.847732f, 1.507683f, 0.000460f),
                new(-0.820413f, 1.511780f, 0.000628f),
                new(-0.792798f, 1.513861f, 0.000785f),
                new(-0.765440f, 1.516783f, 0.000936f),
                new(-0.738080f, 1.519207f, 0.001244f),
                new(-0.710502f, 1.519357f, 0.001884f),
                new(-0.682671f, 1.515928f, 0.002997f),
                new(-0.654890f, 1.510391f, 0.004556f),
                new(-0.627519f, 1.506483f, 0.006350f),
                new(-0.600601f, 1.506997f, 0.008159f),
                new(-0.573837f, 1.510778f, 0.009962f),
                new(-0.546940f, 1.512488f, 0.011963f),
                new(-0.520000f, 1.505607f, 0.014376f),
                new(-0.493519f, 1.486711f, 0.017116f),
                new(-0.468043f, 1.458004f, 0.019707f),
                new(-0.443716f, 1.426283f, 0.021570f),
                new(-0.420146f, 1.399081f, 0.022428f),
                new(-0.396689f, 1.380742f, 0.022391f),
                new(-0.372869f, 1.371112f, 0.021719f),
                new(-0.348510f, 1.367725f, 0.020616f),
                new(-0.323384f, 1.369688f, 0.019208f),
                new(-0.297141f, 1.377236f, 0.017586f),
                new(-0.269073f, 1.392489f, 0.015809f),
                new(-0.238507f, 1.416568f, 0.013881f),
                new(-0.205522f, 1.446708f, 0.011755f),
                new(-0.171350f, 1.476114f, 0.009418f),
                new(-0.137985f, 1.496983f, 0.006981f),
                new(-0.107164f, 1.504636f, 0.004669f),
                new(-0.079380f, 1.499878f, 0.002712f),
                new(-0.053714f, 1.487877f, 0.001233f),
                new(-0.028648f, 1.474412f, 0.000217f),
                new(-0.003308f, 1.462158f, -0.000384f),
                new(0.021893f, 1.449757f, -0.000550f),
                new(0.046376f, 1.435720f, -0.000264f),
                new(0.069543f, 1.418810f, 0.000462f),
                new(0.091786f, 1.400301f, 0.001478f),
                new(0.114519f, 1.383471f, 0.002527f),
                new(0.138968f, 1.370611f, 0.003407f),
                new(0.164768f, 1.360426f, 0.004081f),
                new(0.189538f, 1.348003f, 0.004642f),
                new(0.210000f, 1.327616f, 0.005149f),
                new(0.223983f, 1.296442f, 0.005501f),
                new(0.231951f, 1.256560f, 0.005471f),
                new(0.236892f, 1.213823f, 0.004904f),
                new(0.242600f, 1.174455f, 0.003853f),
                new(0.251562f, 1.141786f, 0.002502f),
                new(0.263969f, 1.115352f, 0.001043f),
                new(0.278530f, 1.092698f, -0.000372f),
                new(0.294374f, 1.072252f, -0.001622f),
                new(0.312432f, 1.054942f, -0.002637f),
                new(0.335633f, 1.043977f, -0.003442f),
                new(0.367272f, 1.042501f, -0.004152f),
                new(0.406182f, 1.048096f, -0.004788f),
                new(0.448128f, 1.055281f, -0.005201f),
                new(0.487714f, 1.058100f, -0.005103f),
                new(0.521251f, 1.053170f, -0.004223f),
                new(0.548720f, 1.041209f, -0.002481f),
                new(0.573393f, 1.025971f, -0.000030f),
                new(0.599339f, 1.011433f, 0.002888f),
                new(0.628583f, 0.999221f, 0.006124f),
                new(0.659929f, 0.988010f, 0.009683f),
                new(0.690199f, 0.975049f, 0.013621f),
                new(0.716778f, 0.958423f, 0.017897f),
                new(0.739359f, 0.938241f, 0.022335f),
                new(0.760189f, 0.916459f, 0.026699f),
                new(0.783200f, 0.895917f, 0.030808f),
                new(0.807574f, 0.875879f, 0.034622f),
                new(0.828862f, 0.853407f, 0.038083f),
                new(0.841448f, 0.825334f, 0.041040f),
                new(0.842065f, 0.790426f, 0.043311f),
                new(0.832059f, 0.750313f, 0.044831f),
                new(0.816767f, 0.708573f, 0.045749f),
                new(0.802400f, 0.668676f, 0.046371f),
                new(0.792664f, 0.632281f, 0.046988f),
                new(0.787471f, 0.598966f, 0.047778f),
                new(0.784505f, 0.567361f, 0.048816f),
                new(0.782236f, 0.536644f, 0.050164f),
                new(0.781891f, 0.507214f, 0.051931f),
                new(0.786758f, 0.480117f, 0.054262f),
                new(0.799325f, 0.455843f, 0.057272f),
                new(0.818525f, 0.433524f, 0.060965f),
                new(0.839429f, 0.411227f, 0.065189f),
                new(0.855424f, 0.386971f, 0.069651f),
                new(0.862188f, 0.359928f, 0.074029f),
                new(0.860998f, 0.330964f, 0.078160f),
                new(0.857447f, 0.301775f, 0.082106f),
                new(0.857934f, 0.273755f, 0.086080f),
                new(0.865979f, 0.247262f, 0.090238f),
                new(0.880913f, 0.221711f, 0.094519f),
                new(0.899673f, 0.196249f, 0.098676f),
                new(0.920080f, 0.170371f, 0.102479f),
                new(0.942915f, 0.144041f, 0.105913f),
                new(0.971106f, 0.117330f, 0.109163f),
                new(1.006665f, 0.090022f, 0.112370f),
                new(1.047838f, 0.061598f, 0.115368f),
                new(1.088876f, 0.031594f, 0.117640f),
                new(1.122901f, 0.000000f, 0.118544f),
                new(1.093804f, -0.031691f, 0.113149f),
                new(1.055421f, -0.061896f, 0.106834f),
                new(1.013787f, -0.090442f, 0.100416f),
                new(0.975640f, -0.117687f, 0.094562f),
                new(0.944685f, -0.144216f, 0.089582f),
                new(0.920347f, -0.170403f, 0.085442f),
                new(0.899673f, -0.196249f, 0.081955f),
                new(0.880647f, -0.221669f, 0.078996f),
                new(0.864226f, -0.246949f, 0.076598f),
                new(0.853466f, -0.272868f, 0.074884f),
                new(0.850464f, -0.300246f, 0.073896f),
                new(0.853600f, -0.329191f, 0.073464f),
                new(0.857404f, -0.358682f, 0.073192f),
                new(0.855424f, -0.386971f, 0.072623f),
                new(0.844165f, -0.412660f, 0.071467f),
                new(0.825776f, -0.435873f, 0.069755f),
                new(0.806100f, -0.458184f, 0.067719f),
                new(0.791050f, -0.481694f, 0.065567f),
                new(0.783558f, -0.507863f, 0.063320f),
                new(0.782487f, -0.536748f, 0.060799f),
                new(0.784505f, -0.567361f, 0.057777f),
                new(0.787224f, -0.598853f, 0.054136f),
                new(0.791048f, -0.631503f, 0.049897f),
                new(0.798303f, -0.666605f, 0.045117f),
                new(0.810399f, -0.705198f, 0.039822f),
                new(0.825350f, -0.746590f, 0.034057f),
                new(0.837751f, -0.787922f, 0.028014f),
                new(0.841448f, -0.825334f, 0.022073f),
                new(0.833084f, -0.856078f, 0.016653f),
                new(0.814000f, -0.880118f, 0.012016f),
                new(0.789168f, -0.900020f, 0.008195f),
                new(0.763946f, -0.919148f, 0.005089f),
                new(0.740808f, -0.939321f, 0.002621f),
                new(0.716995f, -0.958591f, 0.000838f),
                new(0.690199f, -0.975049f, -0.000264f),
                new(0.659719f, -0.987834f, -0.000768f),
                new(0.627214f, -0.998030f, -0.000835f),
                new(0.595893f, -1.008319f, -0.000650f),
                new(0.568074f, -1.020981f, -0.000358f),
                new(0.543157f, -1.035789f, -0.000042f),
                new(0.517702f, -1.049580f, 0.000271f),
                new(0.487714f, -1.058100f, 0.000585f),
                new(0.451547f, -1.059009f, 0.000909f),
                new(0.411344f, -1.053937f, 0.001201f),
                new(0.372025f, -1.048085f, 0.001351f),
                new(0.338599f, -1.047596f, 0.001205f),
                new(0.313567f, -1.056380f, 0.000616f),
                new(0.294542f, -1.072473f, -0.000427f),
                new(0.278530f, -1.092698f, -0.001861f),
                new(0.263809f, -1.115124f, -0.003598f),
                new(0.250533f, -1.140263f, -0.005604f),
                new(0.240038f, -1.170510f, -0.007911f),
                new(0.232984f, -1.207558f, -0.010493f),
                new(0.227914f, -1.249813f, -0.013099f),
                new(0.221439f, -1.292009f, -0.015247f),
                new(0.210000f, -1.327616f, -0.016453f),
                new(0.191922f, -1.352533f, -0.016511f),
                new(0.168314f, -1.367470f, -0.015575f),
                new(0.142183f, -1.377297f, -0.013982f),
                new(0.116492f, -1.387773f, -0.012011f),
                new(0.092527f, -1.401998f, -0.009791f),
                new(0.069651f, -1.419069f, -0.007359f),
                new(0.046376f, -1.435720f, -0.004778f),
                new(0.021795f, -1.449494f, -0.002170f),
                new(-0.003926f, -1.460408f, 0.000315f),
                new(-0.030150f, -1.469906f, 0.002539f),
                new(-0.055944f, -1.480763f, 0.004368f),
                new(-0.081617f, -1.492264f, 0.005720f),
                new(-0.108528f, -1.499663f, 0.006645f),
                new(-0.137985f, -1.496983f, 0.007342f),
                new(-0.170163f, -1.481138f, 0.008036f),
                new(-0.203834f, -1.454476f, 0.008788f),
                new(-0.237050f, -1.423900f, 0.009467f),
                new(-0.268228f, -1.397180f, 0.009871f),
                new(-0.296843f, -1.379077f, 0.009872f),
                new(-0.323343f, -1.369967f, 0.009459f),
                new(-0.348510f, -1.367725f, 0.008711f),
                new(-0.372899f, -1.370831f, 0.007717f),
                new(-0.396855f, -1.378884f, 0.006554f),
                new(-0.420486f, -1.394320f, 0.005212f),
                new(-0.444116f, -1.418805f, 0.003747f),
                new(-0.468327f, -1.450042f, 0.002385f),
                new(-0.493612f, -1.481537f, 0.001441f),
                new(-0.520000f, -1.505607f, 0.001123f),
                new(-0.547032f, -1.517663f, 0.001413f),
                new(-0.574121f, -1.518741f, 0.002098f),
                new(-0.601001f, -1.514474f, 0.002877f),
                new(-0.627859f, -1.511244f, 0.003459f),
                new(-0.655056f, -1.512249f, 0.003632f),
                new(-0.682701f, -1.516209f, 0.003291f),
                new(-0.710502f, -1.519357f, 0.002457f),
                new(-0.738039f, -1.518927f, 0.001223f),
                new(-0.765142f, -1.514943f, -0.000360f),
                new(-0.791953f, -1.509170f, -0.002314f),
                new(-0.818956f, -1.504449f, -0.004758f),
                new(-0.846044f, -1.499915f, -0.007558f),
                new(-0.872086f, -1.490662f, -0.010189f),
                new(-0.895321f, -1.470751f, -0.011960f),
                new(-0.914373f, -1.437343f, -0.012481f),
                new(-0.929235f, -1.393043f, -0.011944f),
                new(-0.941442f, -1.344784f, -0.010972f),
                new(-0.953248f, -1.300060f, -0.010200f),
                new(-0.966389f, -1.263210f, -0.009945f),
                new(-0.981348f, -1.234270f, -0.010159f),
                new(-0.997700f, -1.210934f, -0.010542f),
                new(-1.015237f, -1.191851f, -0.010928f),
                new(-1.034906f, -1.178555f, -0.011430f),
                new(-1.058307f, -1.173696f, -0.012110f),
                new(-1.085672f, -1.176562f, -0.012834f),
                new(-1.116448f, -1.184961f, -0.013495f),
                new(-1.148108f, -1.193299f, -0.013989f),
                new(-1.177181f, -1.195184f, -0.014252f),
                new(-1.201263f, -1.187148f, -0.014254f),
                new(-1.220541f, -1.170649f, -0.014074f),
                new(-1.237681f, -1.150942f, -0.013946f),
                new(-1.256122f, -1.133677f, -0.014167f),
                new(-1.277950f, -1.121643f, -0.014915f),
                new(-1.302920f, -1.113860f, -0.016111f),
                new(-1.329269f, -1.107393f, -0.017451f),
                new(-1.355616f, -1.100221f, -0.018608f),
                new(-1.382352f, -1.092859f, -0.019414f),
                new(-1.411309f, -1.087506f, -0.019797f),
                new(-1.443860f, -1.085489f, -0.019604f),
                new(-1.478913f, -1.085135f, -0.018577f),
                new(-1.512510f, -1.081859f, -0.016599f),
                new(-1.539586f, -1.070565f, -0.013866f),
                new(-1.556830f, -1.048698f, -0.010786f),
                new(-1.563771f, -1.016902f, -0.007732f),
                new(-1.563822f, -0.979458f, -0.004935f),
                new(-1.562092f, -0.941632f, -0.002470f),
                new(-1.561727f, -0.906260f, -0.000343f),
                new(-1.562733f, -0.873129f, 0.001449f),
                new(-1.563210f, -0.840507f, 0.002909f),
                new(-1.561890f, -0.807398f, 0.004069f),
                new(-1.559881f, -0.774726f, 0.004983f),
                new(-1.560128f, -0.744605f, 0.005719f),
                new(-1.564954f, -0.718379f, 0.006355f),
                new(-1.573591f, -0.695116f, 0.007002f),
                new(-1.581813f, -0.671795f, 0.007764f),
                new(-1.584224f, -0.645152f, 0.008651f),
                new(-1.577773f, -0.613841f, 0.009543f),
                new(-1.563195f, -0.578912f, 0.010309f),
                new(-1.544883f, -0.543254f, 0.010936f),
                new(-1.528520f, -0.509961f, 0.011545f),
                new(-1.517259f, -0.480358f, 0.012245f),
                new(-1.510439f, -0.453730f, 0.013007f),
                new(-1.505152f, -0.428466f, 0.013670f),
                new(-1.499256f, -0.403546f, 0.014082f),
                new(-1.493353f, -0.379216f, 0.014209f),
                new(-1.490092f, -0.356422f, 0.014083f),
                new(-1.491311f, -0.335600f, 0.013697f),
                new(-1.495237f, -0.315862f, 0.013000f),
                new(-1.494252f, -0.294711f, 0.012003f),
                new(-1.481870f, -0.270619f, 0.010849f),
                new(-1.455574f, -0.243628f, 0.009662f),
                new(-1.418945f, -0.215437f, 0.008505f),
                new(-1.378213f, -0.188020f, 0.007416f),
                new(-1.338719f, -0.162655f, 0.006425f),
                new(-1.302587f, -0.139615f, 0.005543f),
                new(-1.268971f, -0.118534f, 0.004751f),
                new(-1.236138f, -0.098995f, 0.004019f),
                new(-1.203434f, -0.080855f, 0.003329f),
                new(-1.171220f, -0.064120f, 0.002678f),
                new(-1.138641f, -0.048679f, 0.002055f),
                new(-1.105265f, -0.034511f, 0.001463f),
                new(-1.069852f, -0.021603f, 0.000909f),
                new(-1.031320f, -0.010041f, 0.000410f),
            ],
            HighResIndices =
            [
                42, 43, 41, 71, 72, 70, 72, 73, 70, 73, 69, 70,
                73, 74, 69, 75, 68, 74, 75, 76, 68, 76, 67, 68,
                76, 78, 67, 76, 77, 78, 99, 100, 98, 100, 101, 98,
                101, 97, 98, 101, 102, 97, 104, 95, 103, 104, 105, 95,
                105, 106, 95, 111, 113, 110, 111, 112, 113, 134, 135, 133,
                135, 132, 133, 135, 144, 132, 135, 136, 144, 138, 142, 137,
                138, 141, 142, 138, 139, 141, 139, 140, 141, 168, 169, 167,
                198, 196, 197, 198, 195, 196, 198, 199, 195, 202, 204, 201,
                202, 203, 204, 225, 226, 224, 226, 223, 224, 226, 227, 223,
                232, 241, 231, 232, 233, 241, 233, 234, 241, 236, 239, 235,
                236, 238, 239, 236, 237, 238, 253, 254, 252, 254, 251, 252,
                254, 255, 251, 259, 261, 258, 259, 260, 261, 261, 269, 258,
                261, 262, 269, 264, 267, 263, 264, 266, 267, 264, 265, 266,
                294, 295, 293, 295, 296, 293, 296, 292, 293, 296, 297, 292,
                308, 309, 307, 29, 30, 28, 40, 45, 39, 40, 44, 45,
                40, 41, 44, 41, 43, 44, 69, 74, 68, 67, 79, 66,
                67, 78, 79, 83, 85, 82, 83, 84, 85, 97, 102, 96,
                102, 103, 96, 103, 95, 96, 95, 107, 94, 95, 106, 107,
                110, 114, 109, 110, 113, 114, 132, 144, 131, 144, 145, 131,
                144, 136, 143, 136, 137, 143, 137, 142, 143, 167, 170, 166,
                167, 169, 170, 195, 199, 194, 199, 200, 194, 201, 193, 200,
                201, 204, 193, 204, 192, 193, 204, 205, 192, 223, 227, 222,
                227, 228, 222, 230, 242, 229, 230, 241, 242, 230, 231, 241,
                241, 234, 240, 234, 235, 240, 235, 239, 240, 251, 255, 250,
                255, 256, 250, 258, 269, 257, 269, 270, 257, 269, 262, 268,
                262, 263, 268, 263, 267, 268, 292, 297, 291, 297, 298, 291,
                307, 309, 306, 309, 310, 306, 28, 30, 27, 30, 31, 27,
                35, 48, 34, 35, 36, 48, 36, 47, 48, 36, 37, 47,
                39, 46, 38, 39, 45, 46, 56, 57, 55, 57, 54, 55,
                57, 58, 54, 66, 88, 65, 66, 80, 88, 66, 79, 80,
                82, 86, 81, 82, 85, 86, 86, 87, 81, 87, 80, 81,
                87, 88, 80, 94, 116, 93, 94, 108, 116, 94, 107, 108,
                109, 115, 108, 109, 114, 115, 126, 128, 125, 126, 127, 128,
                128, 129, 125, 129, 124, 125, 129, 130, 124, 131, 145, 130,
                145, 124, 130, 145, 123, 124, 145, 146, 123, 153, 156, 152,
                153, 155, 156, 153, 154, 155, 166, 171, 165, 166, 170, 171,
                182, 183, 181, 194, 200, 193, 192, 206, 191, 192, 205, 206,
                208, 212, 207, 208, 211, 212, 208, 209, 211, 209, 210, 211,
                222, 228, 221, 228, 229, 221, 229, 220, 221, 229, 242, 220,
                242, 243, 220, 249, 270, 248, 249, 257, 270, 249, 256, 257,
                249, 250, 256, 270, 271, 248, 279, 283, 278, 279, 282, 283,
                279, 280, 282, 280, 281, 282, 291, 298, 290, 298, 299, 290,
                300, 289, 299, 300, 301, 289, 301, 288, 289, 301, 302, 288,
                302, 287, 288, 302, 303, 287, 306, 310, 305, 310, 311, 305,
                321, 322, 320, 15, 17, 14, 15, 16, 17, 27, 31, 26,
                31, 32, 26, 33, 51, 32, 33, 50, 51, 33, 34, 50,
                34, 49, 50, 34, 48, 49, 54, 58, 53, 58, 59, 53,
                65, 89, 64, 65, 88, 89, 93, 118, 92, 93, 117, 118,
                93, 116, 117, 116, 108, 115, 123, 147, 122, 123, 146, 147,
                147, 148, 122, 148, 149, 122, 152, 157, 151, 152, 156, 157,
                162, 175, 161, 162, 174, 175, 162, 163, 174, 163, 173, 174,
                163, 164, 173, 164, 172, 173, 164, 165, 172, 165, 171, 172,
                181, 184, 180, 181, 183, 184, 191, 213, 190, 191, 207, 213,
                191, 206, 207, 207, 212, 213, 220, 243, 219, 243, 244, 219,
                248, 272, 247, 248, 271, 272, 278, 283, 277, 283, 284, 277,
                287, 303, 286, 303, 304, 286, 304, 285, 286, 304, 312, 285,
                304, 305, 312, 305, 311, 312, 320, 322, 319, 322, 323, 319,
                14, 17, 13, 17, 18, 13, 20, 10, 19, 20, 9, 10,
                20, 21, 9, 21, 22, 9, 22, 8, 9, 22, 23, 8,
                26, 32, 25, 32, 24, 25, 32, 51, 24, 51, 52, 24,
                53, 60, 52, 53, 59, 60, 64, 89, 63, 89, 90, 63,
                90, 62, 63, 90, 0, 62, 90, 91, 0, 91, 120, 0,
                91, 119, 120, 91, 92, 119, 92, 118, 119, 120, 121, 0,
                121, 150, 0, 121, 122, 150, 122, 149, 150, 151, 158, 150,
                151, 157, 158, 160, 177, 159, 160, 176, 177, 160, 161, 176,
                161, 175, 176, 180, 185, 179, 180, 184, 185, 190, 214, 189,
                190, 213, 214, 218, 244, 217, 218, 219, 244, 244, 245, 217,
                245, 216, 217, 245, 0, 216, 245, 246, 0, 246, 274, 0,
                246, 273, 274, 246, 247, 273, 247, 272, 273, 277, 284, 276,
                284, 285, 276, 285, 312, 276, 312, 313, 276, 314, 276, 313,
                314, 329, 276, 314, 328, 329, 314, 327, 328, 314, 315, 327,
                315, 316, 327, 316, 326, 327, 316, 317, 326, 317, 325, 326,
                317, 318, 325, 319, 324, 318, 319, 323, 324, 13, 18, 12,
                18, 19, 12, 19, 11, 12, 19, 10, 11, 8, 23, 7,
                23, 61, 7, 23, 60, 61, 23, 24, 60, 24, 52, 60,
                62, 0, 61, 0, 1, 61, 0, 275, 335, 0, 274, 275,
                276, 330, 275, 276, 329, 330, 7, 61, 6, 61, 5, 6,
                61, 4, 5, 61, 3, 4, 61, 2, 3, 61, 1, 2,
                335, 275, 334, 275, 333, 334, 275, 332, 333, 275, 331, 332,
                275, 330, 331, 325, 318, 324, 0, 215, 216, 0, 186, 215,
                0, 178, 186, 0, 158, 178, 0, 150, 158, 158, 159, 178,
                159, 177, 178, 179, 186, 178, 179, 185, 186, 188, 214, 187,
                188, 189, 214, 214, 215, 187, 215, 186, 187, 289, 290, 299,
                47, 38, 46, 47, 37, 38,
            ]
        },
    };
}

/// <summary>
/// Why these colors:
/// This preset represents Heuchera 'Palace Purple', historically classified as
/// Heuchera micrantha var. diversifolia 'Palace Purple'.
///
/// Healthy foliage is predominantly dark burgundy-purple to bronze-purple,
/// often with a muted metallic or greyish cast. The undersides are typically
/// wine-red / beet-red. Foliage can fade somewhat toward bronze-green during
/// hot summer conditions, but green is not the normal dominant appearance.
///
/// Young leaves are modeled slightly lighter and redder, becoming deeper,
/// darker burgundy-purple as they mature. Seasonal variation is deliberately
/// subtle: spring and autumn strengthen the purple coloration, while summer
/// can slightly reduce chroma. Senescence progresses from muted wine-purple
/// through reddish bronze into brown.
///
/// Sources:
/// - Missouri Botanical Garden: 'Palace Purple' has attractive deep-purple
///   upper leaf surfaces and beet-red undersides; hot summers may cause some
///   fading toward bronze-green.
/// - NC State Extension: foliage ranges from olive/bronze-purple to deep purple,
///   with dark wine-red undersides.
/// </summary>
public static class HeucheraMicranthaPalacePurplePreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(154, 86, 112)) // fresh leaf: lighter raspberry-plum
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(132, 65, 94)) // expanding: rich red-purple
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(108, 51, 76)) // mature: burgundy-purple
        .Add(0.80f, ColorUtils.OkLabFromSrgb8(91, 43, 65)) // adult: deep wine-purple
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(78, 37, 56)); // full adult: dark muted burgundy

    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3(0.004f, 1.06f, 0.012f)) // early spring: fresh, slightly stronger purple
        .Add(0.25f, new Vector3(0.000f, 1.03f, 0.008f)) // late spring: rich purple
        .Add(0.50f, new Vector3(0.004f, 0.96f, 0.000f)) // summer: slight fading / bronzing
        .Add(0.65f, new Vector3(0.002f, 0.94f, 0.000f)) // hottest period: mildly muted, not green
        .Add(0.78f, new Vector3(-0.006f, 1.03f, 0.010f)) // early autumn: purple returns
        .Add(0.90f, new Vector3(-0.012f, 1.07f, 0.016f)) // late autumn: darker wine-purple
        .Add(1.00f, new Vector3(-0.020f, 1.08f, 0.018f)); // winter: deep muted purple

    // Positive a + negative b in OKLab points toward a red-purple / magenta-purple hue.
    public static readonly Vector2 HealthySeasonDirection =
        Vector2.Normalize(new Vector2(0.65f, -0.76f));

    // Anthocyanic stress is already appropriate for a naturally purple cultivar:
    // stress should deepen / cool the existing coloration rather than introduce green.
    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(112, 56, 72)) // muted wine-purple
        .Add(0.30f, ColorUtils.OkLabFromSrgb8(120, 67, 65)) // reddish bronze
        .Add(0.60f, ColorUtils.OkLabFromSrgb8(111, 67, 54)) // warm red-brown
        .Add(0.82f, ColorUtils.OkLabFromSrgb8(96, 60, 46)) // brown with residual red
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(76, 49, 37)); // dry dark brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(96, 51, 68)) // dull stressed burgundy
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(93, 55, 57)) // muted wine-brown
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(82, 51, 44)) // dark reddish-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(65, 43, 34)); // dead dark brown
}
