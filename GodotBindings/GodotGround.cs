using AgentsSystem;
using Godot;

namespace Agro;

public class GodotGround : MeshInstance
{
    static readonly PlaneMesh PlanePrimitive = new();
    static readonly SpatialMaterial GroundMaterial = new() { AlbedoColor = new(0.2f, 0.1f, 0.02f) };
    public GodotGround()
    {
        SimulationWorld.GodotAddChild(this); // Add it as a child of this node.
        Mesh = PlanePrimitive;
        SetSurfaceMaterial(0, GroundMaterial);
        Translate(new(AgroWorld.FieldSize.X * 0.5f, 2e-6f, AgroWorld.FieldSize.Y * 0.5f));
        ScaleObjectLocal(new Vector3(AgroWorld.FieldSize.X * 100f, 1f, AgroWorld.FieldSize.X * 100f));
    }
}