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
public static class Heuchera_Micrantha
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
        MaxLeaveAge = 24f * 200f,   

        PetioleLength = 0.08f,
        PetioleLengthVar = 0.015f,
        PetioleRadius = 0.002f,
        PetioleRadiusVar = 0.0003f,

        pNewCrown = 0.55f,           
        crownPitch = 0.65f,
        growthFactor = 0.22f,
        MaxRadius = 0.0035f,

        LateralPitch = 15 * (MathF.PI / 180f),
        LateralPitchVar = 10 * (MathF.PI / 180f),
        LateralRoll = 45f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        LateralsPerNode = 2,
        NodeDistance = 0f,          
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.75f,

        RizomeMaxDepth = 3,
        RizomeLength = 0.03f,
        RizomeRadius = 0.003f,
        pExpandRizome = 0.001f,          
        SympodialStartAgeHours = 24f * 40f,
        FloweringStartAgeHours = 24f * 50f,
        FloweringEndAgeHours = 24f * 120f,
        pChaningSeaonns = [0.012f, 0.003f, 0.005f, 0.0f],
        pFloweringSeaonns = [0.003f, 0.045f, 0.003f, 0.0f],  
        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 4e6f,         
        DepthFactor = 0.01f,
        FlowerElasticModulus = 4e7f,

        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.006f,
            PedalRadius = 0.0025f,
            PedalLengthVar = 0.001f,
            pedalGrowthTime = 60f,
            pedalGrowthTimeVar = 12f,

            HasFlowerBaseLeaves = false,
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

            PetalSenescenceDurationH = 144f,

            BudLength = 0.004f,
            BudRadius = 0.0025f,
            BudBloomAge = 120u,
            FlowerMaxAge = 600u,

            PetiolLength = 0.008f,
            PetiolRadius = 0.0004f,
            PetiolLengthVar = 0.003f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            bStemLength = 0.008f,
            bStemLengthVar = 0.004f,
            bStemRadius = 0.0006f,
            bStemRadiusVar = 0.0001f,

            stemLength = 0.012f,
            stemLengthVar = 0.002f,
            fStemRadius = 0.0005f,
            fStemRadiusVar = 0.0001f,

            flowerBaseDebth = 4,
            flowerDebth = 2,
            LateralsPerNode = 2,
            FlowersPerInternode = 2,
            clusterSize = 3,
            clusterAngle = 0.18f,
            deterministic = true,
            continous = false,
            internodeFlower = true,
            internodeFlowerWithStem = true,
            floralDepthFactor = 1,
            pFlowerDebth = 0.5f,

            LateralAngle = 25,
            LateralRoll = 12,
            BaseLaterals = 1,
            BaseLateralAngle = 65,
            BaseLateralRoll = 8,
            pFlowerBaseDebth = 0.6f,

            LeafLength = 0.012f,
            LeafRadius = 0.006f,
            LeafLengthVar = 0.002f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.003f,
            LeavePetioleRadius = 0.0003f,

            growthTime = 90f,
        },

        LeafPhenology = new LeafPhenology(
            expectedAge: 1.5f * 365f * 24f,
            colorModel: new ColorModel(
                HeucheraMicranthaPreset.Lifecycle,
                HeucheraMicranthaPreset.Stress,
                HeucheraMicranthaPreset.SeasonalSenescence,
                HeucheraMicranthaPreset.StressSenescence
            )
            {
                HealthySeasonCurve = HeucheraMicranthaPreset.HealthySeason,
                HealthySeasonDirection = HeucheraMicranthaPreset.HealthySeasonDirection,
            }
        ),

        // Placeholder for leaf shape - to be replaced with actual Heuchera leaf mesh later
        LeafMorphology = new() {
            LowResVertices = null,
            LowResIndices = null,
            HighResVertices = null,
            HighResIndices = null,
        },
    };
}

/// <summary>
/// Why these colors: Heuchera micrantha foliage is typically green with variable 
/// mottling, silver markings, or purple veining. Leaves are semi-evergreen in mild 
/// climates. The lifecycle stays mostly green with subtle seasonal variation, 
/// while senescence follows a green→olive→brown path.
/// Sources:
/// - Missouri Botanical Garden: "leaves are rounded to heart-shaped, often with 
///   silver or purple markings"
/// - RHS: "foliage varies from green to purple-bronze, often with silver marbling"
/// </summary>
public static class HeucheraMicranthaPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(165, 210, 145)) // fresh light green with silver hints
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(105, 165, 105)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 88, 150,  92)) // mature green (may have mottling)
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 82, 142,  88)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 78, 135,  85)); // full adult, stable

    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.000f, 1.00f, 0.000f)) // early spring neutral
        .Add(0.25f, new Vector3( 0.000f, 1.00f, 0.000f)) // spring/summer unchanged
        .Add(0.60f, new Vector3( 0.000f, 1.00f, 0.000f)) // still neutral
        .Add(0.75f, new Vector3(-0.008f, 1.02f, 0.008f)) // early autumn: hint of warmth
        .Add(0.88f, new Vector3(-0.018f, 1.05f, 0.015f)) // late autumn: subtle purple/bronze tint
        .Add(1.00f, new Vector3(-0.025f, 1.07f, 0.018f)); // winter: deeper tone, semi-evergreen

    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(0.65f, -0.76f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(170, 155,  90)) // olive-gold
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(155, 120,  70)) // warm bronze
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(135,  95,  65)) // reddish-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 95,  70,  45)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(125, 125,  75)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(115,  90,  55)) // stress brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 85,  62,  40)); // dark brown
}
