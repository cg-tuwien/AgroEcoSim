namespace GodotFrontend.Core.Scene;

/// <summary>Renderer-neutral triangle mesh (render/core/Geometry.ts MeshData). All arrays are flat and interleaved.</summary>
public sealed class MeshData
{
    public float[] Positions;      // xyz per vertex
    public float[] Normals;        // xyz per vertex, always present
    public float[]? Uvs;           // uv per vertex; for recorded laminae u is the hinge height
    public float[]? Colors;        // linear rgb per vertex (debug box only)
    public int[] Indices;
    public string Name;

    public MeshData(float[] positions, float[] normals, int[] indices, string name, float[]? uvs = null, float[]? colors = null)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
        Name = name;
        Uvs = uvs;
        Colors = colors;
    }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>
    /// Area-weighted smooth vertex normals, matching THREE.BufferGeometry.computeVertexNormals(): the raw face
    /// cross products are accumulated and only normalised at the end.
    /// </summary>
    public static float[] ComputeVertexNormals(float[] positions, int[] indices)
    {
        var normals = new float[positions.Length];
        for (var i = 0; i < indices.Length; i += 3)
        {
            int ia = indices[i] * 3, ib = indices[i + 1] * 3, ic = indices[i + 2] * 3;
            double ax = positions[ia], ay = positions[ia + 1], az = positions[ia + 2];
            double bx = positions[ib], by = positions[ib + 1], bz = positions[ib + 2];
            double cx = positions[ic], cy = positions[ic + 1], cz = positions[ic + 2];
            double e1x = bx - ax, e1y = by - ay, e1z = bz - az;
            double e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
            var nx = (float)(e1y * e2z - e1z * e2y);
            var ny = (float)(e1z * e2x - e1x * e2z);
            var nz = (float)(e1x * e2y - e1y * e2x);
            normals[ia] += nx; normals[ia + 1] += ny; normals[ia + 2] += nz;
            normals[ib] += nx; normals[ib + 1] += ny; normals[ib + 2] += nz;
            normals[ic] += nx; normals[ic + 1] += ny; normals[ic + 2] += nz;
        }
        NormalizeInPlace(normals);
        return normals;
    }

    public static void NormalizeInPlace(float[] v)
    {
        for (var i = 0; i < v.Length; i += 3)
        {
            double x = v[i], y = v[i + 1], z = v[i + 2];
            var len = System.Math.Sqrt(x * x + y * y + z * z);
            if (len > 0)
            {
                var inv = 1 / len;
                v[i] = (float)(x * inv); v[i + 1] = (float)(y * inv); v[i + 2] = (float)(z * inv);
            }
        }
    }

    /// <summary>Axis-aligned bounds of the positions.</summary>
    public (float[] Min, float[] Max) Bounds()
    {
        var min = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        var max = new[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        var p = Positions;
        for (var i = 0; i < p.Length; i += 3)
            for (var c = 0; c < 3; ++c)
            {
                if (p[i + c] < min[c]) min[c] = p[i + c];
                if (p[i + c] > max[c]) max[c] = p[i + c];
            }
        return (min, max);
    }

    /// <summary>Bounding sphere (centre of the AABB, radius to the farthest vertex) for picking pre-tests.</summary>
    public (float cx, float cy, float cz, float r) BoundingSphere()
    {
        var (min, max) = Bounds();
        if (VertexCount == 0) return (0, 0, 0, 0);
        float cx = (min[0] + max[0]) * 0.5f, cy = (min[1] + max[1]) * 0.5f, cz = (min[2] + max[2]) * 0.5f;
        double r2 = 0;
        var p = Positions;
        for (var i = 0; i < p.Length; i += 3)
        {
            double dx = p[i] - cx, dy = p[i + 1] - cy, dz = p[i + 2] - cz;
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > r2) r2 = d2;
        }
        return (cx, cy, cz, (float)System.Math.Sqrt(r2));
    }
}
