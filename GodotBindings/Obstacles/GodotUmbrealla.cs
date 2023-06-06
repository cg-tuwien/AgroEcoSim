using Godot;
using AgentsSystem;
using NumericHelpers;

namespace Agro;

public partial class Umbrella : IObstacle
{
    MeshInstance3D GodotPoleSprite;
    MeshInstance3D GodotDiskSprite;
    static readonly BoxMesh CubePrimitive = new() { Material = AgroWorldGodot.ShadedMaterial };
    static readonly CylinderMesh CylinderPrimitive = new() { Material = AgroWorldGodot.ShadedMaterial };
    static readonly Color UmbrellaColor = new (0.95f, 0.95f, 0.95f);
    public void GodotReady()
    {
        GodotPoleSprite = new();
        SimulationWorld.GodotAddChild(GodotPoleSprite); // Add it as a child of this node.
        GodotPoleSprite.Mesh = CubePrimitive;
        GodotPoleSprite.Translate(Position.ToGodot() + new Vector3(0f, Height * 0.5f, 0f));
        GodotPoleSprite.ScaleObjectLocal(new (Thickness, Height, Thickness));

        GodotDiskSprite = new();
        SimulationWorld.GodotAddChild(GodotDiskSprite);
        GodotDiskSprite.Mesh = CylinderPrimitive;
        GodotDiskSprite.Translate(Position.ToGodot() + new Vector3(0f, Height, 0f));
        var diameter = 2f * Radius;
        GodotDiskSprite.ScaleObjectLocal(new (diameter, 0.01f, diameter));

        GodotPoleSprite.SetInstanceShaderParameter(AgroWorldGodot.COLOR, UmbrellaColor);
        GodotDiskSprite.SetInstanceShaderParameter(AgroWorldGodot.COLOR, UmbrellaColor);
    }
}