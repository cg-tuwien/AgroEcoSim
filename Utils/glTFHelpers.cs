
using System.Numerics;
using System.Collections.Generic;
using glTFLoader;
using glTFLoader.Schema;

namespace NumericHelpers;

public static class NumericExtensionsForGltf
{
    public static float[] ToArray(this Vector3 input) => new[]{input.X, input.Y, input.Z};
    public static float[] ToArray(this Quaternion input) => new[]{input.X, input.Y, input.Z, input.W};
}

public static class GlftHelper
{
    public static Gltf Create(List<Node> nodes)
    {
        var gltf = new Gltf();
        gltf.Asset = new(){ Generator = "Agro", Version="0.1" };
        gltf.Scene = 0;

        var scene = new Scene(){ Name = "T0", Nodes = Enumerable.Range(0, nodes.Count).ToArray()};
        gltf.Scenes = new Scene[]{scene};

        gltf.Nodes = nodes.ToArray();

        gltf.Materials = new Material[]{ new Material(){
            DoubleSided = true,
            Name = "Green Tissue",
            PbrMetallicRoughness = new MaterialPbrMetallicRoughness() {
                BaseColorFactor = new float[] { 0.1f, 0.9f, 0f},
                MetallicFactor = 0,
                RoughnessFactor = 0.9f
            }
        }};

        var boxPrimitive = new MeshPrimitive(){ Mode = MeshPrimitive.ModeEnum.TRIANGLE_STRIP, Attributes = new(){{"POSITION", 0}, {"NORMAL", 1}} };
        var boxMesh = new Mesh{ Name = "box", Primitives = new MeshPrimitive[]{boxPrimitive}};
        gltf.Meshes = new Mesh[] { boxMesh };

        return gltf;
    }

    public static void Export(Gltf gltf, string filename) => Interface.SaveModel(gltf, filename);
}