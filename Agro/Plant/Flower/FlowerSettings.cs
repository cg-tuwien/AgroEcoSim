using System;

namespace Agro
{
    /// <summary>
    /// Defines the dimensions, topology, timing, and phenology of one
    /// inflorescence. Lengths and radii are in metres; durations are in hours.
    /// </summary>
    public sealed class FlowerSettings
    {
        /// <summary>
        /// Length of each upright segment that forms the base of the
        /// inflorescence before it branches.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerBaseSegmentLength { get; init; } = 0.01f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerBaseSegmentLength"/>
        /// when an individual base segment is created.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the base-segment length.</value>
        public float FlowerBaseSegmentLengthVariance { get; init; }

        /// <summary>
        /// Radius of each upright segment that forms the base of the
        /// inflorescence.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerBaseSegmentRadius { get; init; } = 0.0005f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerBaseSegmentRadius"/>
        /// when an individual base segment is created.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the base-segment radius.</value>
        public float FlowerBaseSegmentRadiusVariance { get; init; }

        /// <summary>
        /// Length of a non-basal inflorescence branch segment.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerBranchSegmentLength { get; init; } = 0.01f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerBranchSegmentLength"/>.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the branch-segment length.</value>
        public float FlowerBranchSegmentLengthVariance { get; init; }

        /// <summary>
        /// Radius of a non-basal inflorescence branch segment.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerBranchSegmentRadius { get; init; } = 0.0005f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerBranchSegmentRadius"/>.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the branch-segment radius.</value>
        public float FlowerBranchSegmentRadiusVariance { get; init; }

        /// <summary>
        /// Time for a flower-base or branch segment to reach its target size.
        /// </summary>
        /// <value>Hours; must be greater than zero.</value>
        public float FlowerStemSegmentGrowthDurationHours { get; init; } = 120f;

        /// <summary>
        /// Number of upright base segments, including the first segment that
        /// rises from the crown. A value of one creates a single culm.
        /// </summary>
        /// <value>An integer of at least one.</value>
        public int FlowerBaseSegmentCount { get; init; } = 1;

        /// <summary>
        /// Maximum number of non-basal branch generations before a terminal
        /// flower is forced.
        /// </summary>
        /// <value>Zero creates flowers directly from the completed base; otherwise a non-negative integer.</value>
        public int FlowerBranchMaximumDepth { get; init; } = 1;

        /// <summary>
        /// Number of lateral inflorescence branches attempted at each branch
        /// node.
        /// </summary>
        /// <value>Zero creates an unbranched inflorescence; otherwise a non-negative integer.</value>
        public int FlowerBranchLateralsPerNode { get; init; } = 2;

        /// <summary>
        /// Number of individual flowers emitted at one terminal flower site.
        /// </summary>
        /// <value>An integer of at least one. Values above one create a flower cluster.</value>
        public int FlowersPerCluster { get; init; } = 1;

        /// <summary>
        /// Whether branch and terminal-flower decisions follow the configured
        /// deterministic topology instead of continuing a single random chain.
        /// </summary>
        /// <value>Use true for a repeatable, species-defined inflorescence architecture.</value>
        public bool UseDeterministicBranching { get; init; } = true;

        /// <summary>
        /// Whether a completed branch node also emits a flower before its
        /// lateral branches are considered.
        /// </summary>
        /// <value>Use false for strictly terminal flowers.</value>
        public bool CreateFlowersAtInternodes { get; init; } = true;

        /// <summary>
        /// Whether an internodal flower is supported by its own short flower
        /// stem instead of attaching directly at the branch node.
        /// </summary>
        /// <value>Only used when <see cref="CreateFlowersAtInternodes"/> is true.</value>
        public bool InternodeFlowersHaveStems { get; init; } = true;

