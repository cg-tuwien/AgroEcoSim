using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Run;

namespace GodotFrontend.App.UI.Panels;

/// <summary>Analysis: what the scene shows - the second transport bar, the data mapping, the visibility toggles and the picked details.
/// How it is drawn (lighting, stereo) is the Rendering tab.</summary>
public partial class AnalysisPanel : SidePanelTab
{
    private static readonly VisualMapping[] Mappings =
    {
        VisualMapping.Natural, VisualMapping.Water, VisualMapping.Energy, VisualMapping.Auxins, VisualMapping.Cytokinins,
        VisualMapping.Irradiance, VisualMapping.Resource, VisualMapping.Production,
    };
    private readonly AppRoot app;
    private readonly RunController c;
    private readonly TransportBar transport;
    private readonly ChoiceField mapping;
    private readonly LabeledCheck leaves, terrain, roots, seeds, obstacles, picking;
    private readonly Label details = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };

    public AnalysisPanel(AppRoot app) : base("Analysis")
    {
        this.app = app;
        c = app.Controller;
        var v = c.View;
        transport = new TransportBar(c);
        Add(transport);
        Heading("Visual mapping");
        mapping = Add(new ChoiceField("Mapping", Mappings.Select(PlantColors.MappingName).ToArray(), Array.IndexOf(Mappings, v.Mapping),
            i => { v.Mapping = Mappings[i]; app.ViewChanged(); }));
        Heading("Show");
        leaves = Add(new LabeledCheck("Leaves", v.ShowLeaves, on => { v.ShowLeaves = on; app.ViewChanged(); }));
        terrain = Add(new LabeledCheck("Terrain", v.ShowTerrain, on => { v.ShowTerrain = on; app.ViewChanged(); }));
        roots = Add(new LabeledCheck("Roots", v.ShowRoots, on => { v.ShowRoots = on; app.ViewChanged(); }));
        seeds = Add(new LabeledCheck("Seeds", v.ShowSeeds, on => { v.ShowSeeds = on; app.ViewChanged(); }));
        obstacles = Add(new LabeledCheck("Obstacles", v.ShowObstacles, on => { v.ShowObstacles = on; app.ViewChanged(); }));
        picking = Add(new LabeledCheck("Mouse picking", v.PerformPicking, on => { v.PerformPicking = on; app.ViewChanged(); }));
        Heading("Picked");
        Add(details);
    }

    public override void Refresh()
    {
        transport.Refresh();
        var v = c.View;
        mapping.SelectSilently(Array.IndexOf(Mappings, v.Mapping));
        leaves.SetPressedNoSignal(v.ShowLeaves);
        terrain.SetPressedNoSignal(v.ShowTerrain);
        roots.SetPressedNoSignal(v.ShowRoots);
        seeds.SetPressedNoSignal(v.ShowSeeds);
        obstacles.SetPressedNoSignal(v.ShowObstacles);
        picking.SetPressedNoSignal(v.PerformPicking);
        details.Text = app.PickedDetailsText();
    }
}
