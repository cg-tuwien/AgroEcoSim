using System.Numerics;

namespace Agro.Collisions;

/// <summary>A world-space collision proxy. Width is the blade half-width, not a capsule radius.</summary>
public readonly record struct OrganPose(Vector3 Base, Quaternion Rotation, float Length, float Width, float Radius, bool Rounded = false, Vector4 Deformation = default)
{
    public Vector3 Tip => Base + Vector3.Transform(Width == 0 && Deformation != Vector4.Zero
        ? ContactDeformation.RodPoint(1, Length, Deformation) : new Vector3(Length, 0, 0), Rotation);
    public bool CurvedRod => Width == 0 && !Rounded && Deformation != Vector4.Zero;
    public Vector3 RodNode(int node) => Base + Vector3.Transform(ContactDeformation.RodPoint(node / 3f, Length, Deformation), Rotation);
    public OrganPose RodSpan(int span) => new(RodNode(span), Quaternion.Normalize(Rotation * ContactDeformation.RodRotation(Deformation, span)), Length / 3f, 0, Radius);
    public Vector3 Across => Vector3.Transform(Vector3.UnitZ, Rotation) * Width;

    /// <summary>Upper bound on displacement of every point under pose interpolation.</summary>
    public float MotionBound(in OrganPose other)
    {
        var delta = other.Rotation * Quaternion.Conjugate(Rotation);
        // acos(dot) rounds small, but consequential, turns to zero in single precision.
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return Vector3.Distance(Base, other.Base) + MathF.Abs(Length - other.Length)
            + MathF.Abs(Width - other.Width) + MathF.Abs(Radius - other.Radius)
            + angle * MathF.Max(Length + Width, other.Length + other.Width)
            + ContactDeformation.MotionBound(MathF.Max(Length, other.Length), MathF.Max(Width, other.Width), Deformation, other.Deformation);
    }

    public OrganPose Interpolate(in OrganPose other, float t)
    {
        // CCD endpoints are accepted frames, not approximations reconstructed by
        // lerp/slerp. Cancellation (including a changed Rounded flag) can otherwise
        // invalidate the start or alter the final fallback at a contact boundary.
        if (t == 0) return this;
        if (t == 1) return other;
        return new(
            Base == other.Base ? Base : Vector3.Lerp(Base, other.Base, t),
            Rotation == other.Rotation ? Rotation : Quaternion.Slerp(Rotation, other.Rotation, t),
            Length + (other.Length - Length) * t, Width + (other.Width - Width) * t,
            Radius + (other.Radius - Radius) * t, Rounded || other.Rounded,
            Deformation == other.Deformation ? Deformation : Vector4.Lerp(Deformation, other.Deformation, t));
    }

    public Vector3 Support(Vector3 normal)
    {
        var point = Vector3.Dot(Tip - Base, normal) < 0f ? Tip : Base;
        if (CurvedRod)
            for (var i = 1; i < 3; ++i)
            { var node = RodNode(i); if (Vector3.Dot(node - point, normal) < 0) point = node; }
        var across = Across;
        return point + (Vector3.Dot(across, normal) < 0f ? across : -across);
    }
}
