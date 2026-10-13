using System;

namespace Agro.Collisions;

/// <summary>
/// Numerical controls for constrained growth. Bounded searches suspend uncertified movement;
/// candidate budgets never authorize unchecked penetration. These are model defaults.
/// </summary>
public static class CollisionSettings
{
    /// <summary>Solid-contact tolerance in metres. Thin foliage uses its own smaller skin.</summary>
    public const float ContactTolerance = 0.0001f;
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoCollisions") == null;
    public static bool ObstaclePass { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoObstaclePass") == null;
    public static bool SoilPass { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoilPass") == null;
    public static bool InterPlantPass { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoInterPlantPass") == null;

    /// <summary>Alternating solid and foliage relaxation sweeps before validated rollback.</summary>
    public static int HardRounds { get; set; } = IntFromEnv("AGRO_CollisionHardRounds", 8, 1, 16);
    /// <summary>Per-sweep correction cap, independent of the cumulative joint limit.</summary>
    public static float MaxCorrectionDegrees { get; set; } = FloatFromEnv("AGRO_MaxCorrectionDeg", 5f, .1f, 45f);
    internal static float MaxCorrectionRadians => MaxCorrectionDegrees * MathF.PI / 180f;
    /// <summary>Total elastic rotation from the transported rest frame, including gravity.</summary>
    public static float MaxElasticBendDegrees { get; set; } = FloatFromEnv("AGRO_MaxElasticBendDeg", 25f, .5f, 179f);
    internal static float MaxElasticBendRadians => MaxElasticBendDegrees * MathF.PI / 180f;
    /// <summary>Clearance proof radius; movement invalidates the proof conservatively.</summary>
    public static float ClearanceProbeDistance { get; set; } = .05f;
    /// <summary>Use descendant centres of mass for blades, petioles, rhizomes and floral axes.</summary>
    public static bool VectorBending { get; set; } = Environment.GetEnvironmentVariable("AGRO_ScalarBending") == null;

    // Compatibility properties for old callers and diagnostic tools. The combined contact
    // solver does not use pruning, cutting, plastic rest-frame changes, or dropped soft work.
    public static float ObstructionHoursBeforeCut { get; set; } = 96f;
    public static int ObstructionStepsBeforeCut { get; set; } = 24;
    public static int SoftCandidateBudget { get; set; } = 32768;
    public static int SoftRounds { get; set; } = 2;
    public static bool PruneByCrown { get; set; } = false;
    public static float PlasticFraction { get; set; } = 0f;
    public static float MaxPlasticDegrees { get; set; } = 0f;
    internal static float MaxPlasticRadians => MaxPlasticDegrees * MathF.PI / 180f;
    public static float MaxSoftCorrectionDegrees { get; set; } = 2f;
    internal static float MaxSoftCorrectionRadians => MaxSoftCorrectionDegrees * MathF.PI / 180f;
    public static int NeighbourRefreshInterval { get; set; } = 1;
    public static int InterPlantContactBudget { get; set; } = 20000;
    public static bool DistributeBladeContacts { get; set; } = true;
    public static int ContactsPerPlantPerRound { get; set; } = 4096;

    // Soft contact engine (SimulationRequest.ContactEngine = Soft). None of these are read by
    // the certified engine, so flipping them cannot change a certified run.
    /// <summary>Target separation beyond the physical radii, metres. Foliage settles about this far
    /// apart, and it must exceed the largest relative approach two organs can make in one tick
    /// (sub-millimetre at 4 h), so every approaching pair is remembered before it can cross.</summary>
    public static float FoliageSkin { get; set; } = FloatFromEnv("AGRO_FoliageSkinMm", 1f, .1f, 5f) * .001f;
    /// <summary>Fraction of the measured overlap removed per tick. Below one damps Jacobi overshoot
    /// when several contacts load one joint; the remainder is handled next tick.</summary>
    public static float ContactRelaxation { get; set; } = FloatFromEnv("AGRO_ContactRelaxation", .7f, .2f, 1f);
    /// <summary>Largest witness displacement one tick's foliage correction may cause at a joint,
    /// metres. Keeps a remembered pair from being carried through a thin sheet in one tick; zero disables.</summary>
    public static float MaxCorrectionDisplacement { get; set; } = FloatFromEnv("AGRO_MaxCorrectionDisplacementMm", 1f, 0f, 50f) * .001f;
    /// <summary>Residual overlap beyond which a foliage pair is counted as a deep penetration, metres. Diagnostic only.</summary>
    public static float DeepPenetrationThreshold { get; set; } = FloatFromEnv("AGRO_DeepPenetrationMm", 5f, .1f, 100f) * .001f;
    /// <summary>Ticks a contact memory outlives its last touch before the remembered side is forgotten.</summary>
    public static int ContactMemoryGraceEpochs { get; set; } = IntFromEnv("AGRO_ContactMemoryGrace", 1, 0, 8);
    /// <summary>Push rounds against obstacles and soil per tick in the soft engine.</summary>
    public static int SoftHardPushRounds { get; set; } = IntFromEnv("AGRO_SoftHardPushRounds", 4, 1, 16);
    /// <summary>Clip this tick's elongation of an organ still inside a solid after the pushes.</summary>
    public static bool SolidElongationClip { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSolidElongationClip") == null;

    // Damping of the soft engine's Jacobi response. Each is a separate switch so it can be A/B
    // tested; together they are what stops dense foliage from alternating between two poses
    // every tick (see SOFT_CONTACTS.md, "Shaking").
    /// <summary>Cap a joint's summed turn at the largest single contribution it received, so k
    /// contacts resolving the same motion count once instead of k times. Without it the loop gain
    /// of a joint under k contacts is 0.7k, and from k = 2 the correction overshoots and alternates.</summary>
    public static bool SoftJointSumRule { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoftJointSumRule") == null;
    /// <summary>Inflate the broad-phase boxes by the skin, so a pair resting inside the band stays a
    /// candidate and keeps its side memory instead of dropping out and re-entering as a new side.</summary>
    public static bool SoftBoundsSkin { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoftBoundsSkin") == null;
    /// <summary>Fade the push to zero at the edge of the skin band (x(2-x) of the band deficit) rather
    /// than pushing at full gain until the pair is fully separated and then letting gravity pull it
    /// back in: a pair settles at the band asymptotically instead of bouncing across it.</summary>
    public static bool SoftBandLaw { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoftBandLaw") == null;
    /// <summary>Ramp the tangential slide in over the second quarter of the deep-penetration
    /// threshold instead of switching it on at half of it: a pair hovering at the threshold no
    /// longer slides one tick and not the next.</summary>
    public static bool SoftSlideRamp { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoftSlideRamp") == null;
    /// <summary>Under the soft engine, let petals and unloaded joints relax their elastic contact turns
    /// toward their rest frame at the gravity response, as every loaded joint does. Without it a petal's
    /// turn, and that of a joint with no gravitational moment, was permanent under the default
    /// deformation mode, so pressed flower heads stayed pressed.</summary>
    public static bool SoftRelaxUnloaded { get; set; } = Environment.GetEnvironmentVariable("AGRO_NoSoftRelaxUnloaded") == null;
    /// <summary>Blades shorter than this, metres, meet other organs as capsules of their own width
    /// under the soft engine. A newborn leaf slice a tenth of a millimetre long still carries its
    /// full 46-triangle template, and a cluster of them at one node - every one within the band of
    /// every other - was the costliest thing in a Carex tick: 1-2 ms a pair for organs that cannot
    /// yet touch anything in a way the template resolves.</summary>
    public static float SoftTinyLaminaLength { get; set; } = FloatFromEnv("AGRO_SoftTinyLaminaMm", 5f, 0f, 100f) * .001f;
    /// <summary>Candidate triangle pairs beyond which a blade-to-blade distance is sampled rather than
    /// taken exactly (see FoliageGeometry.DistanceSampled); zero keeps every query exact.</summary>
    public static int SoftExactPairBudget { get; set; } = IntFromEnv("AGRO_SoftExactPairBudget", 256, 0, 1 << 20);
    /// <summary>Time constant, hours, of the low-pass on a remembered contact's witness normal; zero
    /// follows the witness exactly. Face and edge witnesses alternate on sliding blades and turn the
    /// push direction by a right angle between ticks; the filter turns it over a few ticks.</summary>
    public static float SoftNormalFilterHours { get; set; } = FloatFromEnv("AGRO_SoftNormalFilterHours", 12f, 0f, 240f);

    static int IntFromEnv(string name, int fallback, int min, int max)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= min && value <= max ? value : fallback;
    static float FloatFromEnv(string name, float fallback, float min, float max)
        => float.TryParse(Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max ? value : fallback;
}
