namespace GodotFrontend.Core.Math;

/// <summary>
/// Double-precision 3-vector for the engine-free core. Camera and picking maths run in double because
/// three.js computes in float64 and the ported tests assert to five or six decimals; Godot's single
/// precision only enters at the adapter boundary.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double LengthSq => X * X + Y * Y + Z * Z;
    public double Length => System.Math.Sqrt(LengthSq);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>three's Vector3.normalize: divides by the length, or by 1 when the length is zero.</summary>
    public Vec3 Normalized()
    {
        var l = Length;
        return this / (l == 0 ? 1 : l);
    }

    public static double DistanceSq(Vec3 a, Vec3 b) => (a - b).LengthSq;

    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

    public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
}

public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => System.Math.Sqrt(X * X + Y * Y);
}
