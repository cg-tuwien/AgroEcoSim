using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro.Mechanics;

public enum ShellControlStorageStatus : byte
{
    Stored, Restored, InvalidInput, ReconstructionFailed, ShapeMismatch, ValidationRejected
}

/// <summary>Numerical capture gates, not measured biological tolerances.</summary>
public readonly record struct ShellControlCaptureTolerance(double ForceNewtons, double PositionMetres, double DirectorRadians)
{
    internal void Validate()
    {
        if (!(ForceNewtons > 0) || PositionMetres < 0 || DirectorRadians < 0
            || !double.IsFinite(ForceNewtons + PositionMetres + DirectorRadians))
            throw new ArgumentOutOfRangeException(nameof(ShellControlCaptureTolerance));
    }
}

public readonly record struct ShellControlStorageResult(ShellControlStorageStatus Status,
    ShellReconstructionResult? Reconstruction = null, double MaximumPositionError = double.PositiveInfinity,
    double MaximumDirectorError = double.PositiveInfinity);

/// <summary>
/// Experimental bounded control/rod state tied to one shared immutable rest profile.
/// Every capture cold-reconstructs the fine shell and rejects a different branch;
/// every restore rechecks equilibrium and the caller's geometry criteria. Forces,
/// obstacles and the shared profile are external inputs, never hidden per-organ
/// caches. This is not a scene/archive/live-checkpoint format or production V2.
/// </summary>
public sealed class ShellControlStateStorage
{
    public const int HeaderBytes = 128;
    public const int OwnershipBytes = 96;
    public const int WitnessBytes = ShellStateStorage.WitnessBytes;
    public const int PetioleDofs = ShellStateStorage.PetioleDofs;
    readonly ShellControlReconstruction profile;
    readonly byte[] data;
    readonly int capacity, bookkeepingBytes;
    int RodOffset => HeaderBytes + profile.SavedCoordinatePayloadBytes;
    int WitnessOffset => RodOffset + PetioleDofs * sizeof(double);
    int BookkeepingOffset => WitnessOffset + capacity * WitnessBytes;
    public int PersistentBytes => OwnershipBytes + data.Length;
    public bool MeetsTarget => PersistentBytes <= ShellStateStorage.TargetBytes;
    public ulong Revision => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(24));
    public bool HasState => Revision != 0;
    public int WitnessCount => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32));
    public ulong LeafId => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(40));
    public ulong PetioleId => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(48));
    public ReadOnlySpan<byte> ProfileSha256 => data.AsSpan(56, 32);
    /// <summary>Reserved local metadata, charged to persistence. Callbacks must not mutate it.</summary>
    public Span<byte> Bookkeeping => data.AsSpan(BookkeepingOffset, bookkeepingBytes);

    public static int RequiredBytes(int savedCoordinates, int contactCapacity, int bookkeepingBytes)
    {
        if (savedCoordinates < 1 || contactCapacity < 0 || bookkeepingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(savedCoordinates));
        return checked(OwnershipBytes + ((HeaderBytes + savedCoordinates * sizeof(double)
            + PetioleDofs * sizeof(double) + contactCapacity * WitnessBytes + bookkeepingBytes + 7) & ~7));
    }

    /// <param name="profileSha256">Identity of the complete immutable reconstruction assets;
    /// caller must derive it from the actual assets, not merely the species name.</param>
    public ShellControlStateStorage(ShellControlReconstruction profile, int contactCapacity, int bookkeepingBytes,
        ReadOnlySpan<byte> profileSha256, ulong leafId, ulong petioleId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var bytes = RequiredBytes(profile.SavedCoordinateCount, contactCapacity, bookkeepingBytes);
        if (bytes > ShellStateStorage.MaximumBytes) throw new ArgumentException("Control/rod state exceeds the 8 KiB ceiling.");
        if (profileSha256.Length != 32) throw new ArgumentException("Expected a complete SHA-256 profile identity.");
        this.profile = profile; capacity = contactCapacity; this.bookkeepingBytes = bookkeepingBytes;
        data = new byte[bytes - OwnershipBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x31435342); // BSC1: internal only.
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), profile.SavedCoordinateCount);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), profile.FineDofCount);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), capacity);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), bookkeepingBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40), leafId);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(48), petioleId);
        profileSha256.CopyTo(data.AsSpan(56, 32));
    }

    /// <summary>
    /// Commit controls, exact double rod angles and all witnesses only after cold
    /// reconstruction, branch comparison and caller validation. Failure leaves the
    /// stored revision and contents unchanged. No full fine snapshot is retained.
    /// </summary>
    public ShellControlStorageResult TryUpdate(ReadOnlySpan<double> acceptedState, ReadOnlySpan<double> forces,
        ReadOnlySpan<double> rodAngles, ReadOnlySpan<ShellContactWitness> witnesses,
        ShellControlCaptureTolerance tolerance, ShellControlReconstruction.Workspace workspace, ShellStateValidator validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        if (rodAngles.Length != PetioleDofs) throw new ArgumentException("Incorrect rod state size.");
        if (TouchesWorkspace(MemoryMarshal.AsBytes(rodAngles), workspace)
            || TouchesWorkspace(MemoryMarshal.AsBytes(witnesses), workspace))
            throw new ArgumentException("Capture inputs alias mutable reconstruction storage.");
        if (witnesses.Length > capacity || Revision == ulong.MaxValue || !Finite(rodAngles) || !Valid(witnesses))
            return new(ShellControlStorageStatus.InvalidInput);
        var result = profile.CertifyCapture(acceptedState, forces, tolerance, workspace);
        if (result.Status != ShellControlStorageStatus.Stored) return result;
        if (!validate(workspace.State, rodAngles, witnesses)) return result with { Status = ShellControlStorageStatus.ValidationRejected };
        for (var i = 0; i < profile.SavedCoordinateCount; ++i)
            BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(HeaderBytes + 8 * i), workspace.CapturedControls[i]);
        for (var i = 0; i < PetioleDofs; ++i) BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(RodOffset + 8 * i), rodAngles[i]);
        data.AsSpan(WitnessOffset, capacity * WitnessBytes).Clear();
        for (var i = 0; i < witnesses.Length; ++i) WriteWitness(data.AsSpan(WitnessOffset + i * WitnessBytes), witnesses[i]);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), witnesses.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), Revision + 1);
        return result;
    }

    /// <summary>
    /// Restore using current external loads/contact configuration. All outputs stay
    /// unchanged on failure. Caller validation must include applicable rod, support,
    /// self-contact and obstacle tests; a plane fixture alone is not a collision certificate.
    /// </summary>
    public ShellControlStorageResult TryRestore(ReadOnlySpan<double> forces, double maximumForceResidual,
        Span<double> acceptedState, Span<double> rodAngles, Span<ShellContactWitness> witnesses,
        ShellControlReconstruction.Workspace workspace, ShellStateValidator validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        if (workspace.Owner != profile || acceptedState.Length != profile.FineDofCount || rodAngles.Length != PetioleDofs
            || TouchesWorkspace(MemoryMarshal.AsBytes(acceptedState), workspace)
            || TouchesWorkspace(MemoryMarshal.AsBytes(rodAngles), workspace)
            || TouchesWorkspace(MemoryMarshal.AsBytes(witnesses), workspace)
            || witnesses.Length < WitnessCount || forces.Overlaps(acceptedState) || forces.Overlaps(rodAngles)
            || acceptedState.Overlaps(rodAngles) || acceptedState.Overlaps(workspace.State)
            || acceptedState.Overlaps(workspace.Solver.Forces) || acceptedState.Overlaps(workspace.Solver.SpringTargets)
            || acceptedState.Overlaps(workspace.Solver.SpringStiffness)
            || rodAngles.Overlaps(workspace.State) || rodAngles.Overlaps(workspace.Solver.Forces)
            || rodAngles.Overlaps(workspace.Solver.SpringTargets) || rodAngles.Overlaps(workspace.Solver.SpringStiffness)
            || MemoryMarshal.AsBytes(acceptedState).Overlaps(MemoryMarshal.AsBytes(witnesses))
            || MemoryMarshal.AsBytes(rodAngles).Overlaps(MemoryMarshal.AsBytes(witnesses))
            || MemoryMarshal.AsBytes(forces).Overlaps(MemoryMarshal.AsBytes(witnesses)))
            throw new ArgumentException("Incorrect or aliased restore buffers.");
        if (!HasState) return new(ShellControlStorageStatus.InvalidInput);
        for (var i = 0; i < profile.SavedCoordinateCount; ++i)
            workspace.CapturedControls[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(HeaderBytes + 8 * i));
        Span<double> rods = stackalloc double[PetioleDofs];
        // Bounded by the 8 KiB persistent ceiling, not by an unbounded contact list.
        Span<ShellContactWitness> contacts = stackalloc ShellContactWitness[WitnessCount];
        for (var i = 0; i < PetioleDofs; ++i) rods[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(RodOffset + 8 * i));
        for (var i = 0; i < contacts.Length; ++i) contacts[i] = ReadWitness(data.AsSpan(WitnessOffset + i * WitnessBytes));
        var decoded = profile.Reconstruct(workspace.CapturedControls, forces, maximumForceResidual, workspace.State, workspace);
        if (decoded.Status != ShellReconstructionStatus.Converged) return new(ShellControlStorageStatus.ReconstructionFailed, decoded);
        if (!validate(workspace.State, rods, contacts)) return new(ShellControlStorageStatus.ValidationRejected, decoded);
        workspace.State.CopyTo(acceptedState); rods.CopyTo(rodAngles); contacts.CopyTo(witnesses);
        return new(ShellControlStorageStatus.Restored, decoded);
    }

    // Reconstruction writes its control/state scratch, loads and clamp mask before
    // the final validation callback. An aliased input could silently change during
    // capture; an aliased output would violate rollback even when restore fails.
    static bool TouchesWorkspace(ReadOnlySpan<byte> bytes, ShellControlReconstruction.Workspace w)
        => bytes.Overlaps(MemoryMarshal.AsBytes(w.State.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Controls.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.CapturedControls.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Predictor.Displacements.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Solver.Forces.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Solver.Fixed.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Solver.SpringTargets.AsSpan()))
        || bytes.Overlaps(MemoryMarshal.AsBytes(w.Solver.SpringStiffness.AsSpan()));

    static bool Finite(ReadOnlySpan<double> values)
    { foreach (var x in values) if (!double.IsFinite(x)) return false; return true; }
    static bool Valid(ReadOnlySpan<ShellContactWitness> witnesses)
    {
        foreach (var w in witnesses)
            if (w.LocalFeature < 0 || w.OtherFeature < 0 || !float.IsFinite(w.SeparationSide.X)
                || !float.IsFinite(w.SeparationSide.Y) || !float.IsFinite(w.SeparationSide.Z)) return false;
        return true;
    }
    static void WriteWitness(Span<byte> s, ShellContactWitness w)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(s, w.OtherOrganId);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], w.LocalFeature); BinaryPrimitives.WriteInt32LittleEndian(s[12..], w.OtherFeature);
        BinaryPrimitives.WriteSingleLittleEndian(s[16..], w.SeparationSide.X); BinaryPrimitives.WriteSingleLittleEndian(s[20..], w.SeparationSide.Y);
        BinaryPrimitives.WriteSingleLittleEndian(s[24..], w.SeparationSide.Z); BinaryPrimitives.WriteUInt32LittleEndian(s[28..], w.Kind);
    }
    static ShellContactWitness ReadWitness(ReadOnlySpan<byte> s) => new(BinaryPrimitives.ReadUInt64LittleEndian(s),
        BinaryPrimitives.ReadInt32LittleEndian(s[8..]), BinaryPrimitives.ReadInt32LittleEndian(s[12..]),
        new Vector3(BinaryPrimitives.ReadSingleLittleEndian(s[16..]), BinaryPrimitives.ReadSingleLittleEndian(s[20..]),
            BinaryPrimitives.ReadSingleLittleEndian(s[24..])), BinaryPrimitives.ReadUInt32LittleEndian(s[28..]));
}