        /// <summary>
        /// Maximum probability that a lateral at the greatest branch depth
        /// becomes a terminal flower rather than another branch. The runtime
        /// scales this linearly from zero at the branch base to this value at
        /// the maximum depth.
        /// </summary>
        /// <value>From zero to one.</value>
        public float TerminalFlowerProbabilityAtMaximumBranchDepth { get; init; } = 1f;

        /// <summary>
        /// Number of lateral flower-bearing branches attempted from an eligible
        /// flower-base segment.
        /// </summary>
        /// <value>Zero disables lateral branches on the flower base; otherwise a non-negative integer.</value>
        public int FlowerBaseLateralCount { get; init; }

        /// <summary>
        /// Probability that an eligible flower-base segment creates its
        /// configured lateral branches.
        /// </summary>
        /// <value>
        /// From zero to one. Set to zero to suppress basal laterals; also set
        /// <see cref="FlowerBaseLateralActiveTopFraction"/> above zero before
        /// this probability can take effect.
        /// </value>
        public float FlowerBaseLateralProbability { get; init; }

        /// <summary>
        /// Upper fraction of the flower base in which basal laterals may form.
        /// A smooth ramp prevents lateral branches from appearing abruptly at
        /// the first eligible segment.
        /// </summary>
        /// <value>From zero to one. Zero disables basal laterals; one permits them along the full base.</value>
        public float FlowerBaseLateralActiveTopFraction { get; init; }

        /// <summary>
        /// Inclination of ordinary inflorescence laterals away from the axis of
        /// their supporting stem.
        /// </summary>
        /// <value>Degrees from zero to 90. Zero is upright; 90 is horizontal.</value>
        public float FlowerLateralInclinationDegrees { get; init; } = 90f;

        /// <summary>
        /// Azimuthal offset of the first ordinary inflorescence lateral around
        /// its supporting stem.
        /// </summary>
        /// <value>Degrees. Other laterals are evenly spaced from this offset.</value>
        public float FlowerLateralAzimuthOffsetDegrees { get; init; }

        /// <summary>
        /// Inclination of laterals created directly from the upright flower
        /// base.
        /// </summary>
        /// <value>Degrees from zero to 90. Zero is upright; 90 is horizontal.</value>
        public float FlowerBaseLateralInclinationDegrees { get; init; } = 90f;

        /// <summary>
        /// Azimuthal offset of the first lateral created from the flower base.
        /// </summary>
        /// <value>Degrees. Other base laterals are evenly spaced from this offset.</value>
        public float FlowerBaseLateralAzimuthOffsetDegrees { get; init; }

        /// <summary>
        /// Length of the pedicel between an individual flower and its
        /// inflorescence branch.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerPedicelLength { get; init; } = 0.02f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerPedicelLength"/>.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the pedicel length.</value>
        public float FlowerPedicelLengthVariance { get; init; }

        /// <summary>
        /// Radius of an individual flower pedicel.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float FlowerPedicelRadius { get; init; } = 0.0025f;

        /// <summary>
        /// Symmetric variation applied to <see cref="FlowerPedicelRadius"/>.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the pedicel radius.</value>
        public float FlowerPedicelRadiusVariance { get; init; }

        /// <summary>
        /// Number of serial segments used to represent one pedicel.
        /// </summary>
        /// <value>An integer of at least one. One creates a single straight pedicel.</value>
        public int FlowerPedicelSegmentCount { get; init; } = 1;

        /// <summary>
        /// Length of a petal or petal-like floral scale.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float PetalLength { get; init; } = 0.005f;

        /// <summary>
        /// Symmetric variation applied to <see cref="PetalLength"/>.
        /// </summary>
        /// <value>Metres; must be non-negative and smaller than the petal length.</value>
        public float PetalLengthVariance { get; init; }

        /// <summary>
        /// Half-width of a petal or petal-like floral scale.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float PetalRadius { get; init; } = 0.0025f;

