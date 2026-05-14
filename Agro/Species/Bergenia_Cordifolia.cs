namespace Agro.Species;

public static class Bergenia_Cordifolia
{
    public static SpeciesSettings Init() => new()
    {
        Name = "Bergenia Cordifolia",
        Behavior = Behavior.Bergenia_Cordifolia,
        LeafLength = 0.24f,
        LeafRadius = 0.09f,
        PetioleLength = 0.005f,
        PetioleRadius = 0.004f,
        LeafGrowthTime = 24 * 7 * 12,
        Height = 0.04f,
        NodeDistance = 0,
        NodeDistanceVar = 0,
        pChaningSeaonns = [0.015f, 0.02f, 0.01f, 0f],
        pFloweringSeaonns = [0.0005f, 0.005f, 0.0003f, 0f],

        MaxLeaveAge = 100,
        pNewCrown = 0.5f,
        crownPitch = 0.4f,
        growthFactor = 0.2f,
        MaxRadius = 0.005f,
        pExpandRizome = 0.0005f,
        RizomeMaxDepth = 3,
        RizomeLength = 0.04f,
        RizomeRadius = 0.0025f,

        LeafPhenology = new(),
        LeafMorphology = new(),
    };
}