using System.Numerics;

namespace Agro.Species;

/// <summary>
/// Fragaria vesca 'Rügen' (Alpine Strawberry, Red Alpine)
/// Sources:
/// - Missouri Botanical Garden (wild strawberry; trifoliate leaves 5-10cm;
///   white flowers 1-2cm diameter; red fruits 5-8mm; evergreen/semi-evergreen)
/// - RHS Plant Finder (perpetual flowering alpine strawberry; fruits continuously
///   summer through autumn; compact mounding habit 15-25cm tall)
/// - Gardenia.net (fragrant white flowers May-October; sweet aromatic fruits;
///   leaves bright green, serrated margins, trifoliate)
/// </summary>
public static class Fragaria_Vesca_Ruegen
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Fragaria vesca 'Rügen'",
        Aka = "Alpine Strawberry",
        Behavior = Behavior.Herbaceous,

        Height = 0.20f,

        LeafLength = 0.06f,
        LeafLengthVar = 0.012f,
        LeafRadius = 0.045f,       
        LeafRadiusVar = 0.008f,
        LeafPitchVar = 5f * (MathF.PI / 180f),
        LeafPitch = 85 * (MathF.PI / 180f),

        LeafGrowthTime = 24f * 10f * 2f,
        LeafGrowthTimeVar = 24f * 4f,
        MaxLeaveAge = 24f * 180f,   

        PetioleLength = 0.07f,
        PetioleLengthVar = 0.012f,
        PetioleRadius = 0.0018f,
        PetioleRadiusVar = 0.0003f,

        pNewCrown = 0.55f,           
        crownPitch = 0.60f,
        growthFactor = 0.20f,
        MaxRadius = 0.003f,

        LateralPitch = 15 * (MathF.PI / 180f),
        LateralPitchVar = 10 * (MathF.PI / 180f),
        LateralRoll = 45f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        LateralsPerNode = 3,
        NodeDistance = 0f,          
        NodeDistanceVar = 0.000f,
        DominanceFactor = 0.70f,

        RizomeMaxDepth = 2,
        RizomeLength = 0.025f,
        RizomeRadius = 0.0025f,
        pExpandRizome = 0.0008f,          
        SympodialStartAgeHours = 24f * 35f,
        FloweringStartAgeHours = 24f * 40f,
        FloweringEndAgeHours = 24f * 200f,
        pChaningSeaonns = [0.010f, 0.003f, 0.006f, 0.0f],
        pFloweringSeaonns = [0.005f, 0.060f, 0.008f, 0.0f],  
        WoodElasticModulus = 1.0e8f,
        GreenElasticModulus = 4e6f,         
        DepthFactor = 0.01f,
        FlowerElasticModulus = 4e7f,

        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.008f,
            PedalRadius = 0.004f,
            PedalLengthVar = 0.001f,
            pedalGrowthTime = 72f,
            pedalGrowthTimeVar = 18f,
            pedalCount = 5,

            HasFlowerBaseLeaves = false,
            FlowerPhenology = new LeafPhenology(
                expectedAge: 18f * 24f,
                colorModel: new ColorModel(
                    age: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(250, 250, 248))
                        .Add(0.25f, ColorUtils.OkLabFromSrgb8(248, 248, 245))
                        .Add(0.70f, ColorUtils.OkLabFromSrgb8(245, 245, 242))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(240, 240, 238)),
                    stress: new ColorCurve()
                        .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                        .Add(1.00f, new Vector3(-0.04f, 0.82f, 0.01f)),
                    senescenceSeason: new ColorCurve()
                        .Add(0.00f, ColorUtils.OkLabFromSrgb8(235, 235, 230))
                        .Add(0.40f, ColorUtils.OkLabFromSrgb8(220, 215, 205))
                        .Add(0.75f, ColorUtils.OkLabFromSrgb8(195, 185, 170))
                        .Add(1.00f, ColorUtils.OkLabFromSrgb8(165, 155, 135))
                )
            ),

            PetalSenescenceDurationH = 96f,

            BudLength = 0.005f,
            BudRadius = 0.003f,
            BudBloomAge = 120u,
            FlowerMaxAge = 480u,

            PetiolLength = 0.006f,
            PetiolRadius = 0.0004f,
            PetiolLengthVar = 0.002f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            bStemLength = 0.005f,
            bStemLengthVar = 0.002f,
            bStemRadius = 0.0005f,
            bStemRadiusVar = 0.0001f,

            stemLength = 0.008f,
            stemLengthVar = 0.002f,
            fStemRadius = 0.0004f,
            fStemRadiusVar = 0.0001f,

            flowerBaseDebth = 3,
            flowerDebth = 1,
            LateralsPerNode = 1,
            FlowersPerInternode = 1,
            clusterSize = 2,
            clusterAngle = 0.18f,
            deterministic = true,
            continous = true,
            internodeFlower = true,
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 0.5f,

            LateralAngle = 25,
            LateralRoll = 12,
            BaseLaterals = 1,
            BaseLateralAngle = 60,
            BaseLateralRoll = 8,
            pFlowerBaseDebth = 0.5f,

            LeafLength = 0.010f,
            LeafRadius = 0.005f,
            LeafLengthVar = 0.002f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.003f,
            LeavePetioleRadius = 0.0003f,

            growthTime = 84f,
        },

        LeafPhenology = new LeafPhenology(
            expectedAge: 1.0f * 365f * 24f,
            colorModel: new ColorModel(
                FragariaRuegenPreset.Lifecycle,
                FragariaRuegenPreset.Stress,
                FragariaRuegenPreset.SeasonalSenescence,
                FragariaRuegenPreset.StressSenescence
            )
            {
                HealthySeasonCurve = FragariaRuegenPreset.HealthySeason,
                HealthySeasonDirection = FragariaRuegenPreset.HealthySeasonDirection,
            }
        ),

        // Placeholder for leaf shape - to be replaced with actual Fragaria trifoliate leaf mesh later
        // Leaves are trifoliate (3 leaflets) with serrated margins
        LeafMorphology = new() {
            LowResVertices = null,
            LowResIndices = null,
            HighResVertices = null,
            HighResIndices = null,
        },

        // Strawberry-specific: fruits develop from fertilized flowers
        // Fruits are aggregate accessory fruits (receptacle + achenes)
        FruitSettings = new FruitSettings
        {
            FruitType = FruitType.Accessory,
            InitialRadius = 0.002f,
            MatureRadius = 0.006f,
            InitialLength = 0.003f,
            MatureLength = 0.008f,
            GrowthTimeHours = 24f * 21f,
            RipeningTimeHours = 24f * 7f,
            UnripeColor = ColorUtils.OkLabFromSrgb8(220, 235, 210),
            RipeningColor = ColorUtils.OkLabFromSrgb8(200, 50, 45),
            MatureColor = ColorUtils.OkLabFromSrgb8(180, 35, 30),
            OverripeColor = ColorUtils.OkLabFromSrgb8(140, 25, 25),
            SeedCount = 150,
            SeedRadius = 0.0004f,
            SeedColor = ColorUtils.OkLabFromSrgb8(180, 165, 140),
            FruitPerFlowerProbability = 0.85f,
            PollinationRequired = true,
            PollinationWindowHours = 48f,
            FruitRetentionDays = 14,
        },
    };
}

