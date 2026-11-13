using GodotFrontend.Core.Cli;
using GodotFrontend.Core.Stereo;
using Xunit;

namespace GodotFrontend.Tests;

public class StereoTests
{
    [Fact]
    public void CheckerboardStartsWithTheLeftEyeOnEvenRowsAndTheRightEyeOnOddRows()
    {
        // row 0: L R L R ..., row 1: R L R L ...
        Assert.Equal(new[] { Eye.Left, Eye.Right, Eye.Left, Eye.Right }, Enumerable.Range(0, 4).Select(x => StereoMath.EyeAt(x, 0, false)));
        Assert.Equal(new[] { Eye.Right, Eye.Left, Eye.Right, Eye.Left }, Enumerable.Range(0, 4).Select(x => StereoMath.EyeAt(x, 1, false)));
        Assert.Equal(Eye.Left, StereoMath.EyeAt(2559, 1535, false));
        // swapping exchanges every pixel
        for (var y = 0; y < 3; ++y)
            for (var x = 0; x < 3; ++x)
                Assert.NotEqual(StereoMath.EyeAt(x, y, false), StereoMath.EyeAt(x, y, true));
    }

    [Fact]
    public void BothEyesSeeTheConvergencePlaneAtTheSameScreenPosition()
    {
        const double sep = 0.06, conv = 2.5, near = 0.05, fov = 50, aspect = 1.6;
        var tanY = System.Math.Tan(fov * System.Math.PI / 360);
        foreach (var x in new[] { -0.7, 0.0, 0.4 })
        {
            // a point on the convergence plane, in the centre camera's frame
            var px = x * conv * tanY * aspect;
            double Ndc(Eye e)
            {
                var f = StereoMath.For(e, sep, conv, near, fov, aspect);
                var local = px - f.LateralOffset;                 // the eye's own frame
                var onNear = local * near / conv;                 // perspective onto the near plane
                return (onNear - f.FrustumShift) / (near * tanY * aspect);
            }
            Assert.Equal(Ndc(Eye.Left), Ndc(Eye.Right), 12);
            Assert.Equal(x, Ndc(Eye.Left), 12);
        }
        // a nearer point appears further right to the left eye (crossed disparity: in front of the screen)
        var l = StereoMath.For(Eye.Left, sep, conv, near, fov, aspect);
        var r = StereoMath.For(Eye.Right, sep, conv, near, fov, aspect);
        double At(EyeFrustum f, double depth) => ((0 - f.LateralOffset) * near / depth - f.FrustumShift) / (near * tanY * aspect);
        Assert.True(At(l, 1.0) > At(r, 1.0));
        Assert.True(At(l, 10.0) < At(r, 10.0));
    }

    [Fact]
    public void MatchesThreeStereoCameraProjection()
    {
        // three.js StereoCamera.update: eyeSepOnProjection = eyeSep/2 * near / focus; left xmin/xmax shifted by +it,
        // element 8 = (xmax + xmin) / (xmax - xmin); the left camera's matrixWorld is translated by -eyeSep/2.
        const double sep = 0.064, focus = 10, near = 0.1, fov = 50, aspect = 1.25;
        var ymax = near * System.Math.Tan(fov * System.Math.PI / 360);
        var e = sep / 2 * near / focus;
        double El8(double xmin, double xmax) => (xmax + xmin) / (xmax - xmin);
        var left = StereoMath.For(Eye.Left, sep, focus, near, fov, aspect);
        var right = StereoMath.For(Eye.Right, sep, focus, near, fov, aspect);
        Assert.Equal(El8(-ymax * aspect + e, ymax * aspect + e), left.NdcShift, 12);
        Assert.Equal(El8(-ymax * aspect - e, ymax * aspect - e), right.NdcShift, 12);
        Assert.Equal(-sep / 2, left.LateralOffset, 12);
        Assert.Equal(sep / 2, right.LateralOffset, 12);
    }

    [Fact]
    public void ConvergenceFollowsTheTargetOrTheFixedDistance()
    {
        var o = StereoOptions.Default with { Mode = StereoMode.Checkerboard };
        Assert.Equal(5.83, StereoMath.ConvergenceDistance(o, 5.83), 12);
        Assert.Equal(3.0, StereoMath.ConvergenceDistance(o with { ConvergeOnTarget = false, Convergence = 3 }, 5.83), 12);
        Assert.True(StereoMath.ConvergenceDistance(o, 0) > 0);
        Assert.True(o.Active);
        Assert.False(StereoOptions.Default.Active);
        Assert.False((o with { Mode = StereoMode.OpenXr }).Active);
    }

    [Fact]
    public void ParsesTheModeAndTheCommandLine()
    {
        Assert.Equal(StereoMode.Checkerboard, StereoOptions.Parse("checkerboard"));
        Assert.Equal(StereoMode.Mono, StereoOptions.Parse("nonsense"));
        Assert.Equal("checkerboard", StereoOptions.Name(StereoMode.Checkerboard));
        var a = DebugArgs.Parse(new[] { "--stereo", "checkerboard", "--stereo-swap", "--stereo-pattern", "--eye-separation", "0.08", "--convergence", "3" });
        Assert.Equal("checkerboard", a.Stereo);
        Assert.True(a.StereoSwap && a.StereoPattern);
        Assert.Equal(0.08, a.EyeSeparation);
        Assert.Equal(3.0, a.Convergence);
        Assert.Empty(a.Unknown);
    }
}
