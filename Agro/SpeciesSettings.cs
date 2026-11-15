using System.Numerics;
using System.Text.Json.Serialization;
using Agro.Species;

namespace Agro;

public enum Behavior : byte { Default, Herbaceous, Woody }
public enum TreeCrownHabit : byte { Broad, Columnar, Globe }
public enum TreeMainlineModuleMode : byte { Off = 0, RealAxillary = 1, Auto = 2 }

public class SpeciesSettings
{
    // Request properties use setters deliberately: the .NET 8/10 JSON source
    // generator assigns default(T) to omitted init-only members, discarding
    // their initializers. Older run requests must retain these defaults while
    // explicit numeric values (including zero) remain untouched and validated.
    /// <summary>Reversible mechanical shape controls; independent of biological organ counts. BroadLeafV1 is experimental until its acceptance gates pass.</summary>
    [SpeciesUIGroup("Mechanics")]
    [SpeciesUILabel("Contact deformation")]
    [SpeciesUITooltip("0: rigid geometry; 1: experimental broad elastic leaf and petiole bending. Does not change biological organ counts or growth durations.")]
    public ContactDeformationMode ContactDeformationMode { get; set; } = ContactDeformationMode.None;

    /// <summary>Enable blade shape controls in V1. False retains petiole bending with rigid blades.</summary>
    [SpeciesUILabel("V1 blade folding")]
    public bool ContactLeafFoldingEnabled { get; set; } = true;

    /// <summary>Whole petiole contact-bend limit in degrees, including base and internal joints.
    /// Zero inherits the global limit. A numerical safeguard, not a measured tissue yield angle.</summary>
    [SpeciesUILabel("Petiole contact bend limit")]
    public float PetioleContactBendDegrees { get; set; }

    const float DegToRad = MathF.PI / 180f;
    ///<summary>
    /// Species name
    ///</summary>
    [SpeciesUIGroup("Name")]
    [SpeciesUITooltip("Official latin name")]
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Colloquial name
    /// </summary>
    [SpeciesUITooltip("Colloquial name")]
    public string Aka { get; set; }

    /// <summary>
    /// Growth behavoir of the plant
    /// </summary>
    public Behavior Behavior { get; set; } = Behavior.Default;

    [SpeciesUIGroup("Trees")]
    [SpeciesUITooltip("0: broad crown; 1: narrow ascending crown; 2: rounded grafted crown.")]
    public TreeCrownHabit TreeCrownHabit { get; set; } = TreeCrownHabit.Broad;
    /// <summary>Typical mature crown radius in metres; a developmental envelope, not pruning.</summary>
    public float TreeCrownRadius { get; set; } = 5f;
    /// <summary>Leader extension in a favourable growing season, metres. Provisional calibration.</summary>
    public float TreeAnnualExtension { get; set; } = .65f;
    /// <summary>Maximum annual extension of an active explicit woody root tip in metres, before soil temperature, hydration and resource limitations. This provisional model rate is calibrated against nursery development; it is not a root depth limit.</summary>
    [SpeciesUILabel("Annual root extension (m)")]
    [SpeciesUITooltip("Maximum active explicit root-tip extension per nominal 365-day year, before temperature, hydration and resource limitations. Provisional nursery-calibrated rate; does not cap root depth or length.")]
    public float TreeRootAnnualExtension { get; set; } = .65f;
    /// <summary>Maximum annual radial increment, metres, reduced by age, resources and branch order.</summary>
    public float TreeAnnualRadiusGrowth { get; set; } = .004f;
    public float TreeMatureAgeYears { get; set; } = 40f;
    public float TreeFloweringAgeYears { get; set; } = 5f;
    /// <summary>Use a distal axillary relay after a completed growth unit, rather than a perpetually renewing terminal apex.</summary>
    [SpeciesUILabel("Pseudo-terminal shoot renewal")]
    [SpeciesUITooltip("Close each growth unit and renew from a distal axillary bud. The managed primary axis keeps its upright training; lateral units retain their anatomical frame. Disabled preserves terminal renewal.")]
    public bool TreeSympodialRenewal { get; set; }
    [SpeciesUILabel("Dorsiventral foliage sprays")]
    [SpeciesUITooltip("Keep two alternating leaf ranks and lateral sites in a carried branch plane. Each new lateral axis starts with its dorsal plane as level as its heading allows, then carries that frame as it develops.")]
    public bool TreeDorsiventralShoots { get; set; }
    /// <summary>Select the shoot architecture once, before establishment. Missing values preserve existing growth.</summary>
    [SpeciesUILabel("Mainline model")]
    [SpeciesUITooltip("Automatic uses real axillary growth for compatible single-stem, ungrafted forms and existing growth for other forms. Real axillary requires a compatible form.")]
    public TreeMainlineModuleMode TreeMainlineModuleMode { get; set; } = TreeMainlineModuleMode.Off;
    /// <summary>Explicit trial nursery-training endpoint above ground; not a natural age or mature dimension.</summary>
    [SpeciesUILabel("Explicit training height (m)")]
    [SpeciesUITooltip("Real axillary only. Automatic uses clear-trunk height; existing growth ignores this control.")]
    public float TreeMainlineTrainingHeightM { get; set; }
    /// <summary>Provisional gravity-relative reference inclination of each new mainline module.</summary>
    public float TreeMainlineReferenceInclinationDegrees { get; set; } = 35f;
    /// <summary>Provisional fraction of a finite unit devoted to appressed-bud opening.</summary>
    public float TreeMainlineOpeningFraction { get; set; } = .25f;
    internal bool TreeMainlineModulesEnabled { get; private set; }
    internal float EffectiveTreeMainlineTrainingHeightM { get; private set; }
    internal float TreeMainlineReferenceInclinationRadians { get; private set; }
    /// <summary>Provisional instantaneous sinusoidal maturation-strain amplitude. Zero is the exact disabled path; not a measured Celtis coefficient.</summary>
    public float TreeSecondaryMaturationStrainAmplitude { get; set; }
    /// <summary>Trial proximal material fraction frozen after a real successor commits; not a botanical onset measurement.</summary>
    public float TreeSecondaryProximalFraction { get; set; } = .25f;
    /// <summary>Provisional length scale in the local angle/curvature feedback. Used only when strain is positive.</summary>
    public float TreeSecondaryProprioceptiveLengthM { get; set; } = 1f;
    internal bool TreeSecondaryErectionEnabled { get; private set; }
    [SpeciesUILabel("Lateral growth-unit arch (degrees)")]
    [SpeciesUITooltip("Provisional intrinsic tangent change across one lateral growth unit, from accepted extension. Zero disables it. Does not move established wood or set a mature crown boundary.")]
    public float TreeLateralUnitArchDegrees { get; set; }
    [SpeciesUILabel("Light response per growth unit (degrees)")]
    [SpeciesUITooltip("Maximum accumulated light-driven turn across one growth unit, including inclination and azimuth. The response follows accepted new length and is carried into the next internode. Applies when the growth-unit model is enabled.")]
    public float TreeUnitLightDeviationDegrees { get; set; } = 10f;
    [SpeciesUILabel("Axillary bud activation probability")]
    [SpeciesUITooltip("One annual activation decision before an axillary bud expands, flowers or produces leaves. Existing wood and fruit are retained. One preserves the original path; terminal or relay axes always flush. Provisional site-resolution coefficient.")]
    public float TreeAxillaryFlushProbability { get; set; } = 1f;
    /// <summary>Preferred mature blade angle below horizontal. Zero preserves the original leaf-pitch path.</summary>
    [SpeciesUILabel("Mature blade declination (degrees)")]
    [SpeciesUITooltip("Preferred downward blade angle after expansion. Zero keeps the original leaf pitch. The blade approaches this pose as it grows; petiole phyllotaxy, contacts and elastic bending still apply.")]
    public float TreeLeafDeclinationDegrees { get; set; }
    [SpeciesUILabel("Blade declination variation (degrees)")]
    [SpeciesUITooltip("Symmetric variation sampled once per blade. Provisional morphology coefficient; it does not change the blade dimensions.")]
    public float TreeLeafDeclinationVariationDegrees { get; set; }
    /// <summary>Establishment age when an ungrafted broad crown starts releasing apical control. Provisional, independent of reproductive maturity.</summary>
    [SpeciesUILabel("Crown maturation start (years)")]
    [SpeciesUITooltip("Start age for gradual release of the leader's control of branch extension and angle. Ungrafted broad crowns only. Provisional coefficient, independent of flowering age.")]
    public float TreeApicalControlStartYears { get; set; }
    /// <summary>Establishment age when the crown maturation response is complete. Zero disables it and preserves the original hierarchy.</summary>
    [SpeciesUILabel("Crown maturation end (years)")]
    [SpeciesUITooltip("Age when the crown maturation response is complete. Zero disables it. This controls branching, rather than setting a mature size.")]
    public float TreeApicalControlEndYears { get; set; }
    [SpeciesUILabel("Mature leader extension fraction")]
    [SpeciesUITooltip("Fraction of the leader's extension allowance after crown maturation, before the usual light, water, temperature, support and contact constraints.")]
    public float TreeMatureLeaderExtensionFraction { get; set; } = .2f;
    [SpeciesUILabel("Mature branch extension fraction")]
    [SpeciesUITooltip("Extension allowance for higher-order axes at full crown maturation, relative to the species' annual extension. Provisional coefficient.")]
    public float TreeMatureBranchExtensionFraction { get; set; } = 1f;
    [SpeciesUILabel("Mature framework angle fraction")]
    [SpeciesUITooltip("Fraction of the original lateral pitch for newly born branches on the primary axes. Existing wood keeps its orientation. Provisional coefficient.")]
    public float TreeMatureFrameworkPitchFraction { get; set; } = .5f;
    /// <summary>Rootstock height in metres. Zero means an ungrafted tree; the scion is mature material.</summary>
    [SpeciesUILabel("Graft height (m)")]
    [SpeciesUITooltip("Rootstock height; zero selects an ungrafted tree. The scion uses mature cultivar material.")]
    public float TreeGraftHeight { get; set; }
    /// <summary>Managed clear trunk height in metres. Basal stems retain their leaves while establishing.</summary>
    [SpeciesUILabel("Clear trunk height (m)")]
    [SpeciesUITooltip("Managed height without permanent low branches; establishment leaves remain until the crown develops.")]
    public float TreeClearTrunkHeight { get; set; }
    [SpeciesUILabel("Basal stem count")]
    public int TreeStemCount { get; set; } = 1;
    public int TreeMaximumBranchOrder { get; set; } = 4;
    public float TreeBranchProbability { get; set; } = .16f;
    /// <summary>Annual probability that an exposed perennial short shoot forms an axillary short-shoot daughter. Zero preserves isolated spurs. Provisional model coefficient.</summary>
    [SpeciesUILabel("Short-shoot branching probability")]
    [SpeciesUITooltip("Annual trial for an exposed short shoot to form one axillary short-shoot daughter; reduced by measured light. Provisional coefficient, not a measured cultivar rate.")]
    public float TreeShortShootBranchProbability { get; set; }
    /// <summary>Active-season days of sustained asymmetric shade before a spur retires at its next spring renewal. Zero disables this provisional response.</summary>
    [SpeciesUILabel("Short-shoot shade budget (days)")]
    [SpeciesUITooltip("Cumulative active-season shade budget before retirement at the next spring renewal; bright periods restore it. Winter preserves the budget. Zero disables the response. Provisional coefficient.")]
    public float TreeShortShootShadeDiebackDays { get; set; }
    /// <summary>Absolute low-light threshold as a fraction of the vegetative daily light reference. The relative crown threshold must also be met.</summary>
    [SpeciesUILabel("Short-shoot low-light fraction")]
    public float TreeShortShootShadeLightFraction { get; set; } = .05f;
    /// <summary>Relative low-light threshold as a fraction of this crown's daily leaf mean; uniform whole-tree shade alone does not retire a spur.</summary>
    [SpeciesUILabel("Short-shoot relative shade fraction")]
    public float TreeShortShootShadeRelativeFraction { get; set; } = .15f;
    [SpeciesUILabel("Shoot phototropic weight")]
    [SpeciesUITooltip("Blend of measured light inclination (1) and the shoot's inherited architectural inclination (0). Provisional model coefficient.")]
    public float TreePhototropicWeight { get; set; } = .25f;
    [SpeciesUILabel("Short-shoot node count")]
    [SpeciesUITooltip("Compressed nodes renewed each spring; each carries the configured alternate leaf or opposite pair.")]
    public int TreeShortShootNodes { get; set; } = 2;
    [SpeciesUILabel("Short-shoot length (m)")]
    public float TreeShortShootLength { get; set; } = .025f;
    /// <summary>Season fractions measured from 1 March, as AgroWorld.Season.</summary>
    public float TreeBudBreakSeason { get; set; } = .08f;
    public float TreeGrowthEndSeason { get; set; } = .46f;
    public int TreeFlowersPerCluster { get; set; } = 6;
    /// <summary>Optional sterile male clusters alongside solitary fruit-bearing flowers; zero disables them.</summary>
    public int TreeMaleFlowersPerCluster { get; set; }

