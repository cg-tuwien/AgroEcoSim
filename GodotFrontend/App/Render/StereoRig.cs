using Godot;
using GodotFrontend.Core.Stereo;

namespace GodotFrontend.App.Render;

/// <summary>
/// Checkerboard stereo for the institute's stereo LED wall, after the RadFoam viewer's LedWall.cs
/// (github.com/gramegnadominik/vr-radfoam): two eye SubViewports share the main viewport's World3D and render
/// through off-axis frusta, and a full-rect ColorRect inside the main viewport interleaves them pixel by pixel.
///
/// While stereo is on the main viewport's own 3D pass is disabled, so the scene is drawn twice, not three times, and
/// the container that shows the main viewport samples it with nearest filtering: the interleave only survives a
/// one-to-one mapping from viewport pixels to window pixels. When stereo is off the eye viewports stop updating and
/// shrink to 2 x 2, which releases their render targets.
///
/// The eye viewports are CHILDREN of the main viewport, not siblings: Godot renders a viewport's child viewports
/// before the viewport itself, and only that order guarantees the composite samples eyes rendered in the same frame.
/// As siblings their order was unspecified, and after a resize the composite showed freshly allocated, never rendered
/// eye textures - the Godot boot splash and colourful noise - until something else caused a redraw. For the same
/// reason the eyes follow the main viewport's size the moment it changes, not on the next frame.
///
/// Eye cameras are recomputed from the orbit camera every frame but written only when something changed, because
/// writing a camera marks the rendering server dirty and would defeat low-processor mode.
/// </summary>
public sealed class StereoRig
{
    private readonly SubViewport main;
    private readonly Control container;
    private readonly CameraRig rig;
    private readonly SubViewport[] eyes = new SubViewport[2];
    private readonly Camera3D[] cameras = new Camera3D[2];
    private readonly ColorRect overlay;
    private readonly ShaderMaterial material;
    private StereoOptions options = StereoOptions.Default;
    private readonly Transform3D[] lastTransform = new Transform3D[2];
    private readonly Vector4[] lastFrustum = new Vector4[2];
    private static readonly bool Debug = OS.GetCmdlineUserArgs().Contains("--debug-stereo");

    public StereoOptions Options => options;

    public StereoRig(Node host, SubViewport main, Control container, CameraRig rig)
    {
        this.main = main;
        this.container = container;
        this.rig = rig;
        for (var i = 0; i < 2; ++i)
        {
            eyes[i] = new SubViewport
            {
                Name = i == 0 ? "LeftEye" : "RightEye",
                World3D = main.FindWorld3D(),
                Size = new Vector2I(2, 2),
                Msaa3D = main.Msaa3D,
                ScreenSpaceAA = main.ScreenSpaceAA,
                UseDebanding = main.UseDebanding,
                HandleInputLocally = false,
                GuiDisableInput = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            cameras[i] = new Camera3D { Name = "Camera", KeepAspect = Camera3D.KeepAspectEnum.Height, Projection = Camera3D.ProjectionType.Frustum };
            eyes[i].AddChild(cameras[i]);
            main.AddChild(eyes[i]);
            cameras[i].Current = true;
        }
        material = new ShaderMaterial { Shader = GD.Load<Shader>("res://App/Shaders/stereo_checkerboard.gdshader") };
        material.SetShaderParameter("left_eye", eyes[0].GetTexture());
        material.SetShaderParameter("right_eye", eyes[1].GetTexture());
        overlay = new ColorRect { Name = "StereoComposite", Material = material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        main.AddChild(overlay);
        main.SizeChanged += FollowSize;
    }

    private void FollowSize()
    {
        if (!options.Active) return;
        foreach (var e in eyes)
            if (e.Size != main.Size) e.Size = main.Size;
        Sync();
    }

    public void Apply(StereoOptions o)
    {
        var was = options.Active;
        options = o;
        var on = o.Active;
        material.SetShaderParameter("swap_eyes", o.SwapEyes);
        material.SetShaderParameter("test_pattern", o.TestPattern);
        if (on != was)
        {
            overlay.Visible = on;
            main.Disable3D = on;
            container.TextureFilter = on ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.ParentNode;
            foreach (var e in eyes)
            {
                e.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
                if (!on) e.Size = new Vector2I(2, 2);
            }
            Array.Clear(lastTransform);
            Array.Clear(lastFrustum);
        }
        Sync();
    }

    /// <summary>Follows the viewport size and the orbit camera; cheap, and writes nothing when nothing moved.</summary>
    public void Sync()
    {
        if (!options.Active) return;
        var size = main.Size;
        foreach (var e in eyes)
            if (e.Size != size) e.Size = size;
        var cam = rig.Camera;
        if (cam == null) return;
        var controls = rig.Controls;
        var distance = (controls.Position - controls.Target).Length;
        var convergence = StereoMath.ConvergenceDistance(options, distance);
        var aspect = size.Y > 0 ? (double)size.X / size.Y : 1.0;
        var near = cam.Near;
        var height = 2f * near * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f);
        var centre = cam.GlobalTransform;
        for (var i = 0; i < 2; ++i)
        {
            var f = StereoMath.For(i == 0 ? Eye.Left : Eye.Right, options.EyeSeparation, convergence, near, cam.Fov, aspect);
            var t = centre.TranslatedLocal(new Vector3((float)f.LateralOffset, 0, 0));
            var frustum = new Vector4(height, (float)f.FrustumShift, near, cam.Far);
            if (t != lastTransform[i])
            {
                cameras[i].GlobalTransform = t;
                if (Debug) GD.Print($"STEREO eye {i} origin {t.Origin} shift {f.FrustumShift} convergence {convergence} separation {options.EyeSeparation}");
                lastTransform[i] = t;
            }
            if (frustum != lastFrustum[i])
            {
                cameras[i].SetFrustum(frustum.X, new Vector2(frustum.Y, 0), frustum.Z, frustum.W);
                lastFrustum[i] = frustum;
            }
        }
    }
}