        /// <summary>
        /// Number of petals or petal-like scales emitted when a bud opens.
        /// </summary>
        /// <value>An integer of at least one. Use one for an unbranched spikelet representation.</value>
        public int PetalCount { get; init; } = 5;

        /// <summary>
        /// Time for a petal or pedicel to reach its target size.
        /// </summary>
        /// <value>Hours; must be greater than zero.</value>
        public float PetalAndPedicelGrowthDurationHours { get; init; } = 72f;

        /// <summary>
        /// Symmetric variation applied to
        /// <see cref="PetalAndPedicelGrowthDurationHours"/>.
        /// </summary>
        /// <value>Hours; must be non-negative and smaller than the mean duration.</value>
        public float PetalAndPedicelGrowthDurationVarianceHours { get; init; }

        /// <summary>
        /// Length of a closed flower bud.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float BudLength { get; init; } = 0.0075f;

        /// <summary>
        /// Radius of a closed flower bud.
        /// </summary>
        /// <value>Metres; must be greater than zero.</value>
        public float BudRadius { get; init; } = 0.005f;

        /// <summary>
        /// Time from bud creation until the bud reaches bloom size.
        /// </summary>
        /// <value>Hours; must be greater than zero.</value>
        public float BudBloomDurationHours { get; init; } = 168f;

        /// <summary>
        /// Maximum age of every organ in an inflorescence before floral
        /// senescence begins.
        /// </summary>
        /// <value>Hours; must be greater than zero.</value>
        public float FlowerMaximumAgeHours { get; init; } = 720f;

        /// <summary>
        /// Maximum angular spread of individual pedicels within a flower
        /// cluster.
        /// </summary>
        /// <value>Degrees from zero to 180. Zero keeps all flowers collinear.</value>
        public float FlowerClusterMaximumSpreadDegrees { get; init; }

        /// <summary>
        /// Whether the tip of a branched flower base creates reduced leaves.
        /// </summary>
        /// <value>Use true only for species with visible bracts or leaves on the inflorescence base.</value>
        public bool CreateLeavesOnFlowerBase { get; init; }

        /// <summary>
        /// Length of a reduced leaf or bract on the flower base.
        /// </summary>
        /// <value>Metres; must be greater than zero when <see cref="CreateLeavesOnFlowerBase"/> is true.</value>
        public float FlowerBaseLeafLength { get; init; } = 0.01f;

        /// <summary>
        /// Half-width of a reduced leaf or bract on the flower base.
        /// </summary>
        /// <value>Metres; must be greater than zero when <see cref="CreateLeavesOnFlowerBase"/> is true.</value>
        public float FlowerBaseLeafRadius { get; init; } = 0.005f;

        /// <summary>
        /// Length of the petiole supporting a reduced leaf or bract on the
        /// flower base.
        /// </summary>
        /// <value>Metres; must be greater than zero when <see cref="CreateLeavesOnFlowerBase"/> is true.</value>
        public float FlowerBaseLeafPetioleLength { get; init; } = 0.005f;

        /// <summary>
        /// Radius of the petiole supporting a reduced leaf or bract on the
        /// flower base.
        /// </summary>
        /// <value>Metres; must be greater than zero when <see cref="CreateLeavesOnFlowerBase"/> is true.</value>
        public float FlowerBaseLeafPetioleRadius { get; init; } = 0.001f;

        /// <summary>
        /// Phenology and colour model used by flower organs.
        /// </summary>
        /// <value>Provide species-specific curves when floral colour changes with age or senescence.</value>
        public LeafPhenology FlowerPhenology { get; init; } = new();

        /// <summary>
        /// Duration of visible petal senescence after the supporting flower has
        /// retired.
        /// </summary>
        /// <value>Hours; must be greater than zero.</value>
        public float PetalSenescenceDurationHours { get; init; } = 168f;

