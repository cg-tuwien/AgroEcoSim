using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Stereo;

namespace GodotFrontend.App.UI.Panels;

/// <summary>Rendering: how the scene is drawn, as opposed to what it shows (Analysis). The lighting effects and the stereo presentation.</summary>
public partial class RenderingPanel : SidePanelTab
{
    private static readonly StereoMode[] Modes = { StereoMode.Mono, StereoMode.Checkerboard, StereoMode.OpenXr };
    private readonly AppRoot app;
    private readonly RunController c;
    private readonly TransportBar transport;
    private readonly LabeledCheck daylight, shadows, occlusion, indirect, translucency, sky, bounce;
    private readonly ChoiceField mode;
    private readonly SteppedNumberField separation, convergence;
    private readonly LabeledCheck onTarget, swap, pattern;

    public RenderingPanel(AppRoot app) : base("Rendering")
    {
        this.app = app;
        c = app.Controller;
        var v = c.View;
        transport = new TransportBar(c);
        Add(transport);

        Heading("Lighting");
        daylight = Add(new LabeledCheck("Daylight", v.Lighting.Daylight, on => SetLighting(v.Lighting with { Daylight = on })));
        shadows = Add(new LabeledCheck("Sun and shadows", v.Lighting.Shadows, on => SetLighting(v.Lighting with { Shadows = on })));
        occlusion = Add(new LabeledCheck("Occlusion (SSAO)", v.Lighting.Occlusion, on => SetLighting(v.Lighting with { Occlusion = on })));
        indirect = Add(new LabeledCheck("Indirect light (SDFGI)", v.Lighting.Indirect, on => SetLighting(v.Lighting with { Indirect = on })));
        bounce = Add(new LabeledCheck("2 bounces", v.Lighting.SecondBounce, on => SetLighting(v.Lighting with { SecondBounce = on })));
        translucency = Add(new LabeledCheck("Leaf translucency", v.Lighting.LeafTranslucency, on => SetLighting(v.Lighting with { LeafTranslucency = on })));
        sky = Add(new LabeledCheck("Sky", v.Lighting.Sky, on => SetLighting(v.Lighting with { Sky = on })));
        Note("Lighting applies to the natural mapping only; data mappings are unlit readouts.");

        Heading("Stereo");
        mode = Add(new ChoiceField("Mode", new[] { "Mono", "Stereo Checkerboard", "Stereo OpenXR (future)" }, Array.IndexOf(Modes, v.Stereo.Mode),
            i => SetStereo(c.View.Stereo with { Mode = Modes[i] })));
        mode.SetItemDisabled(2, true);
        separation = Add(new SteppedNumberField("Eye separation", v.Stereo.EyeSeparation, new FixedStep(0.005, 0, 10), false, "m"));
        separation.Changed += x => SetStereo(c.View.Stereo with { EyeSeparation = System.Math.Max(0, x) });
        onTarget = Add(new LabeledCheck("Converge on the orbit target", v.Stereo.ConvergeOnTarget, on => SetStereo(c.View.Stereo with { ConvergeOnTarget = on })));
        convergence = Add(new SteppedNumberField("Convergence", v.Stereo.Convergence, new FixedStep(0.25, 0.05, 1000), false, "m"));
        convergence.Changed += x => SetStereo(c.View.Stereo with { Convergence = System.Math.Max(0.05, x) });
        swap = Add(new LabeledCheck("Swap eyes", v.Stereo.SwapEyes, on => SetStereo(c.View.Stereo with { SwapEyes = on })));
        pattern = Add(new LabeledCheck("Test pattern (left red, right blue)", v.Stereo.TestPattern, on => SetStereo(c.View.Stereo with { TestPattern = on })));
        Note("Checkerboard: every pixel shows one eye, counted from the top-left corner - even rows L R L R ..., odd rows R L R L .... " +
             "The interleave needs the view on whole display pixels: F11 toggles fullscreen and F10 hides this panel. " +
             "A window placed at an odd pixel offset swaps the eyes; Swap eyes corrects it, and the test pattern shows which eye sees what.");
    }

    private void SetLighting(LightingOptions o)
    {
        c.View.Lighting = o;
        app.ViewChanged();
    }

    private void SetStereo(StereoOptions o)
    {
        c.View.Stereo = o;
        app.ViewChanged();
    }

    public override void Refresh()
    {
        transport.Refresh();
        var v = c.View;
        var natural = v.Mapping == VisualMapping.Natural;
        foreach (var (box, on) in new[] { (daylight, v.Lighting.Daylight), (shadows, v.Lighting.Shadows), (occlusion, v.Lighting.Occlusion),
                     (indirect, v.Lighting.Indirect), (bounce, v.Lighting.SecondBounce), (translucency, v.Lighting.LeafTranslucency), (sky, v.Lighting.Sky) })
        {
            box.SetPressedNoSignal(on);
            box.Disabled = !natural;
        }
        bounce.Disabled = !natural || !v.Lighting.Indirect;

        var s = v.Stereo;
        mode.SelectSilently(Array.IndexOf(Modes, s.Mode));
        separation.SetSilently(s.EyeSeparation);
        onTarget.SetPressedNoSignal(s.ConvergeOnTarget);
        convergence.SetSilently(s.Convergence);
        swap.SetPressedNoSignal(s.SwapEyes);
        pattern.SetPressedNoSignal(s.TestPattern);
        var active = s.Active;
        separation.Disabled = !active;
        onTarget.Disabled = !active;
        convergence.Disabled = !active || s.ConvergeOnTarget;
        swap.Disabled = !active;
        pattern.Disabled = !active;
    }
}
