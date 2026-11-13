using Godot;
using GodotFrontend.Core.Camera;
using GodotFrontend.Core.Math;

namespace GodotFrontend.App;

/// <summary>
/// Owns the Camera3D and the engine-free <see cref="OrbitControls"/> that drives it. The controls hold the truth;
/// this node only copies position and basis into the camera whenever the controls report a change.
///
/// three's camera basis columns (right, up, back) are exactly the column vectors Godot's Basis takes, and both
/// cameras look down their local -Z, so the copy involves no axis flips.
/// </summary>
public partial class CameraRig : Node3D
{
    public OrbitControls Controls { get; } = new();
    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D
        {
            Name = "Camera",
            Fov = (float)CameraPose.DefaultFovY,
            Near = (float)CameraPose.DefaultNear,
            Far = (float)CameraPose.DefaultFar,
            KeepAspect = Camera3D.KeepAspectEnum.Height, // vertical field of view, as three's PerspectiveCamera
            Projection = Camera3D.ProjectionType.Perspective,
            Current = true,
        };
        AddChild(Camera);
        // The WebGPU backend's clamp, which keeps the scene inside the frustum; every other setting stays at
        // three's defaults (see the plan: unbounded dolly pushes the camera past the far plane).
        Controls.MinDistance = CameraPose.DefaultNear * 2;
        Controls.MaxDistance = CameraPose.DefaultFar * 0.5;
        Controls.Changed += Apply;
        Apply();
    }

    public CameraPose Pose
    {
        get => Controls.Pose;
        set
        {
            Controls.Pose = value;
            Apply();
        }
    }

    /// <summary>still.mjs CAMERA= semantics: position and target replaced, up/fov/near/far kept.</summary>
    public void SetPositionAndTarget(Vec3 position, Vec3 target)
    {
        Pose = Controls.Pose with { Position = position, Target = target };
    }

    public void SetViewportSize(double width, double height) => Controls.SetViewport(width, height);

    private void Apply()
    {
        if (Camera == null) return;
        var b = Controls.Basis;
        var basis = new Basis(ToG(b.Col0), ToG(b.Col1), ToG(b.Col2));
        Camera.GlobalTransform = new Transform3D(basis, ToG(Controls.Position));
        Camera.Fov = (float)Controls.FovY;
        Camera.Near = (float)Controls.Near;
        Camera.Far = (float)Controls.Far;
    }

    private static Vector3 ToG(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
