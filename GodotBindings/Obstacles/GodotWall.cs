using AgentsSystem;
using Godot;
using NumericHelpers;

namespace Agro;

public partial class Wall : IObstacle
{
    MeshInstance GodotSprite;
    static readonly CubeMesh CubePrimitive = new();
    static readonly SpatialMaterial WallMaterial = new() { AlbedoColor = new(0.95f, 0.95f, 0.95f) };
    public void GodotReady()
    {
        GodotSprite = new();
        SimulationWorld.GodotAddChild(GodotSprite); // Add it as a child of this node.
        GodotSprite.Mesh = CubePrimitive;
        GodotSprite.SetSurfaceMaterial(0, WallMaterial);
        GodotSprite.Translate(Position.ToGodot() + new Vector3(0f, Height * 0.5f, 0f));
        GodotSprite.RotateObjectLocal(Vector3.Up, Orientation);
        GodotSprite.ScaleObjectLocal(new Vector3(Length, Height, Thickness) * 0.5f);
    }
}