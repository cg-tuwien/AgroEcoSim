using AgentsSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Utils;

namespace Agro;

/// <summary>
/// An obstacle given as an arbitrary triangle mesh, typically one named object of an imported OBJ
/// scene: a facade, a window, a roof.
/// </summary>
/// <remarks>
/// Vertices are remapped to a local, compact array instead of holding on to the whole scene's
/// vertex buffer. A facade model splits into a couple of thousand named objects, so sharing the
/// full buffer with each of them would cost gigabytes.
/// </remarks>
public partial class MeshObstacle : IObstacle
{
    /// <summary>Number of triangles a single exported surface may hold; the binary mesh format
    /// stores the count in one byte.</summary>
    const int MaxTrianglesPerSurface = 255;

    public string Name { get; }

    readonly Vector3[] PointData;
    readonly int[] IndexData;
    readonly ArraySegment<byte> PrimitiveDataClustered;
    readonly ArraySegment<byte> PrimitiveDataInterleaved;

    public MeshObstacle(string name, IList<Vector3> sourceVertices, IReadOnlyList<Vector3i> triangles)
        : this(name, sourceVertices, Flatten(triangles)) { }

    /// <param name="name">Name of the source object.</param>
    /// <param name="sourceVertices">Shared vertex buffer of the whole scene.</param>
    /// <param name="triangleIndices">Flat triple-wise indices into <paramref name="sourceVertices"/>.</param>
    public MeshObstacle(string name, IList<Vector3> sourceVertices, IReadOnlyList<int> triangleIndices)
    {
        Name = name ?? string.Empty;

        var remap = new Dictionary<int, int>(triangleIndices.Count);
        var points = new List<Vector3>(triangleIndices.Count);
        var indices = new List<int>(triangleIndices.Count);
        for (int i = 0; i + 2 < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            if (a == b || b == c || a == c)
                continue; //degenerate, contributes no surface and only confuses the hierarchy
            indices.Add(Local(a));
            indices.Add(Local(b));
            indices.Add(Local(c));
        }

        PointData = [.. points];
        IndexData = [.. indices];

        using var clusteredStream = new MemoryStream();
        using var clustered = new BinaryWriter(clusteredStream);
        using var interleavedStream = new MemoryStream();
        using var interleaved = new BinaryWriter(interleavedStream);

        clustered.WriteU32(1);
        interleaved.WriteU32(1);

        clustered.WriteU8(0);
        ExportMesh(clustered);

        interleaved.WriteU8(0);
        ExportMesh(interleaved);
        interleaved.Write(false);

        clusteredStream.TryGetBuffer(out PrimitiveDataClustered);
        interleavedStream.TryGetBuffer(out PrimitiveDataInterleaved);

        int Local(int source)
        {
            if (!remap.TryGetValue(source, out var local))
            {
                local = points.Count;
                remap.Add(source, local);
                points.Add(sourceVertices[source]);
            }
            return local;
        }
    }

    static int[] Flatten(IReadOnlyList<Vector3i> triangles)
    {
        var result = new int[triangles.Count * 3];
        for (int i = 0; i < triangles.Count; ++i)
        {
            result[3 * i] = triangles[i].X;
            result[3 * i + 1] = triangles[i].Y;
            result[3 * i + 2] = triangles[i].Z;
        }
        return result;
    }

    /// <summary>Fan-triangulates convex polygons of any vertex count.</summary>
    public static int[] Triangulate(IEnumerable<IReadOnlyList<int>> polygons)
    {
        var result = new List<int>();
        foreach (var polygon in polygons)
            for (int i = 2; i < polygon.Count; ++i)
            {
                result.Add(polygon[0]);
                result.Add(polygon[i - 1]);
                result.Add(polygon[i]);
            }
        return [.. result];
    }

    public int TriangleCount => IndexData.Length / 3;

    public void AppendTriangles(List<Vector3> vertices, List<int> indices)
    {
        var offset = vertices.Count;
        vertices.AddRange(PointData);
        for (int i = 0; i < IndexData.Length; ++i)
            indices.Add(IndexData[i] + offset);
    }

    public void ExportTriangles(List<Vector3> points, BinaryWriter writer)
    {
        var offset = points.Count;
        var triangles = TriangleCount;
        //Split into as many surfaces as the one-byte triangle count of the format allows.
        var surfaces = (triangles + MaxTrianglesPerSurface - 1) / MaxTrianglesPerSurface;
        writer.WriteU32(surfaces);

        points.AddRange(PointData);

        var written = 0;
        for (int s = 0; s < surfaces; ++s)
        {
            var batch = Math.Min(MaxTrianglesPerSurface, triangles - written);
            writer.WriteU8(batch);
            for (int t = 0; t < batch; ++t)
            {
                var i = 3 * (written + t);
                writer.WriteU32(IndexData[i] + offset);
                writer.WriteU32(IndexData[i + 1] + offset);
                writer.WriteU32(IndexData[i + 2] + offset);
            }
            written += batch;
        }
    }

    public void ExportObj(List<Vector3> points, StringBuilder obji)
    {
        var p1 = points.Count + 1;
        points.AddRange(PointData);
        for(int i = 0; i < IndexData.Length; i += 3)
            obji.AppendLine($"f {IndexData[i] + p1} {IndexData[i+1] + p1} {IndexData[i+2] + p1}");
    }

    public void ExportAsPrimitivesClustered(BinaryWriter writer) => writer.Write(PrimitiveDataClustered);
    public void ExportAsPrimitivesInterleaved(BinaryWriter writer) => writer.Write(PrimitiveDataInterleaved);

    public void ExportMesh(BinaryWriter writer) //for the frontend
    {
        writer.Write(PointData.Length);
        for (int i = 0; i < PointData.Length; ++i)
            writer.WriteV32(PointData[i]);
        writer.Write(IndexData.Length);
        for (int i = 0; i < IndexData.Length; ++i)
            writer.Write(IndexData[i]);
    }
}
