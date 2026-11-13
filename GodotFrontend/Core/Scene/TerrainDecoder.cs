namespace GodotFrontend.Core.Scene;

public abstract record TerrainItem(string Id, float Px, float Py, float Pz, int Index);

/// <summary>A box tray: Py is its TOP surface, so it is drawn a full height lower (BoxCorner at (px, py - sy, pz)).</summary>
public sealed record BoxTerrainItem(string Id, float Px, float Py, float Pz, float Sx, float Sy, float Sz, float Qx, float Qy, float Qz, float Qw, int Index)
    : TerrainItem(Id, Px, Py, Pz, Index);

/// <summary>A mesh tray from an imported model: translate-only, drawn with area-weighted vertex normals.</summary>
public sealed record MeshTerrainItem(string Id, float Px, float Py, float Pz, float[] Points, int[] Triangles, int Index)
    : TerrainItem(Id, Px, Py, Pz, Index)
{
    private MeshData? cached;
    public MeshData Mesh => cached ??= new MeshData(Points, MeshData.ComputeVertexNormals(Points, Triangles), Triangles, $"terrain:{Id}");
}

/// <summary>One named object inside the merged imported obstacle mesh (vertex and index ranges are contiguous).</summary>
public sealed record ObstacleGroup(string Name, int VertexFirst, int VertexCount, int IndexFirst, int IndexCount);

public sealed class TerrainData
{
    public byte Version;
    public List<TerrainItem> Items = new();
    /// <summary>The imported obstacles merged into one mesh, or null. Groups say which slice each named object owns.</summary>
    public MeshData? ObstacleMesh;
    public List<ObstacleGroup> Groups = new();
}

/// <summary>BinaryReader.readTerrain: the soil stream SoilFormationsList.Serialize writes (hub Terrain message, GET run/{id}/terrain).</summary>
public static class TerrainDecoder
{
    public static TerrainData Decode(ReadOnlySpan<byte> bytes)
    {
        var r = new SpanReader(bytes);
        var t = new TerrainData { Version = r.U8() };
        var items = r.I32();
        if (items < 0) throw new InvalidDataException("negative terrain item count");
        for (var i = 0; i < items; ++i)
        {
            var type = r.U8();
            if (type == 0)
            {
                var d = r.F32Array(3 + 3 + 4);
                var id = r.String();
                t.Items.Add(new BoxTerrainItem(id, d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7], d[8], d[9], i));
            }
            else if (type == 1)
            {
                var id = r.String();
                var pos = r.F32Array(3);
                var pointsCount = r.I32();
                var points = r.F32Array(pointsCount * 3);
                var trianglesCount = r.I32();
                var triangles = r.I32Array(trianglesCount * 3);
                t.Items.Add(new MeshTerrainItem(id, pos[0], pos[1], pos[2], points, triangles, i));
            }
            else throw new InvalidDataException($"unknown terrain item type {type}");
        }
        var obstacles = r.I32();
        for (var i = 0; i < obstacles; ++i)
        {
            var vertices = r.I32();
            var positions = r.F32Array(vertices * 3);
            var indexCount = r.I32();
            var indices = r.I32Array(indexCount);
            var mesh = new MeshData(positions, MeshData.ComputeVertexNormals(positions, indices), indices, "obstacle:mesh");
            if (i == 0) t.ObstacleMesh = mesh; // the web keeps one merged mesh; later ones never occur
        }
        if (t.Version >= 1)
        {
            var groupCount = r.I32();
            for (var i = 0; i < groupCount; ++i)
            {
                var name = r.String();
                var vFirst = r.I32();
                var vCount = r.I32();
                var iFirst = r.I32();
                var iCount = r.I32();
                if (vCount > 0) t.Groups.Add(new ObstacleGroup(name, vFirst, vCount, iFirst, iCount));
            }
        }
        if (!r.IsEnd) throw new InvalidDataException($"terrain stream has {r.Remaining} trailing bytes");
        return t;
    }
}
