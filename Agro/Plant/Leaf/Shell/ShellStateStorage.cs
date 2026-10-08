using System.Buffers.Binary;
using System.Numerics;

namespace Agro.Mechanics;

public enum ShellDirectorStorage : byte { Float64, Float32 }
public enum ShellPositionStorage : byte { Float64, Fixed48 }

/// <summary>A stable contact witness, not a pressure or a cached triangle mesh.</summary>
public readonly record struct ShellContactWitness(ulong OtherOrganId, int LocalFeature,
    int OtherFeature, Vector3 SeparationSide, uint Kind);

/// <summary>
/// Validate the exact decoded candidate, including equilibrium and all applicable
/// strain/contact criteria. Returning false preserves the previous stored state.
/// </summary>
public delegate bool ShellStateValidator(ReadOnlySpan<double> candidate,
    ReadOnlySpan<double> rodAngles, ReadOnlySpan<ShellContactWitness> witnesses);

/// <summary>
/// Bounded experimental shell/rod storage. Rod angles retain all double-precision
/// bits. Optional fixed-point positions and float directors must pass the caller's
/// equilibrium and geometric validation after rounding. This is not a
/// live checkpoint or scene format, and is not connected to modes 0 or 1.
/// One buffer owns the state, witnesses and explicitly charged local bookkeeping;
/// no proportional state is moved into a global cache. Solver scratch is separate.
/// </summary>
public sealed class ShellStateStorage
{
    public const int TargetBytes = 4 * 1024;
    public const int MaximumBytes = 8 * 1024;
    public const int HeaderBytes = 128;
    public const int WitnessBytes = 32;
    public const int PetioleDofs = PetioleBeam.DofCount;
    // Conservative x64 CLR allowance for this object, the array header/alignment,
    // and an owning reference. Actual allocation is measured in the storage probe.
    public const int OwnershipBytes = 96;
    readonly byte[] data;
    readonly int vertices, edges, witnessCapacity, bookkeepingBytes;
    readonly ShellDirectorStorage directorStorage;
    readonly ShellPositionStorage positionStorage;
    readonly int positionUnitExponent;
    int PositionBytes => checked(vertices * 3 * (positionStorage == ShellPositionStorage.Float64 ? 8 : 6));
    int DirectorBytes => edges * (directorStorage == ShellDirectorStorage.Float64 ? 8 : 4);
    int RodOffset => HeaderBytes + PositionBytes + DirectorBytes;
    int WitnessOffset => RodOffset + PetioleDofs * sizeof(double);
    int BookkeepingOffset => WitnessOffset + witnessCapacity * WitnessBytes;
    public int DofCount => 3 * vertices + edges;
    public int PersistentBytes => OwnershipBytes + ((data.Length + 7) & ~7);
    public bool MeetsTarget => PersistentBytes <= TargetBytes;
    public ulong Revision => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(24));
    public int WitnessCount => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32));
    public bool HasState => Revision != 0;
    /// <summary>Explicitly budgeted per-pair caches/pending metadata, included in the image.</summary>
    public Span<byte> Bookkeeping => data.AsSpan(BookkeepingOffset, bookkeepingBytes);

    public static int RequiredBytes(int vertexCount, int edgeCount, int contactCapacity,
        int bookkeepingBytes, ShellDirectorStorage directorStorage = ShellDirectorStorage.Float64,
        ShellPositionStorage positionStorage = ShellPositionStorage.Float64)
    {
        if (vertexCount < 3 || edgeCount < 3 || contactCapacity < 0 || bookkeepingBytes < 0
            || directorStorage is not (ShellDirectorStorage.Float64 or ShellDirectorStorage.Float32)
            || positionStorage is not (ShellPositionStorage.Float64 or ShellPositionStorage.Fixed48))
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        return checked(OwnershipBytes + ((HeaderBytes + vertexCount * 3 * (positionStorage == ShellPositionStorage.Float64 ? 8 : 6)
            + edgeCount * (directorStorage == ShellDirectorStorage.Float64 ? 8 : 4)
            + PetioleDofs * sizeof(double) + contactCapacity * WitnessBytes + bookkeepingBytes + 7) & ~7));
    }

    public ShellStateStorage(int vertexCount, int edgeCount, int contactCapacity, int bookkeepingBytes,
        ReadOnlySpan<byte> profileSha256, ulong leafId, ulong petioleId,
        ShellDirectorStorage directorStorage = ShellDirectorStorage.Float64,
        ShellPositionStorage positionStorage = ShellPositionStorage.Float64, int positionUnitExponent = -48)
    {
        var bytes = RequiredBytes(vertexCount, edgeCount, contactCapacity, bookkeepingBytes, directorStorage, positionStorage);
        if (bytes > MaximumBytes)
            throw new ArgumentException($"Shell/rod state needs {bytes} bytes; the limit is {MaximumBytes} including witnesses and bookkeeping.");
        if (profileSha256.Length != 32) throw new ArgumentException("Expected a SHA-256 profile identity.", nameof(profileSha256));
        // Signed 48-bit local coordinates. The default quantum 2^-48 m gives
        // [-0.5, 0.5) m range and <= 2^-49 m rounding error. No clamping, origin
        // drift or automatic rescaling is allowed; larger profiles choose an
        // explicit power-of-two unit or fall back to Float64 after validation.
        if (positionUnitExponent < -1022 || positionUnitExponent > 976)
            throw new ArgumentOutOfRangeException(nameof(positionUnitExponent));
        vertices = vertexCount; edges = edgeCount; witnessCapacity = contactCapacity;
        this.bookkeepingBytes = bookkeepingBytes; this.directorStorage = directorStorage;
        this.positionStorage = positionStorage; this.positionUnitExponent = positionUnitExponent;
        data = new byte[bytes - OwnershipBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x33545342); // BST3: optional fixed positions; internal storage only.
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), vertices);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), edges);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), witnessCapacity);
        data[16] = (byte)directorStorage;
        data[17] = PetioleDofs;
        data[18] = (byte)positionStorage;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), bookkeepingBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(40), leafId);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(48), petioleId);
        profileSha256.CopyTo(data.AsSpan(56, 32));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(88), positionUnitExponent);
    }

    /// <summary>
    /// Store only after validation of the decoded candidate. Excess witnesses are
    /// rejected, never truncated. Scratch belongs to the worker, not this organ.
    /// The validator must not mutate this object; concurrent access is unsupported.
    /// </summary>
    public bool TryUpdate(ReadOnlySpan<double> state, ReadOnlySpan<double> rodAngles,
        ReadOnlySpan<ShellContactWitness> witnesses, Span<double> scratch, ShellStateValidator validate)
    {
        if (state.Length != DofCount || rodAngles.Length != PetioleDofs || scratch.Length < DofCount)
            throw new ArgumentException("Incorrect state or scratch size.");
        if (state.Overlaps(scratch) || rodAngles.Overlaps(scratch))
            throw new ArgumentException("Candidate scratch must not alias the caller's state.");
        ArgumentNullException.ThrowIfNull(validate);
        if (witnesses.Length > witnessCapacity || Revision == ulong.MaxValue) return false;
        foreach (var v in state) if (!double.IsFinite(v)) return false;
        foreach (var v in rodAngles) if (!double.IsFinite(v)) return false;
        foreach (var w in witnesses)
            if (w.LocalFeature < 0 || w.OtherFeature < 0 || !float.IsFinite(w.SeparationSide.X)
                || !float.IsFinite(w.SeparationSide.Y) || !float.IsFinite(w.SeparationSide.Z)) return false;
        var candidate = scratch[..DofCount]; state.CopyTo(candidate);
        if (positionStorage == ShellPositionStorage.Fixed48)
            for (var i = 0; i < 3 * vertices; ++i)
            {
                var encoded = Math.Round(Math.ScaleB(candidate[i], -positionUnitExponent), MidpointRounding.ToEven);
                if (!double.IsFinite(encoded) || encoded < -140737488355328.0 || encoded > 140737488355327.0) return false;
                candidate[i] = Math.ScaleB(encoded, positionUnitExponent);
            }
        if (directorStorage == ShellDirectorStorage.Float32)
            for (var i = 3 * vertices; i < DofCount; ++i)
            {
                var rounded = (float)candidate[i];
                if (!float.IsFinite(rounded)) return false;
                candidate[i] = rounded;
            }
        if (!validate(candidate, rodAngles, witnesses)) return false;
        var offset = HeaderBytes;
        for (var i = 0; i < 3 * vertices; ++i)
            if (positionStorage == ShellPositionStorage.Float64)
            { BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(offset), candidate[i]); offset += 8; }
            else
            {
                var encoded = (long)Math.ScaleB(candidate[i], -positionUnitExponent);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), unchecked((uint)encoded));
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4), unchecked((ushort)(encoded >> 32)));
                offset += 6;
            }
        for (var i = 3 * vertices; i < DofCount; ++i)
            if (directorStorage == ShellDirectorStorage.Float64)
            { BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(offset), candidate[i]); offset += 8; }
            else { BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), (float)candidate[i]); offset += 4; }
        for (var i = 0; i < PetioleDofs; ++i)
            BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(RodOffset + 8 * i), rodAngles[i]);
        data.AsSpan(WitnessOffset, WitnessBytes * witnessCapacity).Clear();
        for (var i = 0; i < witnesses.Length; ++i)
        {
            var s = data.AsSpan(WitnessOffset + i * WitnessBytes, WitnessBytes); var w = witnesses[i];
            BinaryPrimitives.WriteUInt64LittleEndian(s, w.OtherOrganId);
            BinaryPrimitives.WriteInt32LittleEndian(s[8..], w.LocalFeature);
            BinaryPrimitives.WriteInt32LittleEndian(s[12..], w.OtherFeature);
            BinaryPrimitives.WriteSingleLittleEndian(s[16..], w.SeparationSide.X);
            BinaryPrimitives.WriteSingleLittleEndian(s[20..], w.SeparationSide.Y);
            BinaryPrimitives.WriteSingleLittleEndian(s[24..], w.SeparationSide.Z);
            BinaryPrimitives.WriteUInt32LittleEndian(s[28..], w.Kind);
        }
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), witnesses.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), Revision + 1);
        return true;
    }

    public void Read(Span<double> state, Span<double> rodAngles, Span<ShellContactWitness> witnesses)
    {
        if (!HasState) throw new InvalidOperationException("No validated state has been stored.");
        if (state.Length != DofCount || rodAngles.Length != PetioleDofs || witnesses.Length < WitnessCount)
            throw new ArgumentException("Incorrect output size.");
        var offset = HeaderBytes;
        for (var i = 0; i < 3 * vertices; ++i)
            if (positionStorage == ShellPositionStorage.Float64)
            { state[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset)); offset += 8; }
            else
            {
                var low = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
                var high = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));
                var encoded = ((long)high << 48 | (long)low << 16) >> 16;
                state[i] = Math.ScaleB(encoded, positionUnitExponent); offset += 6;
            }
        for (var i = 3 * vertices; i < DofCount; ++i)
            if (directorStorage == ShellDirectorStorage.Float64)
            { state[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset)); offset += 8; }
            else { state[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)); offset += 4; }
        for (var i = 0; i < PetioleDofs; ++i)
            rodAngles[i] = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(RodOffset + 8 * i));
        for (var i = 0; i < WitnessCount; ++i)
        {
            var s = data.AsSpan(WitnessOffset + i * WitnessBytes, WitnessBytes);
            witnesses[i] = new(BinaryPrimitives.ReadUInt64LittleEndian(s), BinaryPrimitives.ReadInt32LittleEndian(s[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(s[12..]), new(BinaryPrimitives.ReadSingleLittleEndian(s[16..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[20..]), BinaryPrimitives.ReadSingleLittleEndian(s[24..])),
                BinaryPrimitives.ReadUInt32LittleEndian(s[28..]));
        }
    }
}
