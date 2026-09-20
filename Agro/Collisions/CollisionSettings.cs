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

    static int IntFromEnv(string name, int fallback, int min, int max)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= min && value <= max ? value : fallback;
    static float FloatFromEnv(string name, float fallback, float min, float max)
        => float.TryParse(Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= min && value <= max ? value : fallback;
}
