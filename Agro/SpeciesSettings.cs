using System.Numerics;
using System.Text.Json.Serialization;
using Agro.Species;

namespace Agro;

public enum Behavior : byte { Default, Herbaceous}

public class SpeciesSettings
{
    const float DegToRad = MathF.PI / 180f;
    ///<summary>
    /// Species name
    ///</summary>
    public string Name { get; init; } = "Default";

    /// <summary>
    /// Colloquial name
    /// </summary>
    public string Aka { get; init; }

    /// <summary>
    /// Growth behavoir of the plant
    /// </summary>
    public Behavior Behavior { get; init; } = Behavior.Default;

    ///<summary>
    /// Standard height of the plant (in meters)
    ///</summary>
    public float Height { get; init; } = 10f;

    ///<summary>
    /// Standard length of a stem segment (in meters)
    ///</summary>
    public float NodeDistance { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the stem segment length (in meters)
    ///</summary>
    public float NodeDistanceVar { get; init; } = 0.01f;

    ///<summary>
    /// For monopodial branching select 1, for dichotomous select 0 and for anisotomous select sth. between
    ///</summary>
    public float MonopodialFactor { get; init; } = 1;

    private float dominanceFactor;
    public float[] DominanceFactors = [0.7f];

    ///<summary>
    /// Dominance factor reduces (or boosts) growth of lateral branches. Multiplies with each recursion level.
    ///</summary>
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

    /// <summary>Age (hours) when plant switches to deterministic sympodial growth.</summary>
    public float SympodialStartAgeHours = 24f * 45f;

    /// <summary>Age (hours) when flowering ends; growth stops afterwards.</summary>
    public float FloweringEndAgeHours { get; internal set; } = 24f * 90f;
    public float FloweringStartAgeHours { get; internal set; } = 24f * 45f;
    public float FlowerLengthVar { get; internal set; }
    public float FlowerRadiusVar { get; internal set; }

    public FlowerSettings FlowerSettings { get; set; } = new FlowerSettings();
    ///<summary>
    /// Number of lateral branches emerging from a node
    ///</summary>
    public int LateralsPerNode { get; init; } = 2;

    ///<summary>
    /// Angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    public float LateralRoll { get; init; } = 0f;

    ///<summary>
    /// Variance of the angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    public float LateralRollVar { get; init; } = 5f * DegToRad;

    ///<summary>
    /// Angle of the lateral to its parent, in radians
    ///</summary>
    public float LateralPitch { get; init; } = 45f * DegToRad;

    ///<summary>
    /// Variance of the angle of the lateral to its parent, in radians
    ///</summary>
    public float LateralPitchVar { get; init; } = 5f * DegToRad;


    public float TwigsBending { get; init; } = 0.5f;
    public float TwigsBendingLevel { get; init; } = 1;
    public float TwigsBendingApical { get; set; } = 0.02f;
    public float ShootsGravitaxis { get; set; } = 0.2f;

    /// <summary>
    /// Standard wood growth time (in hours)
    /// </summary>
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
    public float LeafLength { get; init; } = 0.12f;

    ///<summary>
    /// Variance of the leaf length along its main axis (in meters)
    ///</summary>
    public float LeafLengthVar { get; init; } = 0.02f;

    ///<summary>
    /// Standard leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    public float LeafRadius { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    public float LeafRadiusVar { get; init; } = 0.01f;

    ///<summary>
    /// Standard growth time of a leaf (in hours)
    ///</summary>
    public float LeafGrowthTime { get; init; } = 480f;

    ///<summary>
    /// Variance of the growth time of a leaf (in hours)
    ///</summary>
    public float LeafGrowthTimeVar { get; init; } = 120f;


    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceSeasonalDuration { get; init; } = 480f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceSeasonalDurationVar { get; init; } = 120f;

    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceStressDuration { get; init; } = 140f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceStressDurationVar { get; init; } = 60f;

    /// <summary>
    /// If positive, marks a typical senescence period (in seasonal progress ,i.e. think of roman months)
    /// </summary>
    public float SeasonalSenescence { get; init; } = 8.5f / 12;

    ///<summary>
    /// Standard leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    public float LeafPitch { get; init; } = 20f * DegToRad;

    ///<summary>
    /// Variance of the leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    public float LeafPitchVar { get; init; } = 5f * DegToRad;

    ///<summary>
    /// Standard petiole length (in meters)
    ///</summary>
    public float PetioleLength { get; init; } = 0.04f;

    ///<summary>
    /// Variance of the petiole length (in meters)
    ///</summary>
    public float PetioleLengthVar { get; init; } = 0.01f;

    ///<summary>
    /// Standard petiole radius (in meters)
    ///</summary>
    public float PetioleRadius { get; init; } = 0.0025f;

    ///<summary>
    /// Variance of the petiole radius (in meters)
    ///</summary>
    public float PetioleRadiusVar { get; init; } = 0.0005f;

    ///<summary>
    /// Density of the roots system (affects branching probabiilty). Valued 0 to 1 (with one being the most dense)
    ///</summary>
    public float RootsDensity { get; init; } = 0.5f;

    ///<summary>
    /// Correction factor to point the roots growth downwards
    ///</summary>
    public float RootsGravitaxis { get; init; } = 0.2f;

    // public float RootRadiusGrowthPerH { get; init; }
    // public float RootLengthGrowthPerH { get; init; }

    //public int FirstFruitHour { get; init; }

    public float AuxinsProduction { get; init; } = 40;
    public float CytokininsProduction { get; init; } = 40;

    public float AuxinsReach { get; init; } = 1;

    public float CytokininsReach { get; init; } = 1;

    public float AuxinsThreshold => 1f;

    public float DensityDryWood = 700_000; //in g/m³
	public float DensityDryStem = 200_000; //in g/m³

    public float PetioleCoverThreshold { get; private set; } = float.MaxValue;


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

    public static List<SpeciesSettings> Predefined = [];

    [JsonPropertyName("WEM")]
    public float WoodElasticModulus { get; init; } = 1e6f; // in Pa

    [JsonPropertyName("GEM")]
    public float GreenElasticModulus { get; init; } = 1e8f; // in Pa

    // public static SpeciesSettings Geranium_Macrorhizum;
    // public static SpeciesSettings Geranium_Cantabrigiense;
    // public static SpeciesSettings Bergenia_Cordifolia;
    [JsonPropertyName("DF")]
    public float DepthFactor { get; init; } = 0.01f; // in Pa

    public static SpeciesSettings Default => Predefined[0];
    //-- custom parmeter Geranium & Bergania
    #region GeraniumBergania
    public float MaxLeaveAge { get; set; } = 2*365;

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
    public float BaseElasticModulus { get; set; }=2e8f;
    public float PetiolElasticModulus { get;  set; } = 2e8f;
    public float FlowerElasticModulus { get;  set; } = 5e6f;
    public float FlowerPetiolElasticModulus { get;  set; } = 4e5f;

    #endregion


    static SpeciesSettings()
    {
        Predefined.Add(new());

       

        Predefined.Add(Geranium_Macrorrhizum.Init());
        Predefined.Add(Geranium_x_Cantabrigiense.Init());
        Predefined.Add(Bergenia_Cordifolia.Init());

        // Campanula;
        // Heuchera;
        // Fragaria;
        // Carex;

    }

    private bool Initialized = false;

    public void Init(int hoursPerTick)
    {
        Console.WriteLine($"SpeciesSettings.Init called on {Name}, BudLength: {FlowerSettings.BudLength}");

        if (!Initialized)
        {
            // AuxinsDegradationPerTick = AuxinsReach * hoursPerTick;
            // CytokininsDegradationPerTick = CytokininsReach * hoursPerTick;
            TwigsBendingApical = TwigsBendingApical * TwigsBendingLevel;
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