namespace GodotFrontend.Core.Scene;

/// <summary>
/// The per-species lamina meshes served by REST (helpers/Species.ts readLaminaFrom): GET mesh/leaf/{name}/slices
/// is a u8 count followed by that many meshes, GET mesh/petal/{name} is one mesh; a mesh is i32 vertex count,
/// f32 xyz..., i32 index count, i32 indices... with x and y normalised to the organ's half length and half width.
/// </summary>
public static class LaminaMeshReader
{
    public static MeshData ReadOne(ReadOnlySpan<byte> bytes, string name)
    {
        var r = new SpanReader(bytes);
        var mesh = Read(ref r, name);
        if (!r.IsEnd) throw new InvalidDataException($"lamina mesh '{name}' has trailing bytes");
        return mesh;
    }

    public static MeshData[] ReadSlices(ReadOnlySpan<byte> bytes, string name)
    {
        var r = new SpanReader(bytes);
        var count = r.U8();
        var slices = new MeshData[count];
        for (var i = 0; i < count; ++i) slices[i] = Read(ref r, $"{name}/{i}");
        if (!r.IsEnd) throw new InvalidDataException($"lamina slices '{name}' have trailing bytes");
        return slices;
    }

    private static MeshData Read(ref SpanReader r, string name)
    {
        var vertices = r.I32();
        if (vertices < 0 || vertices > 1_000_000) throw new InvalidDataException("invalid lamina vertex count");
        var positions = r.F32Array(vertices * 3);
        var indexCount = r.I32();
        if (indexCount < 0 || indexCount % 3 != 0) throw new InvalidDataException("invalid lamina index count");
        var indices = r.I32Array(indexCount);
        return new MeshData(positions, MeshData.ComputeVertexNormals(positions, indices), indices, name);
    }
}
