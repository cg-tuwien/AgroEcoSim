using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    /// <summary>
    /// First soil boundary vertically above a planting point. Unlike segment proximity,
    /// this chooses the nearest crossing when several trays share the same column.
    /// Winding is ignored; obstacles and vertical side faces cannot supply the datum.
    /// </summary>
    internal bool TrySoilSurfaceAbove(Vector3 point, out float height)
    {
        height = point.Y;
        if (SoilTriangles == 0 || NodeCount == 0)
            return false;
        const float tolerance = 1e-5f;
        var best = float.PositiveInfinity;
        var stack = NodeStack ??= new int[128];
        var count = 0;
        stack[count++] = 0;
        while (count > 0)
        {
            var index = stack[--count];
            if ((SurfaceMasks[index] & (1 << (int)StaticSurface.Soil)) == 0)
                continue;
            ref var node = ref Nodes[index];
            if (point.X < node.Min.X - tolerance || point.X > node.Max.X + tolerance
                || point.Z < node.Min.Z - tolerance || point.Z > node.Max.Z + tolerance
                || node.Max.Y < point.Y - tolerance || node.Min.Y > point.Y + best)
                continue;
            if (node.Count == 0)
            {
                stack[count++] = node.Left;
                stack[count++] = node.Left + 1;
                continue;
            }
            for (int j = 0; j < node.Count; j++)
            {
                var triangle = node.Left + j;
                if (Kind[triangle] != StaticSurface.Soil)
                    continue;
                var cross = Vector3.Cross(Vector3.UnitY, V[triangle]);
                var determinant = Vector3.Dot(U[triangle], cross);
                if (MathF.Abs(determinant) < 1e-12f)
                    continue;
                var offset = point - A[triangle];
                var u = Vector3.Dot(offset, cross) / determinant;
                if (u < -1e-6f || u > 1.000001f)
                    continue;
                var q = Vector3.Cross(offset, U[triangle]);
                var v = q.Y / determinant;
                if (v < -1e-6f || u + v > 1.000001f)
                    continue;
                var distance = Vector3.Dot(V[triangle], q) / determinant;
                if (distance >= -tolerance && distance < best)
                    best = MathF.Max(0f, distance);
            }
        }
        if (!float.IsFinite(best))
            return false;
        height = point.Y + best;
        return true;
    }
}