/// <summary>
/// Fruit settings for Fragaria vesca 'Rügen'
/// Alpine strawberries produce small aggregate accessory fruits continuously
/// through the growing season once established.
/// </summary>
public class FruitSettings
{
    public FruitType FruitType { get; init; } = FruitType.Accessory;
    
    public float InitialRadius { get; init; } = 0.002f;
    public float MatureRadius { get; init; } = 0.006f;
    public float InitialLength { get; init; } = 0.003f;
    public float MatureLength { get; init; } = 0.008f;
    
    public float GrowthTimeHours { get; init; } = 24f * 21f;
    public float RipeningTimeHours { get; init; } = 24f * 7f;
    
    public Vector3 UnripeColor { get; init; }
    public Vector3 RipeningColor { get; init; }
    public Vector3 MatureColor { get; init; }
    public Vector3 OverripeColor { get; init; }
    
    public int SeedCount { get; init; } = 150;
    public float SeedRadius { get; init; } = 0.0004f;
    public Vector3 SeedColor { get; init; }
    
    public float FruitPerFlowerProbability { get; init; } = 0.85f;
    public bool PollinationRequired { get; init; } = true;
    public float PollinationWindowHours { get; init; } = 48f;
    public float FruitRetentionDays { get; init; } = 14f;
}

public enum FruitType { Berry, Accessory, Aggregate, Pome, Drupe }
