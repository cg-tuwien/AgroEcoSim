using Agro;
using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Time;

namespace GodotFrontend.App.UI.Panels;

/// <summary>
/// Simulation (pass 1 subset of the web's Simulation tab plus the seed shortcuts): time step and horizon, start date,
/// seed, render mode, contact engine, flags, field size, and the seeds - one at the centre or an even raster.
/// </summary>
public partial class SimulationPanel : SidePanelTab
{
    private static readonly double[] HoursTable = { 1, 2, 3, 4, 6, 8, 12, 24, 48, 72, 96, 120, 144, 168 };
    private readonly RunController c;
    private readonly SteppedNumberField hoursPerTick, totalHours, fieldX, fieldZ, fieldD, spacing;
    private readonly TextField initialDate, seed;
    private readonly LabeledCheck randomize, deterministic, downloadRoots, exactPreview;
    private readonly ChoiceField renderMode, contactEngine, species;
    private readonly Label seedsLabel = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private int speciesVersion = -1;

    private sealed class MagnitudeStep : IStepPolicy
    {
        private static double StepOf(double v) => v < 24 ? 1 : v < 720 ? 24 : v < 8760 ? 720 : 8760;
        public double Up(double v) => v + StepOf(v);
        public double Down(double v) => System.Math.Max(1, v - StepOf(v - 1));
    }

    public SimulationPanel(AppRoot app) : base("Simulation")
    {
        c = app.Controller;
        var r = c.Request;
        Heading("Time");
        hoursPerTick = Add(new SteppedNumberField("Hours per tick", r.HoursPerTick, new TableStep(HoursTable), true, "h"));
        hoursPerTick.Changed += v => { r.HoursPerTick = (int)v; app.Invalidate(); };
        totalHours = Add(new SteppedNumberField("Total hours", r.TotalHours, new MagnitudeStep(), true, "h"));
        totalHours.Changed += v => { r.TotalHours = (int)v; app.Invalidate(); };
        initialDate = Add(new TextField("Initial date", r.InitialDate, t => { r.InitialDate = SimClock.ParseDate(t).ToString("yyyy-MM-dd"); app.Invalidate(); }));
        Heading("Run");
        seed = Add(new TextField("Seed", r.InitNumber, t => { if (ulong.TryParse(t.Trim(), out _)) r.InitNumber = t.Trim(); app.Invalidate(); }));
        randomize = Add(new LabeledCheck("Randomized initialization", r.Randomize, on => { r.Randomize = on; app.Invalidate(); }));
        renderMode = Add(new ChoiceField("Render mode", new[] { "Ambient", "Mitsuba3", "Tamashii", "Internal (CPU)" }, r.RenderMode, i => r.RenderMode = (byte)i));
        contactEngine = Add(new ChoiceField("Contact engine", new[] { "Certified", "Soft", "Mixed" }, (int)r.ContactEngine, i => r.ContactEngine = (ContactEngineKind)i));
        deterministic = Add(new LabeledCheck("Sequential tick (diagnostic, slower)", r.Deterministic, on => r.Deterministic = on));
        downloadRoots = Add(new LabeledCheck("Download roots", r.DownloadRoots, on => r.DownloadRoots = on));
        exactPreview = Add(new LabeledCheck("Exact preview (can stall the server)", r.ExactPreview, on => r.ExactPreview = on));
        Heading("Field");
        fieldX = Add(new SteppedNumberField("Size X", r.FieldSizeX, new FixedStep(1, 0.1, 1000), false, "m"));
        fieldX.Changed += v => { r.FieldSizeX = (float)v; app.FieldChanged(); };
        fieldZ = Add(new SteppedNumberField("Size Z", r.FieldSizeZ, new FixedStep(1, 0.1, 1000), false, "m"));
        fieldZ.Changed += v => { r.FieldSizeZ = (float)v; app.FieldChanged(); };
        fieldD = Add(new SteppedNumberField("Depth", r.FieldSizeD, new FixedStep(0.5, 0.1, 100), false, "m"));
        fieldD.Changed += v => { r.FieldSizeD = (float)v; app.FieldChanged(); };
        Heading("Seeds");
        species = Add(new ChoiceField("Species", Array.Empty<string>(), 0, i => speciesIndex = i));
        Add(seedsLabel);
        var row = Add(new HBoxContainer());
        var centre = new Button { Text = "One at the centre" };
        centre.Pressed += () =>
        {
            r.Seeds.Clear();
            r.AddDefaultSeed(SelectedSpecies());
            app.SeedsChanged();
        };
        row.AddChild(centre);
        var clear = new Button { Text = "Clear" };
        clear.Pressed += () => { r.Seeds.Clear(); app.SeedsChanged(); };
        row.AddChild(clear);
        spacing = Add(new SteppedNumberField("Raster spacing", 0.165, new FixedStep(0.005, 0.01, 10), false, "m"));
        var raster = Add(new Button { Text = "Evenly distributed seeds" });
        raster.Pressed += () => { AddRaster(r, spacing.Value, SelectedSpecies()); app.SeedsChanged(); };
    }

