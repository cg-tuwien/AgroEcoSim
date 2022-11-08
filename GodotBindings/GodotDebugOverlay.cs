using AgentsSystem;
using Godot;

namespace Agro;

public class GodotDebugOverlay : TextureRect
{
    //static readonly ColorRect Overlay = new();
    //static readonly SpatialMaterial GroundMaterial = new() { AlbedoColor = new(0.2f, 0.1f, 0.02f) };
    public GodotDebugOverlay()
    {
        SimulationWorld.GodotAddChild(this); // Add it as a child of this node.
        Expand = true;
        // Mesh = PlanePrimitive;
        // SetSurfaceMaterial(0, GroundMaterial);
        // Translate(new(AgroWorld.FieldSize.X * 0.5f, 2e-6f, AgroWorld.FieldSize.Y * 0.5f));
        // ScaleObjectLocal(new Vector3(AgroWorld.FieldSize.X * 100f, 1f, AgroWorld.FieldSize.X * 100f));
    }

    public override void _Process(float delta)
    {
        RectSize = new(GetViewportRect().Size);
        base._Process(delta);
    }
}