    // Branch orders are bounded to six. Evaluate the original power expressions once,
    // preserving their exact float results without a power call for every stem/tick.
    static readonly float[] TreeRadiusOrderFactors = BuildTreeOrderFactors(.65f);
    float[]? TreeExtensionOrderFactors;
    bool TreeApicalControlEnabled;
    float TreeLeaderExtensionReduction, TreeFrameworkPitchReduction;
    internal float TreeLeafInclinationRadians { get; private set; }
    internal float TreeLeafInclinationVariationRadians { get; private set; }
    internal bool TreeGrowthUnitsEnabled { get; private set; }
    internal float TreeLateralUnitArchRadians { get; private set; }
    internal float TreeUnitLightDeviationRadians { get; private set; }
    static float[] BuildTreeOrderFactors(float basis)
    {
        var factors = new float[6];
        for (var i = 0; i < factors.Length; ++i) factors[i] = MathF.Pow(basis, i);
        return factors;
    }
    internal static float TreeRadiusByOrder(byte order)
        => TreeRadiusOrderFactors[Math.Clamp(order - 1, 0, 5)];
    internal float TreeExtensionByOrder(byte order)
        => (TreeExtensionOrderFactors ??= BuildTreeOrderFactors(DominanceFactor))[Math.Clamp(order - 1, 0, 5)];

    internal float TreeApicalControlRelease(float ageYears)
    {
        if (!TreeApicalControlEnabled) return 0f;
        var phase = Math.Clamp((ageYears - TreeApicalControlStartYears)
            / (TreeApicalControlEndYears - TreeApicalControlStartYears), 0f, 1f);
        return phase * phase * (3f - 2f * phase);
    }

    internal float TreeExtensionByOrder(byte order, float release)
    {
        // Exact original arithmetic for disabled profiles and juvenile trees.
        var original = TreeExtensionByOrder(order);
        if (release <= 0f) return original;
        return order == 1 ? 1f - TreeLeaderExtensionReduction * release
            : original * (1f - release) + TreeMatureBranchExtensionFraction * release;
    }

    internal float TreeFrameworkPitch(float release)
        => release <= 0f ? LateralPitch : LateralPitch * (1f - TreeFrameworkPitchReduction * release);

    /// <summary>Hours of establishment before a compressed rosette starts a leafy bolting axis. Zero disables the transition.</summary>
    [SpeciesUIGroup("Herbaceous architecture")]
    [SpeciesUILabel("Bolting age (hours)")]
    public float BoltingAgeHours { get; set; }

    /// <summary>Internode length during the pre-bolting rosette, in metres.</summary>
    [SpeciesUILabel("Rosette node distance (m)")]
    public float RosetteNodeDistance { get; set; } = 0.002f;

    /// <summary>Maximum leafy nodes before bolting; the apex waits without being removed.</summary>
    [SpeciesUILabel("Rosette node limit")]
    public int RosetteMaximumNodes { get; set; } = 8;

    /// <summary>Probability of one axillary leafy branch at each new node, not per tick.</summary>
    [SpeciesUILabel("Branch probability per node")]
    public float HerbaceousBranchProbability { get; set; }

    /// <summary>Maximum leafy shoot order, including the main shoot as order one.</summary>
    [SpeciesUILabel("Maximum shoot order")]
    public int HerbaceousBranchMaximumOrder { get; set; } = 2;

    /// <summary>Independent flower stalks leave the basal rosette apex vegetative.</summary>
    [SpeciesUILabel("Independent rosette scapes")]
    public bool RosetteScapes { get; set; }

    /// <summary>Die after the first completed reproductive season, rather than rearming next spring.</summary>
    [SpeciesUILabel("Single reproductive season")]
    public bool Monocarpic { get; set; }

    /// <summary>Explicit roots retain a dominant storage axis with finer lateral roots. Virtual roots remain an aggregate proxy.</summary>
    [SpeciesUILabel("Dominant taproot")]
    public bool Taproot { get; set; }

    ///<summary>
    /// Mean height of the plant (in meters). This is a descriptive growth hint,
    /// not a hard geometric constraint.
    ///</summary>
    [SpeciesUIGroup("Name")]
    [SpeciesUILabel("Mean height")]
    public float Height { get; set; } = 10f;

    ///<summary>
    /// Standard length of a stem segment (in meters)
    ///</summary>
    [SpeciesUIGroup("Name")]
    [SpeciesUILabel("Node distance")]
    [SpeciesUITooltip("Stem segments length (in meters)")]
    public float NodeDistance { get; set; } = 0.04f;

    ///<summary>
    /// Variance of the stem segment length (in meters)
    ///</summary>
    public float NodeDistanceVar { get; set; } = 0.01f;

    [SpeciesUIGroup("Branching")]
    ///<summary>
    /// For monopodial branching select 1, for dichotomous select 0 and for anisotomous select sth. between
    ///</summary>
    [SpeciesUILabel("Monopodial factor")]
    public float MonopodialFactor { get; set; } = 1;

