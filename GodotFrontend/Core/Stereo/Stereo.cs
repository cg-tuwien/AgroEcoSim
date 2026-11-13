namespace GodotFrontend.Core.Stereo;

/// <summary>How the 3D view is presented. OpenXR is reserved: listed in the UI, not implemented yet.</summary>
public enum StereoMode { Mono, Checkerboard, OpenXr }

public enum Eye { Left, Right }

/// <summary>
/// The stereo settings of the viewport. Separation and convergence are in world units (metres).
/// <para>ConvergeOnTarget puts the zero-parallax plane through the orbit target, so whatever the camera orbits sits on
/// the display surface and nearer objects come out of it; otherwise the plane is at <see cref="Convergence"/>.</para>
/// <para>SwapEyes corrects a display whose pixel grid starts on the other eye (a window placed at an odd pixel offset,
/// or a wall wired the other way); TestPattern paints the left eye red and the right eye blue to check exactly that.</para>
/// </summary>
public sealed record StereoOptions(StereoMode Mode, double EyeSeparation, bool ConvergeOnTarget, double Convergence, bool SwapEyes, bool TestPattern)
{
    /// <summary>Six centimetres, as the RadFoam LED-wall viewer (gramegnadominik/vr-radfoam, LedWall.cs).</summary>
    public const double DefaultEyeSeparation = 0.06;
    public const double DefaultConvergence = 2.0;

    public static readonly StereoOptions Default = new(StereoMode.Mono, DefaultEyeSeparation, true, DefaultConvergence, false, false);

    /// <summary>True when the view is rendered once per eye.</summary>
    public bool Active => Mode == StereoMode.Checkerboard;

    public static string Name(StereoMode m) => m switch
    {
        StereoMode.Checkerboard => "checkerboard",
        StereoMode.OpenXr => "openxr",
        _ => "mono",
    };

    public static StereoMode Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "checkerboard" or "stereo-checkerboard" or "stereo checkerboard" => StereoMode.Checkerboard,
        "openxr" or "stereo-openxr" => StereoMode.OpenXr,
        _ => StereoMode.Mono,
    };
}

/// <summary>One eye of an off-axis (parallel-axis, asymmetric-frustum) stereo pair.</summary>
/// <param name="LateralOffset">Translation of the eye along the camera's right axis, in world units.</param>
/// <param name="FrustumShift">Horizontal shift of the frustum window at the near plane, in world units.</param>
/// <param name="NdcShift">The same shift in normalised device coordinates: what three.js's StereoCamera writes into
/// element 8 of the projection matrix, (right + left) / (right - left).</param>
public readonly record struct EyeFrustum(double LateralOffset, double FrustumShift, double NdcShift);

public static class StereoMath
{
    /// <summary>
    /// The checkerboard interleave: pixel (x, y), counted from the top-left corner of the display from zero, belongs to
    /// the left eye when x + y is even. Row 0 reads L R L R ..., row 1 reads R L R L ...
    /// </summary>
    public static Eye EyeAt(int x, int y, bool swap) => (((x + y) & 1) == 0) != swap ? Eye.Left : Eye.Right;

    /// <summary>Distance of the zero-parallax plane from the camera.</summary>
    public static double ConvergenceDistance(StereoOptions o, double targetDistance) =>
        System.Math.Max(o.ConvergeOnTarget ? targetDistance : o.Convergence, 1e-4);

    /// <summary>
    /// The off-axis frustum of one eye (Paul Bourke, "Calculating Stereo Pairs"; three.js StereoCamera): the eye moves
    /// half the separation along the camera's right axis and its frustum window moves the other way by
    /// half the separation scaled to the near plane, so both frusta meet exactly in the convergence plane and no
    /// vertical parallax appears (no toe-in).
    /// </summary>
    public static EyeFrustum For(Eye eye, double separation, double convergence, double near, double fovYDegrees, double aspect)
    {
        var side = eye == Eye.Left ? -1.0 : 1.0;
        var half = separation * 0.5;
        var shift = -side * half * near / convergence;
        var halfWidth = near * System.Math.Tan(fovYDegrees * System.Math.PI / 360.0) * aspect;
        return new EyeFrustum(side * half, shift, shift / halfWidth);
    }
}
