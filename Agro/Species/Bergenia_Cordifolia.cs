using System.Numerics;

namespace Agro.Species;

public static class Bergenia_Cordifolia
{
    const float DegToRad = MathF.PI / 180f;
    public static SpeciesSettings Init() => new SpeciesSettings()
    {
        Name = "Bergenia Cordifolia",
        Behavior = Behavior.Herbaceous,
        LeafLength = 0.14f,
        LeafRadius = 0.06f,
        PetioleLength = 0.01f, 
        PetioleRadius = 0.0025f,
        LeafGrowthTime = 24 * 7 * 12,
        Height = 0.04f,
        NodeDistance = 0,
        NodeDistanceVar = 0,
        pChaningSeaonns = [0.008f, 0.002f, 0.00f, 0f],
        pFloweringSeaonns = [0f, 0.005f, 0.0005f, 0f],

        MaxLeaveAge = 24f * 180f * 2f,
        pNewCrown = 0.5f,
        crownPitch = 0.4f,
        growthFactor = 0.2f,
        MaxRadius = 0.005f,
        pExpandRizome = 0.005f,
        RizomeMaxDepth = 4,
        RizomeLength = 0.03f,
        RizomeRadius = 0.005f,
        LateralRoll = 45,
        LateralPitch= 30f * DegToRad,
        PetiolElasticModulus = 1e4f,
        FlowerElasticModulus = 5e4f,
        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.011f,
            PedalRadius = 0.0035f,
            PedalLengthVar = 0.001f,
            pedalGrowthTime = 96f,
            pedalGrowthTimeVar = 24f,

            FlowerPhenology = new LeafPhenology(
        expectedAge: 504f,
        colorModel: new ColorModel(
            age: new ColorCurve()
                .Add(0.00f, ColorUtils.OkLabFromSrgb8(230, 100, 165))
                .Add(0.25f, ColorUtils.OkLabFromSrgb8(215, 85, 150))
                .Add(0.70f, ColorUtils.OkLabFromSrgb8(200, 80, 140))
                .Add(1.00f, ColorUtils.OkLabFromSrgb8(180, 72, 125)),
            stress: new ColorCurve()
                .Add(0.00f, new Vector3(0.00f, 1.00f, 0.00f))
                .Add(1.00f, new Vector3(-0.04f, 0.82f, 0.01f)),
            senescenceSeason: new ColorCurve()
                .Add(0.00f, ColorUtils.OkLabFromSrgb8(170, 100, 130))
                .Add(0.40f, ColorUtils.OkLabFromSrgb8(145, 85, 95))
                .Add(0.75f, ColorUtils.OkLabFromSrgb8(115, 70, 65))
                .Add(1.00f, ColorUtils.OkLabFromSrgb8(88, 58, 48))
        )
    ),

            PetalSenescenceDurationH = 168f,

            BudLength = 0.009f,
            BudRadius = 0.004f,
            BudBloomAge = 144u,
            FlowerMaxAge = 504u,

            PetiolLength = 0.0025f,
            PetiolRadius = 0.0005f,
            PetiolLengthVar = 0.0010f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            bStemLength = 0.0040f,
            bStemLengthVar = 0.0004f,
            bStemRadius = 0.005f,
            bStemRadiusVar = 0.001f,

            stemLength = 0.0015f,
            stemLengthVar = 0.0003f,
            fStemRadius = 0.0012f,
            fStemRadiusVar = 0.0003f,

            flowerBaseDebth = 25,
            flowerDebth = 6,
            LateralsPerNode = 1,
            FlowersPerInternode = 0,
            clusterSize = 2,
            clusterAngle = 0.25f,
            deterministic = false,
            continous = false,
            internodeFlower = false,
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 1.0f,

            LateralAngle = 90,
            LateralRoll = 20,
            BaseLaterals = 1,
            BaseLateralAngle = 70,
            BaseLateralRoll = 15,
            pFlowerBaseDebth = 1.0f,

            pBaseFlowerHeight = 0.2f,

            LeafLength = 0.005f,
            LeafRadius = 0.002f,
            LeafLengthVar = 0.001f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.001f,
            LeavePetioleRadius = 0.0002f,

            growthTime = 72f,


        },

        
        LeafPhenology = new LeafPhenology(
            expectedAge: 2f * 365f * 24f,
            colorModel: new ColorModel(
                BergeniaCordifoliaPreset.Lifecycle,
                BergeniaCordifoliaPreset.Stress,
                BergeniaCordifoliaPreset.SeasonalSenescence,
                BergeniaCordifoliaPreset.StressSenescence
            )
            {
                HealthySeasonCurve = BergeniaCordifoliaPreset.BergeniaHealthySeason,
                HealthySeasonDirection = BergeniaCordifoliaPreset.HealthySeasonDirection,
            }
        ),
        LeafMorphology = new(),
    };
}