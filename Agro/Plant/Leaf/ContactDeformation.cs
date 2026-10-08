using System.Numerics;

namespace Agro;

/// <summary>Mechanical geometry, independent of biological organ segmentation.</summary>
public enum ContactDeformationMode : byte { None, BroadLeafV1 }

/// <summary>Versioned, reversible four-coordinate broad-leaf/three-span rod kinematics.</summary>
public static class ContactDeformation
{
    public const byte ProfileVersion = 1;
    public const float MidribLimit = 25f * MathF.PI / 180f;
    public const float FoldLimit = 75f * MathF.PI / 180f;
    public const float StrainTolerance = .02f;

    public static Quaternion RodRotation(Vector4 q, int span)
    {
        if (span == 0 || q == Vector4.Zero) return Quaternion.Identity;
        var first = Quaternion.CreateFromYawPitchRoll(q.Y, 0, q.X);
        return span == 1 ? first : Quaternion.Normalize(first * Quaternion.CreateFromYawPitchRoll(q.W, 0, q.Z));
    }

    public static Vector3 RodPoint(float fraction, float length, Vector4 q)
    {
        if (q == Vector4.Zero) return new(fraction * length, 0, 0);
        var third = length / 3f;
        var a = new Vector3(third, 0, 0);
        if (fraction <= 1f / 3f) return new(fraction * length, 0, 0);
        var b = Vector3.Transform(Vector3.UnitX, RodRotation(q, 1));
        if (fraction <= 2f / 3f) return a + b * (fraction * length - third);
        return a + b * third + Vector3.Transform(Vector3.UnitX, RodRotation(q, 2)) * (fraction * length - 2f * third);
    }

    public static float ClosestRodFraction(Vector3 point, float length, Vector4 q)
    {
        var best = float.PositiveInfinity; var fraction = 0f;
        for (var i = 0; i < 3; ++i)
        {
            var a = RodPoint(i / 3f, length, q); var b = RodPoint((i + 1) / 3f, length, q);
            var d = b - a; var t = Math.Clamp(Vector3.Dot(point - a, d) / MathF.Max(1e-30f, d.LengthSquared()), 0, 1);
            var distance = Vector3.DistanceSquared(point, a + t * d);
            if (distance < best) { best = distance; fraction = (i + t) / 3f; }
        }
        return fraction;
    }

    public static Vector3 BladePoint(Vector3 p, float length, Vector4 q, float hingeHeight = 0)
    {
        if (q == Vector4.Zero || length <= 0) return p;
        // Fold about the midrib, retaining the authored relief. The shared vertex map is
        // continuous at the midrib, including a nonzero authored midrib height.
        var fold = p.Z >= 0 ? q.Z : q.W;
        var side = p.Z >= 0 ? 1f : -1f;
        var relief = p.Y - hingeHeight;
        var folded = new Vector3(0, hingeHeight + relief * MathF.Cos(fold) + MathF.Abs(p.Z) * MathF.Sin(fold),
            p.Z * MathF.Cos(fold) - side * relief * MathF.Sin(fold));
        var t = p.X / length;
        var third = length / 3f;
        var centre = new Vector3(MathF.Min(p.X, third), 0, 0);
        var angle = 0f;
        if (t > 1f / 3f)
        {
            var f = Math.Clamp(3f * t - 1f, 0f, 1f);
            centre += Arc(third * f, q.X * f);
            angle = q.X * f;
        }
        if (t > 2f / 3f)
        {
            var f = Math.Clamp(3f * t - 2f, 0f, 1f);
            centre += Vector3.Transform(Arc(third * f, q.Y * f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, q.X));
            angle = q.X + q.Y * f;
        }
        if (t > 1f) centre += Vector3.Transform(new Vector3((t - 1f) * length, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle));
        return centre + Vector3.Transform(folded, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle));
    }

    static Vector3 Arc(float length, float angle)
    {
        // Stable sinc/cosc near zero. Subtracting cos from one loses precision in
        // both float arithmetic and shader trig implementations, moving a shallow
        // fold by fractions of a millimetre between otherwise identical backends.
        if (MathF.Abs(angle) < .25f)
        {
            var a2 = angle * angle;
            return new(length * (1f-a2/6f+a2*a2/120f-a2*a2*a2/5040f),
                length * angle * (.5f-a2/24f+a2*a2/720f-a2*a2*a2/40320f), 0);
        }
        return new(length*MathF.Sin(angle)/angle, length*(1f-MathF.Cos(angle))/angle, 0);
    }

    public static float MotionBound(float length, float width, Vector4 from, Vector4 to)
    {
        var delta = Vector4.Abs(to - from);
        return (MathF.Abs(length) + MathF.Abs(width)) * (delta.X + delta.Y + delta.Z + delta.W);
    }

    public static Vector4 Clamp(Vector4 q, bool blade, float petioleLimit = 0f)
    {
        if (!float.IsFinite(q.X + q.Y + q.Z + q.W)) return Vector4.Zero;
        if (blade) return Vector4.Clamp(q, new(-MidribLimit, -MidribLimit, -FoldLimit, -FoldLimit),
            new(MidribLimit, MidribLimit, FoldLimit, FoldLimit));
        // One whole-petiole bend budget, independent of subdivision count.
        var angle = new Vector2(q.X, q.Y).Length() + new Vector2(q.Z, q.W).Length();
        var limit = petioleLimit > 0 ? petioleLimit : Collisions.CollisionSettings.MaxElasticBendRadians;
        return angle > limit ? q * (limit / angle) : q;
    }
}
