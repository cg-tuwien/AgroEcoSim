using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utils;

namespace Agro.DelunayTerrain;

[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("{X} {Y} {Z}")]
public readonly struct Vector3big
{
    public readonly BigInteger X;
    public readonly BigInteger Y;
    public readonly BigInteger Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Vector3big(BigInteger x, BigInteger y, BigInteger z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsZero() => BigInteger.Abs(X) == 0 && BigInteger.Abs(Y) == 0 && BigInteger.Abs(Z) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BigInteger LengthSquared() => X * X + Y * Y + Z * Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3big operator +(in Vector3big a, in Vector3big b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3big operator -(in Vector3big a, in Vector3big b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3big operator *(in Vector3big a, in BigInteger s) => new(a.X * s, a.Y * s, a.Z * s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3big operator *(in BigInteger s, in Vector3big a) => new(a.X * s, a.Y * s, a.Z * s);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BigInteger Dot(in Vector3int a, in Vector3big b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3big Cross(in Vector3big a, in Vector3big b) => new(a.Y * b.Z - b.Y * a.Z, a.Z * b.X - a.X * b.Z,  a.X * b.Y - b.X * a.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3big Cross(in Vector3int a, in Vector3int b) => new((long)a.Y * b.Z - (long)b.Y * a.Z, (long)a.Z * b.X - (long)a.X * b.Z, (long)a.X * b.Y - (long)b.X * a.Y);
}