    ///<summary>
    /// Dominance factor reduces (or boosts) growth of lateral branches. Multiplies with each recursion level.
    ///</summary>
    [SpeciesUILabel("Dominance factor")]
    public float DominanceFactor { get; set; } = 0.7f;

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
        //The last entry is the saturation value returned for every deeper level,
        //so it must be filled as well; leaving it at zero froze all growth there.
        for (int i = 3; i <= DominanceFactorsCount; ++i)
            DominanceFactors[i] = MathF.Pow(DominanceFactor, i);
    }

    /// <summary>
    /// Converts a probability that applies to a whole seasonal window into the
    /// per-timestep hazard that reproduces it, so the outcome does not depend on
    /// HoursPerTick.
    /// </summary>
    internal static float SeasonalProbabilityPerTick(float probabilityPerSeason, float windowHours, float hoursPerTick)
    {
        if (probabilityPerSeason <= 0f)
            return 0f;
        if (probabilityPerSeason >= 1f || !(windowHours > 0f))
            return 1f;

        return 1f - MathF.Pow(1f - probabilityPerSeason, hoursPerTick / windowHours);
    }

    ///<summary>
    /// Number of lateral branches emerging from a node
    ///</summary>
    [SpeciesUILabel("Branching amount")]
    public int LateralsPerNode { get; set; } = 2;

    ///<summary>
    /// Angle of the lateral to its parent, in radians
    ///</summary>
    [SpeciesUILabel("Lateral pitch")]
    [SpeciesUITooltip("Angle of the growth direction to the growth direction of the parent (in deg)")]
    public float LateralPitch { get; set; } = 45f * DegToRad;

    ///<summary>
    /// Variance of the angle of the lateral to its parent, in radians
    ///</summary>
    public float LateralPitchVar { get; set; } = 5f * DegToRad;

    ///<summary>
    /// Angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    [SpeciesUILabel("Lateral roll")]
    [SpeciesUITooltip("Orientation along the growth direction to the orientation of the parent (in deg)")]
    public float LateralRoll { get; set; } = 0f;

    ///<summary>
    /// Variance of the angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    public float LateralRollVar { get; set; } = 5f * DegToRad;

    [SpeciesUILabel("Twigs bending rate")]
    public float TwigsBending { get; set; } = 0.5f;

    [SpeciesUILabel("Twigs hierarchical bending")]
    [SpeciesUITooltip("Twigs bending factor by hierarchy level (multiplicative)")]
    public float TwigsBendingByLevel { get; set; } = 1;

    [SpeciesUILabel("Apex bending rate")]
    public float TwigsBendingApical { get; set; } = 0.02f;

    [SpeciesUILabel("Shoots gravitaxis")]
    public float ShootsGravitaxis { get; set; } = 0.2f;

    /// <summary>
    /// Angle from vertical that a shoot's own gravitropism steers toward.
    /// </summary>
    /// <value>
    /// Radians. Zero is an orthotropic, upright shoot and keeps the historical
    /// behaviour exactly. Values near pi/2 describe a plagiotropic shoot - a
    /// decumbent, trailing or creeping stem that actively holds itself near the
    /// horizontal rather than reaching for the vertical. Without it every shoot was
    /// pulled upright whenever its axis dipped below horizontal, in proportion to how
    /// far it had dipped and regardless of the species, so a trailing bellflower
    /// climbed to 2.4 m in two seasons instead of sprawling into a mat.
    /// </value>
    [SpeciesUILabel("Shoot gravitropic set-point angle")]
    public float ShootsGravitropicSetPointAngle { get; set; } = 0f;

    /// <summary>
    /// Negative gravitropism of a stem after birth: the rate per hour at which a stem segment's
    /// rest frame is steered toward <see cref="ShootsGravitropicSetPointAngle"/>.
    /// </summary>
    /// <value>
    /// Zero to one per hour. Zero, the default, keeps the historical behaviour in which a stem's
    /// rest frame is set at birth and never referred back to the vertical. See
    /// FlowerSettings.ScapeGravitropicRatePerHour for the scape's equivalent and the rate to use.
    /// </value>
    [SpeciesUILabel("Shoot gravitropic rate per hour")]
    public float ShootsGravitropicRatePerHour { get; set; } = 0f;

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
    /// Standard leaf axis length from petiole attachment toward the tip (in meters).
    /// Total blade length also includes any basal lobes behind the attachment and the
    /// authored outline's tip extent; it is not necessarily equal to this axis length.
    ///</summary>
    [SpeciesUIGroup("Leafs")]
    [SpeciesUILabel("Leaf length")]
    [SpeciesUITooltip("Leaf axis length from the petiole attachment toward the tip (m); basal lobes can make the total blade longer.")]
    public float LeafLength { get; set; } = 0.12f;

    ///<summary>
    /// Variation of the leaf attachment-to-tip axis length (in meters), not the total blade envelope.
    ///</summary>
    public float LeafLengthVar { get; set; } = 0.02f;

    ///<summary>
    /// Longest blade segment a leaf is built from, in metres. Zero keeps a leaf as one rigid piece.
    ///</summary>
    ///<remarks>
    /// A Carex blade is 30 cm long and 6 mm wide - a 50:1 strip simulated as one rectangle, which
    /// cannot arch the way a real sedge leaf does. Splitting it into a chain of <see
    /// cref="OrganTypes.Leaf"/> segments lets gravity bend it progressively, and it makes the
    /// collision response local: a correction rotates an organ about its base, so a 2 degree turn
    /// sweeps a 30 cm blade's tip by a centimetre while a 2 cm petiole moves 0.07 mm.
    ///
    /// Segments are ordinary leaf organs, so every export path, the leaf area index and the
    /// photosynthesis loop handle them unchanged, and the total blade area is unaffected - the
    /// species' leaf length is divided among the segments rather than repeated by each.
    ///
    /// Organ count is the cost: at day 730 Campanula carries 1,588 leaves, so a four-way split adds
    /// nearly five thousand organs to one plant. Leave this at zero for species whose leaves are
    /// short enough not to need it.
    ///</remarks>
    public float LeafSegmentLength { get; set; } = 0f;

    ///<summary>
    /// Effective thickness of a blade for bending, in metres. Stands in for the keel, midrib and
    /// turgor that hold a real leaf up; the drawn blade stays <see cref="AboveGroundAgent.LeafThickness"/>.
    ///</summary>
    ///<remarks>
    /// Bending stiffness goes as the cube of this, so it is the knob that decides whether a long
    /// blade arches gently or collapses. A flat strip of the rendered 0.1 mm would have essentially
    /// no bending stiffness at all, which is not how a sedge behaves.
    ///</remarks>
    public float LeafKeelThickness { get; set; } = 0.0028f;

    ///<summary>
    /// Elastic modulus of a leaf blade, in pascals. Zero falls back to
    /// <see cref="GreenElasticModulus"/>.
    ///</summary>
    ///<remarks>
    /// A blade needs its own figure because the basal stiffness ramp does not apply to one. That
    /// ramp exists to stiffen the base of a shoot and is keyed on topological depth, which is
    /// meaningless for a leaf - and because it is applied with a max(), it was raising every Carex
    /// blade to <see cref="BasalGreenElasticModulus"/>, 1.8e8 against the species' own 3e6. Sixty
    /// times too stiff is why segmented blades came out straight: 0.9 degrees of curvature over a
    /// 30 cm leaf.
    ///</remarks>
    public float LeafElasticModulus { get; set; } = 0f;

    /// <summary>Experimental shell membrane modulus (Pa); zero uses LeafElasticModulus, then GreenElasticModulus.</summary>
    [SpeciesUIGroup("Mechanics")]
    [SpeciesUILabel("Shell membrane modulus (Pa)")]
    [SpeciesUITooltip("Reserved for shell validation; modes 0 and 1 do not use this value. Zero retains the existing blade modulus. Not a measured Rotblum value.")]
    public float LeafMembraneElasticModulus { get; set; } = 0f;

    /// <summary>Experimental shell bending modulus (Pa), separate from stretching; zero uses the existing blade modulus.</summary>
    [SpeciesUILabel("Shell bending modulus (Pa)")]
    [SpeciesUITooltip("Reserved for shell validation; modes 0 and 1 do not use this value. Separate from stretching for layered lamina tissue. Zero retains the existing blade modulus.")]
    public float LeafBendingElasticModulus { get; set; } = 0f;

    /// <summary>Experimental lamina mechanical thickness (m); zero retains the existing 0.1 mm tissue assumption. Does not alter biomass or rendered relief.</summary>
    [SpeciesUILabel("Shell tissue thickness (m)")]
    [SpeciesUITooltip("Reserved for shell validation; modes 0 and 1 do not use this value. Zero uses 0.0001 m. Independent of the midrib keel, rendered relief and physiological mass.")]
    public float LeafMechanicalThickness { get; set; } = 0f;

    /// <summary>Experimental shell Poisson ratio; 0.3 is a provisional engineering parameter, not a measured Bergenia value.</summary>
    [SpeciesUILabel("Shell Poisson ratio")]
    [SpeciesUITooltip("Reserved for shell validation; modes 0 and 1 do not use this value. Must be greater than -1 and less than 0.5. The default 0.3 is provisional.")]
    public float LeafPoissonRatio { get; set; } = .3f;

    ///<summary>
    /// Effective keel thickness at the tip of a blade as a fraction of its value at the base.
    /// One means a blade of uniform stiffness.
    ///</summary>
    ///<remarks>
    /// A uniform blade cannot do what a sedge leaf does. Stiffness drives both the arch and the
    /// plant's overall height, so with one figure for the whole blade the two trade directly against
    /// each other: measured on Carex at day 100, enough compliance for a 39 degree arch cost ten
    /// centimetres of height and put 58 organs under the soil, while enough stiffness to hold 31 cm
    /// left only 9 degrees of curve. A real blade tapers - it is thickest and most strongly keeled
    /// where it leaves the sheath - so the base holds the leaf up while the tip is free to fall.
    /// Bending stiffness goes as the cube of this, so 0.35 makes a tip roughly twenty times more
    /// compliant than the base.
    ///</remarks>
    public float LeafTipStiffnessFraction { get; set; } = 1f;

    ///<summary>
    /// Number of segments a blade of this species is built from. Derived from
    /// <see cref="LeafSegmentLength"/>, and computed from the species length alone so that every
    /// leaf of a species is divided the same way regardless of its own length variance.
    ///</summary>
    public int LeafSegmentCount => LeafSegmentLength > 0f && LeafLength > LeafSegmentLength
        ? (int)Math.Min(MaximumLeafSegments, Math.Ceiling((double)LeafLength / LeafSegmentLength - CountEpsilon))
        : 1;

    /// <summary>Slack on the segment-count division, so a length that divides exactly does not get
    /// an extra segment from a float quotient landing a few ulps above the integer.</summary>
    const float CountEpsilon = 1e-4f;

    /// <summary>Upper bound on the split, so a species preset cannot multiply the organ count without limit.</summary>
    /// <remarks>
    /// Sixteen rather than eight because eight was exactly the value Carex asks for, and a cap that
    /// coincides with a species' own figure silently becomes the thing that decides it.
    /// </remarks>
    public const int MaximumLeafSegments = 16;

    ///<summary>
    /// Half the full leaf blade width perpendicular to its main axis (in meters).
    /// A radius of 0.035 produces a 0.07 m wide mature blade, independent of mesh coordinates.
    ///</summary>
    [SpeciesUILabel("Leaf half-width")]
    public float LeafRadius { get; set; } = 0.04f;

    ///<summary>
    /// Variation of the leaf half-width (in meters). Full blade width varies by twice this amount.
    ///</summary>
    public float LeafRadiusVar { get; set; } = 0.01f;

    ///<summary>
    /// Standard leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    [SpeciesUILabel("Leaf pitch")]
    [SpeciesUITooltip("Mean leaf angle to the petiole (in deg)")]
    public float LeafPitch { get; set; } = 20f * DegToRad;

    ///<summary>
    /// Variance of the leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    public float LeafPitchVar { get; set; } = 5f * DegToRad;

    ///<summary>
    /// Standard petiole length (in meters)
    ///</summary>
    [SpeciesUILabel("Petiole length")]
    public float PetioleLength { get; set; } = 0.04f;

    ///<summary>
    /// Variance of the petiole length (in meters)
    ///</summary>
    public float PetioleLengthVar { get; set; } = 0.01f;

    ///<summary>
    /// Standard petiole radius (in meters)
    ///</summary>
    [SpeciesUILabel("Petiole radius")]
    public float PetioleRadius { get; set; } = 0.0025f;

    ///<summary>
    /// Variance of the petiole radius (in meters)
    ///</summary>
    public float PetioleRadiusVar { get; set; } = 0.0005f;

    [SpeciesUIIgnore]
    public float PetioleCoverThreshold { get; private set; } = float.MaxValue;

    ///<summary>
    /// Standard growth time of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf growth time")]
    public float LeafGrowthTime { get; set; } = 480f;

    /// <summary>CPU-light calibration multiplier for blade and petiole expansion.
    /// Retains the actual energy budget, environmental response and support limits.
    /// One preserves the previous rate; this is not extra photosynthetic energy.
    /// Applies to herbaceous blades and petioles under every light model.</summary>
    [SpeciesUILabel("Leaf expansion rate scale")]
    public float LeafExpansionRateScale { get; set; } = 1f;

    /// <summary>Apply the empirical air-temperature response to vegetative development.
    /// Disabled for custom/older species unless explicitly configured. This controls tissue
    /// development, not frost survival, soil temperature or photosynthesis.</summary>
    [SpeciesUIGroup("Seasonal growth")]
    [SpeciesUILabel("Temperature-limited vegetative growth")]
    public bool VegetativeTemperatureResponseEnabled { get; set; } = false;

    /// <summary>Air temperature in degrees Celsius at which vegetative development stops.
    /// The Geranium default of 5 C is provisional, not a measured cultivar threshold.</summary>
    [SpeciesUILabel("Vegetative growth base temperature (C)")]
    public float VegetativeGrowthBaseTemperatureC { get; set; } = 5f;

    /// <summary>Air temperature in degrees Celsius at which the empirical developmental
    /// multiplier reaches one. The response is linear between base and full temperature,
    /// capped at one; it does not model heat injury.</summary>
    [SpeciesUILabel("Vegetative growth full-rate temperature (C)")]
    public float VegetativeGrowthFullTemperatureC { get; set; } = 20f;

    /// <summary>Previous-day incident light in Wh/m2 at which vegetative development reaches
    /// its nominal rate. Zero retains legacy relative-production expansion. Positive values
    /// use the propagated absolute leaf exposure, including at petioles and crown supports.
    /// This empirical developmental response does not create or debit assimilates.</summary>
    [SpeciesUILabel("Vegetative growth reference light (Wh/m2/day)")]
    public float VegetativeGrowthLightReferenceWhM2 { get; set; } = 0f;

    /// <summary>
    /// Experimental construction debit in grams of glucose equivalent per gram
    /// of new dry tissue. A positive value also enables glucose-equivalent
    /// photosynthesis, water stoichiometry, and dry-mass maintenance. Zero
    /// retains the legacy carbon-accounting mode; general water-routing fixes still apply.
    /// Covers expansion and committed births; initial germination biomass is an initial condition.
    /// </summary>
    [SpeciesUIGroup("Leafs")]
    [SpeciesUILabel("Construction cost (g glucose/g dry tissue)")]
    public float ConstructionEnergyPerDryGram { get; set; }

    ///<summary>
    /// Variance of the growth time of a leaf (in hours)
    ///</summary>
    public float LeafGrowthTimeVar { get; set; } = 120f;

    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf seasonal senescence")]
    public float LeafSenescenceSeasonalDuration { get; set; } = 480f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceSeasonalDurationVar { get; set; } = 120f;

    ///<summary>
    /// Standard senescence duration of a leaf (in hours)
    ///</summary>
    [SpeciesUILabel("Leaf stress senescence")]
    public float LeafSenescenceStressDuration { get; set; } = 140f;

    ///<summary>
    /// Variance of the senescence duration of a leaf (in hours)
    ///</summary>
    public float LeafSenescenceStressDurationVar { get; set; } = 60f;

    /// <summary>
    /// If positive, marks a typical senescence period (in seasonal progress, i.e. think of roman months)
    /// </summary>
    //TODO should be simply conted from the astronomical spring
    [SpeciesUILabel("Season duration (in months)")]
    public float SeasonalSenescence { get; set; } = 7.8f / 11;

    ///<summary>
    /// Density of the roots system (affects branching probabiilty). Valued 0 to 1 (with one being the most dense)
    ///</summary>
    [SpeciesUIGroup("Roots")]
    [SpeciesUILabel("Roots density")]
    public float RootsDensity { get; set; } = 0.5f;

    /// <summary>Maximum child-root radius relative to its parent. Woody roots apply this ratio at branching junctions; serial segments share their parent's ceiling. Other behaviors apply it at every segment. One retains the historical untapered root network.</summary>
    [SpeciesUILabel("Root radius taper")]
    public float RootRadiusTaper { get; set; } = 1f;

    ///<summary>
    /// Correction factor to point the roots growth downwards
    ///</summary>
    [SpeciesUILabel("Roots gravitaxis")]
    public float RootsGravitaxis { get; set; } = 0.2f;

    // public float RootRadiusGrowthPerH { get; init; }
    // public float RootLengthGrowthPerH { get; init; }

    //public int FirstFruitHour { get; init; }

    /// <summary>
    /// Dry wood density in g/m³
    /// </summary>
    [SpeciesUILabel("Dry wood density")]
    public float DensityDryWood { get; set; } = 700_000;
    /// <summary>
    /// Dry stem density in g/m³
    /// </summary>
	[SpeciesUILabel("Dry stem density")]
    public float DensityDryStem { get; set; } = 200_000;

    public float AuxinsProduction { get; set; } = 40;
    public float CytokininsProduction { get; set; } = 40;

    public float AuxinsReach { get; set; } = 1;

    public float CytokininsReach { get; set; } = 1;

    public float AuxinsThreshold { get; set; } = 1f;


    #region Leaf Appearance
    public LeafPhenology LeafPhenology { get; internal set; } = new();

    /// <summary>
    /// Colours of the stem through its life: young shoot, hardened stem or twig, old base or
    /// bark, and standing dead. Null takes the generic model of the species' habit.
    /// </summary>
    /// <remarks>
    /// Like the leaf phenology this is a runtime asset of the named preset, not an editable
    /// number, so it never travels in a request.
    /// </remarks>
    [JsonIgnore]
    public StemColorModel? StemColors { get; internal set; }

    internal StemColorModel EffectiveStemColors
        => StemColors ?? (Behavior == Behavior.Herbaceous ? StemColorModel.Herbaceous : StemColorModel.Woody);

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
    public float FloweringStartAgeHours { get; set; } = 24f * 45f;

    /// <summary>
    /// Start of the flowering window in the March-to-February simulation year.
    /// </summary>
    /// <value>
    /// A normalized value from 0 to 1, where 0 is March 1. It must not be
    /// greater than FloweringSeasonEnd; windows that cross March are unsupported.
    /// </value>
    [SpeciesUILabel("Flowering season start")]
    public float FloweringSeasonStart { get; set; } = 2f / 11f;

    /// <summary>
    /// End of the flowering window in the March-to-February simulation year.
    /// </summary>
    /// <value>
    /// A normalized value from 0 to 1, where 0 is March 1. Use a value greater
    /// than FloweringSeasonStart for a reliable non-empty window; an equal
    /// boundary can be skipped by a discrete timestep.
    /// </value>
    [SpeciesUILabel("Flowering season end")]
    public float FloweringSeasonEnd { get; set; } = 5f / 11f;

    /// <summary>
    /// Maximum time spent in the flowering phase after that phase starts.
    /// </summary>
    /// <value>Hours; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flowering phase duration")]
    public float FloweringPhaseDurationHours { get; set; } = 24f * 30f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// before the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero disables pre-flowering commitment.</value>
    [SpeciesUILabel("Pre-flowering commitment probability per hour")]
    public float PreFloweringCommitmentProbabilityPerHour { get; set; } = 0.0005f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// during the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. A positive value is required for seasonal flowering.</value>
    [SpeciesUILabel("Flowering commitment probability per hour")]
    public float FloweringCommitmentProbabilityPerHour { get; set; } = 0.045f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// after the configured flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero disables post-flowering commitment.</value>
    [SpeciesUILabel("Post-flowering commitment probability per hour")]
    public float PostFloweringCommitmentProbabilityPerHour { get; set; } = 0.001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem commits to a flower
    /// while the seasonal state is dormant or waiting to reset.
    /// </summary>
    /// <value>From 0 to 1. Keep at zero for seasonally dormant species.</value>
    [SpeciesUILabel("Dormant flowering probability per hour")]
    public float DormantFloweringCommitmentProbabilityPerHour { get; set; } = 0f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment before the flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero prevents pre-flowering crown continuation.</value>
    [SpeciesUILabel("Pre-flowering crown continuation per hour")]
    public float PreFloweringCrownContinuationProbabilityPerHour { get; set; } = 0f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment during the flowering window.
    /// </summary>
    /// <value>From 0 to 1. The flower-commitment event is evaluated first.</value>
    [SpeciesUILabel("Flowering crown continuation per hour")]
    public float FloweringCrownContinuationProbabilityPerHour { get; set; } = 0.0001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment after the flowering window.
    /// </summary>
    /// <value>From 0 to 1. Zero prevents post-flowering crown continuation.</value>
    [SpeciesUILabel("Post-flowering crown continuation per hour")]
    public float PostFloweringCrownContinuationProbabilityPerHour { get; set; } = 0.001f;

    /// <summary>
    /// Hourly probability that a completed crown meristem continues as another
    /// vegetative crown segment during dormancy.
    /// </summary>
    /// <value>From 0 to 1. Keep at zero to stop shoot growth during dormancy.</value>
    [SpeciesUILabel("Dormant crown continuation per hour")]
    public float DormantCrownContinuationProbabilityPerHour { get; set; } = 0f;

    /// <summary>
    /// Whether flower commitment immediately creates a new vegetative crown
    /// meristem alongside the inflorescence.
    /// </summary>
    /// <value>
    /// Use false for determinate rosette shoots; use true for an indeterminate
    /// crown that continues vegetative growth after initiating a flower.
    /// </value>
    [SpeciesUILabel("Continue crown after flower commitment")]
    public bool ContinueCrownAfterFlowerCommitment { get; set; } = true;

    /// <summary>Maximum basal flower-scape emergence search tilt, in degrees.</summary>
    /// <value>Zero disables the multi-step scape clearance forecast. This is a
    /// provisional numerical search bound, not a measured species posture.</value>
    [SpeciesUILabel("Basal scape emergence search angle")]
    public float BasalScapeEmergenceMaxTiltDegrees
    {
        get => basalScapeEmergenceMaxTiltDegrees;
        init { basalScapeEmergenceMaxTiltDegrees = value; basalScapeEmergenceWasSet = true; }
    }

    private float basalScapeEmergenceMaxTiltDegrees;
    private bool basalScapeEmergenceWasSet;

    // Requests archived before this setting existed omit it. Inherit the named
    // preset's new default in that case, while preserving an explicit zero.
    internal void InheritMissingScapeEmergence(SpeciesSettings preset)
    {
        if (!basalScapeEmergenceWasSet)
            basalScapeEmergenceMaxTiltDegrees = preset.BasalScapeEmergenceMaxTiltDegrees;
    }

    /// <summary>
    /// Variation in the time required to grow an inflorescence stem segment.
    /// </summary>
    /// <value>
    /// Hours; must be non-negative and smaller than FlowerSettings.FlowerStemSegmentGrowthDurationHours
    /// when positive, so a sampled growth time cannot reach zero.
    /// </value>
    [SpeciesUILabel("Flower stem segment growth duration variance")]
    public float FlowerStemSegmentGrowthDurationVarianceHours { get; set; }

    /// <summary>
    /// Variation in the time from a closed flower bud to bloom.
    /// </summary>
    /// <value>
    /// Hours; must be non-negative and smaller than FlowerSettings.BudBloomDurationHours
    /// when positive, so a sampled bloom time cannot reach zero.
    /// </value>
    [SpeciesUILabel("Flower bud bloom duration variance")]
    public float BudBloomDurationVarianceHours { get; set; }

    /// <summary>
    /// Number of flower axes a single aerial crown may carry at once.
    /// </summary>
    /// <value>
    /// At least one. One suits a rosette whose shoot ends in a single terminal
    /// inflorescence, such as Geranium macrorrhizum. Species that bear flowers along
    /// a leafy trailing stem, or that rebloom from the same crown, need a larger value.
    /// Counted by walking from the committing meristem up to the rhizome.
    /// </value>
    [SpeciesUILabel("Maximum flowers per crown")]
    public int MaximumFlowersPerCrown { get; set; } = 1;

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
    public float WoodElasticModulus { get; set; } = 1e8f;

    /// <summary>
    /// Minimum Young's modulus of non-lignified shoot tissue used by gravity bending.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Green tissue elastic modulus")]
    public float GreenElasticModulus { get; set; } = 1e8f;

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
    public float BasalGreenElasticModulus { get; set; } = 2e8f;

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
    public float BasalStiffnessAttenuationPerSegment { get; set; } = 0.01f;

    /// <summary>
    /// Young's modulus used for vegetative leaf petioles.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Petiole elastic modulus")]
    public float PetioleElasticModulus { get; set; } = 2e8f;

    /// <summary>
    /// Young's modulus used for flower organs other than individual pedicels.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flower organ elastic modulus")]
    public float FlowerOrganElasticModulus { get; set; } = 5e6f;

    /// <summary>
    /// Young's modulus used for the pedicel supporting an individual flower.
    /// </summary>
    /// <value>Pascals; must be finite and greater than zero.</value>
    [SpeciesUILabel("Flower pedicel elastic modulus")]
    public float FlowerPedicelElasticModulus { get; set; } = 4e7f;

    /// <summary>
    /// Elastic modulus of the inflorescence axis - the scape, its branches and the
    /// meristems that build them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FlowerOrganElasticModulus"/> because the axis and the
    /// flower it carries are not equally stiff, and because a species needs to choose:
    /// a Bergenia scape is a rigid fleshy column that holds its panicle straight up,
    /// while a Geranium peduncle is meant to bend under its own cyme. Sharing one value
    /// made every scape as stiff as the stiffest one. Zero falls back to
    /// <see cref="FlowerOrganElasticModulus"/>.
    /// </remarks>
    /// <value>Pascals; non-negative.</value>
    [SpeciesUILabel("Flower scape elastic modulus")]
    public float FlowerScapeElasticModulus { get; set; }

    /// <summary>
    /// Age at which a herbaceous leaf and its petiole become eligible for renewal.
    /// </summary>
    /// <value>Hours; must be finite and greater than zero.</value>
    [SpeciesUIGroup("Herbaceous shoots")]
    [SpeciesUILabel("Maximum leaf age")]
    public float LeafMaximumAgeHours { get; set; } = 24f * 2f * 365f;

    /// <summary>
    /// RGB color of a newly created herbaceous leaf in the legacy color path.
    /// </summary>
    /// <value>Each component must be finite and between 0 and 1.</value>
    public Vector3 YoungLeafColor { get; set; } = new(0.18f, 0.42f, 0.12f);

    /// <summary>
    /// RGB color approached as a herbaceous leaf reaches its maximum age.
    /// </summary>
    /// <value>Each component must be finite and between 0 and 1.</value>
    public Vector3 SenescentLeafColor { get; set; } = new(0.45f, 0.30f, 0.08f);

    /// <summary>
    /// Seasonal phases in which a dormant aerial crown bud on a rhizome may make
    /// its single activation attempt for the current seasonal cycle.
    /// </summary>
    /// <value>
    /// One or more <see cref="SeasonalPhase"/> flags. The probability setting must
    /// also be greater than zero for activation to occur.
    /// </value>
    [SpeciesUIGroup("Herbaceous shoots")]
    [SpeciesUILabel("Crown bud activation period")]
    public SeasonalPhase CrownBudActivationPeriod { get; set; } = SeasonalPhase.PreFlower;

    /// <summary>
    /// Probability that a dormant aerial crown bud borne on a rhizome activates
    /// during its single eligible attempt in a seasonal cycle.
    /// </summary>
    /// <value>
    /// From 0 to 1. Zero keeps crown buds dormant; one activates every eligible
    /// bud. The bud must be older than 24 hours and have a positive startup reserve.
    /// The probability applies to the whole seasonal bud-break window, not to a
    /// single instant; it is spread over <see cref="CrownBudBreakWindowHours"/> so
    /// that buds of one cohort do not all break on the same day. A bud still breaks
    /// at most once per March-to-February cycle.
    /// </value>
    [SpeciesUILabel("Crown bud activation probability per season")]
    public float CrownBudActivationProbabilityPerSeason { get; set; } = 1f;

    /// <summary>
    /// Length of the seasonal window over which crown bud break is spread.
    /// </summary>
    /// <value>
    /// Hours; must be finite and positive. Bud break in a perennial runs over
    /// several weeks rather than being decided at one instant, so
    /// <see cref="CrownBudActivationProbabilityPerSeason"/> is converted into a
    /// per-tick hazard across this window.
    /// </value>
    [SpeciesUILabel("Crown bud break window")]
    [SpeciesUITooltip("Hours over which the seasonal crown bud break probability is spread")]
    public float CrownBudBreakWindowHours { get; set; } = 24f * 30f;


    /// <summary>
    /// Radius of a dormant vegetative crown bud borne by a rhizome.
    /// </summary>
    /// <value>
    /// Meters; must be finite, at least <see cref="AboveGroundAgent.InitialRadius"/>,
    /// and no greater than <see cref="ShootMaximumRadius"/>. A nonzero physical
    /// radius is required for the bud to store resources and establish a supported
    /// aerial shoot after activation.
    /// </value>
    [SpeciesUILabel("Dormant crown bud radius")]
    public float CrownBudRadius { get; set; } = 0.0005f;

    /// <summary>
    /// Vertical clearance between the soil surface and the first compact crown node.
    /// </summary>
    /// <value>Meters; must be finite and greater than or equal to zero.</value>
    [SpeciesUILabel("First crown node height above soil")]
    public float CrownNodeHeightAboveSoil { get; set; } = 0.005f;

    /// <summary>
    /// Shortest length an aerial shoot segment may target.
    /// </summary>
    /// <value>
    /// Meters; must be finite and positive. A rosette has compressed but non-zero
    /// internodes - successive leaves sit a millimetre or two apart rather than at
    /// one point - so this floors both the crown riser and every crown internode
    /// even when <see cref="NodeDistance"/> is zero. A zero-length segment would
    /// also have zero volume, and therefore no storage capacity or life support.
    /// </value>
    [SpeciesUILabel("Minimum internode length")]
    [SpeciesUITooltip("Shortest aerial shoot segment (in meters)")]
    public float MinimumInternodeLength { get; set; } = 0.001f;

    /// <summary>
    /// Pitch of a new aerial crown relative to its horizontal rhizome axis.
    /// </summary>
    /// <value>
    /// Radians from 0 to pi. Zero follows the rhizome; pi/2 creates an upright crown.
    /// </value>
    [SpeciesUILabel("Crown pitch")]
    public float CrownPitch { get; set; } = 0.5f * MathF.PI;

    /// <summary>
    /// Largest number of vegetative nodes one crown may carry in total before its
    /// apices stop extending. Zero leaves the crown unbounded.
    /// </summary>
    /// <value>
    /// A rosette perennial carries a compressed caudex, not an indeterminate shoot:
    /// its apex adds leaf nodes for a season and the crown still stays a few
    /// centimetres tall because old leaves are shed and new crowns arise beside it.
    /// The simulator had no such bound, so a crown grew one node per continuation
    /// event forever - a Fragaria crown reached 108 stacked nodes and 31 cm of stem in
    /// two years, which renders as a leaf ball on a stick, and a Campanula axis reached
    /// 2.4 m. The cap also stops the chain from curling underground: every segment
    /// bends a little under load and a long enough chain always ends up pointing down.
    /// Set it to roughly the number of live leaf nodes the rosette carries, divided by
    /// <see cref="LateralsPerNode"/>. Leave it at zero for a genuinely indeterminate
    /// trailing or climbing shoot.
    /// </value>
    [SpeciesUILabel("Max nodes per crown axis")]
    public int CrownMaximumNodes { get; set; } = 0;

    /// <summary>
    /// Dimensionless multiplier for herbaceous shoot elongation and radial growth.
    /// </summary>
    /// <value>
    /// Must be finite and non-negative. Zero stops axial shoot growth but does not
    /// stop separately timed leaf, petiole, flower, or rhizome-tip elongation.
    /// </value>
    [SpeciesUILabel("Herbaceous growth rate scale")]
    public float HerbaceousGrowthRateScale { get; set; } = 0.2f;

    /// <summary>
    /// Maximum radius of non-rhizome herbaceous shoot segments.
    /// </summary>
    /// <value>
    /// Meters; must be finite and at least the simulator's initial shoot radius.
    /// </value>
    [SpeciesUILabel("Maximum shoot radius")]
    public float ShootMaximumRadius { get; set; } = 0.0005f;

    /// <summary>
    /// Maximum reorientation of existing petioles when a crown adds another segment.
    /// </summary>
    /// <value>Radians from 0 to pi and not less than PetioleReorientationStepAngle.</value>
    [SpeciesUILabel("Maximum petiole reorientation")]
    public float PetioleReorientationMaximumAngle { get; set; } = 0.3f * MathF.PI;

    /// <summary>
    /// Petiole angle added when a crown continues with another segment.
    /// </summary>
    /// <value>
    /// Radians from 0 to PetioleReorientationMaximumAngle. Zero disables this
    /// architectural reorientation; elastic gravity bending remains active.
    /// </value>
    [SpeciesUILabel("Petiole reorientation step")]
    public float PetioleReorientationStepAngle { get; set; }

    /// <summary>
    /// Length of one petiole segment, in metres. Zero leaves a petiole as a single organ.
    /// </summary>
    /// <remarks>
    /// The same device <see cref="LeafSegmentLength"/> gives a blade, for the same reason: one organ
    /// can only rotate rigidly about its base, so a petiole modelled as one segment stays dead
    /// straight however correct its bending law is. A long one - Heuchera's, or Geranium
    /// macrorrhizum's 18 cm - visibly should not.
    ///
    /// Each segment is an ordinary petiole organ, so nothing downstream needed a new organ type,
    /// and the species' petiole length is divided among them, leaving the petiole the same length.
    /// </remarks>
    [SpeciesUILabel("Petiole segment length")]
    public float PetioleSegmentLength { get; set; } = 0f;

    /// <summary>
    /// Number of serial segments used to model a vegetative leaf petiole. Derived from
    /// <see cref="PetioleSegmentLength"/>, so every petiole of a species divides the same way.
    /// </summary>
    /// <value>An integer of at least one. One creates a single straight petiole.</value>
    public int PetioleSegmentCount => PetioleSegmentLength > 0f && PetioleLength > PetioleSegmentLength
        ? Math.Min(MaximumPetioleSegments, (int)MathF.Ceiling(PetioleLength / PetioleSegmentLength - CountEpsilon))
        : 1;

    /// <summary>Upper bound on the split, so a preset cannot multiply the organ count without limit.</summary>
    public const int MaximumPetioleSegments = 8;

    /// <summary>
    /// Dimensionless multiplier for rhizome axial and radial growth.
    /// </summary>
    /// <value>
    /// Must be finite and non-negative. Before resource, water, and dominance limits,
    /// a value of one permits at most 0.001 m/h axial and 0.00002 m/h radial growth.
    /// Zero stops rhizome growth but not separately timed aerial organs.
    /// </value>
    [SpeciesUIGroup("Rhizomes")]
    [SpeciesUILabel("Rhizome growth rate scale")]
    public float RhizomeGrowthRateScale { get; set; } = 0.05f;

    /// <summary>
    /// Seasonal phases in which dormant basal and axillary rhizome buds
    /// may activate into growing rhizome meristems.
    /// </summary>
    /// <value>
    /// One or more <see cref="SeasonalPhase"/> flags. Outside these phases rhizome
    /// buds remain dormant even when their activation probability is non-zero.
    /// Activation also requires a bud older than 24 hours with sufficient stored energy.
    /// </value>
    [SpeciesUILabel("Rhizome bud activation period")]
    public SeasonalPhase RhizomeBudActivationPeriod { get; set; } =
        SeasonalPhase.PreFlower | SeasonalPhase.Flowering | SeasonalPhase.PostFlower;

    /// <summary>
    /// Hourly probability that a dormant basal rhizome bud activates and begins a
    /// primary rhizome axis during an eligible seasonal phase.
    /// </summary>
    /// <value>
    /// From 0 to 1. Zero prevents primary-axis initiation. Once activated, the
    /// apical meristem continues across numerical segment boundaries without another
    /// bud-break event. Values are accumulated over HoursPerTick for timestep-stable timing.
    /// </value>
    [SpeciesUILabel("Basal rhizome bud activation probability per hour")]
    public float RhizomeBasalBudActivationProbabilityPerHour { get; set; } = 0.005f;

    /// <summary>
    /// Hourly probability that a dormant axillary rhizome bud activates into a
    /// lateral rhizome branch during an eligible seasonal phase.
    /// </summary>
    /// <value>
    /// From 0 to 1. Zero preserves axillary buds in a dormant state. This setting
    /// does not affect continuation of an already active apical meristem. Values
    /// accumulate over HoursPerTick for timestep-stable timing.
    /// </value>
    [SpeciesUILabel("Axillary rhizome bud activation probability per hour")]
    public float RhizomeAxillaryBudActivationProbabilityPerHour { get; set; }

    /// <summary>
    /// Probability that a dormant axillary rhizome bud breaks during one seasonal
    /// cycle, producing a lateral rhizome branch.
    /// </summary>
    /// <value>
    /// From 0 to 1. When greater than zero this takes precedence over
    /// <see cref="RhizomeAxillaryBudActivationProbabilityPerHour"/>. It is the
    /// botanically appropriate form for a creeping rhizome: the apex terminates or
    /// pauses at the end of a season and growth resumes sympodially from one or two
    /// axillary buds, so branching is an annual developmental event rather than a
    /// constant hourly hazard. Spread over
    /// <see cref="RhizomeBudBreakWindowHours"/>; a bud breaks at most once per cycle.
    /// Repeated annual branching is what turns a few radiating axes into a closed
    /// carpet instead of isolated rays.
    /// </value>
    [SpeciesUILabel("Axillary rhizome bud activation probability per season")]
    public float RhizomeAxillaryBudActivationProbabilityPerSeason { get; set; }

    /// <summary>
    /// Length of the seasonal window over which rhizome bud break is spread.
    /// </summary>
    /// <value>Hours; must be finite and positive.</value>
    [SpeciesUILabel("Rhizome bud break window")]
    [SpeciesUITooltip("Hours over which the seasonal rhizome bud break probability is spread")]
    public float RhizomeBudBreakWindowHours { get; set; } = 24f * 30f;

    /// <summary>
    /// Azimuth correction applied to a rhizome apex blocked by an established rhizome.
    /// </summary>
    /// <value>
    /// Radians from 0 to pi. A creeping apex that meets an obstruction is deflected
    /// around it rather than aborting, which is what lets later axes fill the gaps
    /// between earlier ones. Zero restores the previous behaviour of terminating the
    /// tip on first contact.
    /// </value>
    [SpeciesUIGroup("Rhizomes")]
    [SpeciesUILabel("Rhizome obstacle deflection angle")]
    public float RhizomeObstacleDeflectionAngle { get; set; } = 50f * DegToRad;

    /// <summary>
    /// Number of dormant axillary rhizome buds formed by each completed segment.
    /// </summary>
    /// <value>
    /// From 0 to 32. Each non-terminal completed segment forms this many lateral
    /// buds. The active terminal meristem continues independently while below
    /// RhizomeMaximumSegmentDepth.
    /// </value>
    [SpeciesUILabel("Axillary rhizome buds per segment")]
    public int RhizomeAxillaryBudsPerSegment { get; set; }

    /// <summary>
    /// Number of dormant basal rhizome buds formed around the seedling crown.
    /// </summary>
    /// <value>
    /// From 0 to 32. The buds are distributed radially and must activate before
    /// becoming growing primary rhizome axes.
    /// </value>
    [SpeciesUILabel("Initial basal rhizome buds")]
    public int InitialBasalRhizomeBuds { get; set; } = 3;

    /// <summary>
    /// Maximum graph depth of rhizome segments measured from the seed segment.
    /// </summary>
    /// <value>
    /// A non-negative segment count. This is topological depth, not burial depth;
    /// zero prevents creation of new rhizome segments.
    /// </value>
    [SpeciesUILabel("Maximum rhizome segment depth")]
    public int RhizomeMaximumSegmentDepth { get; set; } = 15;

    /// <summary>
    /// Target length of each rhizome segment.
    /// </summary>
    /// <value>
    /// Meters; must be finite and greater than zero. A completed segment can
    /// exceed this target by at most one discrete growth increment.
    /// </value>
    [SpeciesUILabel("Rhizome segment length")]
    public float RhizomeSegmentLength { get; set; } = 0.025f;

    /// <summary>
    /// Variable length (additive) of each rhizome segment.
    /// </summary>
    /// <value>
    /// Meters; must be finite and greater than zero. A completed segment can
    /// exceed this target by at most one discrete growth increment.
    /// </value>
    [SpeciesUILabel("Rhizome segment length")]
    public float RhizomeSegmentLengthVar { get; set; } = 0.02f;

    /// <summary>
    /// Target radius of each rhizome segment.
    /// </summary>
    /// <value>
    /// Meters; must be finite and at least the simulator's initial shoot radius.
    /// </value>
    [SpeciesUILabel("Rhizome segment radius")]
    public float RhizomeSegmentRadius { get; set; } = 0.0025f;

    /// <summary>
    /// Radius of a dormant basal or axillary rhizome bud.
    /// </summary>
    /// <value>
    /// Meters; must be finite, at least <see cref="AboveGroundAgent.InitialRadius"/>,
    /// and no greater than <see cref="RhizomeSegmentRadius"/>. This physical size
    /// determines storage and maintenance requirements before bud break.
    /// </value>
    [SpeciesUILabel("Dormant rhizome bud radius")]
    public float RhizomeBudRadius { get; set; } = 0.0005f;

    /// <summary>
    /// Legacy JSON input for the probability that created rhizome continuation
    /// offshoots. It now maps to axillary bud activation because apical continuation
    /// is an uninterrupted meristem process.
    /// </summary>
    [Obsolete($"Use {nameof(RhizomeAxillaryBudActivationProbabilityPerHour)}.")]
    [SpeciesUIIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float RhizomeOffshootProbabilityPerHour
    {
        get => 0f;
        init => RhizomeAxillaryBudActivationProbabilityPerHour =
            Math.Max(RhizomeAxillaryBudActivationProbabilityPerHour, value);
    }

    /// <summary>
    /// Legacy JSON input for the extra lateral-branch probability. The current
    /// model represents it as axillary rhizome bud activation.
    /// </summary>
    [Obsolete($"Use {nameof(RhizomeAxillaryBudActivationProbabilityPerHour)}.")]
    [SpeciesUIIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float RhizomeSecondaryLateralProbabilityPerHour
    {
        get => 0f;
        init => RhizomeAxillaryBudActivationProbabilityPerHour =
            Math.Max(RhizomeAxillaryBudActivationProbabilityPerHour, value);
    }

    /// <summary>
    /// Legacy JSON input for offshoot count. One old offshoot is represented by the
    /// continuously active apex; any remaining offshoots become axillary buds.
    /// </summary>
    [Obsolete($"Use {nameof(RhizomeAxillaryBudsPerSegment)}.")]
    [SpeciesUIIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RhizomeOffshootsPerSegment
    {
        get => 0;
        init => RhizomeAxillaryBudsPerSegment = Math.Max(0, value - 1);
    }

    /// <summary>Legacy JSON input for the number of primary basal rhizome buds.</summary>
    [Obsolete($"Use {nameof(InitialBasalRhizomeBuds)}.")]
    [SpeciesUIIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int InitialRhizomeAxes
    {
        get => 0;
        init => InitialBasalRhizomeBuds = value;
    }

    /// <summary>
    /// Compatibility input for the short-lived apical-bud name. Active apices no
    /// longer break dormancy at numerical boundaries, so this maps to basal bud break.
    /// </summary>
    [Obsolete($"Use {nameof(RhizomeBasalBudActivationProbabilityPerHour)}.")]
    [SpeciesUIIgnore]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float RhizomeApicalBudActivationProbabilityPerHour
    {
        get => 0f;
        init => RhizomeBasalBudActivationProbabilityPerHour = value;
    }

    /// <summary>
    /// Retains a perennial rhizome in the plant graph when its modeled local energy
    /// reserve is temporarily depleted.
    /// </summary>
    /// <value>
    /// Enable for dormant clonal perennials that can be replenished by connected
    /// organs; disable when reserve depletion should kill that rhizome segment.
    /// </value>
    [SpeciesUILabel("Retain dormant rhizomes")]
    public bool RetainDormantRhizomes { get; set; }

    /// <summary>
    /// Plant water stress, on the 0-1 scale of <see cref="PlantFormation2.DroughtStress"/>, at which
    /// a herbaceous plant starts committing existing leaves to drought senescence. Below it a dry
    /// plant only grows less. See Agro/DROUGHT_RESPONSE.md.
    /// </summary>
    [SpeciesUIGroup("Drought")]
    [SpeciesUILabel("Drought senescence onset")]
    [SpeciesUITooltip("Water stress (0 = well watered, 1 = no plant water) at which existing leaves start to senesce. Growth slows below it already.")]
    public float DroughtSenescenceOnset { get; set; } = 0.3f;

    /// <summary>
    /// Days to remove half of the leaf area above the drought target canopy. Zero switches the
    /// whole drought response off and restores the earlier behaviour, for comparison.
    /// </summary>
    [SpeciesUILabel("Canopy adjustment half-time (days)")]
    [SpeciesUITooltip("Days to commit half of the excess leaf area above the drought target to senescence. 0 disables the drought response.")]
    public float CanopyAdjustmentHalfTimeDays { get; set; } = 7f;

    /// <summary>
    /// Hours a leaf killed by drought senescence stays attached, brown, before it is shed.
    /// </summary>
    [SpeciesUILabel("Dead leaf attachment (hours)")]
    [SpeciesUITooltip("Hours a leaf that completed drought senescence stays attached before it falls; it still shades and collides meanwhile.")]
    public float DeadLeafAttachmentHours { get; set; } = 336f;

    /// <summary>
    /// Share of its green leaf area a herbaceous perennial loses each autumn, from 0 to 1, taken from
    /// its older summer leaves only. Zero, the default, keeps every leaf through the winter.
    /// </summary>
    /// <remarks>
    /// Evergreen and semi-evergreen perennials do not keep a leaf for ever: from autumn into winter
    /// the older summer leaves colour, die and fall while the younger winter leaves overwinter
    /// (Fragaria vesca makes distinct summer and winter leaf cohorts; Geranium macrorrhizum and
    /// G. x cantabrigiense colour red and orange; Heuchera, Bergenia and Carex lose their oldest
    /// leaves over winter). The oldest and most shaded leaves go first, with
    /// <see cref="CanopyAdjustmentHalfTimeDays"/>, through the seasonal senescence path over
    /// <see cref="LeafSenescenceSeasonalDuration"/>, then hang for
    /// <see cref="DeadLeafAttachmentHours"/> and fall to a node bud. Needs the canopy response
    /// (<see cref="CanopyAdjustmentHalfTimeDays"/> above zero). See Agro/ORGAN_COLORS.md.
    /// </remarks>
    [SpeciesUILabel("Autumn leaf loss (share)")]
    [SpeciesUITooltip("Share of the leaf area shed each autumn, from the older summer leaves: oldest and most shaded first, through the autumn colours. 0 keeps every leaf over winter.")]
    public float AutumnLeafLossFraction { get; set; }

    /// <summary>
    /// When the autumn leaf loss begins, as a season fraction from 1 March; it runs until dormancy at
    /// <see cref="SeasonalSenescence"/>. The default 0.6 is 6 October.
    /// </summary>
    [SpeciesUILabel("Autumn leaf loss start (season)")]
    [SpeciesUITooltip("Season fraction from 1 March at which old leaves start to colour and fall; 0.6 is early October. It ends at dormancy.")]
    public float AutumnLeafLossStartSeason { get; set; } = 0.6f;
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
        Predefined.Add(Persicaria_Minor.Init());
        Predefined.Add(Lactuca_Serriola.Init());
        Predefined.Add(Taraxacum_Officinale.Init());
        Predefined.Add(Celtis_Australis.Init());
        Predefined.Add(Amelanchier_Alnifolia_Obelisk.Init());
        Predefined.Add(Acer_Platanoides_Globosum.Init());
    }

    // Runtime coefficients must not overwrite public request values. Saved requests
    // are rebuilt on resume; in-place conversion applied these factors a second time.
    internal float EffectiveShootsGravitaxis { get; private set; }
    internal float EffectiveTwigsBendingApical { get; private set; }
    private bool Initialized = false;

    public void Init(int hoursPerTick)
    {
        Console.WriteLine($"SpeciesSettings.Init called on {Name}, BudLength: {FlowerSettings.BudLength}");

        if (!Initialized)
        {
            Validate();
            TreeMainlineModulesEnabled = UsesRealAxillaryMainline;
            EffectiveTreeMainlineTrainingHeightM = TreeMainlineModulesEnabled
                ? TreeMainlineModuleMode == global::Agro.TreeMainlineModuleMode.Auto ? TreeClearTrunkHeight : TreeMainlineTrainingHeightM
                : 0f;
            TreeMainlineReferenceInclinationRadians = TreeMainlineReferenceInclinationDegrees * DegToRad;
            TreeSecondaryErectionEnabled = TreeMainlineModulesEnabled && TreeSecondaryMaturationStrainAmplitude > 0f;

            if (Behavior == Behavior.Woody)
            {
                TreeExtensionOrderFactors = BuildTreeOrderFactors(DominanceFactor);
                TreeApicalControlEnabled = TreeCrownHabit == TreeCrownHabit.Broad
                    && TreeGraftHeight == 0f && TreeApicalControlEndYears > 0f;
                TreeLeaderExtensionReduction = 1f - TreeMatureLeaderExtensionFraction;
                TreeFrameworkPitchReduction = 1f - TreeMatureFrameworkPitchFraction;
                TreeLeafInclinationRadians = MathF.PI * .5f + TreeLeafDeclinationDegrees * DegToRad;
                TreeLeafInclinationVariationRadians = TreeLeafDeclinationVariationDegrees * DegToRad;
                TreeGrowthUnitsEnabled = TreeSympodialRenewal || TreeLateralUnitArchDegrees > 0f;
                TreeLateralUnitArchRadians = TreeLateralUnitArchDegrees * DegToRad;
                TreeUnitLightDeviationRadians = TreeUnitLightDeviationDegrees * DegToRad;
            }

            // AuxinsDegradationPerTick = AuxinsReach * hoursPerTick;
            // CytokininsDegradationPerTick = CytokininsReach * hoursPerTick;
            EffectiveTwigsBendingApical = TwigsBendingApical * TwigsBendingByLevel;
            EffectiveShootsGravitaxis = ShootsGravitaxis * 0.4f;
            Initialized = true;

            PetioleCoverThreshold = MathF.Cos(MathF.PI * 0.5f - LateralPitch) * PetioleLength * 0.25f;

            //BUG with petiole -> stem and not meristem
            //Remove length factor at apex distribution for the current segment
            //Bending suddenly does not work
            //BUG water distribution
        }
    }

    // Resolve from form, rather than the species name. An automatic fallback is
    // a whole-plant choice before any tissue or random fate is created.
    bool HasRealAxillaryForm => Behavior == Behavior.Woody && TreeSympodialRenewal && TreeDorsiventralShoots
        && TreeStemCount == 1 && TreeGraftHeight == 0f && LateralsPerNode == 1
        && MathF.Abs(LateralRoll - MathF.PI) <= 1e-5f;
    bool UsesRealAxillaryMainline => TreeMainlineModuleMode == global::Agro.TreeMainlineModuleMode.RealAxillary
        || TreeMainlineModuleMode == global::Agro.TreeMainlineModuleMode.Auto && HasRealAxillaryForm && TreeClearTrunkHeight > 0f;

    private void Validate()
    {
        if (TreeMainlineModuleMode != global::Agro.TreeMainlineModuleMode.Off && Behavior != Behavior.Woody)
            throw new ArgumentException("Mainline modules are an opt-in woody-tree behavior.");
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

        RequireFiniteNonNegative(TreeSecondaryMaturationStrainAmplitude, nameof(TreeSecondaryMaturationStrainAmplitude));
        if (TreeSecondaryMaturationStrainAmplitude > 0f && Behavior != Behavior.Woody)
            throw new ArgumentException("Secondary erection requires woody real mainline modules.");
        if (Behavior == Behavior.Woody)
        {
            RequireFinitePositive(Height, nameof(Height));
            if (!Enum.IsDefined(TreeCrownHabit))
                throw new ArgumentOutOfRangeException(nameof(TreeCrownHabit));
            RequireFinitePositive(TreeCrownRadius, nameof(TreeCrownRadius));
            RequireFinitePositive(TreeAnnualExtension, nameof(TreeAnnualExtension));
            RequireFinitePositive(TreeRootAnnualExtension, nameof(TreeRootAnnualExtension));
            RequireFinitePositive(TreeAnnualRadiusGrowth, nameof(TreeAnnualRadiusGrowth));
            RequireFinitePositive(TreeMatureAgeYears, nameof(TreeMatureAgeYears));
            RequireFiniteNonNegative(TreeApicalControlStartYears, nameof(TreeApicalControlStartYears));
            RequireFiniteNonNegative(TreeApicalControlEndYears, nameof(TreeApicalControlEndYears));
            RequireFiniteNonNegative(TreeLateralUnitArchDegrees, nameof(TreeLateralUnitArchDegrees));
            RequireProbability(TreeSecondaryProximalFraction, nameof(TreeSecondaryProximalFraction));
            RequireFinitePositive(TreeSecondaryProprioceptiveLengthM, nameof(TreeSecondaryProprioceptiveLengthM));
            if (TreeSecondaryMaturationStrainAmplitude > 0f && (TreeMainlineModuleMode == global::Agro.TreeMainlineModuleMode.Off
                || TreeSecondaryProximalFraction <= 0f))
                throw new ArgumentException("Secondary erection requires a real or automatic mainline model and a positive frozen proximal fraction.");
            if (TreeMainlineModuleMode != global::Agro.TreeMainlineModuleMode.Off
                && TreeMainlineModuleMode != global::Agro.TreeMainlineModuleMode.RealAxillary
                && TreeMainlineModuleMode != global::Agro.TreeMainlineModuleMode.Auto)
                throw new ArgumentOutOfRangeException(nameof(TreeMainlineModuleMode));
            if (UsesRealAxillaryMainline)
            {
                if (TreeMainlineModuleMode == global::Agro.TreeMainlineModuleMode.RealAxillary)
                    RequireFinitePositive(TreeMainlineTrainingHeightM, nameof(TreeMainlineTrainingHeightM));
                RequireFinitePositive(TreeMainlineReferenceInclinationDegrees, nameof(TreeMainlineReferenceInclinationDegrees));
                RequireProbability(TreeMainlineOpeningFraction, nameof(TreeMainlineOpeningFraction));
                if (!HasRealAxillaryForm
                    || TreeMainlineOpeningFraction <= 0f || TreeMainlineOpeningFraction >= 1f
                    || TreeMainlineReferenceInclinationDegrees + TreeLateralUnitArchDegrees >= 180f)
                    throw new ArgumentException("Real axillary mainline modules require an ungrafted, single-stem distichous sympodial tree and a finite bounded opening program.");
            }
            RequireFiniteNonNegative(TreeUnitLightDeviationDegrees, nameof(TreeUnitLightDeviationDegrees));
            RequireProbability(TreeAxillaryFlushProbability, nameof(TreeAxillaryFlushProbability));
            if (TreeLateralUnitArchDegrees > 90f || TreeUnitLightDeviationDegrees > 45f)
                throw new ArgumentException("Growth-unit arch must not exceed 90 degrees and its light-turn budget must not exceed 45 degrees.");
            RequireFiniteNonNegative(TreeLeafDeclinationDegrees, nameof(TreeLeafDeclinationDegrees));
            RequireFiniteNonNegative(TreeLeafDeclinationVariationDegrees, nameof(TreeLeafDeclinationVariationDegrees));
            if (TreeLeafDeclinationDegrees > 85f || TreeLeafDeclinationVariationDegrees > 45f
                || TreeLeafDeclinationDegrees > 0f && (TreeLeafDeclinationDegrees - TreeLeafDeclinationVariationDegrees <= 0f
                    || TreeLeafDeclinationDegrees + TreeLeafDeclinationVariationDegrees > 85f))
                throw new ArgumentException("Enabled blade declination and its variation must remain between zero and 85 degrees below horizontal.");
            RequireProbability(TreeMatureLeaderExtensionFraction, nameof(TreeMatureLeaderExtensionFraction));
            RequireProbability(TreeMatureBranchExtensionFraction, nameof(TreeMatureBranchExtensionFraction));
            RequireProbability(TreeMatureFrameworkPitchFraction, nameof(TreeMatureFrameworkPitchFraction));
            if (TreeApicalControlEndYears > 0f && TreeApicalControlEndYears <= TreeApicalControlStartYears)
                throw new ArgumentException("Crown maturation must end after it starts, or end at zero to disable the response.");
            RequireFinitePositive(TreeShortShootLength, nameof(TreeShortShootLength));
            RequireFiniteNonNegative(TreeFloweringAgeYears, nameof(TreeFloweringAgeYears));
            RequireFiniteNonNegative(TreeGraftHeight, nameof(TreeGraftHeight));
            RequireFiniteNonNegative(TreeClearTrunkHeight, nameof(TreeClearTrunkHeight));
            RequireProbability(TreeBranchProbability, nameof(TreeBranchProbability));
            RequireProbability(TreeShortShootBranchProbability, nameof(TreeShortShootBranchProbability));
            RequireFiniteNonNegative(TreeShortShootShadeDiebackDays, nameof(TreeShortShootShadeDiebackDays));
            RequireProbability(TreeShortShootShadeLightFraction, nameof(TreeShortShootShadeLightFraction));
            RequireProbability(TreeShortShootShadeRelativeFraction, nameof(TreeShortShootShadeRelativeFraction));
            RequireProbability(TreePhototropicWeight, nameof(TreePhototropicWeight));
            RequireProbability(TreeBudBreakSeason, nameof(TreeBudBreakSeason));
            RequireProbability(TreeGrowthEndSeason, nameof(TreeGrowthEndSeason));
            if (TreeStemCount is < 1 or > 8 || TreeMaximumBranchOrder is < 1 or > 6 || TreeFlowersPerCluster is < 1 or > 32 || TreeMaleFlowersPerCluster is < 0 or > 8 || TreeShortShootNodes is < 1 or > 8)
                throw new ArgumentOutOfRangeException(nameof(TreeStemCount), "Tree stem counts must be 1..8, branch orders 1..6, short-shoot nodes 1..8, fertile cluster counts 1..32 and male cluster counts 0..8.");
            if (TreeGrowthEndSeason <= TreeBudBreakSeason || TreeGrowthEndSeason >= SeasonalSenescence
                || TreeGraftHeight >= Height || TreeClearTrunkHeight > Height)
                throw new ArgumentException("Trees require bud break before growth cessation before senescence, and a graft below mature height.");
        }

        static void RequireColor(Vector3 value, string name)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
                value.X < 0f || value.X > 1f || value.Y < 0f || value.Y > 1f || value.Z < 0f || value.Z > 1f)
            {
                throw new ArgumentOutOfRangeException(name, value, "RGB components must be finite and between zero and one.");
            }
        }

        static void RequireSeasonalPhases(SeasonalPhase value, string name)
        {
            const SeasonalPhase allPhases = SeasonalPhase.PreFlower | SeasonalPhase.Flowering |
                SeasonalPhase.PostFlower | SeasonalPhase.ResetPending;
            if (value == 0 || (value & ~allPhases) != 0)
                throw new ArgumentOutOfRangeException(name, value, "At least one valid seasonal phase is required.");
        }

        RequireFiniteNonNegative(FloweringStartAgeHours, nameof(FloweringStartAgeHours));
        RequireFiniteNonNegative(BoltingAgeHours, nameof(BoltingAgeHours));
        RequireFinitePositive(RootRadiusTaper, nameof(RootRadiusTaper));
        RequireProbability(RootRadiusTaper, nameof(RootRadiusTaper));
        RequireFinitePositive(RosetteNodeDistance, nameof(RosetteNodeDistance));
        RequireProbability(HerbaceousBranchProbability, nameof(HerbaceousBranchProbability));
        if (RosetteMaximumNodes < 1)
            throw new ArgumentOutOfRangeException(nameof(RosetteMaximumNodes), "Rosette node limit must be positive.");
        if (HerbaceousBranchMaximumOrder is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(HerbaceousBranchMaximumOrder), "Shoot orders must be between 1 and 16.");
        if (!float.IsFinite(ShootsGravitropicSetPointAngle) || ShootsGravitropicSetPointAngle < 0f ||
            ShootsGravitropicSetPointAngle > MathF.PI * 0.5f)
        {
            throw new ArgumentOutOfRangeException(nameof(ShootsGravitropicSetPointAngle),
                ShootsGravitropicSetPointAngle, "The set-point angle must lie between zero and pi/2 radians.");
        }
        if (!float.IsFinite(ShootsGravitropicRatePerHour) || ShootsGravitropicRatePerHour < 0f || ShootsGravitropicRatePerHour > 1f)
            throw new ArgumentOutOfRangeException(nameof(ShootsGravitropicRatePerHour), ShootsGravitropicRatePerHour, "The rate must lie between zero and one per hour.");
        if (CrownMaximumNodes < 0)
            throw new ArgumentOutOfRangeException(nameof(CrownMaximumNodes), CrownMaximumNodes, "Value must be non-negative.");
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
        if (MaximumFlowersPerCrown < 1)
            throw new ArgumentOutOfRangeException(nameof(MaximumFlowersPerCrown), MaximumFlowersPerCrown, "A crown must be allowed at least one flower axis.");

        FlowerSettings.Validate();
        RequireFiniteNonNegative(BasalScapeEmergenceMaxTiltDegrees, nameof(BasalScapeEmergenceMaxTiltDegrees));
        if (BasalScapeEmergenceMaxTiltDegrees > 75f)
            throw new ArgumentOutOfRangeException(nameof(BasalScapeEmergenceMaxTiltDegrees));
        RequireFiniteNonNegative(FlowerStemSegmentGrowthDurationVarianceHours, nameof(FlowerStemSegmentGrowthDurationVarianceHours));
        if (FlowerStemSegmentGrowthDurationVarianceHours >= FlowerSettings.FlowerStemSegmentGrowthDurationHours)
            throw new ArgumentOutOfRangeException(nameof(FlowerStemSegmentGrowthDurationVarianceHours), FlowerStemSegmentGrowthDurationVarianceHours, "Variance must be smaller than the mean flower stem growth duration.");
        RequireFiniteNonNegative(BudBloomDurationVarianceHours, nameof(BudBloomDurationVarianceHours));
        if (BudBloomDurationVarianceHours >= FlowerSettings.BudBloomDurationHours)
            throw new ArgumentOutOfRangeException(nameof(BudBloomDurationVarianceHours), BudBloomDurationVarianceHours, "Variance must be smaller than the mean flower bud bloom duration.");

        if (!Enum.IsDefined(ContactDeformationMode)) throw new ArgumentOutOfRangeException(nameof(ContactDeformationMode));
        RequireFiniteNonNegative(PetioleContactBendDegrees, nameof(PetioleContactBendDegrees));
        RequireFinitePositive(LeafExpansionRateScale, nameof(LeafExpansionRateScale));
        if (!float.IsFinite(VegetativeGrowthBaseTemperatureC))
            throw new ArgumentOutOfRangeException(nameof(VegetativeGrowthBaseTemperatureC));
        if (!float.IsFinite(VegetativeGrowthFullTemperatureC)
            || VegetativeGrowthFullTemperatureC <= VegetativeGrowthBaseTemperatureC)
            throw new ArgumentOutOfRangeException(nameof(VegetativeGrowthFullTemperatureC),
                "The full-rate temperature must be finite and greater than the base temperature.");
        RequireFiniteNonNegative(VegetativeGrowthLightReferenceWhM2, nameof(VegetativeGrowthLightReferenceWhM2));
        RequireFiniteNonNegative(ConstructionEnergyPerDryGram, nameof(ConstructionEnergyPerDryGram));
        if (ConstructionEnergyPerDryGram > 0f)
        {
            RequireFinitePositive(DensityDryStem, nameof(DensityDryStem));
            RequireFinitePositive(DensityDryWood, nameof(DensityDryWood));
        }
        if (PetioleContactBendDegrees > 90f) throw new ArgumentOutOfRangeException(nameof(PetioleContactBendDegrees));
        if (ContactDeformationMode == ContactDeformationMode.BroadLeafV1 && LeafSegmentCount != 1)
            throw new ArgumentException("BroadLeafV1 requires one biological blade organ; use None for segmented leaves.");
        RequireFinitePositive(WoodElasticModulus, nameof(WoodElasticModulus));
        RequireFinitePositive(GreenElasticModulus, nameof(GreenElasticModulus));
        RequireFinitePositive(BasalGreenElasticModulus, nameof(BasalGreenElasticModulus));
        RequireFiniteNonNegative(BasalStiffnessAttenuationPerSegment, nameof(BasalStiffnessAttenuationPerSegment));
        RequireFinitePositive(PetioleElasticModulus, nameof(PetioleElasticModulus));
        RequireFinitePositive(FlowerOrganElasticModulus, nameof(FlowerOrganElasticModulus));
        RequireFinitePositive(FlowerPedicelElasticModulus, nameof(FlowerPedicelElasticModulus));
        RequireFiniteNonNegative(FlowerScapeElasticModulus, nameof(FlowerScapeElasticModulus));
        RequireFiniteNonNegative(LeafMembraneElasticModulus, nameof(LeafMembraneElasticModulus));
        RequireFiniteNonNegative(LeafBendingElasticModulus, nameof(LeafBendingElasticModulus));
        RequireFiniteNonNegative(LeafMechanicalThickness, nameof(LeafMechanicalThickness));
        if (!float.IsFinite(LeafPoissonRatio) || LeafPoissonRatio <= -1 || LeafPoissonRatio >= .5f)
            throw new ArgumentOutOfRangeException(nameof(LeafPoissonRatio));

        RequireFinitePositive(LeafMaximumAgeHours, nameof(LeafMaximumAgeHours));
        if (!float.IsFinite(DroughtSenescenceOnset) || DroughtSenescenceOnset < 0f || DroughtSenescenceOnset >= 1f)
            throw new ArgumentOutOfRangeException(nameof(DroughtSenescenceOnset), DroughtSenescenceOnset, "The onset must lie in [0, 1).");
        RequireFiniteNonNegative(CanopyAdjustmentHalfTimeDays, nameof(CanopyAdjustmentHalfTimeDays));
        RequireFiniteNonNegative(DeadLeafAttachmentHours, nameof(DeadLeafAttachmentHours));
        RequireProbability(AutumnLeafLossFraction, nameof(AutumnLeafLossFraction));
        RequireProbability(AutumnLeafLossStartSeason, nameof(AutumnLeafLossStartSeason));
        RequireFinitePositive(LeafSenescenceStressDuration, nameof(LeafSenescenceStressDuration));
        RequireFiniteNonNegative(LeafSenescenceStressDurationVar, nameof(LeafSenescenceStressDurationVar));
        if (LeafSenescenceStressDurationVar >= LeafSenescenceStressDuration)
            throw new ArgumentOutOfRangeException(nameof(LeafSenescenceStressDurationVar), LeafSenescenceStressDurationVar, "Variance must be smaller than the mean stress senescence duration.");
        RequireColor(YoungLeafColor, nameof(YoungLeafColor));
        RequireColor(SenescentLeafColor, nameof(SenescentLeafColor));
        RequireSeasonalPhases(CrownBudActivationPeriod, nameof(CrownBudActivationPeriod));
        RequireProbability(CrownBudActivationProbabilityPerSeason, nameof(CrownBudActivationProbabilityPerSeason));
        RequireFinitePositive(CrownBudBreakWindowHours, nameof(CrownBudBreakWindowHours));
        RequireFinitePositive(MinimumInternodeLength, nameof(MinimumInternodeLength));
        RequireFinitePositive(CrownBudRadius, nameof(CrownBudRadius));
        RequireFiniteNonNegative(CrownNodeHeightAboveSoil, nameof(CrownNodeHeightAboveSoil));
        if (!float.IsFinite(CrownPitch) || CrownPitch < 0f || CrownPitch > MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(CrownPitch), CrownPitch, "Crown pitch must be between zero and pi radians.");
        RequireFiniteNonNegative(HerbaceousGrowthRateScale, nameof(HerbaceousGrowthRateScale));
        RequireFinitePositive(ShootMaximumRadius, nameof(ShootMaximumRadius));
        if (ShootMaximumRadius < AboveGroundAgent.InitialRadius)
            throw new ArgumentOutOfRangeException(nameof(ShootMaximumRadius), ShootMaximumRadius, "Maximum radius cannot be smaller than the initial shoot radius.");
        if (CrownBudRadius < AboveGroundAgent.InitialRadius || CrownBudRadius > ShootMaximumRadius)
            throw new ArgumentOutOfRangeException(nameof(CrownBudRadius), CrownBudRadius, "Crown bud radius must be between the initial and maximum shoot radii.");

        if (!float.IsFinite(PetioleReorientationMaximumAngle) || PetioleReorientationMaximumAngle < 0f || PetioleReorientationMaximumAngle > MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(PetioleReorientationMaximumAngle), PetioleReorientationMaximumAngle, "Maximum angle must be between zero and pi radians.");
        if (!float.IsFinite(PetioleReorientationStepAngle) || PetioleReorientationStepAngle < 0f || PetioleReorientationStepAngle > PetioleReorientationMaximumAngle)
            throw new ArgumentOutOfRangeException(nameof(PetioleReorientationStepAngle), PetioleReorientationStepAngle, "Step angle must be between zero and the maximum angle.");
        if (PetioleSegmentCount < 1)
            throw new ArgumentOutOfRangeException(nameof(PetioleSegmentCount), PetioleSegmentCount, "At least one petiole segment is required.");

        RequireSeasonalPhases(RhizomeBudActivationPeriod, nameof(RhizomeBudActivationPeriod));
        RequireProbability(RhizomeBasalBudActivationProbabilityPerHour, nameof(RhizomeBasalBudActivationProbabilityPerHour));
        RequireProbability(RhizomeAxillaryBudActivationProbabilityPerHour, nameof(RhizomeAxillaryBudActivationProbabilityPerHour));
        RequireProbability(RhizomeAxillaryBudActivationProbabilityPerSeason, nameof(RhizomeAxillaryBudActivationProbabilityPerSeason));
        RequireFinitePositive(RhizomeBudBreakWindowHours, nameof(RhizomeBudBreakWindowHours));
        RequireFiniteNonNegative(RhizomeObstacleDeflectionAngle, nameof(RhizomeObstacleDeflectionAngle));
        if (RhizomeObstacleDeflectionAngle > MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(RhizomeObstacleDeflectionAngle), RhizomeObstacleDeflectionAngle, "Deflection angle cannot exceed pi.");
        RequireFiniteNonNegative(RhizomeGrowthRateScale, nameof(RhizomeGrowthRateScale));
        if (RhizomeAxillaryBudsPerSegment is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(RhizomeAxillaryBudsPerSegment), RhizomeAxillaryBudsPerSegment, "The supported range is zero to 32.");
        if (InitialBasalRhizomeBuds is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(InitialBasalRhizomeBuds), InitialBasalRhizomeBuds, "The supported range is zero to 32.");
        if (RhizomeMaximumSegmentDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(RhizomeMaximumSegmentDepth), RhizomeMaximumSegmentDepth, "Depth must be non-negative.");
        RequireFinitePositive(RhizomeSegmentLength, nameof(RhizomeSegmentLength));
        RequireFinitePositive(RhizomeSegmentRadius, nameof(RhizomeSegmentRadius));
        if (RhizomeSegmentRadius < AboveGroundAgent.InitialRadius)
            throw new ArgumentOutOfRangeException(nameof(RhizomeSegmentRadius), RhizomeSegmentRadius, "Target radius cannot be smaller than the initial shoot radius.");
        RequireFinitePositive(RhizomeBudRadius, nameof(RhizomeBudRadius));
        if (RhizomeBudRadius < AboveGroundAgent.InitialRadius || RhizomeBudRadius > RhizomeSegmentRadius)
            throw new ArgumentOutOfRangeException(nameof(RhizomeBudRadius), RhizomeBudRadius, "Rhizome bud radius must be between the initial and target rhizome radii.");
    }
}
