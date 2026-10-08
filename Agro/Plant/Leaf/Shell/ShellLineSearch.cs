namespace Agro.Mechanics;

/// <summary>Local roundoff fallback, not an equilibrium or collision certificate.</summary>
public static class ShellLineSearch
{
    internal const double MaximumRoundoffStep = 1e-7;
    /// <summary>
    /// Hager--Zhang (2005), equations 4.1--4.2: approximate Wolfe with
    /// delta=1e-4 and sigma=.9. This restricted use additionally requires a
    /// tiny nondimensional step and at least a halving of the full free-force
    /// residual. The caller supplies an absolute energy-error allowance in J,
    /// certifies the complete motion, and still checks final equilibrium.
    /// Zero allowance disables the fallback. No convergence proof for the
    /// original conjugate-gradient algorithm is implied for this Newton solver.
    /// </summary>
    public static bool AcceptRoundoffStep(double energy, double candidate, double energyAllowance,
        double initialSlope, double candidateSlope, double force, double candidateForce,
        double scaledStep)
    {
        if (!double.IsFinite(energy) || !double.IsFinite(candidate)
            || !double.IsFinite(energyAllowance) || energyAllowance <= 0
            || !double.IsFinite(initialSlope) || initialSlope >= 0
            || !double.IsFinite(candidateSlope) || !double.IsFinite(force) || force <= 0
            || !double.IsFinite(candidateForce) || candidateForce < 0
            || !double.IsFinite(scaledStep) || scaledStep <= 0 || scaledStep > MaximumRoundoffStep)
            return false;
        return candidate - energy <= energyAllowance && candidateForce <= .5 * force
            && candidateSlope >= .9 * initialSlope && candidateSlope <= (2e-4 - 1) * initialSlope;
    }
}
