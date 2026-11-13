using Godot;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Picking;

namespace GodotFrontend.App.Render;

/// <summary>The seeds of the request as small dodecahedra, coloured by their selection state (Seed.ts palette #008).</summary>
public sealed class SeedMarkers
{
    private readonly MultiMeshInstance3D node;
    private readonly MultiMesh mm;
    private static readonly RGB Base = ColorMath.FromHex("#000088");
    private static readonly RGB Hover = ColorMath.LerpHSL(Base, GodotFrontend.Core.Color.Theme.NeutralColor, 0.1);
    private static readonly RGB Select = ColorMath.LerpHSL(Base, GodotFrontend.Core.Color.Theme.NeutralColor, 0.2);

    public SeedMarkers(Node3D parent, GeometryUploader geometry, ShaderMaterial material)
    {
        mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = geometry.Get(Geom.Dodecahedron),
        };
        node = new MultiMeshInstance3D { Name = "Seeds", Multimesh = mm, MaterialOverride = material };
        parent.AddChild(node);
    }

    public bool Visible { set => node.Visible = value; }

    public void Update(IReadOnlyList<(GodotFrontend.Core.Math.Vec3 position, SelectionState state)> seeds)
    {
        mm.InstanceCount = seeds.Count;
        var custom = new Color(0, 0, 0, 4); // organ class 2 (cylinder), no colour flag: readouts never apply
        for (var i = 0; i < seeds.Count; ++i)
        {
            var (p, st) = seeds[i];
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3((float)p.X, (float)p.Y, (float)p.Z)));
            var c = st switch { SelectionState.Hover => Hover, SelectionState.None => Base, _ => Select };
            mm.SetInstanceColor(i, new Color(c.R, c.G, c.B));
            mm.SetInstanceCustomData(i, custom);
        }
    }
}
