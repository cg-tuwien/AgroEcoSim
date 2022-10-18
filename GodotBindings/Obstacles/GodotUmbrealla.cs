using Godot;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public partial class Umbrella : IObstacle
{
    MeshInstance GodotPoleSprite;
    MeshInstance GodotDiskSprite;
    static readonly CubeMesh CubePrimitive = new();
    static readonly CylinderMesh CylinderPrimitive = new();
    static readonly SpatialMaterial UmbrellaMaterial = new() { AlbedoColor = new Color(0.95f, 0.95f, 0.95f) };
    public void GodotReady()
    {
        GodotPoleSprite = new();
        SimulationWorld.GodotAddChild(GodotPoleSprite); // Add it as a child of this node.
        GodotPoleSprite.Mesh = CubePrimitive;
        GodotPoleSprite.SetSurfaceMaterial(0, UmbrellaMaterial);
        var scale = new Vector3(Thickness, Height, Thickness) * 0.5f;
        GodotPoleSprite.Translate(Position.ToGodot() + scale);
        GodotPoleSprite.ScaleObjectLocal(scale);

        GodotDiskSprite = new();
        SimulationWorld.GodotAddChild(GodotDiskSprite);
        GodotDiskSprite.Mesh = CylinderPrimitive;
        GodotDiskSprite.SetSurfaceMaterial(0, UmbrellaMaterial);
        GodotDiskSprite.Translate(Position.ToGodot() + new Vector3(0f, Height, 0f));
        GodotDiskSprite.ScaleObjectLocal(new (Radius, 0.005f, Radius));
    }
}