    private string SelectedSpecies()
    {
        var list = c.Request.Species;
        if (list.Count == 0) return "Default";
        var i = System.Math.Clamp(speciesIndex, 0, list.Count - 1);
        return list[i].Name;
    }

    private int speciesIndex;

    /// <summary>appstate.pushSeedRaster over the plain field and over box trays (the selected species instead of a random one).</summary>
    private void AddRaster(GodotFrontend.Core.Model.RequestState r, double dist, string sp)
    {
        void Grid(double sx, double sz, double y, int field)
        {
            var xCount = System.Math.Max(1, (int)System.Math.Round(sx / dist));
            var zCount = System.Math.Max(1, (int)System.Math.Round(sz / dist));
            double xd = sx / xCount, zd = sz / zCount;
            for (var x = 0; x < xCount; ++x)
                for (var z = 0; z < zCount; ++z)
                    r.Seeds.Add(new GodotFrontend.Core.Model.SeedModel { Species = sp, Px = xd * 0.5 + x * xd, Py = y, Pz = zd * 0.5 + z * zd, Field = field });
        }
        var terrain = c.Terrain;
        if (terrain is { Items.Count: > 0 })
        {
            foreach (var item in terrain.Items)
                if (item is GodotFrontend.Core.Scene.BoxTerrainItem b) Grid(b.Sx, b.Sz, -0.02, b.Index);
        }
        else Grid(r.FieldSizeX, r.FieldSizeZ, -0.02, 0);
    }

    public override void Refresh()
    {
        var r = c.Request;
        hoursPerTick.SetSilently(r.HoursPerTick);
        var snapped = SimClock.Snap(r.HoursPerTick);
        hoursPerTick.Note = snapped != r.HoursPerTick ? $"runs as {snapped}" : "";
        totalHours.SetSilently(r.TotalHours);
        totalHours.Note = SimClock.EncodeTime(r.TotalHours);
        initialDate.Text = r.InitialDate;
        seed.Text = r.InitNumber;
        seed.Editable = !r.Randomize;
        randomize.SetPressedNoSignal(r.Randomize);
        renderMode.SelectSilently(r.RenderMode);
        contactEngine.SelectSilently((int)r.ContactEngine);
        deterministic.SetPressedNoSignal(r.Deterministic);
        downloadRoots.SetPressedNoSignal(r.DownloadRoots);
        exactPreview.SetPressedNoSignal(r.ExactPreview);
        fieldX.SetSilently(r.FieldSizeX);
        fieldZ.SetSilently(r.FieldSizeZ);
        fieldD.SetSilently(r.FieldSizeD);
        var version = r.Species.Count * 31 + (r.Species.Count > 0 ? r.Species[0].Name.GetHashCode() : 0);
        if (version != speciesVersion)
        {
            speciesVersion = version;
            var names = r.Species.Select(s => string.IsNullOrEmpty(s.Aka) ? s.Name : $"{s.Name} ({s.Aka})").ToArray();
            species.SetItems(names, System.Math.Min(speciesIndex, System.Math.Max(0, names.Length - 1)));
        }
        var groups = r.Seeds.GroupBy(s => s.Species).Select(g => $"{g.Count()} x {g.Key}");
        seedsLabel.Text = r.Seeds.Count == 0 ? "no seeds - add some to simulate" : $"{r.Seeds.Count} seeds: " + string.Join(", ", groups);
    }
}
