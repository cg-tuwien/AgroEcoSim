namespace Agro;

public sealed record LeafPhysiology
{
    // Basic photosynthesis parameters (placeholders; tune per species)
    public float Amax_umolCO2_m2_s { get; init; } = 18f;  // maximum assimilation rate
    public float QuantumYield { get; init; } = 0.05f;    // initial slope
    public float Rd_umolCO2_m2_s { get; init; } = 1.5f;  // dark respiration

    // Stomatal / water stress (0..1)
    public float WaterStressSensitivity { get; init; } = 0.7f;

    // Leaf traits relevant for growth and energy
    public float SpecificLeafArea_m2_kg { get; init; } = 15f; // SLA
    public float NitrogenContent_g_m2 { get; init; } = 1.5f;

    public static LeafPhysiology Default => new();

    /// <summary>
    /// Simple light-response model (non-rectangular hyperbola style).
    /// Inputs: PPFD (umol photons m^-2 s^-1), stress 0..1.
    /// Output: net photosynthesis (umol CO2 m^-2 s^-1).
    /// </summary>
    public float NetPhotosynthesis(float ppfd, float stress01)
    {
        stress01 = Math.Clamp(stress01, 0f, 1f);
        var stressFactor = 1f - WaterStressSensitivity * stress01;

        var Amax = Amax_umolCO2_m2_s * stressFactor;
        var alpha = QuantumYield * stressFactor;

        // Rectangular hyperbola: A = (alpha*I*Amax)/(alpha*I + Amax) - Rd
        var gross = (alpha * ppfd * Amax) / Math.Max(1e-6f, (alpha * ppfd + Amax));
        return gross - Rd_umolCO2_m2_s;
    }
}