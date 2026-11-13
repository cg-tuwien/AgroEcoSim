using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Camera;

/// <summary>
/// The camera pose as ThreeFrontend's IViewportRenderer exposes it: position, orbit target, up, vertical field of
/// view in degrees, near and far planes. The defaults are the web app's (ThreeRenderer.ts:157-160, Mat4.ts).
/// </summary>
public readonly record struct CameraPose(Vec3 Position, Vec3 Target, Vec3 Up, double FovY, double Near, double Far)
{
    public const double DefaultFovY = 50;
    /// <summary>core/Mat4.ts CAMERA_NEAR: 0.05 m, raised from 0.025 to stop depth fighting on facades.</summary>
    public const double DefaultNear = 0.05;
    /// <summary>core/Mat4.ts CAMERA_FAR: 500 m.</summary>
    public const double DefaultFar = 500;

    public static CameraPose Default => new(new Vec3(0, 3, 5), Vec3.Zero, Vec3.UnitY, DefaultFovY, DefaultNear, DefaultFar);

    public double Distance => (Position - Target).Length;
}
