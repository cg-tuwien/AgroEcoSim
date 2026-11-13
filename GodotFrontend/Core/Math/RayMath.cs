namespace GodotFrontend.Core.Math;

public static class RayMath
{
    /// <summary>Möller-Trumbore, double-sided; returns t &gt; 0 of the hit or null.</summary>
    public static double? Triangle(Vec3 o, Vec3 d, Vec3 a, Vec3 b, Vec3 c)
    {
        var e1 = b - a;
        var e2 = c - a;
        var pv = Vec3.Cross(d, e2);
        var det = Vec3.Dot(e1, pv);
        if (System.Math.Abs(det) < 1e-18) return null;
        var inv = 1 / det;
        var tv = o - a;
        var u = Vec3.Dot(tv, pv) * inv;
        if (u < 0 || u > 1) return null;
        var qv = Vec3.Cross(tv, e1);
        var v = Vec3.Dot(d, qv) * inv;
        if (v < 0 || u + v > 1) return null;
        var t = Vec3.Dot(e2, qv) * inv;
        return t > 1e-9 ? t : null;
    }

    /// <summary>Slab test against an AABB given the reciprocal direction; returns the entry distance or null.</summary>
    public static double? Slab(Vec3 o, double ix, double iy, double iz, double minx, double miny, double minz, double maxx, double maxy, double maxz, double maxT)
    {
        double t0 = 0, t1 = maxT;
        if (!Axis(o.X, ix, minx, maxx, ref t0, ref t1)) return null;
        if (!Axis(o.Y, iy, miny, maxy, ref t0, ref t1)) return null;
        if (!Axis(o.Z, iz, minz, maxz, ref t0, ref t1)) return null;
        return t0;
    }

    private static bool Axis(double o, double inv, double min, double max, ref double t0, ref double t1)
    {
        if (double.IsInfinity(inv))
            return o >= min && o <= max;
        var tn = (min - o) * inv;
        var tf = (max - o) * inv;
        if (tn > tf) (tn, tf) = (tf, tn);
        if (tn > t0) t0 = tn;
        if (tf < t1) t1 = tf;
        return t0 <= t1;
    }

    /// <summary>Nearest non-negative intersection with a sphere, or null.</summary>
    public static double? Sphere(Vec3 o, Vec3 d, Vec3 c, double r)
    {
        var oc = o - c;
        var a = d.LengthSq;
        var b = Vec3.Dot(oc, d);
        var cc = oc.LengthSq - r * r;
        var disc = b * b - a * cc;
        if (disc < 0) return null;
        var s = System.Math.Sqrt(disc);
        var t = (-b - s) / a;
        if (t < 0) t = (-b + s) / a;
        return t >= 0 ? t : null;
    }
}
