using System.Numerics;

namespace Agro.Species;

public static class Bergenia_Cordifolia
{
    const float DegToRad = MathF.PI / 180f;
    public static SpeciesSettings Init() => new SpeciesSettings()
    {
        Name = "Bergenia Cordifolia",
        Behavior = Behavior.Herbaceous,
        LeafLength = 0.24f,
        LeafRadius = 0.09f,
        PetioleLength = 0.01f, 
        PetioleRadius = 0.004f,
        LeafGrowthTime = 24 * 7 * 12,
        Height = 0.04f,
        NodeDistance = 0,
        NodeDistanceVar = 0,
        pChaningSeaonns = [0.8f, 0.02f, 0.01f, 0f],
        pFloweringSeaonns = [0.005f, 0f, 0f, 0f],

        MaxLeaveAge = 2* 365,
        pNewCrown = 0.5f,
        crownPitch = 0.4f,
        growthFactor = 0.2f,
        MaxRadius = 0.005f,
        pExpandRizome = 0.05f,
        RizomeMaxDepth = 4,
        RizomeLength = 0.01f,
        RizomeRadius = 0.025f,
        LateralRoll = 45,
        LateralPitch= 30f * DegToRad,
        FlowerSettings = new FlowerSettings
        {

            // --- Petal (FlowerPadel) ---
            // Petals 10–12 mm long, 6–8 mm wide, obovate. Bell/cup-shaped flower
            // ~18 mm across. Thicker and more substantial than Geranium petals.
            PedalLength = 0.011f,
            PedalRadius = 0.004f,
            PedalLengthVar = 0.001f,
            pedalGrowthTime = 96f,       // fast expansion — early spring rush, ~4 days
            pedalGrowthTimeVar = 24f,
            pedalColor = new Vector3(220, 80, 150), // deep magenta-pink (Purpurea); use (240,160,190) for pale pink species type

            // --- Bud (FlowerBud) ---
            // Rounded buds, deep pink before opening. Open over ~5–8 days.
            BudLength = 0.009f,
            BudRadius = 0.004f,
            BudBloomAge = 244u,      // ~6 days at 1 tick/hr
            FlowerMaxAge = 504u,      // ~21 days — flowers last 3 weeks in cool weather

            // --- Pedicel (FlowerPetiol) ---
            // Very short — flowers are nearly sessile within the dense panicle head.
            // Pedicel ~4–8 mm, very thin.
            PetiolLength = 0.005f,
            PetiolRadius = 0.0003f,
            PetiolLengthVar = 0.002f,
            PetiolRadiusVar = 0.0001f,
            petiolSegments = 1,

            // --- Scape / inflorescence stem (FlowerStem) ---
            // Thick, leafless, red scape 30–60 cm tall, rising 2–3× foliage height.
            // The scape is the dominant visual feature — stout and erect.
            stemLength = 0.0040f,
            stemLengthVar = 0.0010f,
            fStemRadius = 0.005f,    // thick rhubarb-red scape, ~8–10 mm diameter
            fStemRadiusVar = 0.001f,
            bStemLength = 0.006f,     // branching sub-peduncle within the panicle head
            bStemRadius = 0.002f,
            bStemLengthVar = 0.002f,
            bStemRadiusVar = 0.0005f,

            // --- Inflorescence branching ---
            // Dense corymb/panicle: 3 levels of branching, 2–3 flowers per terminal node.
            // A single scape carries 20–50+ flowers in a dense rounded head.
            // flowerDebth = 3 + clusterSize = 2 gives 2^3 × 2 = 16 terminal buds — realistic.
            flowerDebth = 6,
            flowerBaseDebth = 25,
            LateralsPerNode = 1,
            FlowersPerInternode = 0,
            clusterSize = 2,
            clusterAngle = 0.25f,     // wide spreading angle — open corymb shape
            deterministic = false,
            continous = false,
            internodeFlower = false,     // all flowers at terminal — no internode flowers on scape
            internodeFlowerWithStem = false,
            floralDepthFactor = 1,
            pFlowerDebth = 1.0f,

            // --- Angles ---
            // Flowers nod/droop slightly outward from the central scape.
            // Lateral branches spread broadly, ~60–80° from scape axis.
            LateralAngle = 90,
            LateralRoll = 20,
            BaseLaterals = 1,         // 2–3 main branches at base of panicle
            BaseLateralAngle = 70,
            BaseLateralRoll = 15,
            pFlowerBaseDebth = 1.0f,

            // --- Floral leaf / bract ---
            // Bergenia has NO leaf bracts on the scape — it is leafless.
            // Set near zero; any small scale-like bracts are negligible.
            LeafLength = 0.005f,
            LeafRadius = 0.002f,
            LeafLengthVar = 0.001f,
            LeafRadiusVar = 0.001f,
            LeavePetioleLength = 0.001f,
            LeavePetioleRadius = 0.0002f,

            // --- Growth timing ---
            // Fast early-spring growth — scape elongates rapidly over ~2 weeks.
            growthTime = 72f,
        },


        LeafPhenology = new(),
        LeafMorphology = new(),
    };
}