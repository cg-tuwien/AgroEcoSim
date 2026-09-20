using System.Numerics;

namespace Agro.Collisions;

/// <summary>A world-space collision proxy. Width is the blade half-width, not a capsule radius.</summary>
public readonly record struct OrganPose(Vector3 Base, Quaternion Rotation, float Length, float Width, float Radius, bool Rounded = false)
{
    public Vector3 Tip => Base + Vector3.Transform(Vector3.UnitX, Rotation) * Length;
    public Vector3 Across => Vector3.Transform(Vector3.UnitZ, Rotation) * Width;

    /// <summary>Upper bound on displacement of every point under pose interpolation.</summary>
    public float MotionBound(in OrganPose other)
    {
        var delta = other.Rotation * Quaternion.Conjugate(Rotation);
        // acos(dot) rounds small, but consequential, turns to zero in single precision.
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return Vector3.Distance(Base, other.Base) + MathF.Abs(Length - other.Length)
            + MathF.Abs(Width - other.Width) + MathF.Abs(Radius - other.Radius)
            + angle * MathF.Max(Length + Width, other.Length + other.Width);
    }

    public OrganPose Interpolate(in OrganPose other, float t) => new(
        Vector3.Lerp(Base, other.Base, t), Quaternion.Slerp(Rotation, other.Rotation, t),
        Length + (other.Length - Length) * t, Width + (other.Width - Width) * t,
        Radius + (other.Radius - Radius) * t, Rounded || other.Rounded);

    public Vector3 Support(Vector3 normal)
    {
        var point = Vector3.Dot(Tip - Base, normal) < 0f ? Tip : Base;
        var across = Across;
        return point + (Vector3.Dot(across, normal) < 0f ? across : -across);
    }
}
