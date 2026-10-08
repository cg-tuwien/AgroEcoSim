using System.Numerics;
using System.Security.Cryptography;

namespace Agro.Mechanics;

/// <summary>How additional samples represent the authored unloaded surface.</summary>
public enum ShellRestInterpolation : byte { Faceted, CubicHeightField }

/// <summary>
/// Immutable mechanical mesh, built once for a rest profile, not once per tick.
/// Refinement preserves original vertices, the projected outline and connected
/// seams. Cubic interpolation describes one C2 height field through the authored
/// samples; every refinement level samples that same field. It is a geometric
/// reconstruction, not a measurement of additional botanical curvature.
/// Coordinates are lamina-local: X along the blade, Y relief, Z transverse.
/// </summary>
public sealed partial class ShellMesh
{
    public const byte ProfileVersion = 1;
    readonly Vector3[] vertices;
    readonly int[] triangles, parents, vertexParents;
    readonly CubicHeightField? restSurface;
    public ReadOnlySpan<Vector3> RestVertices => vertices;
    public ReadOnlySpan<int> Triangles => triangles;
    public ReadOnlySpan<int> OriginalFaces => parents;
    /// <summary>Two midpoint parents per vertex; (-1,-1) identifies an authored sample.</summary>
    public ReadOnlySpan<int> VertexParents => vertexParents;
    public int OriginalVertexCount { get; }
    public int RefinementLevel { get; }
    public int AdaptiveRefinementSteps { get; }
    public ShellRestInterpolation Interpolation { get; }
    public int EdgeCount { get; }
    public int DofCount => 3 * vertices.Length + EdgeCount;
    /// <summary>Accepted position/director values only; excludes rod state and bookkeeping.</summary>
    public int SinglePrecisionStateBytes => checked(sizeof(float) * DofCount);
    public double MaximumRestOffset { get; }
    public string Identity { get; }

