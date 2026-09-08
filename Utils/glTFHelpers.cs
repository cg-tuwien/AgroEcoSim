
using System.Numerics;
// using glTFLoader;
// using glTFLoader.Schema;

namespace NumericHelpers;

public static class NumericExtensionsForGltf
{
	public static float[] ToArray(this Vector3 input) => [input.X, input.Y, input.Z];
	public static float[] ToArray(this Quaternion input) => [input.X, input.Y, input.Z, input.W];
}
/*
public static class GlftHelper
{
	public static Gltf Create(List<Node> nodes)
	{
		var scene = new Scene(){ Name = "T0", Nodes = [.. Enumerable.Range(0, nodes.Count)] };
		var gltf = new Gltf {
			Asset = new() { Generator = "Agro", Version = "0.1" },
			Scene = 0,
			Scenes = [scene],
			Nodes = [.. nodes],
			Materials = [
				new Material() {
					DoubleSided = true,
					Name = "Green Tissue",
					PbrMetallicRoughness = new MaterialPbrMetallicRoughness() {
						BaseColorFactor = [0.1f, 0.9f, 0f],
						MetallicFactor = 0,
						RoughnessFactor = 0.9f
					}
				}
			]
		};

		var boxPrimitive = new MeshPrimitive() { Mode = MeshPrimitive.ModeEnum.TRIANGLE_STRIP, Attributes = new(){{"POSITION", 0}, {"NORMAL", 1}} };
		var boxMesh = new Mesh{ Name = "box", Primitives = [boxPrimitive]};
		gltf.Meshes = [boxMesh];

		return gltf;
	}

	public static void Export(Gltf gltf, string filename) => Interface.SaveModel(gltf, filename);
}
*/