using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Utils;

/// <summary>
/// Representation of 3D vectors and points with integer coordinates.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Vector3i : IEquatable<Vector3i>, IComparable<Vector3i>
{
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
        X = (int)vector.X;
        Y = (int)vector.Y;
        Z = (int)vector.Z;
    }

    public Vector3i(ReadOnlySpan<int> readOnlySpan) : this()
    {
        X = readOnlySpan[0];
        Y = readOnlySpan[1];
        Z = readOnlySpan[2];
    }

    #region Operators
    public static bool operator ==(Vector3i a, Vector3i b) => a.Equals(b);
    public static bool operator !=(Vector3i a, Vector3i b) => !a.Equals(b);

    public override bool Equals(object? obj) => (obj is Vector3i i) && Equals(i);

    public bool Equals(Vector3i other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <summary>
    /// Hash using Fowlen-Noll-Vo https://en.wikipedia.org/wiki/Fowler–Noll–Vo_hash_function
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static Vector3i operator +(Vector3i a, Vector3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3i operator *(Vector3i a, int i) => new(a.X * i, a.Y * i, a.Z * i);
    #endregion Operators

    public override string ToString() => $"[{X}; {Y}; {Z}]";

    public string ToString(string format) => string.Format(format, X, Y, Z);
    
    public int ManhattanDistance(Vector3i other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);

    public int CompareTo(Vector3i other)
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

    internal Vector3i ShiftedLeft() => new(Y, Z, X);

    internal Vector3i ShiftedRight() => new(Z, X, Y);

    public bool Contains(int value) => X == value || Y == value || Z == value;
}