using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    /// <summary>Contact normal and deepest proxy feature on the anchored side of a shell.</summary>
    public bool Contact(in OrganPose pose, Vector3 trusted, StaticSurface surface,
        out Vector3 point, out Vector3 normal, out float depth, out bool supported)
    {
        point = normal = default; depth = 0f; supported = false;
        if (!Closest(pose.Base, pose.Tip, pose.Across, pose.Radius + 0.005f, surface, out var hit)) return false;
        normal = hit.Normal;
        if (surface == StaticSurface.Obstacle && Vector3.Dot(trusted - hit.Point, normal) < 0f) normal = -normal;
        point = pose.Support(normal);
        var signed = Vector3.Dot(point - hit.Point, normal);
        supported = normal.Y > 0.3f && hit.Distance <= pose.Radius + 0.005f && signed >= -CollisionSettings.ContactTolerance;
        if (hit.Distance > pose.Radius + CollisionSettings.ContactTolerance) return false;
        // At a finite triangle's edge, radial separation is the actual distance constraint.
        // Plane depth is appropriate only for an intersection, not a remote continuation of it.
        if (hit.Distance > 1e-6f && Vector3.Dot(hit.OnQuery - hit.Point, normal) >= 0f)
        {
            point = hit.OnQuery;
            normal = Vector3.Normalize(hit.OnQuery - hit.Point);
            depth = SurfaceRadius(pose, normal) - hit.Distance;
        }
        else depth = SurfaceRadius(pose, normal) - signed;
        return depth > CollisionSettings.ContactTolerance;
    }

    // Rendered stems have flat ends. A capsule's hemispherical end falsely makes a short,
    // upright crown penetrate the soil as its radius increases, despite its whole cylinder
    // remaining above the surface. Project the actual circular section onto the normal.
    static float SurfaceRadius(in OrganPose pose, Vector3 normal)
    {
        if (pose.Width > 0f || pose.Rounded) return pose.Radius;
        var axial = Vector3.Dot(Vector3.Transform(Vector3.UnitX, pose.Rotation), normal);
        return pose.Radius * MathF.Sqrt(MathF.Max(0f, 1f - axial * axial));
    }

    /// <summary>Conservative swept proxy test, including rotation between the endpoints.
    /// Triangle-plane bounds make persistent sliding cheap; ambiguous intervals subdivide.</summary>
    public bool SweepClear(in OrganPose from, in OrganPose to, StaticSurface surface)
    {
        if (NodeCount == 0 || from.MotionBound(to) < CollisionSettings.ContactTolerance * 0.25f) return true;
        Bounds(from, out var lo, out var hi); Bounds(to, out var toLo, out var toHi);
        var pad = new Vector3(MathF.Max(from.Radius, to.Radius) + ArcBound(from, to));
        lo = Vector3.Min(lo, toLo) - pad; hi = Vector3.Max(hi, toHi) + pad;
        var stack = NodeStack ??= new int[128]; int sp = 0; stack[sp++] = 0;
        while (sp > 0)
        {
            var nodeIndex = stack[--sp];
            if ((SurfaceMasks[nodeIndex] & (1 << (int)surface)) == 0) continue;
            var node = Nodes[nodeIndex];
            if (BoxDistanceSquared(node, lo, hi) > 0f) continue;
            if (node.Count == 0) { stack[sp++] = node.Left; stack[sp++] = node.Left + 1; continue; }
            for (int j = 0; j < node.Count; ++j)
            {
                var t = node.Left + j;
                var normal = N[t];
                if (Vector3.Dot((from.Base + from.Tip) * 0.5f - A[t], normal) < 0f) normal = -normal;
                var intervals = 256;
                if (Kind[t] == surface && !SweepTriangle(from, to, t, normal, 0, ref intervals)) return false;
            }
        }
        return true;
    }

    bool SweepTriangle(in OrganPose from, in OrganPose to, int triangle, Vector3 normal, int depth, ref int intervals)
    {
        if (--intervals < 0) return false;
        var point = A[triangle];
        var margin = MathF.Max(SurfaceRadius(from, normal), SurfaceRadius(to, normal))
            + ArcBound(from, to) - CollisionSettings.ContactTolerance;
        var positive = MathF.Min(Vector3.Dot(from.Support(normal) - point, normal), Vector3.Dot(to.Support(normal) - point, normal));
        // Keep the starting side throughout subdivision. Otherwise a thin lamina can
        // cross the tolerance band and each half of the sweep certifies a different side.
        if (positive >= margin) return true;
        var mid = from.Interpolate(to, 0.5f);
        var distance = mid.Width > 1e-7f
            ? RectangleTriangleDistance(mid.Base - mid.Across, mid.Base + mid.Across, mid.Tip + mid.Across,
                mid.Tip - mid.Across, A[triangle], U[triangle], V[triangle], out _, out _)
            : SegmentTriangleDistance(mid.Base, mid.Tip, A[triangle], U[triangle], V[triangle], out _, out _);
        if (distance + CollisionSettings.ContactTolerance < mid.Radius) return false;
        var bound = MathF.Max(from.MotionBound(mid), to.MotionBound(mid));
        if (distance - MathF.Max(mid.Radius, CollisionSettings.ContactTolerance) > bound) return true;
        if (depth >= 16) return false;
        return SweepTriangle(from, mid, triangle, normal, depth + 1, ref intervals)
            && SweepTriangle(mid, to, triangle, normal, depth + 1, ref intervals);
    }

    static float ArcBound(in OrganPose a, in OrganPose b)
    {
        var delta = Quaternion.Normalize(b.Rotation * Quaternion.Conjugate(a.Rotation));
        var sineHalfAngle = new Vector3(delta.X, delta.Y, delta.Z).Length();
        var cosineHalfAngle = Math.Clamp(MathF.Abs(delta.W), 0f, 1f);
        // Sagitta plus the cross term from simultaneous elongation and rotation.
        return MathF.Max(a.Length + a.Width + a.Radius, b.Length + b.Width + b.Radius) * (1f - cosineHalfAngle)
            + (MathF.Abs(a.Length - b.Length) + MathF.Abs(a.Width - b.Width) + MathF.Abs(a.Radius-b.Radius)) * 0.5f * sineHalfAngle;
    }

    static void Bounds(in OrganPose p, out Vector3 lo, out Vector3 hi)
    {
        var across = Vector3.Abs(p.Across);
        lo = Vector3.Min(p.Base, p.Tip) - across; hi = Vector3.Max(p.Base, p.Tip) + across;
    }
}
