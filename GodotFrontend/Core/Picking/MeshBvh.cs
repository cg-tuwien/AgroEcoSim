using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Picking;

/// <summary>
/// A median-split bounding volume hierarchy over one static triangle mesh (trays, the merged imported obstacles),
/// built once per import, answering nearest-hit ray queries double-sided. 1.4M triangles build in well under a
/// second and a query visits a few dozen nodes.
/// </summary>
public sealed class MeshBvh
{
    private readonly float[] p;
    private readonly int[] idx;
    private readonly int[] tri;
    private float[] nodeMin = Array.Empty<float>(), nodeMax = Array.Empty<float>();
    private int[] nodeLeft = Array.Empty<int>(), nodeRight = Array.Empty<int>(), nodeStart = Array.Empty<int>(), nodeCount = Array.Empty<int>();
    private int nodes;
    private const int LeafSize = 8;

    /// <summary>Translation applied to the mesh (a tray's position).</summary>
    public Vec3 Offset { get; }
    public int TriangleCount => tri.Length;

    public MeshBvh(float[] positions, int[] indices, Vec3 offset = default)
    {
        p = positions;
        idx = indices;
        Offset = offset;
        var n = indices.Length / 3;
        tri = new int[n];
        var centroid = new float[n * 3];
        for (var t = 0; t < n; ++t)
        {
            tri[t] = t;
            for (var c = 0; c < 3; ++c)
                centroid[t * 3 + c] = (p[idx[t * 3] * 3 + c] + p[idx[t * 3 + 1] * 3 + c] + p[idx[t * 3 + 2] * 3 + c]) / 3f;
        }
        Grow(System.Math.Max(4, n / 2 + 4));
        if (n > 0) Build(centroid, 0, n);
    }

    private void Grow(int cap)
    {
        Array.Resize(ref nodeMin, cap * 3);
        Array.Resize(ref nodeMax, cap * 3);
        Array.Resize(ref nodeLeft, cap);
        Array.Resize(ref nodeRight, cap);
        Array.Resize(ref nodeStart, cap);
        Array.Resize(ref nodeCount, cap);
    }

    private int Build(float[] centroid, int start, int count)
    {
        if (nodes >= nodeLeft.Length) Grow(nodeLeft.Length * 2);
        var node = nodes++;
        float mnx = float.PositiveInfinity, mny = mnx, mnz = mnx, mxx = float.NegativeInfinity, mxy = mxx, mxz = mxx;
        float cmnx = mnx, cmny = mnx, cmnz = mnx, cmxx = mxx, cmxy = mxx, cmxz = mxx;
        for (var i = start; i < start + count; ++i)
        {
            var t = tri[i];
            for (var k = 0; k < 3; ++k)
            {
                var v = idx[t * 3 + k] * 3;
                float x = p[v], y = p[v + 1], z = p[v + 2];
                if (x < mnx) mnx = x; if (x > mxx) mxx = x;
                if (y < mny) mny = y; if (y > mxy) mxy = y;
                if (z < mnz) mnz = z; if (z > mxz) mxz = z;
            }
            float qx = centroid[t * 3], qy = centroid[t * 3 + 1], qz = centroid[t * 3 + 2];
            if (qx < cmnx) cmnx = qx; if (qx > cmxx) cmxx = qx;
            if (qy < cmny) cmny = qy; if (qy > cmxy) cmxy = qy;
            if (qz < cmnz) cmnz = qz; if (qz > cmxz) cmxz = qz;
        }
        nodeMin[node * 3] = mnx; nodeMin[node * 3 + 1] = mny; nodeMin[node * 3 + 2] = mnz;
        nodeMax[node * 3] = mxx; nodeMax[node * 3 + 1] = mxy; nodeMax[node * 3 + 2] = mxz;
        if (count <= LeafSize)
        {
            nodeLeft[node] = -1; nodeRight[node] = -1; nodeStart[node] = start; nodeCount[node] = count;
            return node;
        }
        float ex = cmxx - cmnx, ey = cmxy - cmny, ez = cmxz - cmnz;
        var axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
        var keys = new float[count];
        for (var i = 0; i < count; ++i) keys[i] = centroid[tri[start + i] * 3 + axis];
        // Array.Sort(keys, items, index, length) applies the index to BOTH arrays; keys holds only this node's range,
        // so the sort has to run on the two spans.
        MemoryExtensions.Sort(keys.AsSpan(), tri.AsSpan(start, count));
        var half = count / 2;
        var left = Build(centroid, start, half);
        var right = Build(centroid, start + half, count - half);
        nodeLeft[node] = left; nodeRight[node] = right;
        return node;
    }

    /// <summary>Nearest hit distance along the ray (in the units of its direction), or null.</summary>
    public double? Raycast(Ray ray, double maxT = double.PositiveInfinity)
    {
        if (nodes == 0) return null;
        var o = ray.Origin - Offset;
        var d = ray.Direction;
        double ix = 1 / d.X, iy = 1 / d.Y, iz = 1 / d.Z;
        double? best = null;
        var bestT = maxT;
        Span<int> stack = stackalloc int[96];
        var sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            var node = stack[--sp];
            if (!SlabHit(node, o, ix, iy, iz, bestT)) continue;
            if (nodeLeft[node] < 0)
            {
                var end = nodeStart[node] + nodeCount[node];
                for (var i = nodeStart[node]; i < end; ++i)
                {
                    var t = tri[i];
                    var h = RayMath.Triangle(o, d, P(idx[t * 3]), P(idx[t * 3 + 1]), P(idx[t * 3 + 2]));
                    if (h is double th && th < bestT) { bestT = th; best = th; }
                }
            }
            else if (sp + 2 <= stack.Length)
            {
                stack[sp++] = nodeRight[node];
                stack[sp++] = nodeLeft[node];
            }
        }
        return best;
    }

    private Vec3 P(int v) => new(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]);

    private bool SlabHit(int node, Vec3 o, double ix, double iy, double iz, double maxT)
        => RayMath.Slab(o, ix, iy, iz, nodeMin[node * 3], nodeMin[node * 3 + 1], nodeMin[node * 3 + 2],
            nodeMax[node * 3], nodeMax[node * 3 + 1], nodeMax[node * 3 + 2], maxT) is not null;
}