        internal void Validate()
        {
            static void RequirePositive(float value, string name)
            {
                if (!float.IsFinite(value) || value <= 0f)
                    throw new ArgumentOutOfRangeException(name, value, "Value must be finite and greater than zero.");
            }

            static void RequireNonNegative(float value, string name)
            {
                if (!float.IsFinite(value) || value < 0f)
                    throw new ArgumentOutOfRangeException(name, value, "Value must be finite and non-negative.");
            }

            static void RequireProbability(float value, string name)
            {
                if (!float.IsFinite(value) || value < 0f || value > 1f)
                    throw new ArgumentOutOfRangeException(name, value, "Probability must be between zero and one.");
            }

            static void RequireVariance(float variance, float mean, string name)
            {
                RequireNonNegative(variance, name);
                if (variance >= mean)
                    throw new ArgumentOutOfRangeException(name, variance, "Variance must be smaller than its mean so sampled values remain positive.");
            }

            static void RequireInclination(float value, string name)
            {
                if (!float.IsFinite(value) || value < 0f || value > 90f)
                    throw new ArgumentOutOfRangeException(name, value, "Inclination must be between zero and 90 degrees.");
            }

            static void RequireFinite(float value, string name)
            {
                if (!float.IsFinite(value))
                    throw new ArgumentOutOfRangeException(name, value, "Value must be finite.");
            }

            RequirePositive(FlowerBaseSegmentLength, nameof(FlowerBaseSegmentLength));
            RequireVariance(FlowerBaseSegmentLengthVariance, FlowerBaseSegmentLength, nameof(FlowerBaseSegmentLengthVariance));
            RequirePositive(FlowerBaseSegmentRadius, nameof(FlowerBaseSegmentRadius));
            RequireVariance(FlowerBaseSegmentRadiusVariance, FlowerBaseSegmentRadius, nameof(FlowerBaseSegmentRadiusVariance));
            RequirePositive(FlowerBranchSegmentLength, nameof(FlowerBranchSegmentLength));
            RequireVariance(FlowerBranchSegmentLengthVariance, FlowerBranchSegmentLength, nameof(FlowerBranchSegmentLengthVariance));
            RequirePositive(FlowerBranchSegmentRadius, nameof(FlowerBranchSegmentRadius));
            RequireVariance(FlowerBranchSegmentRadiusVariance, FlowerBranchSegmentRadius, nameof(FlowerBranchSegmentRadiusVariance));
            RequirePositive(FlowerStemSegmentGrowthDurationHours, nameof(FlowerStemSegmentGrowthDurationHours));

            if (FlowerBaseSegmentCount < 1)
                throw new ArgumentOutOfRangeException(nameof(FlowerBaseSegmentCount), FlowerBaseSegmentCount, "At least one flower-base segment is required.");
            if (FlowerBranchMaximumDepth < 0)
                throw new ArgumentOutOfRangeException(nameof(FlowerBranchMaximumDepth), FlowerBranchMaximumDepth, "Depth must be non-negative.");
            if (FlowerBranchLateralsPerNode < 0)
                throw new ArgumentOutOfRangeException(nameof(FlowerBranchLateralsPerNode), FlowerBranchLateralsPerNode, "Lateral count must be non-negative.");
            if (FlowersPerCluster < 1)
                throw new ArgumentOutOfRangeException(nameof(FlowersPerCluster), FlowersPerCluster, "At least one flower per cluster is required.");
            if (FlowerBaseLateralCount < 0)
                throw new ArgumentOutOfRangeException(nameof(FlowerBaseLateralCount), FlowerBaseLateralCount, "Lateral count must be non-negative.");
            RequireProbability(TerminalFlowerProbabilityAtMaximumBranchDepth, nameof(TerminalFlowerProbabilityAtMaximumBranchDepth));
            RequireProbability(FlowerBaseLateralProbability, nameof(FlowerBaseLateralProbability));
            RequireProbability(FlowerBaseLateralActiveTopFraction, nameof(FlowerBaseLateralActiveTopFraction));
            RequireInclination(FlowerLateralInclinationDegrees, nameof(FlowerLateralInclinationDegrees));
            RequireInclination(FlowerBaseLateralInclinationDegrees, nameof(FlowerBaseLateralInclinationDegrees));
            RequireFinite(FlowerLateralAzimuthOffsetDegrees, nameof(FlowerLateralAzimuthOffsetDegrees));
            RequireFinite(FlowerBaseLateralAzimuthOffsetDegrees, nameof(FlowerBaseLateralAzimuthOffsetDegrees));

            RequirePositive(FlowerPedicelLength, nameof(FlowerPedicelLength));
            RequireVariance(FlowerPedicelLengthVariance, FlowerPedicelLength, nameof(FlowerPedicelLengthVariance));
            RequirePositive(FlowerPedicelRadius, nameof(FlowerPedicelRadius));
            RequireVariance(FlowerPedicelRadiusVariance, FlowerPedicelRadius, nameof(FlowerPedicelRadiusVariance));
            if (FlowerPedicelSegmentCount < 1)
                throw new ArgumentOutOfRangeException(nameof(FlowerPedicelSegmentCount), FlowerPedicelSegmentCount, "At least one flower pedicel segment is required.");

            RequirePositive(PetalLength, nameof(PetalLength));
            RequireVariance(PetalLengthVariance, PetalLength, nameof(PetalLengthVariance));
            RequirePositive(PetalRadius, nameof(PetalRadius));
            if (PetalCount < 1)
                throw new ArgumentOutOfRangeException(nameof(PetalCount), PetalCount, "At least one petal or floral scale is required.");
            RequirePositive(PetalAndPedicelGrowthDurationHours, nameof(PetalAndPedicelGrowthDurationHours));
            RequireVariance(PetalAndPedicelGrowthDurationVarianceHours, PetalAndPedicelGrowthDurationHours, nameof(PetalAndPedicelGrowthDurationVarianceHours));
            RequirePositive(BudLength, nameof(BudLength));
            RequirePositive(BudRadius, nameof(BudRadius));
            RequirePositive(BudBloomDurationHours, nameof(BudBloomDurationHours));
            RequirePositive(FlowerMaximumAgeHours, nameof(FlowerMaximumAgeHours));
            if (!float.IsFinite(FlowerClusterMaximumSpreadDegrees) || FlowerClusterMaximumSpreadDegrees < 0f || FlowerClusterMaximumSpreadDegrees > 180f)
                throw new ArgumentOutOfRangeException(nameof(FlowerClusterMaximumSpreadDegrees), FlowerClusterMaximumSpreadDegrees, "Maximum cluster spread must be between zero and 180 degrees.");

            RequirePositive(FlowerBaseLeafLength, nameof(FlowerBaseLeafLength));
            RequirePositive(FlowerBaseLeafRadius, nameof(FlowerBaseLeafRadius));
            RequirePositive(FlowerBaseLeafPetioleLength, nameof(FlowerBaseLeafPetioleLength));
            RequirePositive(FlowerBaseLeafPetioleRadius, nameof(FlowerBaseLeafPetioleRadius));
            RequirePositive(PetalSenescenceDurationHours, nameof(PetalSenescenceDurationHours));
        }
    }

    /// <summary>
    /// Botanical fruit types represented by <see cref="FruitSettings"/>.
    /// </summary>
    public enum FruitType : byte
    {
        /// <summary>A simple berry with a fleshy pericarp.</summary>
        Berry,

        /// <summary>A fruit with substantial tissue derived from non-ovary organs.</summary>
        Accessory,

        /// <summary>A cluster of fruitlets derived from one flower with multiple carpels.</summary>
        Aggregate,

        /// <summary>An accessory fruit with a pome morphology.</summary>
        Pome,

        /// <summary>A stone fruit with a lignified inner pericarp.</summary>
        Drupe
    }
}
