using AgentsSystem;
using Godot;
using NumericHelpers;

namespace Agro;

public partial class Wall : IObstacle
{
    MeshInstance3D GodotSprite;
    static readonly BoxMesh CubePrimitive = new();
    static readonly Color WallColor = new(0.95f, 0.95f, 0.95f);
    static readonly ShaderMaterial WallMaterial = AgroWorldGodot.ShadedMaterial();

    public void GodotReady()
    {
        GodotSprite = new();
        SimulationWorld.GodotAddChild(GodotSprite); // Add it as a child of this node.
        GodotSprite.Mesh = CubePrimitive;
        WallMaterial.SetShaderParameter(AgroWorldGodot.COLOR, WallColor);
        GodotSprite.MaterialOverride = WallMaterial;
        GodotSprite.Translate(Position.ToGodot() + new Vector3(0f, Height * 0.5f, 0f));
        GodotSprite.RotateObjectLocal(Vector3.Up, Orientation);
        GodotSprite.ScaleObjectLocal(new Vector3(Length, Height, Thickness));
    }
}