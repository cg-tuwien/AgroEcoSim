using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Utils;

/// <summary>
/// Representation of 3D vectors and points with integer coordinates.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Vector3i : IEquatable<Vector3i>, IComparable<Vector3i>
{
    const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public static readonly Vector3i Zero = new(0, 0, 0);

    public Vector3i(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Vector3i(Vector3 vector)
    {
        X = (int)Math.Floor(vector.X);
        Y = (int)Math.Floor(vector.Y);
        Z = (int)Math.Floor(vector.Z);
    }

    public Vector3i(ReadOnlySpan<int> readOnlySpan) : this()
    {
        X = readOnlySpan[0];
        Y = readOnlySpan[1];
        Z = readOnlySpan[2];
    }

    #region Operators
    [M(AI)]public static bool operator ==(Vector3i a, Vector3i b) => a.Equals(b);
    [M(AI)]public static bool operator !=(Vector3i a, Vector3i b) => !a.Equals(b);

    [M(AI)]public override bool Equals(object? obj) => (obj is Vector3i i) && Equals(i);

    [M(AI)]public bool Equals(Vector3i other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <summary>
    /// Hash using Fowlen-Noll-Vo https://en.wikipedia.org/wiki/Fowler–Noll–Vo_hash_function
    /// </summary>
    [M(AI)]public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    [M(AI)]public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    [M(AI)]public static Vector3i operator -(Vector3i a, Vector3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    [M(AI)]public static Vector3i operator *(Vector3i a, int i) => new(a.X * i, a.Y * i, a.Z * i);
    [M(AI)]public static Vector3i operator /(Vector3i a, int i) => new(a.X / i, a.Y / i, a.Z / i);

    [M(AI)]public static Vector3 operator *(Vector3i a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    [M(AI)]public static Vector3 operator *(Vector3 a, Vector3i b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    #endregion Operators

    [M(AI)] public override string ToString() => $"[{X}; {Y}; {Z}]";

    [M(AI)]public string ToString(string format) => string.Format(format, X, Y, Z);

    [M(AI)]public int ManhattanDistance(Vector3i other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);

    [M(AI)]public int CompareTo(Vector3i other)
    {
        var x = X.CompareTo(other.X);
        if (x != 0)
            return x;
        else
        {
            var y = Y.CompareTo(other.Y);
            return (y != 0) ? y : Z.CompareTo(other.Z);
        }
    }

    [M(AI)]internal Vector3i ShiftedLeft() => new(Y, Z, X);

    [M(AI)]internal Vector3i ShiftedRight() => new(Z, X, Y);

    [M(AI)]public bool Contains(int value) => X == value || Y == value || Z == value;

    [M(AI)]public void Deconstruct(out int x, out int y, out int z)
    {
        x = X;
        y = Y;
        z = Z;
    }
}