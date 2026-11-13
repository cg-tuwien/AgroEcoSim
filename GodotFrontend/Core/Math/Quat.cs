namespace GodotFrontend.Core.Math;

/// <summary>Unit quaternion (x, y, z, w), a port of the handful of three.js Quaternion methods OrbitControls needs.</summary>
public readonly record struct Quat(double X, double Y, double Z, double W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    public static double Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public Quat Conjugate() => new(-X, -Y, -Z, W);

    public Quat Normalized()
    {
        var l = System.Math.Sqrt(Dot(this, this));
        return l == 0 ? Identity : new Quat(X / l, Y / l, Z / l, W / l);
    }

    /// <summary>three's Quaternion.setFromUnitVectors: the rotation taking <paramref name="from"/> onto <paramref name="to"/>.</summary>
    public static Quat FromUnitVectors(Vec3 from, Vec3 to)
    {
        var r = Vec3.Dot(from, to) + 1;
        if (r < double.Epsilon)
        {
            // from and to point in opposite directions: pick any axis orthogonal to from.
            if (System.Math.Abs(from.X) > System.Math.Abs(from.Z))
                return new Quat(-from.Y, from.X, 0, 0).Normalized();
            return new Quat(0, -from.Z, from.Y, 0).Normalized();
        }
        return new Quat(
            from.Y * to.Z - from.Z * to.Y,
            from.Z * to.X - from.X * to.Z,
            from.X * to.Y - from.Y * to.X,
            r).Normalized();
    }

    /// <summary>three's Vector3.applyQuaternion.</summary>
    public Vec3 Apply(Vec3 v)
    {
        // t = 2 * cross(q.xyz, v); v' = v + q.w * t + cross(q.xyz, t)
        var tx = 2 * (Y * v.Z - Z * v.Y);
        var ty = 2 * (Z * v.X - X * v.Z);
        var tz = 2 * (X * v.Y - Y * v.X);
        return new Vec3(
            v.X + W * tx + (Y * tz - Z * ty),
            v.Y + W * ty + (Z * tx - X * tz),
            v.Z + W * tz + (X * ty - Y * tx));
    }

    /// <summary>three's Quaternion.setFromRotationMatrix over a pure rotation basis (columns x, y, z).</summary>
    public static Quat FromRotationMatrix(Basis3 b)
    {
        double m11 = b.Col0.X, m12 = b.Col1.X, m13 = b.Col2.X;
        double m21 = b.Col0.Y, m22 = b.Col1.Y, m23 = b.Col2.Y;
        double m31 = b.Col0.Z, m32 = b.Col1.Z, m33 = b.Col2.Z;
        var trace = m11 + m22 + m33;
        if (trace > 0)
        {
            var s = 0.5 / System.Math.Sqrt(trace + 1.0);
            return new Quat((m32 - m23) * s, (m13 - m31) * s, (m21 - m12) * s, 0.25 / s);
        }
        if (m11 > m22 && m11 > m33)
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m11 - m22 - m33);
            return new Quat(0.25 * s, (m12 + m21) / s, (m13 + m31) / s, (m32 - m23) / s);
        }
        if (m22 > m33)
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m22 - m11 - m33);
            return new Quat((m12 + m21) / s, 0.25 * s, (m23 + m32) / s, (m13 - m31) / s);
        }
        {
            var s = 2.0 * System.Math.Sqrt(1.0 + m33 - m11 - m22);
            return new Quat((m13 + m31) / s, (m23 + m32) / s, 0.25 * s, (m21 - m12) / s);
        }
    }
}
