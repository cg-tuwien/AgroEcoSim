using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utils;

namespace Agro.DelunayTerrain;

[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{X} {Y} {Z}")]
public readonly struct Vector3int : IEquatable<Vector3int>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3int(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3int(Vector3 v)
    {
        X = (int)Math.Round(v.X * 1e4f);
        Y = (int)Math.Round(v.Y * 1e4f);
        Z = (int)Math.Round(v.Z * 1e4f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsZero() => Math.Abs(X) == 0 && Math.Abs(Y) == 0 && Math.Abs(Z) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BigInteger LengthSquared() => X * (BigInteger)X + Y * (BigInteger)Y + Z * (BigInteger)Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3int operator +(in Vector3int a, in Vector3int b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3int operator -(in Vector3int a, in Vector3int b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3int operator /(in Vector3int a, in int s) => new(a.X / s, a.Y / s, a.Z / s);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Vector3int other) => X == other.X && Y == other.Y && Z == other.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Hyperface other && Equals(other);

    // Faster than HashCode.Combine for hot loops (and alloc-free)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}