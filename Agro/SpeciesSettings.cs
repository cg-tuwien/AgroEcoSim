using System.Numerics;
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
    /// Mean height of the plant (in meters). This is a descriptive growth hint,
    /// not a hard geometric constraint.
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

    ///<summary>
    /// Dominance factor reduces (or boosts) growth of lateral branches. Multiplies with each recursion level.
    ///</summary>
    [SpeciesUILabel("Dominance factor")]
    public float DominanceFactor { get; init; } = 0.7f;

    private float[] DominanceFactors = null;
    const int DominanceFactorsCount = 16;
    internal float GetDominanceFactor(int index)
    {
        if (index < 2)
            return 1;

        if (DominanceFactors == null)
            InitDominanceFactors();

        return index < DominanceFactorsCount ? DominanceFactors[index] : DominanceFactors[DominanceFactorsCount];
    }

    private void InitDominanceFactors()
    {
        DominanceFactors = new float[DominanceFactorsCount + 1];
        DominanceFactors[0] = 1;
        DominanceFactors[1] = 1;
        DominanceFactors[2] = DominanceFactor;
        for (int i = 3; i < DominanceFactorsCount; ++i)
            DominanceFactors[i] = MathF.Pow(DominanceFactor, i);
    }

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
    public float SeasonalSenescence { get; init; } = 7.8f / 11;

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
    public LeafPhenology LeafPhenology { get; internal set; } = new();

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

    public LeafMorphology LeafMorphology { get; internal set; } = new()
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
    /// Minimum plant age at which the flowering phase can begin, in hours.
    /// </summary>
    /// <value>Must be finite and greater than or equal to zero.</value>
    [SpeciesUIGroup("Flowering")]
    [SpeciesUILabel("Minimum flowering age")]
    public float FloweringStartAgeHours { get; init; } = 24f * 45f;

    /// <summary>
    /// Start of the flowering window in the March-to-February simulation year.
    /// </summary>
    /// <value>
    /// A normalized value from 0 to 1, where 0 is March 1. It must not be
    /// greater than FloweringSeasonEnd; windows that cross March are unsupported.
    /// </value>
    [SpeciesUILabel("Flowering season start")]
    public float FloweringSeasonStart { get; init; } = 2f / 11f;

    /// <summary>
    /// End of the flowering window in the March-to-February simulation year.
    /// </summary>
    /// <value>
    /// A normalized value from 0 to 1, where 0 is March 1. Use a value greater
    /// than FloweringSeasonStart for a reliable non-empty window; an equal
    /// boundary can be skipped by a discrete timestep.
    /// </value>
    [SpeciesUILabel("Flowering season end")]
    public float FloweringSeasonEnd { get; init; } = 5f / 11f;

    /// <summary>
    /// Maximum time spent in the flowering phase after that phase starts.
    /// </summary>
    /// <value>Hours; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flowering phase duration")]
    public float FloweringPhaseDurationHours { get; init; } = 24f * 30f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// before the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero disables pre-flowering commitment.</value>
    [SpeciesUILabel("Pre-flowering commitment probability per hour")]
    public float PreFloweringCommitmentProbabilityPerHour { get; init; } = 0.0005f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// during the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. A positive value is required for seasonal flowering.</value>
    [SpeciesUILabel("Flowering commitment probability per hour")]
    public float FloweringCommitmentProbabilityPerHour { get; init; } = 0.045f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// after the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero disables post-flowering commitment.</value>
    [SpeciesUILabel("Post-flowering commitment probability per hour")]
    public float PostFloweringCommitmentProbabilityPerHour { get; init; } = 0.001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// while the seasonal state is dormant or waiting to reset.
    /// </summary>
    /// <value>From 0 to 1. Keep at zero for seasonally dormant species.</value>
    [SpeciesUILabel("Dormant flowering probability per hour")]
    public float DormantFloweringCommitmentProbabilityPerHour { get; init; } = 0f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment before the flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero prevents pre-flowering crown continuation.</value>
    [SpeciesUILabel("Pre-flowering crown continuation per hour")]
    public float PreFloweringCrownContinuationProbabilityPerHour { get; init; } = 0f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment during the flowering window.
    /// </summary>
    /// <value>From 0 to 1. The flower-commitment event is evaluated first.</value>
    [SpeciesUILabel("Flowering crown continuation per hour")]
    public float FloweringCrownContinuationProbabilityPerHour { get; init; } = 0.0001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment after the flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero prevents post-flowering crown continuation.</value>
    [SpeciesUILabel("Post-flowering crown continuation per hour")]
    public float PostFloweringCrownContinuationProbabilityPerHour { get; init; } = 0.001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment during dormancy.
    /// </summary>
    /// <value>From 0 to 1. Keep at zero to stop shoot growth during dormancy.</value>
    [SpeciesUILabel("Dormant crown continuation per hour")]
    public float DormantCrownContinuationProbabilityPerHour { get; init; } = 0f;

    /// <summary>
    /// Whether flower commitment immediately creates a new vegetative crown
    /// meristem alongside the inflorescence.
    /// </summary>
    /// <value>
    /// Use false for determinate rosette shoots; use true for an indeterminate
    /// crown that continues vegetative growth after initiating a flower.
    /// </value>
    [SpeciesUILabel("Continue crown after flower commitment")]
    public bool ContinueCrownAfterFlowerCommitment { get; init; } = true;

    /// <summary>
    /// Variation in the time required to grow an inflorescence stem segment.
    /// </summary>
    /// <value>
    /// Hours; must be non-negative and smaller than FlowerSettings.FlowerStemSegmentGrowthDurationHours
    /// when positive, so a sampled growth time cannot reach zero.
    /// </value>
    [SpeciesUILabel("Flower stem segment growth duration variance")]
    public float FlowerStemSegmentGrowthDurationVarianceHours { get; init; }

    /// <summary>
    /// Variation in the time from a closed flower bud to bloom.
    /// </summary>
    /// <value>
    /// Hours; must be non-negative and smaller than FlowerSettings.BudBloomDurationHours
    /// when positive, so a sampled bloom time cannot reach zero.
    /// </value>
    [SpeciesUILabel("Flower bud bloom duration variance")]
    public float BudBloomDurationVarianceHours { get; init; }

    /// <summary>
    /// Detailed dimensions, topology, timing, and appearance of flowers.
    /// </summary>
    public FlowerSettings FlowerSettings { get; internal set; } = new();

    #region Fruit Settings
    /// <summary>
    /// Fruit production settings for species that produce edible fruits.
    /// Null for ornamental-only species.
    /// </summary>
    public FruitSettings? FruitSettings { get; internal set; } = null;
    #endregion

    /// <summary>
    /// Young's modulus of fully lignified shoot tissue used by gravity bending.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUIGroup("Mechanics")]
    [SpeciesUILabel("Wood elastic modulus")]
    public float WoodElasticModulus { get; init; } = 1e8f;

    /// <summary>
    /// Minimum Young's modulus of non-lignified shoot tissue used by gravity bending.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Green tissue elastic modulus")]
    public float GreenElasticModulus { get; init; } = 1e8f;

    /// <summary>
    /// Young's modulus assigned to non-lignified tissue at the basal end of the
    /// shoot graph before depth attenuation is applied.
    /// </summary>
    /// <value>
    /// Pascals; must be finite and greater than zero. At topological depth
    /// <c>d</c>, non-woody stiffness is
    /// <c>max(GreenElasticModulus, BasalGreenElasticModulus * exp(-d * BasalStiffnessAttenuationPerSegment))</c>.
    /// Set this no higher than <see cref="GreenElasticModulus"/> to disable the
    /// extra basal-stiffness effect.
    /// </value>
    [SpeciesUILabel("Basal green tissue elastic modulus")]
    public float BasalGreenElasticModulus { get; init; } = 2e8f;

    /// <summary>
    /// Exponential attenuation of the additional basal green-tissue stiffness
    /// for each parent-child segment away from the shoot-graph root.
    /// </summary>
    /// <value>
    /// A finite, non-negative dimensionless coefficient per graph segment. Zero
    /// preserves the basal modulus throughout the shoot. This is topological
    /// depth, not height above soil or rhizome burial depth.
    /// </value>
    [SpeciesUILabel("Basal stiffness attenuation per segment")]
    public float BasalStiffnessAttenuationPerSegment { get; init; } = 0.01f;

    /// <summary>
    /// Young's modulus used for vegetative leaf petioles.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Petiole elastic modulus")]
    public float PetioleElasticModulus { get; init; } = 2e8f;

    /// <summary>
    /// Young's modulus used for flower organs other than individual pedicels.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flower organ elastic modulus")]
    public float FlowerOrganElasticModulus { get; init; } = 5e6f;

    /// <summary>
    /// Young's modulus used for the pedicel supporting an individual flower.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flower pedicel elastic modulus")]
    public float FlowerPedicelElasticModulus { get; init; } = 4e7f;

    /// <summary>
    /// Age at which a herbaceous leaf and its petiole become eligible for renewal.
    /// </summary>
    /// <value>Hours; must be finite and greater than zero.</value>
    [SpeciesUIGroup("Herbaceous shoots")]
    [SpeciesUILabel("Maximum leaf age")]
    public float LeafMaximumAgeHours { get; init; } = 24f * 2f * 365f;

    /// <summary>
    /// RGB color of a newly created herbaceous leaf in the legacy color path.
    /// </summary>
    /// <value>Each component must be finite and between 0 and 1.</value>
    public Vector3 YoungLeafColor { get; init; } = new(0.18f, 0.42f, 0.12f);

    /// <summary>
    /// RGB color approached as a herbaceous leaf reaches its maximum age.
    /// </summary>
    /// <value>Each component must be finite and between 0 and 1.</value>
    public Vector3 SenescentLeafColor { get; init; } = new(0.45f, 0.30f, 0.08f);

    public SeasonalPhase CrownBudActivationPeriod { get; init; } = SeasonalPhase.PreFlower;

    /// <summary>
    /// Probability that a dormant rhizome bud activates into an aerial crown
    /// during its single pre-flowering attempt in a seasonal cycle.
    /// </summary>
    /// <value>
    /// From 0 to 1. Zero keeps crown buds dormant; one activates every eligible
    /// bud. A failed attempt is rearmed only after the seasonal reset phase.
    /// </value>
    [SpeciesUILabel("Crown bud activation probability per season")]
    public float CrownBudActivationProbabilityPerSeason { get; init; } = 1f;

    /// <summary>
    /// Vertical clearance between the soil surface and the first compact crown node.
    /// </summary>
    /// <value>Meters; must be finite and greater than or equal to zero.</value>
    [SpeciesUILabel("First crown node height above soil")]
    public float CrownNodeHeightAboveSoil { get; init; }

    /// <summary>
    /// Pitch of a new aerial crown relative to its horizontal rhizome axis.
    /// </summary>
    /// <value>
    /// Radians from 0 to pi. Zero follows the rhizome; pi/2 creates an upright crown.
    /// </value>
    [SpeciesUILabel("Crown pitch")]
    public float CrownPitch { get; init; } = 0.5f * MathF.PI;

    /// <summary>
    /// Dimensionless multiplier for herbaceous shoot elongation and radial growth.
    /// </summary>
    /// <value>
    /// Must be finite and non-negative. Zero stops axial shoot growth but does not
    /// stop separately timed leaf, petiole, flower, or rhizome-tip elongation.
    /// </value>
    [SpeciesUILabel("Herbaceous growth rate scale")]
    public float HerbaceousGrowthRateScale { get; init; } = 0.2f;

    /// <summary>
    /// Dimensionless multiplier for herbaceous rhizome elongation and radial growth.
    /// </summary>
    /// <value>
    /// Must be finite and non-negative. Zero stops axial shoot growth but does not
    /// stop separately timed leaf, petiole, flower, or rhizome-tip elongation.
    /// </value>
    [SpeciesUILabel("Rhizome growth rate scale")]
    public float RhizomeGrowthRateScale { get; init; } = 1f;

    /// <summary>
    /// Maximum radius of non-rhizome herbaceous shoot segments.
    /// </summary>
    /// <value>
    /// Meters; must be finite and at least the simulator's initial shoot radius.
    /// </value>
    [SpeciesUILabel("Maximum shoot radius")]
    public float ShootMaximumRadius { get; init; } = 0.0005f;

    /// <summary>
    /// Maximum reorientation of existing petioles when a crown adds another segment.
    /// </summary>
    /// <value>Radians from 0 to pi and not less than PetioleReorientationStepAngle.</value>
    [SpeciesUILabel("Maximum petiole reorientation")]
    public float PetioleReorientationMaximumAngle { get; init; } = 0.3f * MathF.PI;

    /// <summary>
    /// Petiole angle added when a crown continues with another segment.
    /// </summary>
    /// <value>
    /// Radians from 0 to PetioleReorientationMaximumAngle. Zero disables this
    /// architectural reorientation; elastic gravity bending remains active.
    /// </value>
    [SpeciesUILabel("Petiole reorientation step")]
    public float PetioleReorientationStepAngle { get; init; }

    /// <summary>
    /// Number of serial segments used to model a vegetative leaf petiole.
    /// </summary>
    /// <value>An integer of at least one. One creates a single straight petiole.</value>
    [SpeciesUILabel("Petiole segment count")]
    public int PetioleSegmentCount { get; init; } = 1;

    /// <summary>
    /// Hourly probability that a completed rhizome segment creates each configured
    /// continuation offshoot.
    /// </summary>
    /// <value>
    /// From 0 to 1. Zero stops extension after the initial axes; values are
    /// accumulated over HoursPerTick for timestep-stable event timing.
    /// </value>
    [SpeciesUIGroup("Rhizomes")]
    [SpeciesUILabel("Rhizome offshoot probability per hour")]
    public float RhizomeOffshootProbabilityPerHour { get; init; } = 0.005f;

    /// <summary>
    /// Hourly probability that a completed non-initial rhizome segment creates one
    /// additional side branch.
    /// </summary>
    /// <value>From 0 to 1. Zero keeps the configured continuation axes unbranched.</value>
    [SpeciesUILabel("Secondary rhizome lateral probability per hour")]
    public float RhizomeSecondaryLateralProbabilityPerHour { get; init; }

    /// <summary>
    /// Number of continuation offshoots available to each completed rhizome segment.
    /// </summary>
    /// <value>From 0 to 32. One continues a single axis; values above one branch.</value>
    [SpeciesUILabel("Rhizome offshoots per segment")]
    public int RhizomeOffshootsPerSegment { get; init; } = 2;

    /// <summary>
    /// Number of rhizome axes established together at the seed crown.
    /// </summary>
    /// <value>From 0 to 32. Values above one distribute axes radially.</value>
    [SpeciesUILabel("Initial rhizome axes")]
    public int InitialRhizomeAxes { get; init; } = 3;

    /// <summary>
    /// Maximum graph depth of rhizome segments measured from the seed segment.
    /// </summary>
    /// <value>
    /// A non-negative segment count. This is topological depth, not burial depth;
    /// zero prevents creation of new rhizome segments.
    /// </value>
    [SpeciesUILabel("Maximum rhizome segment depth")]
    public int RhizomeMaximumSegmentDepth { get; init; } = 15;

    /// <summary>
    /// Target length of each rhizome segment.
    /// </summary>
    /// <value>
    /// Meters; must be finite and greater than zero. A completed segment can
    /// exceed this target by at most one discrete growth increment.
    /// </value>
    [SpeciesUILabel("Rhizome segment length")]
    public float RhizomeSegmentLength { get; init; } = 0.025f;

    /// <summary>
    /// Target radius of each rhizome segment.
    /// </summary>
    /// <value>
    /// Meters; must be finite and at least the simulator's initial shoot radius.
    /// </value>
    [SpeciesUILabel("Rhizome segment radius")]
    public float RhizomeSegmentRadius { get; init; } = 0.0025f;

    /// <summary>
    /// Retains a perennial rhizome in the plant graph when its modeled local energy
    /// reserve is temporarily depleted.
    /// </summary>
    /// <value>
    /// Enable for dormant clonal perennials that can be replenished by connected
    /// organs; disable when reserve depletion should kill that rhizome segment.
    /// </value>
    [SpeciesUILabel("Retain dormant rhizomes")]
    public bool RetainDormantRhizomes { get; init; }
    public static SpeciesSettings Default => Predefined[0];
    public static List<SpeciesSettings> Predefined = [];

    static SpeciesSettings()
    {
        Predefined.Add(new());

        Predefined.Add(Geranium_Macrorrhizum.Init());
        Predefined.Add(Geranium_x_Cantabrigiense.Init());
        Predefined.Add(Bergenia_Cordifolia.Init());
        Predefined.Add(Heuchera_Micrantha_Palace_Purple.Init());
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
            Validate();

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

    private void Validate()
    {
        static void RequireFiniteNonNegative(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name, value, "Value must be finite and non-negative.");
        }

        static void RequireFinitePositive(float value, string name)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name, value, "Value must be finite and greater than zero.");
        }

        static void RequireProbability(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(name, value, "Probability must be between zero and one.");
        }

        static void RequireColor(Vector3 value, string name)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
                value.X < 0f || value.X > 1f || value.Y < 0f || value.Y > 1f || value.Z < 0f || value.Z > 1f)
            {
                throw new ArgumentOutOfRangeException(name, value, "RGB components must be finite and between zero and one.");
            }
        }

        RequireFiniteNonNegative(FloweringStartAgeHours, nameof(FloweringStartAgeHours));
        RequireProbability(FloweringSeasonStart, nameof(FloweringSeasonStart));
        RequireProbability(FloweringSeasonEnd, nameof(FloweringSeasonEnd));
        if (FloweringSeasonStart > FloweringSeasonEnd)
            throw new ArgumentException("The flowering window cannot cross the March boundary.", nameof(FloweringSeasonStart));
        RequireFinitePositive(FloweringPhaseDurationHours, nameof(FloweringPhaseDurationHours));

        RequireProbability(PreFloweringCommitmentProbabilityPerHour, nameof(PreFloweringCommitmentProbabilityPerHour));
        RequireProbability(FloweringCommitmentProbabilityPerHour, nameof(FloweringCommitmentProbabilityPerHour));
        RequireProbability(PostFloweringCommitmentProbabilityPerHour, nameof(PostFloweringCommitmentProbabilityPerHour));
        RequireProbability(DormantFloweringCommitmentProbabilityPerHour, nameof(DormantFloweringCommitmentProbabilityPerHour));
        RequireProbability(PreFloweringCrownContinuationProbabilityPerHour, nameof(PreFloweringCrownContinuationProbabilityPerHour));
        RequireProbability(FloweringCrownContinuationProbabilityPerHour, nameof(FloweringCrownContinuationProbabilityPerHour));
        RequireProbability(PostFloweringCrownContinuationProbabilityPerHour, nameof(PostFloweringCrownContinuationProbabilityPerHour));
        RequireProbability(DormantCrownContinuationProbabilityPerHour, nameof(DormantCrownContinuationProbabilityPerHour));

        FlowerSettings.Validate();
        RequireFiniteNonNegative(FlowerStemSegmentGrowthDurationVarianceHours, nameof(FlowerStemSegmentGrowthDurationVarianceHours));
        if (FlowerStemSegmentGrowthDurationVarianceHours >= FlowerSettings.FlowerStemSegmentGrowthDurationHours)
            throw new ArgumentOutOfRangeException(nameof(FlowerStemSegmentGrowthDurationVarianceHours), FlowerStemSegmentGrowthDurationVarianceHours, "Variance must be smaller than the mean flower stem growth duration.");
        RequireFiniteNonNegative(BudBloomDurationVarianceHours, nameof(BudBloomDurationVarianceHours));
        if (BudBloomDurationVarianceHours >= FlowerSettings.BudBloomDurationHours)
            throw new ArgumentOutOfRangeException(nameof(BudBloomDurationVarianceHours), BudBloomDurationVarianceHours, "Variance must be smaller than the mean flower bud bloom duration.");

        RequireFinitePositive(WoodElasticModulus, nameof(WoodElasticModulus));
        RequireFinitePositive(GreenElasticModulus, nameof(GreenElasticModulus));
        RequireFinitePositive(BasalGreenElasticModulus, nameof(BasalGreenElasticModulus));
        RequireFiniteNonNegative(BasalStiffnessAttenuationPerSegment, nameof(BasalStiffnessAttenuationPerSegment));
        RequireFinitePositive(PetioleElasticModulus, nameof(PetioleElasticModulus));
        RequireFinitePositive(FlowerOrganElasticModulus, nameof(FlowerOrganElasticModulus));
        RequireFinitePositive(FlowerPedicelElasticModulus, nameof(FlowerPedicelElasticModulus));

        RequireFinitePositive(LeafMaximumAgeHours, nameof(LeafMaximumAgeHours));
        RequireColor(YoungLeafColor, nameof(YoungLeafColor));
        RequireColor(SenescentLeafColor, nameof(SenescentLeafColor));
        RequireProbability(CrownBudActivationProbabilityPerSeason, nameof(CrownBudActivationProbabilityPerSeason));
        RequireFiniteNonNegative(CrownNodeHeightAboveSoil, nameof(CrownNodeHeightAboveSoil));
        if (!float.IsFinite(CrownPitch) || CrownPitch < 0f || CrownPitch > MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(CrownPitch), CrownPitch, "Crown pitch must be between zero and pi radians.");
        RequireFiniteNonNegative(HerbaceousGrowthRateScale, nameof(HerbaceousGrowthRateScale));
        RequireFinitePositive(ShootMaximumRadius, nameof(ShootMaximumRadius));
        if (ShootMaximumRadius < AboveGroundAgent.InitialRadius)
            throw new ArgumentOutOfRangeException(nameof(ShootMaximumRadius), ShootMaximumRadius, "Maximum radius cannot be smaller than the initial shoot radius.");

        if (!float.IsFinite(PetioleReorientationMaximumAngle) || PetioleReorientationMaximumAngle < 0f || PetioleReorientationMaximumAngle > MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(PetioleReorientationMaximumAngle), PetioleReorientationMaximumAngle, "Maximum angle must be between zero and pi radians.");
        if (!float.IsFinite(PetioleReorientationStepAngle) || PetioleReorientationStepAngle < 0f || PetioleReorientationStepAngle > PetioleReorientationMaximumAngle)
            throw new ArgumentOutOfRangeException(nameof(PetioleReorientationStepAngle), PetioleReorientationStepAngle, "Step angle must be between zero and the maximum angle.");
        if (PetioleSegmentCount < 1)
            throw new ArgumentOutOfRangeException(nameof(PetioleSegmentCount), PetioleSegmentCount, "At least one petiole segment is required.");

        RequireProbability(RhizomeOffshootProbabilityPerHour, nameof(RhizomeOffshootProbabilityPerHour));
        RequireProbability(RhizomeSecondaryLateralProbabilityPerHour, nameof(RhizomeSecondaryLateralProbabilityPerHour));
        if (RhizomeOffshootsPerSegment is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(RhizomeOffshootsPerSegment), RhizomeOffshootsPerSegment, "The supported range is zero to 32.");
        if (InitialRhizomeAxes is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(InitialRhizomeAxes), InitialRhizomeAxes, "The supported range is zero to 32.");
        if (RhizomeMaximumSegmentDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(RhizomeMaximumSegmentDepth), RhizomeMaximumSegmentDepth, "Depth must be non-negative.");
        RequireFinitePositive(RhizomeSegmentLength, nameof(RhizomeSegmentLength));
        RequireFinitePositive(RhizomeSegmentRadius, nameof(RhizomeSegmentRadius));
        if (RhizomeSegmentRadius < AboveGroundAgent.InitialRadius)
            throw new ArgumentOutOfRangeException(nameof(RhizomeSegmentRadius), RhizomeSegmentRadius, "Target radius cannot be smaller than the initial shoot radius.");
    }
}
