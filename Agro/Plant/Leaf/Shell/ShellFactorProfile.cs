namespace Agro.Mechanics;

/// <summary>
/// Immutable, shared symbolic factorization for one shell and a set of DOFs
/// whose Newton increments are always zero. Build once per boundary-condition
/// profile, never per leaf or contact. No numeric factors or accepted state are
/// retained here. Other DOFs may be temporarily fixed without rebuilding.
/// </summary>
public sealed class ShellFactorProfile
{
    internal readonly BacShell Shell;
    internal readonly bool[] Prescribed;
    internal readonly ShellFactorPattern Pattern;
    public int PrescribedDofCount { get; }
    /// <summary>Shared array payloads including the copied mask; excludes CLR headers.</summary>
    public long ArrayPayloadBytes => Pattern.ArrayPayloadBytes + Prescribed.Length;

    public ShellFactorProfile(BacShell shell, ReadOnlySpan<bool> prescribedDofs)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (prescribedDofs.Length != shell.DofCount)
            throw new ArgumentException("A factor profile requires a complete prescribed-DOF mask.", nameof(prescribedDofs));
        Shell = shell; Prescribed = prescribedDofs.ToArray();
        foreach (var prescribed in Prescribed) if (prescribed) ++PrescribedDofCount;
        Pattern = new(shell, Prescribed);
    }

    internal void ValidateMask(ReadOnlySpan<bool> fixedDofs)
    {
        if (fixedDofs.Length != Prescribed.Length)
            throw new ArgumentException("Incorrect fixed-DOF mask size.", nameof(fixedDofs));
        for (var i = 0; i < Prescribed.Length; ++i)
            if (Prescribed[i] && !fixedDofs[i])
                throw new ArgumentException("A DOF prescribed by the factor profile was freed. Use a compatible profile or the unrestricted solver.", nameof(fixedDofs));
    }
}
