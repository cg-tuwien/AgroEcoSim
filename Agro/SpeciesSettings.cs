using System.Numerics;
using System.Text.Json.Serialization;
using Agro.Species;

namespace Agro;

public enum Behavior : byte { Default, Herbaceous }

public class SpeciesSettings
{
    const float DegToRad = MathF.PI / 180f;
    ///<summary>
    /// Species name
    ///</summary>
    [SpeciesUIGroup("Name")]
    [SpeciesUITooltip("Official latin name")]
    public string Name { get; init; } = "Default";

    /// <summary>
    /// Colloquial name
    /// </summary>
    [SpeciesUITooltip("Colloquial name")]
    public string Aka { get; init; }

    /// <summary>
    /// Growth behavoir of the plant
    /// </summary>
    public Behavior Behavior { get; init; } = Behavior.Default;

    ///<summary>
    /// Mean height of the plant (in meters)
    ///</summary>
    [SpeciesUILabel("Mean height")]
    public float Height { get; init; } = 10f;

    ///<summary>
    /// Standard length of a stem segment (in meters)
    ///</summary>
    [SpeciesUIGroup("Name")]
    [SpeciesUILabel("Node distance")]
    [SpeciesUITooltip("Stem segments length (in meters)")]
    public float NodeDistance { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the stem segment length (in meters)
    ///</summary>
    public float NodeDistanceVar { get; init; } = 0.01f;

    [SpeciesUIGroup("Branching")]
    ///<summary>
    /// For monopodial branching select 1, for dichotomous select 0 and for anisotomous select sth. between
    ///</summary>
    [SpeciesUILabel("Monopodial factor")]
    public float MonopodialFactor { get; init; } = 1;

    private float dominanceFactor;
    public float[] DominanceFactors = [0.7f];

    ///<summary>
    /// Dominance factor reduces (or boosts) growth of lateral branches. Multiplies with each recursion level.
    ///</summary>
    [SpeciesUILabel("Dominance factor")]
    public float DominanceFactor { get => dominanceFactor; init {
        dominanceFactor = value;
        const int factors = 16;
        DominanceFactors = new float[factors + 1];
        DominanceFactors[0] = 1;
        DominanceFactors[1] = 1;
        DominanceFactors[2] = dominanceFactor;
        for(int i = 3; i < factors; ++i)
            DominanceFactors[i] = MathF.Pow(dominanceFactor, i);
    } }

    ///<summary>
    /// Number of lateral branches emerging from a node
    ///</summary>
    [SpeciesUILabel("Branching amount")]
    public int LateralsPerNode { get; init; } = 2;

    ///<summary>
    /// Angle of the lateral to its parent, in radians
    ///</summary>
    [SpeciesUILabel("Lateral pitch")]
    [SpeciesUITooltip("Angle of the growth direction to the growth direction of the parent (in deg)")]
    public float LateralPitch { get; init; } = 45f * DegToRad;

    ///<summary>
    /// Variance of the angle of the lateral to its parent, in radians
    ///</summary>
    public float LateralPitchVar { get; init; } = 5f * DegToRad;

    ///<summary>
    /// Angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    [SpeciesUILabel("Lateral roll")]
    [SpeciesUITooltip("Orientation along the growth direction to the orientation of the parent (in deg)")]
    public float LateralRoll { get; init; } = 0f;

    ///<summary>
    /// Variance of the angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    public float LateralRollVar { get; init; } = 5f * DegToRad;

    [SpeciesUILabel("Twigs bending rate")]
    public float TwigsBending { get; init; } = 0.5f;

    [SpeciesUILabel("Twigs hierarchical bending")]
    [SpeciesUITooltip("Twigs bending factor by hierarchy level (multiplicative)")]
    public float TwigsBendingByLevel { get; init; } = 1;

    [SpeciesUILabel("Apex bending rate")]
    public float TwigsBendingApical { get; set; } = 0.02f;

    [SpeciesUILabel("Shoots gravitaxis")]
    public float ShootsGravitaxis { get; set; } = 0.2f;

    /// <summary>
    /// Standard wood growth time (in hours)
    /// </summary>
    [SpeciesUILabel("Wood maturing time (hours)")]
    public float WoodGrowthTime { get; set; } = 100f;
    /// <summary>
    /// Variance of the wood growth time (in hours)
    /// </summary>
    public float WoodGrowthTimeVar { get; set; } = 10f;

    ///<summary>
    /// Maximum branch level that supports leaves (here level denotes the max. subtree depth)
    ///</summary>
    // public int LeafLevel { get; init; } = 2;

    ///<summary>
    /// Standard leaf length along its main axis (in meters)
    ///</summary>
    [SpeciesUIGroup("Leafs")]
    [SpeciesUILabel("Leaf length")]
    public float LeafLength { get; init; } = 0.12f;

    ///<summary>
    /// Variance of the leaf length along its main axis (in meters)
    ///</summary>
    public float LeafLengthVar { get; init; } = 0.02f;

    ///<summary>
    /// Standard leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    [SpeciesUILabel("Leaf radius")]
    public float LeafRadius { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    public float LeafRadiusVar { get; init; } = 0.01f;

    ///<summary>
    /// Standard leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    [SpeciesUILabel("Leaf pitch")]
    [SpeciesUITooltip("Mean leaf angle to the petiole (in deg)")]
    public float LeafPitch { get; init; } = 20f * DegToRad;

    ///<summary>
    /// Variance of the leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    public float LeafPitchVar { get; init; } = 5f * DegToRad;

    ///<summary>
    /// Standard petiole length (in meters)
    ///</summary>
    [SpeciesUILabel("Petiole length")]
    public float PetioleLength { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the petiole length (in meters)
    ///</summary>
    public float PetioleLengthVar { get; init; } = 0.01f;

    ///<summary>
    /// Standard petiole radius (in meters)
    ///</summary>
    [SpeciesUILabel("Petiole radius")]
    public float PetioleRadius { get; init; } = 0.0025f;

    ///<summary>
    /// Variance of the petiole radius (in meters)
    ///</summary>
    public float PetioleRadiusVar { get; init; } = 0.0005f;

    [SpeciesUIIgnore]
    public float PetioleCoverThreshold { get; private set; } = float.MaxValue;

    ///<summary>
    /// Standard growth time of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf growth time")]
    public float LeafGrowthTime { get; init; } = 480f;

    ///<summary>
    /// Variance of the growth time of a leaf (in hours)
    ///</summary>
    public float LeafGrowthTimeVar { get; init; } = 120f;

    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf seasonal senescence")]
    public float LeafSenescenceSeasonalDuration { get; init; } = 480f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceSeasonalDurationVar { get; init; } = 120f;

    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf stress senescence")]
    public float LeafSenescenceStressDuration { get; init; } = 140f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceStressDurationVar { get; init; } = 60f;

    /// <summary>
    /// If positive, marks a typical senescence period (in seasonal progress, i.e. think of roman months)
    /// </summary>
    //TODO should be simply conted from the astronomical spring
    [SpeciesUILabel("Season duration (in months)")]
    public float SeasonalSenescence { get; init; } = 7.8f / 12;

    ///<summary>
    /// Density of the roots system (affects branching probabiilty). Valued 0 to 1 (with one being the most dense)
    ///</summary>
    [SpeciesUIGroup("Roots")]
    [SpeciesUILabel("Roots density")]
    public float RootsDensity { get; init; } = 0.5f;

    ///<summary>
    /// Correction factor to point the roots growth downwards
    ///</summary>
    [SpeciesUILabel("Roots gravitaxis")]
    public float RootsGravitaxis { get; init; } = 0.2f;

    // public float RootRadiusGrowthPerH { get; init; }
    // public float RootLengthGrowthPerH { get; init; }

    //public int FirstFruitHour { get; init; }

    /// <summary>
    /// Dry wood density in g/m³
    /// </summary>
    [SpeciesUILabel("Dry wood density")]
    public float DensityDryWood { get; init; } = 700_000;
    /// <summary>
    /// Dry stem density in g/m³
    /// </summary>
	[SpeciesUILabel("Dry stem density")]
    public float DensityDryStem { get; init; } = 200_000;

    public float AuxinsProduction { get; init; } = 40;
    public float CytokininsProduction { get; init; } = 40;

    public float AuxinsReach { get; init; } = 1;

    public float CytokininsReach { get; init; } = 1;

    public float AuxinsThreshold { get; init; } = 1f;


    #region Leaf Appearance
    public LeafPhenology LeafPhenology { get; set; } = new();

    /// <summary>
    /// Radius-Length ratios along the leaf. If null or empty, fallback to the the default quad.
    /// </summary>
    /// <remarks>
    /// Assuming Radius is the half-width of the leaf at x-axis and Length is its y-axis.
    /// Then every entry means: The leaf reaches x factor of its Radius at y of its length.
    /// Both x and y are normalized, only values from [0..1] are valid.
    /// Implicit values: [Petiole_radius, 0]
    /// </remarks>
    //public Vector2[] LeafShape { get; init; }

    public LeafMorphology LeafMorphology { get; init; } = new()
    {
        Complexity = LeafComplexity.Simple,
        Shape = new OvateLeafShape(),
        Dimensions = LeafDimensions.Create(lengthM: 0.07f, widthM: 0.06f, thicknessM: 0.00035f, petioleM: 0.015f),
        Margin = LeafMarginType.Entire,
        Venation = LeafVenationType.Palmate,
        LobeCount = 3,
    };
    #endregion

    /// <summary>
    /// Age (hours) when plant switches to deterministic sympodial growth.
    /// </summary>
    public float SympodialStartAgeHours = 24f * 45f;

    /// <summary>
    /// Minimum plant age to start flowering (in hours)
    /// </summary>
    [SpeciesUIGroup("Flowers")]
    [SpeciesUILabel("Flowering minimum age")]
    [SpeciesUITooltip(" Minimum plant age to start flowering (in hours)")]
    public int FloweringStartAgeHours { get; internal set; } = 24 * 45;

    /// <summary>
    /// Typical flowering start within the season (in seasonal factor)
    /// </summary>
    [SpeciesUILabel("Seasonal flowering typical start")]
    public float FloweringStartSeason { get; internal set; } = 2f / 12;

    /// <summary>
    /// Latest flowering start within the season (in seasonal factor)
    /// </summary>
    [SpeciesUILabel("Seasonal flowering latest start")]
    public float FloweringEndSeason { get; internal set; } = 5f / 12;
    /// <summary>
    /// Flowering duration (in hours); growth stops afterwards.
    /// </summary>
    [SpeciesUILabel("Flowering duration")]
    public float FloweringDurationAgeHours { get; internal set; } = 24f * 30f;
    public float FloweringDurationAgeHoursVar { get; internal set; } = 0f; //TODO
    // public float FlowerLengthVar { get; internal set; }
    // public float FlowerRadiusVar { get; internal set; }

    public FlowerSettings FlowerSettings { get; set; } = new FlowerSettings();

    #region Fruit Settings
    /// <summary>
    /// Fruit production settings for species that produce edible fruits.
    /// Null for ornamental-only species.
    /// </summary>
    public FruitSettings? FruitSettings { get; init; } = null;
    #endregion

    [JsonPropertyName("WEM")]
    public float WoodElasticModulus { get; init; } = 1e6f; // in Pa

    [JsonPropertyName("GEM")]
    public float GreenElasticModulus { get; init; } = 1e8f; // in Pa

    [JsonPropertyName("DF")]
    public float DepthFactor { get; init; } = 0.01f; // in Pa


    //-- custom parmeter Geranium & Bergania
    #region GeraniumBergania
    /// <summary>
    /// Maximum leaf age (in hours)
    /// </summary>
    public int MaxLeaveAge { get; set; } = 24*2*365;

    public Vector3 BaseLeafColor { get; set; }
    public Vector3 OldLeafColor { get; set; }
    public float pNewCrown { get; set; } = 1f;
    public float crownPitch { get; set; } = 0.5f;

    public float growthFactor { get; set; } = 0.2f;

    public float MaxRadius { get; set; } = 0.0005f;
    public float[] pChaningSeaonns { get; set; } = [ 0.045f, 0.0005f, 0.001f, 0f];
    public float[] pFloweringSeaonns { get; set; } = [ 0f, 0.0001f, 0.001f, 0f ];

    public float pExpandRizome { get; set; } = 0.005f;
    public int RizomeMaxDepth { get; set; } = 15;
    public float RizomeLength { get; set; } = 0.025f;
    public float RizomeRadius { get; set; } = 0.0025f;
    public float PetiolMoveDownMax { get; set; } = 0.3f;
    public float PetiolMoveDown { get; set; } = 0f;
    public int petiolSegments { get; set; } = 1;
    public float growthTimeVar { get;  set; }
    public float BudBloomAgeVar { get;  set; }
    public float BaseElasticModulus { get; set; } = 2e8f;
    public float PetiolElasticModulus { get;  set; } = 2e8f;
    public float FlowerElasticModulus { get;  set; } = 5e6f;
    public float FlowerPetiolElasticModulus { get;  set; } = 4e5f;

    #endregion
    public static SpeciesSettings Default => Predefined[0];
    public static List<SpeciesSettings> Predefined = [];

    static SpeciesSettings()
    {
        Predefined.Add(new());

        Predefined.Add(Geranium_Macrorrhizum.Init());
        Predefined.Add(Geranium_x_Cantabrigiense.Init());
        Predefined.Add(Bergenia_Cordifolia.Init());
        Predefined.Add(Heuchera_Micrantha.Init());
        Predefined.Add(Campanula_Poscharskyana.Init());
        Predefined.Add(Carex_Morrowii_Variegata.Init());
        Predefined.Add(Fragaria_Vesca_Ruegen.Init());
    }

    private bool Initialized = false;

    public void Init(int hoursPerTick)
    {
        Console.WriteLine($"SpeciesSettings.Init called on {Name}, BudLength: {FlowerSettings.BudLength}");

        if (!Initialized)
        {
            // AuxinsDegradationPerTick = AuxinsReach * hoursPerTick;
            // CytokininsDegradationPerTick = CytokininsReach * hoursPerTick;
            TwigsBendingApical = TwigsBendingApical * TwigsBendingByLevel;
            ShootsGravitaxis *= 0.4f;
            Initialized = true;

            PetioleCoverThreshold = MathF.Cos(MathF.PI * 0.5f - LateralPitch) * PetioleLength * 0.25f;

            //BUG with petiole -> stem and not meristem
            //Remove length factor at apex distribution for the current segment
            //Bending suddenly does not work
            //BUG water distribution
        }
    }
}
