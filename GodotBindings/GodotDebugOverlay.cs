using System.Diagnostics;
using AgentsSystem;
using Godot;

namespace Agro;

public class GodotDebugOverlay : TextureRect
{
	//static readonly ColorRect Overlay = new();
	//readonly SpatialMaterial OverlayMaterial = new() { AlbedoColor = new(1f, 1f, 1f, 1f)};
	public GodotDebugOverlay()
	{
		SimulationWorld.GodotAddChild(this); // Add it as a child of this node.
		Expand = true;
		//Material = OverlayMaterial;
		// Mesh = PlanePrimitive;
		// SetSurfaceMaterial(0, GroundMaterial);
		// Translate(new(AgroWorld.FieldSize.X * 0.5f, 2e-6f, AgroWorld.FieldSize.Y * 0.5f));
		// ScaleObjectLocal(new Vector3(AgroWorld.FieldSize.X * 100f, 1f, AgroWorld.FieldSize.X * 100f));
	}

	public float Opacity
	{
		set => SelfModulate = new(1f, 1f, 1f, value);
	}
	public override void _Process(float delta)
	{
		RectSize = new(GetViewportRect().Size);
		base._Process(delta);
	}
}