    public ShellMesh(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices,
        int refinementLevel, ShellRestInterpolation interpolation = ShellRestInterpolation.Faceted)
    {
        if (positions.Length < 3 || indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException("Invalid shell mesh.");
        // Larger reference meshes should be requested explicitly in a separate
        // workflow; a corrupt saved profile must not request exponential growth.
        if ((uint)refinementLevel > 4) throw new ArgumentOutOfRangeException(nameof(refinementLevel));
        if (interpolation is not (ShellRestInterpolation.Faceted or ShellRestInterpolation.CubicHeightField))
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        foreach (var p in positions)
            if (!float.IsFinite(p.X + p.Y + p.Z)) throw new ArgumentException("Nonfinite shell vertex.");
        foreach (var index in indices)
            if ((uint)index >= positions.Length) throw new ArgumentException("Invalid shell index.");
        var edges = CountEdges(indices, positions);
        OriginalVertexCount = positions.Length; RefinementLevel = refinementLevel; Interpolation = interpolation;
        var points = new List<Vector3>(positions.ToArray());
        var faceted = new List<Vector3>(positions.ToArray());
        var parentVertices = new List<int>(Enumerable.Repeat(-1, 2 * positions.Length));
        var faces = indices.ToArray(); var originalFaces = Enumerable.Range(0, faces.Length / 3).ToArray();
        var surface = restSurface = interpolation == ShellRestInterpolation.CubicHeightField ? new CubicHeightField(positions) : null;
        for (var level = 0; level < refinementLevel; ++level)
        {
            var midpoints = new Dictionary<(int, int), int>();
            int Midpoint(int a, int b)
            {
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (midpoints.TryGetValue(key, out var id)) return id;
                var p = (points[a] + points[b]) * .5f;
                if (surface != null) p.Y = (float)surface.Height(p.X, p.Z);
                id = points.Count; points.Add(p); faceted.Add((faceted[a] + faceted[b]) * .5f);
                parentVertices.Add(a); parentVertices.Add(b);
                midpoints.Add(key, id); return id;
            }
            var next = new int[checked(faces.Length * 4)]; var ancestry = new int[next.Length / 3];
            for (var f = 0; f < originalFaces.Length; ++f)
            {
                var a = faces[3 * f]; var b = faces[3 * f + 1]; var c = faces[3 * f + 2];
                var ab = Midpoint(a, b); var bc = Midpoint(b, c); var ca = Midpoint(c, a);
                var start = 12 * f;
                next[start] = a; next[start + 1] = ab; next[start + 2] = ca;
                next[start + 3] = ab; next[start + 4] = b; next[start + 5] = bc;
                next[start + 6] = ca; next[start + 7] = bc; next[start + 8] = c;
                next[start + 9] = ab; next[start + 10] = bc; next[start + 11] = ca;
                Array.Fill(ancestry, originalFaces[f], 4 * f, 4);
            }
            faces = next; originalFaces = ancestry;
        }
        vertices = points.ToArray(); triangles = faces; parents = originalFaces; vertexParents = parentVertices.ToArray();
        EdgeCount = refinementLevel == 0 ? edges : CountEdges(triangles, vertices);
        for (var i = 0; i < vertices.Length; ++i)
            MaximumRestOffset = Math.Max(MaximumRestOffset, Vector3.Distance(vertices[i], faceted[i]));
        Identity = ComputeIdentity();
    }

    string ComputeIdentity()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        // Keep uniform profile v1 identities stable. Adaptive profiles have a
        // separate versioned suffix; neither representation is a live-run format.
        writer.Write(AdaptiveRefinementSteps == 0 ? ProfileVersion : (byte)2);
        writer.Write((byte)Interpolation); writer.Write(RefinementLevel);
        if (AdaptiveRefinementSteps != 0) writer.Write(AdaptiveRefinementSteps);
        writer.Write(OriginalVertexCount); writer.Write(vertices.Length);
        foreach (var p in vertices) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
        writer.Write(triangles.Length); foreach (var i in triangles) writer.Write(i);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length))).ToLowerInvariant();
    }

    static int CountEdges(ReadOnlySpan<int> indices, ReadOnlySpan<Vector3> positions)
    {
        var edges = new Dictionary<(int, int), byte>();
        for (var f = 0; f < indices.Length; f += 3)
        {
            var a = new ShellVector(positions[indices[f]]); var b = new ShellVector(positions[indices[f + 1]]);
            var c = new ShellVector(positions[indices[f + 2]]);
            if (!((b - a).Cross(c - a).Norm > 0)) throw new ArgumentException("Degenerate shell face.");
            for (var j = 0; j < 3; ++j)
            {
                var i = indices[f + j]; var k = indices[f + (j + 1) % 3];
                var key = (Math.Min(i, k), Math.Max(i, k)); var bit = (byte)(i < k ? 1 : 2);
                var used = edges.GetValueOrDefault(key);
                if ((used & bit) != 0) throw new ArgumentException("Nonmanifold or inconsistently wound shell.");
                edges[key] = (byte)(used | bit);
            }
        }
        return edges.Count;
    }

    /// <summary>Cubic radial-basis interpolant plus affine polynomial, in a scaled rest plane.</summary>
    sealed class CubicHeightField
    {
        readonly double[] x, z, coefficients;
        readonly double originX, originZ, scale;
        internal CubicHeightField(ReadOnlySpan<Vector3> points)
        {
            var n = points.Length; var size = n + 3;
            x = new double[n]; z = new double[n]; coefficients = new double[size];
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
            var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;
            foreach (var p in points)
            { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
            originX = (minX + maxX) * .5; originZ = (minZ + maxZ) * .5;
            scale = Math.Max(maxX - minX, maxZ - minZ);
            if (!(scale > 0)) throw new ArgumentException("The rest surface needs a nondegenerate XZ projection.");
            var matrix = new double[checked(size * size)];
            for (var i = 0; i < n; ++i)
            { x[i] = (points[i].X - originX) / scale; z[i] = (points[i].Z - originZ) / scale; coefficients[i] = points[i].Y; }
            for (var i = 0; i < n; ++i)
            {
                for (var j = 0; j < n; ++j)
                {
                    var dx = x[i] - x[j]; var dz = z[i] - z[j]; var r2 = dx * dx + dz * dz;
                    matrix[i * size + j] = r2 * Math.Sqrt(r2);
                }
                matrix[i * size + n] = matrix[n * size + i] = 1;
                matrix[i * size + n + 1] = matrix[(n + 1) * size + i] = x[i];
                matrix[i * size + n + 2] = matrix[(n + 2) * size + i] = z[i];
            }
            // Preprocessing only. Pivoting also rejects coincident projected
            // samples instead of silently smoothing away conflicting input data.
            for (var k = 0; k < size; ++k)
            {
                var pivot = k;
                for (var i = k + 1; i < size; ++i)
                    if (Math.Abs(matrix[i * size + k]) > Math.Abs(matrix[pivot * size + k])) pivot = i;
                if (Math.Abs(matrix[pivot * size + k]) < 1e-14)
                    throw new ArgumentException("Ill-conditioned or non-single-valued rest height field.");
                if (pivot != k)
                {
                    for (var j = k; j < size; ++j)
                        (matrix[k * size + j], matrix[pivot * size + j]) = (matrix[pivot * size + j], matrix[k * size + j]);
                    (coefficients[k], coefficients[pivot]) = (coefficients[pivot], coefficients[k]);
                }
                for (var i = k + 1; i < size; ++i)
                {
                    var factor = matrix[i * size + k] / matrix[k * size + k];
                    for (var j = k + 1; j < size; ++j) matrix[i * size + j] -= factor * matrix[k * size + j];
                    coefficients[i] -= factor * coefficients[k];
                }
            }
            for (var i = size - 1; i >= 0; --i)
            {
                for (var j = i + 1; j < size; ++j) coefficients[i] -= matrix[i * size + j] * coefficients[j];
                coefficients[i] /= matrix[i * size + i];
            }
        }

        internal double Height(double px, double pz)
        {
            px = (px - originX) / scale; pz = (pz - originZ) / scale;
            var n = x.Length; var result = coefficients[n] + coefficients[n + 1] * px + coefficients[n + 2] * pz;
            for (var i = 0; i < n; ++i)
            { var dx = px - x[i]; var dz = pz - z[i]; var r2 = dx * dx + dz * dz; result += coefficients[i] * r2 * Math.Sqrt(r2); }
            return result;
        }
    }
}
