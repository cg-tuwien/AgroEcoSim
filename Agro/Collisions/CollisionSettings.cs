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

    static int IntFromEnv(string name, int fallback, int min, int max)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= min && value <= max ? value : fallback;
    static float FloatFromEnv(string name, float fallback, float min, float max)
        => float.TryParse(Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max ? value : fallback;
}
