using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;
using GodotFrontend.Core.Run;

namespace GodotFrontend.App.UI.Panels;

/// <summary>Analysis: the second transport bar, the data mapping, the visibility toggles, the lighting switches and the picked details.</summary>
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
    private readonly LabeledCheck daylight, shadows, occlusion, indirect, translucency, sky, bounce;
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
        Heading("Lighting");
        daylight = Add(new LabeledCheck("Daylight", v.Lighting.Daylight, on => SetLighting(v.Lighting with { Daylight = on })));
        shadows = Add(new LabeledCheck("Sun and shadows", v.Lighting.Shadows, on => SetLighting(v.Lighting with { Shadows = on })));
        occlusion = Add(new LabeledCheck("Occlusion (SSAO)", v.Lighting.Occlusion, on => SetLighting(v.Lighting with { Occlusion = on })));
        indirect = Add(new LabeledCheck("Indirect light (SDFGI)", v.Lighting.Indirect, on => SetLighting(v.Lighting with { Indirect = on })));
        bounce = Add(new LabeledCheck("2 bounces", v.Lighting.SecondBounce, on => SetLighting(v.Lighting with { SecondBounce = on })));
        translucency = Add(new LabeledCheck("Leaf translucency", v.Lighting.LeafTranslucency, on => SetLighting(v.Lighting with { LeafTranslucency = on })));
        sky = Add(new LabeledCheck("Sky", v.Lighting.Sky, on => SetLighting(v.Lighting with { Sky = on })));
        Note("Lighting applies to the natural mapping only; data mappings are unlit readouts.");
        Heading("Picked");
        Add(details);
    }

    private void SetLighting(LightingOptions o)
    {
        c.View.Lighting = o;
        app.ViewChanged();
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
        var natural = v.Mapping == VisualMapping.Natural;
        foreach (var (box, on) in new[] { (daylight, v.Lighting.Daylight), (shadows, v.Lighting.Shadows), (occlusion, v.Lighting.Occlusion),
                     (indirect, v.Lighting.Indirect), (bounce, v.Lighting.SecondBounce), (translucency, v.Lighting.LeafTranslucency), (sky, v.Lighting.Sky) })
        {
            box.SetPressedNoSignal(on);
            box.Disabled = !natural;
        }
        bounce.Disabled = !natural || !v.Lighting.Indirect;
        details.Text = app.PickedDetailsText();
    }
}
