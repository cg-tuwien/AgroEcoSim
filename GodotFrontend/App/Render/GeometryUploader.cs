using Godot;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.App.Render;

/// <summary>Turns core MeshData into ArrayMesh resources, cached per geometry id and re-uploaded when the library's version of that id changes.</summary>
public sealed class GeometryUploader
{
    private readonly GeometryLibrary lib;
    private readonly Dictionary<int, (ArrayMesh mesh, int version)> cache = new();

    public GeometryUploader(GeometryLibrary lib)
    {
        this.lib = lib;
    }

    public int VersionOf(int id) => lib.VersionOf(id);

    public ArrayMesh Get(int id)
    {
        var version = lib.VersionOf(id);
        if (cache.TryGetValue(id, out var e) && e.version == version) return e.mesh;
        var mesh = Build(lib.Get(id));
        cache[id] = (mesh, version);
        return mesh;
    }

    public static ArrayMesh Build(MeshData m)
    {
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, ToArrays(m));
        mesh.ResourceName = m.Name;
        return mesh;
    }

    public static Godot.Collections.Array ToArrays(MeshData m)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        var n = m.VertexCount;
        var v = new Vector3[n];
        var nrm = new Vector3[n];
        for (var i = 0; i < n; ++i)
        {
            v[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            nrm[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Normal] = nrm;
        if (m.Uvs != null)
        {
            var uv = new Vector2[n];
            for (var i = 0; i < n; ++i) uv[i] = new Vector2(m.Uvs[i * 2], m.Uvs[i * 2 + 1]);
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
        }
        if (m.Colors != null)
        {
            var c = new Color[n];
            for (var i = 0; i < n; ++i) c[i] = new Color(m.Colors[i * 3], m.Colors[i * 3 + 1], m.Colors[i * 3 + 2]);
            arrays[(int)Mesh.ArrayType.Color] = c;
        }
        // three.js treats counter-clockwise triangles as front faces, Godot clockwise ones: reverse every triangle so
        // back-face culling and FRONT_FACING mean what they mean in the web renderer (the double-sided normal flip
        // of the laminae depends on it).
        var idx = new int[m.Indices.Length];
        for (var t = 0; t + 2 < idx.Length; t += 3)
        {
            idx[t] = m.Indices[t];
            idx[t + 1] = m.Indices[t + 2];
            idx[t + 2] = m.Indices[t + 1];
        }
        arrays[(int)Mesh.ArrayType.Index] = idx;
        return arrays;
    }
}
