using Godot;

namespace GodotFrontend.App;

/// <summary>
/// --transition-probe DIR: checks what the window shows while the 3D view changes size. After the session has loaded
/// it presses F10 twice (side panel off and on) and resizes the window twice (what F11 does to it, without taking over
/// the screen), and grabs every frame Godot actually draws after each step. Nothing else touches the window, so in
/// low-processor mode a change that causes no draw shows up as a step with no frames.
///
/// Every drawn frame must be either the image from before the step, placed by <see cref="InputRouter.HoldRect"/>, or
/// the settled image at the new size; anything else is a frame that showed something never rendered for it. For a
/// side-panel step, where the height stays, the settled image must also agree with the old one where they overlap -
/// the proof that the held image previews the new one. Prints PASS/FAIL lines and saves the first and last frames of
/// each step, and any failing frame, to DIR. PROBE_TRACE=1 prints the process/draw timeline of every step,
/// PROBE_NOGRAB=1 skips the readbacks (timing only), PROBE_STEP=S sets the seconds per step.
/// </summary>
public partial class TransitionProbe : Node
{
    private sealed record Shot(byte[] Rgba, int Width, int Height, Vector2I World, Rect2 Panel, double Ms, string File, Vector2I Root = default);

    private readonly AppRoot app;
    private readonly string dir;
    private string step = "";
    private ulong stepUs;
    private Shot? before;
    private readonly List<Shot> frames = new();
    private int sequence, failures;
    private readonly List<string> ticks = new();
    private readonly List<double> drawTimes = new();
    // Three seconds per step. With 1.6 s, the readbacks of the previous step's frames were followed by a main-loop stall
    // of over a second right after the next change (no stall without readbacks), which says nothing about the app.
    private static readonly double StepSeconds = double.TryParse(System.Environment.GetEnvironmentVariable("PROBE_STEP"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 3.0;
    /// <summary>When the view or the window actually changed size after the step's action; 0 until it has.</summary>
    private ulong changedUs;
    private Vector2I drawnRoot;

    private double drawnMs = -1;
    private Vector2I drawnWorld;
    private Rect2 drawnPanel;

    public override void _Process(double delta)
    {
        if (step.Length > 0 && ticks.Count < 40) ticks.Add($"p{(Time.GetTicksUsec() - stepUs) / 1000.0:F0}");
        // The frame drawn last is still in the window's texture until the next draw; read it here rather than inside
        // FramePostDraw, where a readback stalled the main loop for over a second right after the side panel
        // reappeared (an artefact of the probe: without readbacks every frame came at the display's pace).
        if (drawnMs < 0) return;
        var ms = drawnMs;
        drawnMs = -1;
        if (step.Length == 0) return;
        var t0 = Time.GetTicksUsec();
        frames.Add(Grab(ms, $"{sequence++:000}-{ms:0000}ms.png") with { World = drawnWorld, Panel = drawnPanel, Root = drawnRoot });
        if (ticks.Count < 40) ticks.Add($"g{ms:F0}({(Time.GetTicksUsec() - t0) / 1000.0:F0})");
    }

    public TransitionProbe(AppRoot app, string dir)
    {
        this.app = app;
        this.dir = dir;
        Name = "TransitionProbe";
    }

    public override void _Ready()
    {
        System.IO.Directory.CreateDirectory(dir);
        RenderingServer.FramePostDraw += OnDrawn;
        app.ViewportPanel.Resized += Changed;
        GetTree().Root.SizeChanged += Changed;
        _ = Run();
    }

    public override void _ExitTree() => RenderingServer.FramePostDraw -= OnDrawn;

    private void Changed()
    {
        if (step.Length > 0 && changedUs == 0) changedUs = Time.GetTicksUsec();
    }

    private async Task Run()
    {
        await Wait(6.0); // session loaded, first frame shown, idle
        var original = DisplayServer.WindowGetSize();
        await Step("panel-hidden", () => Press(Key.F10));
        await Step("panel-shown", () => Press(Key.F10));
        await Step("window-grown", () => DisplayServer.WindowSetSize(new Vector2I(original.X + 320, original.Y + 180)));
        await Step("window-restored", () => DisplayServer.WindowSetSize(original));
        GD.Print(failures == 0 ? "TRANSITION_PROBE OK" : $"TRANSITION_PROBE {failures} FAILED");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private async Task Step(string name, Action action)
    {
        before = Grab(0, "");
        Save(before, $"{name}-0-before.png");
        frames.Clear();
        drawTimes.Clear();
        changedUs = 0;
        step = name;
        stepUs = Time.GetTicksUsec();
        var drawnBefore = Engine.GetFramesDrawn();
        var processBefore = Engine.GetProcessFrames();
        action();
        ticks.Clear();
        await Wait(StepSeconds);
        step = "";
        if (System.Environment.GetEnvironmentVariable("PROBE_TRACE") == "1")
            GD.Print($"  {name}: process {Engine.GetProcessFrames() - processBefore}, drawn {Engine.GetFramesDrawn() - drawnBefore}, world {app.World.Size}, panel {app.ViewportPanel.Size}, " +
                     $"root {app.GetViewport().GetVisibleRect().Size}, window {DisplayServer.WindowGetSize()}; ticks: {string.Join(" ", ticks.Take(24))}");
        Check(name);
    }

    private void Check(string name)
    {
        var b = before!;
        var stereo = app.ViewportPanel.KeepPixelParity;
        if (changedUs == 0)
        {
            // the window manager can refuse a size (this one kept a grown window when asked to shrink it back)
            GD.Print($"SKIP {name}: nothing changed size ({frames.Count} frames drawn)");
            return;
        }
        var changedMs = (changedUs - stepUs) / 1000.0;
        if (frames.Count == 0) { Fail(name, "nothing was drawn"); return; }
        // A frame read back after a window resize event arrived is the new, not yet drawn window texture, not the frame
        // that was drawn; it cannot be judged.
        var crossed = frames.RemoveAll(f => f.Root.X != f.Width || f.Root.Y != f.Height);
        if (frames.Count == 0) { Fail(name, $"no frame could be read back ({crossed} crossed a resize)"); return; }
        var last = frames[^1];
        Save(frames[0], $"{name}-1-first.png");
        Save(last, $"{name}-2-last.png");
        int held = 0, settled = 0;
        foreach (var f in frames)
        {
            var settledSize = f.World.X == (int)MathF.Round(f.Panel.Size.X) && f.World.Y == (int)MathF.Round(f.Panel.Size.Y);
            if (f.World == b.World && !settledSize)
            {
                var (ok, what) = MatchesHold(b, f, stereo);
                if (ok) { ++held; continue; }
                Save(f, $"{name}-bad-{f.File}");
                app.World.GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"{name}-bad-world-now-{f.File}"));
                Fail(name, $"frame at {f.Ms:F0} ms is not the held image ({what})");
            }
            else if (settledSize)
            {
                var differing = Differing(f, 0, 0, last, 0, 0, (int)f.Panel.Size.X, (int)f.Panel.Size.Y);
                if (differing <= 0.002 * f.Panel.Size.X * f.Panel.Size.Y) { ++settled; continue; }
                Save(f, $"{name}-bad-{f.File}");
                Fail(name, $"frame at {f.Ms:F0} ms has the new size but not the settled image ({differing} pixels differ)");
            }
            else
            {
                Save(f, $"{name}-bad-{f.File}");
                Fail(name, $"frame at {f.Ms:F0} ms shows a {f.World} image in a {f.Panel.Size} panel");
            }
        }
        var lastSettled = last.World.X == (int)MathF.Round(last.Panel.Size.X) && last.World.Y == (int)MathF.Round(last.Panel.Size.Y);
        if (!lastSettled) Fail(name, $"the view never took the new size ({last.World} in {last.Panel.Size})");
        // every draw counts here, including one whose image could not be read back
        var firstDraw = drawTimes.Where(t => t >= changedMs - 1).DefaultIfEmpty(double.MaxValue).First();
        // A window resize first passes through the window system: on the development desktop the main loop then sits
        // for 100-300 ms (swapchain and X11 resize handling, with and without readbacks) before anything can be drawn,
        // so for those steps the delay is reported, not judged. A side-panel change is entirely the app's.
        if (name.StartsWith("window"))
            GD.Print($"INFO {name}: first frame drawn {firstDraw - changedMs:F0} ms after the size change");
        else if (firstDraw - changedMs > 100)
            Fail(name, $"no frame drawn within 100 ms of the size change at {changedMs:F0} ms (first at {firstDraw:F0} ms)");
        // Same height: the new image must agree with the old one where they overlap, offset by half the growth.
        var grow = (int)MathF.Round(last.Panel.Size.X) - b.World.X;
        if (lastSettled && last.World.Y == b.World.Y && grow % 2 == 0)
        {
            var dx = grow / 2;
            var x0 = System.Math.Max(0, dx); var x1 = System.Math.Min(last.World.X, b.World.X + dx);
            var differing = Differing(last, x0, 0, b, x0 - dx, 0, x1 - x0, last.World.Y);
            var area = (x1 - x0) * last.World.Y;
            // stereo: an odd half-growth puts every pixel on the other eye, so only even ones are comparable
            if (!stereo || dx % 2 == 0)
            {
                if (differing > 0.002 * area) Fail(name, $"the new image disagrees with the old one where they overlap ({differing} of {area} pixels)");
                else GD.Print($"PASS {name}: the settled image matches the held one where they overlap ({differing} of {area} pixels differ)");
            }
        }
        GD.Print($"{(held + settled == frames.Count && lastSettled ? "PASS" : "FAIL")} {name}: size changed after {changedMs:F0} ms, {frames.Count} frames judged " +
                 $"(first at {frames[0].Ms:F0} ms; {crossed} read back across a resize), {held} held, {settled} settled, view {b.World} -> {last.World}");
    }

    /// <summary>The held image must be the old one at <see cref="InputRouter.HoldRect"/>: exact when unscaled, close when scaled.</summary>
    private static (bool, string) MatchesHold(Shot old, Shot f, bool stereo)
    {
        var rect = InputRouter.HoldRect(old.World, f.Panel.Size, stereo);
        var scale = rect.Size.Y / old.World.Y;
        int pw = (int)f.Panel.Size.X, ph = (int)f.Panel.Size.Y;
        if (MathF.Abs(scale - 1) < 1e-6f)
        {
            var dx = (int)rect.Position.X;
            var x0 = System.Math.Max(0, dx); var x1 = System.Math.Min(pw, dx + old.World.X);
            var differing = Differing(f, x0, 0, old, x0 - dx, 0, x1 - x0, System.Math.Min(ph, old.World.Y));
            var area = (x1 - x0) * System.Math.Min(ph, old.World.Y);
            return (differing <= 0.001 * area, $"{differing} of {area} pixels differ, offset {dx}");
        }
        // scaled: compare a grid of samples against the nearest source pixel, with room for the filtering
        int good = 0, total = 0;
        for (var j = 0; j < 60; ++j)
            for (var i = 0; i < 96; ++i)
            {
                var x = rect.Position.X + (i + 0.5f) * rect.Size.X / 96;
                var y = (j + 0.5f) * rect.Size.Y / 60;
                if (x < 0 || x >= pw || y >= ph) continue;
                var sx = (int)((x - rect.Position.X) / scale); var sy = (int)(y / scale);
                ++total;
                if (Close(f, (int)x, (int)y, old, sx, sy, 48)) ++good;
            }
        return (good >= 0.85 * total, $"{good} of {total} samples close, scale {scale:F3}");
    }

    private static bool Close(Shot a, int ax, int ay, Shot b, int bx, int by, int tolerance)
    {
        var i = (ay * a.Width + ax) * 4; var j = (by * b.Width + bx) * 4;
        for (var k = 0; k < 3; ++k) if (System.Math.Abs(a.Rgba[i + k] - b.Rgba[j + k]) > tolerance) return false;
        return true;
    }

    private static int Differing(Shot a, int ax, int ay, Shot b, int bx, int by, int w, int h)
    {
        var n = 0;
        for (var y = 0; y < h; ++y)
            for (var x = 0; x < w; ++x)
                if (!Close(a, ax + x, ay + y, b, bx + x, by + y, 2)) ++n;
        return n;
    }

    private void Fail(string name, string what)
    {
        ++failures;
        GD.Print($"FAIL {name}: {what}");
    }

    private Shot Grab(double ms, string file)
    {
        var image = app.GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        return new Shot(image.GetData(), image.GetWidth(), image.GetHeight(), app.World.Size, app.ViewportPanel.GetGlobalRect(), ms, file);
    }

    private void Save(Shot s, string file)
    {
        var image = Image.CreateFromData(s.Width, s.Height, false, Image.Format.Rgba8, s.Rgba);
        image.SavePng(System.IO.Path.Combine(dir, file));
    }

    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);

    private static void Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    private void OnDrawn()
    {
        if (step.Length == 0) return;
        var ms = (Time.GetTicksUsec() - stepUs) / 1000.0;
        if (ticks.Count < 40) ticks.Add($"d{ms:F0}");
        drawTimes.Add(ms);
        // the sizes the frame was drawn with; the image itself is read on the next process step
        drawnWorld = app.World.Size;
        drawnPanel = app.ViewportPanel.GetGlobalRect();
        drawnRoot = (Vector2I)app.GetViewport().GetVisibleRect().Size;
        if (System.Environment.GetEnvironmentVariable("PROBE_NOGRAB") != "1") drawnMs = ms;
    }
}
