using Godot;
using GodotFrontend.Core.Offline;

namespace GodotFrontend.App;

/// <summary>
/// The --screenshot path: renders the shared 3D world through its own SubViewport at W*ssaa x H*ssaa with a copy
/// of the rig's camera, waits for the settle frames, box-downsamples in encoded sRGB and writes a PNG, then quits.
/// Bypasses the window entirely, so the result does not depend on the window size or the side panel.
/// </summary>
public partial class ScreenshotRunner : Node
{
    private readonly AppRoot app;
    private readonly string path;
    private readonly int width, height, ssaa, settle;
    private SubViewport? view;
    private Camera3D? camera;
    private int framesLeft;
    private bool started;

    public ScreenshotRunner(AppRoot app, string path, int width, int height, int ssaa, int settle)
    {
        this.app = app;
        this.path = path;
        this.width = width;
        this.height = height;
        this.ssaa = ssaa;
        this.settle = settle;
    }

    /// <summary>Called once the scene content is in place (the frame uploaded, or immediately when nothing is to be loaded).</summary>
    public void Begin()
    {
        if (started) return;
        started = true;
        view = new SubViewport
        {
            Name = "Screenshot",
            Size = new Vector2I(width * ssaa, height * ssaa),
            World3D = app.World.FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = app.World.Msaa3D,
        };
        camera = new Camera3D { Current = true };
        view.AddChild(camera);
        AddChild(view);
        SyncCamera();
        framesLeft = System.Math.Max(1, settle);
    }

    private void SyncCamera()
    {
        if (camera == null) return;
        var c = app.Rig.Camera;
        camera.GlobalTransform = c.GlobalTransform;
        camera.Fov = c.Fov;
        camera.Near = c.Near;
        camera.Far = c.Far;
        camera.KeepAspect = c.KeepAspect;
    }

    public override void _Process(double delta)
    {
        if (!started || view == null) return;
        SyncCamera();
        if (--framesLeft > 0) return;
        try
        {
            GD.Print($"screenshot: shared world {ReferenceEquals(view.FindWorld3D(), app.World.FindWorld3D())} camera current {camera!.IsCurrent()} env {view.FindWorld3D()?.Environment != null} size {view.Size} main {app.World.Size} panel {app.ViewportPanel.Size} window {DisplayServer.WindowGetSize()}");
            var mainImg = app.World.GetTexture().GetImage();
            mainImg.SavePng(path + ".main.png");
            var img = view.GetTexture().GetImage();
            img.Convert(Image.Format.Rgba8);
            var bytes = img.GetData();
            var down = BoxDownsample.Rgba8(bytes, img.GetWidth(), img.GetHeight(), ssaa, out var w, out var h);
            var outImg = Image.CreateFromData(w, h, false, Image.Format.Rgba8, down);
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var err = outImg.SavePng(path);
            if (err != Error.Ok) throw new IOException($"SavePng failed: {err}");
            GD.Print($"SCREENSHOT {path} {w}x{h} ssaa {ssaa}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"screenshot failed: {ex}");
            GetTree().Quit(3);
        }
        started = false;
    }
}
