using Godot;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.App.Render;

/// <summary>
/// Draws the soil: the configured field box when no terrain is loaded (BoxCorner scaled (X, D, Z) at (0, -D, 0)),
/// otherwise the trays of the terrain stream (box trays at (px, py - sy, pz), mesh trays translate-only) and the
/// merged imported obstacle mesh with its group table kept for painting.
/// </summary>
public sealed class TerrainBuilder
{
    private readonly Node3D root;
    private readonly GeometryUploader geometry;
    private readonly MaterialSet materials;
    private readonly List<MeshInstance3D> trays = new();
    private MeshInstance3D? obstacles;
    private bool traysVisible = true, obstaclesVisible = true;

    public TerrainData? Terrain { get; private set; }
    public int TrayCount => trays.Count;
    public int ObstacleGroupCount => Terrain?.Groups.Count ?? 0;

    public TerrainBuilder(Node3D root, GeometryUploader geometry, MaterialSet materials)
    {
        this.root = root;
        this.geometry = geometry;
        this.materials = materials;
    }

    public void SetVisible(bool showTrays, bool showObstacles)
    {
        traysVisible = showTrays;
        obstaclesVisible = showObstacles;
        foreach (var t in trays) t.Visible = showTrays;
        if (obstacles != null) obstacles.Visible = showObstacles;
    }

    /// <summary>The plain field box the web viewport draws from the request's field size when it has no terrain.</summary>
    public void ShowFieldBox(float sizeX, float sizeD, float sizeZ)
    {
        Clear();
        var box = new MeshInstance3D
        {
            Name = "FieldBox",
            Mesh = geometry.Get(Geom.BoxCorner),
            MaterialOverride = materials.Terrain,
            Transform = new Transform3D(new Basis(new Vector3(sizeX, 0, 0), new Vector3(0, sizeD, 0), new Vector3(0, 0, sizeZ)), new Vector3(0, -sizeD, 0)),
            Visible = traysVisible,
        };
        root.AddChild(box);
        trays.Add(box);
    }

    public void Show(TerrainData terrain)
    {
        Clear();
        Terrain = terrain;
        foreach (var item in terrain.Items)
        {
            MeshInstance3D node;
            switch (item)
            {
                case BoxTerrainItem b:
                    node = new MeshInstance3D
                    {
                        Name = $"tray_{b.Index}",
                        Mesh = geometry.Get(Geom.BoxCorner),
                        Transform = new Transform3D(new Basis(new Vector3(b.Sx, 0, 0), new Vector3(0, b.Sy, 0), new Vector3(0, 0, b.Sz)), new Vector3(b.Px, b.Py - b.Sy, b.Pz)),
                    };
                    break;
                case MeshTerrainItem m:
                    node = new MeshInstance3D
                    {
                        Name = $"tray_{m.Index}",
                        Mesh = GeometryUploader.Build(m.Mesh),
                        Transform = new Transform3D(Basis.Identity, new Vector3(m.Px, m.Py, m.Pz)),
                    };
                    break;
                default:
                    continue;
            }
            node.MaterialOverride = materials.Terrain;
            node.Visible = traysVisible;
            root.AddChild(node);
            trays.Add(node);
        }
        if (terrain.ObstacleMesh != null)
        {
            obstacles = new MeshInstance3D
            {
                Name = "ImportedObstacles",
                Mesh = GeometryUploader.Build(terrain.ObstacleMesh),
                MaterialOverride = materials.Obstacle,
                Visible = obstaclesVisible,
            };
            root.AddChild(obstacles);
        }
    }

    /// <summary>Removes the trays, the field box and the imported obstacles.</summary>
    public void Clear()
    {
        Terrain = null;
        foreach (var t in trays) t.QueueFree();
        trays.Clear();
        obstacles?.QueueFree();
        obstacles = null;
    }
}
