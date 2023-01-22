using AgentsSystem;
using Godot;

namespace Agro;

public partial class GodotGround : MeshInstance3D
{
	static readonly PlaneMesh PlanePrimitive = new();
	static readonly Color GroundColor = new(0.2f, 0.1f, 0.02f);
	public GodotGround()
	{
		SimulationWorld.GodotAddChild(this); // Add it as a child of this node.
		Mesh = PlanePrimitive;
		var material = AgroWorldGodot.UnshadedMaterial();
		material.SetShaderParameter(AgroWorldGodot.COLOR, GroundColor);
		MaterialOverride = material;

		Translate(new(AgroWorld.FieldSize.X * 0.5f, 2e-6f, AgroWorld.FieldSize.Y * 0.5f));
		ScaleObjectLocal(new Vector3(AgroWorld.FieldSize.X * 100f, 1f, AgroWorld.FieldSize.X * 100f));
	}
}
