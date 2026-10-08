namespace Agro.Mechanics;

/// <summary>
/// Frictionless infinite-plane fixture and contact energy primitive. The admissible
/// side has dot(position, Normal) > Offset. Offset includes the desired geometric
/// clearance, independently of material thickness. This is not finite-triangle CCD.
/// </summary>
public readonly struct ShellPlaneContact
{
    public ShellVector Normal { get; }
    public double Offset { get; }
    public double ActivationDistance { get; }
    /// <summary>Numerical coefficient: N/m for the legacy nodal barrier, N/m³
    /// for a surface barrier. Neither is a measured tissue modulus.</summary>
    public double BarrierStiffness { get; }
    public bool UsesRestArea { get; }

    public ShellPlaneContact(ShellVector normal, double offset, double activationDistance, double barrierStiffness)
        : this(normal, offset, activationDistance, barrierStiffness, false) { }

    /// <summary>Barrier energy density integrated over material surface area.
    /// The coefficient is N/m³; Evaluate returns J/m² before quadrature weighting.
    /// This removes the legacy dependence on vertex count for uniform gaps. It is
    /// an infinite-plane primitive, not the full two-body Convergent IPC model.</summary>
    public static ShellPlaneContact SurfaceBarrier(ShellVector normal, double offset,
        double activationDistance, double stiffnessPerArea)
        => new(normal, offset, activationDistance, stiffnessPerArea, true);

    ShellPlaneContact(ShellVector normal, double offset, double activationDistance, double barrierStiffness, bool usesRestArea)
    {
        var norm = normal.Norm;
        if (!(norm > 0) || !(activationDistance > 0) || !(barrierStiffness > 0)
            || !double.IsFinite(norm + offset + activationDistance + barrierStiffness))
            throw new ArgumentOutOfRangeException(nameof(normal));
        Normal = normal * (1 / norm); Offset = offset / norm;
        ActivationDistance = activationDistance; BarrierStiffness = barrierStiffness;
        UsesRestArea = usesRestArea;
    }

    /// <summary>C2 log barrier. Minus the first derivative is the nodal normal
    /// force, or force per material area for SurfaceBarrier; the solver applies
    /// material-area quadrature to its energy and both derivatives together.</summary>
    public double Evaluate(double gap, out double derivative, out double secondDerivative)
    {
        derivative = secondDerivative = 0;
        if (!(gap > 0)) return double.PositiveInfinity;
        if (gap >= ActivationDistance) return 0;
        var ratio = gap / ActivationDistance; var delta = ratio - 1;
        var logarithm = Math.Log(ratio); var k = BarrierStiffness;
        derivative = -k * ActivationDistance * (2 * delta * logarithm + delta * delta / ratio);
        secondDerivative = -k * (2 * logarithm + 4 * delta / ratio - delta * delta / (ratio * ratio));
        return -k * ActivationDistance * ActivationDistance * delta * delta * logarithm;
    }
}
