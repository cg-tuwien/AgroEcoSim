using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Geometry;

/// <summary>render/core/DebugBox.ts: a 2x2x2 box with a distinct vertex colour per face.</summary>
public static class DebugBoxMesh
{
    public static MeshData Build()
    {
        float[] left = { 1, 0.0f, 0.1f }, right = { 1, 0.1f, 0.1f }, bottom = { 0.1f, 1, 0.1f }, top = { 0, 1, 0 }, front = { 0.1f, 0.1f, 1 }, back = { 0, 0, 1 };
        (float[] pos, float[] norm, float[] color)[] verts =
        {
            (new float[] { -1, -1, 1 }, new float[] { 0, 0, 1 }, front), (new float[] { 1, -1, 1 }, new float[] { 0, 0, 1 }, front),
            (new float[] { -1, 1, 1 }, new float[] { 0, 0, 1 }, front), (new float[] { 1, 1, 1 }, new float[] { 0, 0, 1 }, front),
            (new float[] { 1, -1, 1 }, new float[] { 1, 0, 0 }, right), (new float[] { 1, -1, -1 }, new float[] { 1, 0, 0 }, right),
            (new float[] { 1, 1, 1 }, new float[] { 1, 0, 0 }, right), (new float[] { 1, 1, -1 }, new float[] { 1, 0, 0 }, right),
            (new float[] { 1, -1, -1 }, new float[] { 0, 0, -1 }, back), (new float[] { -1, -1, -1 }, new float[] { 0, 0, -1 }, back),
            (new float[] { 1, 1, -1 }, new float[] { 0, 0, -1 }, back), (new float[] { -1, 1, -1 }, new float[] { 0, 0, -1 }, back),
            (new float[] { -1, -1, -1 }, new float[] { -1, 0, 0 }, left), (new float[] { -1, -1, 1 }, new float[] { -1, 0, 0 }, left),
            (new float[] { -1, 1, -1 }, new float[] { -1, 0, 0 }, left), (new float[] { -1, 1, 1 }, new float[] { -1, 0, 0 }, left),
            (new float[] { 1, 1, -1 }, new float[] { 0, 1, 0 }, top), (new float[] { -1, 1, -1 }, new float[] { 0, 1, 0 }, top),
            (new float[] { 1, 1, 1 }, new float[] { 0, 1, 0 }, top), (new float[] { -1, 1, 1 }, new float[] { 0, 1, 0 }, top),
            (new float[] { 1, -1, 1 }, new float[] { 0, -1, 0 }, bottom), (new float[] { -1, -1, 1 }, new float[] { 0, -1, 0 }, bottom),
            (new float[] { 1, -1, -1 }, new float[] { 0, -1, 0 }, bottom), (new float[] { -1, -1, -1 }, new float[] { 0, -1, 0 }, bottom),
        };
        var positions = new float[verts.Length * 3];
        var normals = new float[verts.Length * 3];
        var colors = new float[verts.Length * 3];
        for (var i = 0; i < verts.Length; ++i)
        {
            Array.Copy(verts[i].pos, 0, positions, i * 3, 3);
            Array.Copy(verts[i].norm, 0, normals, i * 3, 3);
            Array.Copy(verts[i].color, 0, colors, i * 3, 3);
        }
        int[] indices =
        {
            0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7, 8, 9, 10, 10, 9, 11,
            12, 13, 14, 14, 13, 15, 16, 17, 18, 18, 17, 19, 20, 21, 22, 22, 21, 23,
        };
        return new MeshData(positions, normals, indices, "DebugBox", null, colors);
    }
}
