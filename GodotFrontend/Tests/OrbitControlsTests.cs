using GodotFrontend.Core.Camera;
using GodotFrontend.Core.Math;
using Xunit;

namespace GodotFrontend.Tests;

/// <summary>
/// Pins the camera conventions: a port of ThreeFrontend/tests/orbitCamera.test.ts, driven through the mouse API
/// of the OrbitControls port, plus golden numbers computed from three's formulas and the three-specific rules
/// (wheel ignored while dragging, one dolly step per middle-drag move, modifier mapping).
/// </summary>
public class OrbitControlsTests
{
    private static readonly CameraPose Pose = new(new Vec3(0, 3, 5), Vec3.Zero, Vec3.UnitY, 50, 0.025, 5000);
    private const double Height = 800;

    private static OrbitControls Make(bool clampLikeWebGpu = false)
    {
        var c = new OrbitControls(Pose);
        c.SetViewport(1280, Height);
        if (clampLikeWebGpu)
        {
            c.MinDistance = Pose.Near * 2;
            c.MaxDistance = Pose.Far * 0.5;
        }
        return c;
    }

    private static void Drag(OrbitControls c, OrbitButton button, double dx, double dy, OrbitModifiers mods = OrbitModifiers.None)
    {
        Assert.True(c.BeginDrag(button, mods, 400, 300));
        c.Move(400 + dx, 300 + dy);
        c.EndDrag();
    }

    private static void Rotate(OrbitControls c, double dx, double dy) => Drag(c, OrbitButton.Left, dx, dy);
    private static void Pan(OrbitControls c, double dx, double dy) => Drag(c, OrbitButton.Right, dx, dy);

    [Fact]
    public void WheelDownMovesAwayFromTheTarget()
    {
        var c = Make();
        var before = c.GetDistance();
        c.Wheel(+100);
        Assert.True(c.GetDistance() > before);
    }

    [Fact]
    public void WheelUpMovesTowardsTheTarget()
    {
        var c = Make();
        var before = c.GetDistance();
        c.Wheel(-100);
        Assert.True(c.GetDistance() < before);
    }

    [Fact]
    public void WheelIsOneStepPerEventAndReversible()
    {
        var c = Make();
        var before = c.GetDistance();
        c.Wheel(+1);
        Assert.Equal(before / 0.95, c.GetDistance(), 9);
        c.Wheel(-3000); // magnitude is irrelevant
        Assert.Equal(before, c.GetDistance(), 6);
    }

    [Fact]
    public void WheelGoldenPose()
    {
        var c = Make();
        c.Wheel(+1);
        Assert.Equal(0, c.Position.X, 6);
        Assert.Equal(3.1578947, c.Position.Y, 6);
        Assert.Equal(5.2631579, c.Position.Z, 6);
    }

    [Fact]
    public void DistanceClampKeepsTheSceneInsideTheFrustum()
    {
        // The WebGPU backend clamps to [2*near, far/2]; the Godot app configures OrbitControls the same way.
        var c = Make(clampLikeWebGpu: true);
        for (var i = 0; i < 500; ++i) c.Wheel(-1);
        Assert.True(c.GetDistance() > 0);
        Assert.Equal(Pose.Near * 2, c.GetDistance(), 9);
        for (var i = 0; i < 2000; ++i) c.Wheel(+1);
        Assert.True(c.GetDistance() < Pose.Far);
        Assert.Equal(Pose.Far * 0.5, c.GetDistance(), 6);
    }

    [Fact]
    public void UnclampedThreeDefaultsStayFinite()
    {
        var c = Make();
        for (var i = 0; i < 500; ++i) c.Wheel(-1);
        Assert.True(c.GetDistance() > 0);
        for (var i = 0; i < 2000; ++i) c.Wheel(+1);
        Assert.True(c.Position.IsFinite);
    }

    [Fact]
    public void RotateKeepsTheDistanceAndTheTarget()
    {
        var c = Make();
        var before = c.GetDistance();
        Rotate(c, 120, 40);
        Assert.Equal(before, c.GetDistance(), 5);
        Assert.Equal(Pose.Target, c.Target);
    }

    [Fact]
    public void DraggingRightSwingsTheCameraTowardsNegativeX()
    {
        var c = Make();
        Rotate(c, 100, 0);
        Assert.True(c.Position.X < 0);
        // theta = -2*pi*100/800 = -pi/4
        Assert.Equal(-3.535534, c.Position.X, 5);
        Assert.Equal(3, c.Position.Y, 9);
        Assert.Equal(3.535534, c.Position.Z, 5);
    }

    [Fact]
    public void DraggingDownLiftsTheCameraAndUpLowersIt()
    {
        var up = Make();
        Rotate(up, 0, 60);
        Assert.True(up.Position.Y > Pose.Position.Y);

        var down = Make();
        Rotate(down, 0, -60);
        Assert.True(down.Position.Y < Pose.Position.Y);
    }

    [Fact]
    public void PolarAngleIsClampedAtBothPoles()
    {
        var c = Make();
        var r = c.GetDistance();
        for (var i = 0; i < 50; ++i) Rotate(c, 0, -100);
        Assert.Equal(r, c.GetDistance(), 5);
        Assert.True(System.Math.Abs(c.Position.Y) <= r + 1e-6);
        Assert.Equal(System.Math.PI - OrbitControls.Eps, c.GetPolarAngle(), 12);

        for (var i = 0; i < 100; ++i) Rotate(c, 0, 100);
        Assert.Equal(r, c.GetDistance(), 5);
        Assert.True(double.IsFinite(c.Position.Y));
        Assert.Equal(OrbitControls.Eps, c.GetPolarAngle(), 12);
        // The basis stays orthonormal at the pole, where Godot's own LookingAt would degrade.
        AssertOrthonormal(c.Basis);
    }

    [Fact]
    public void PanMovesTheTargetAndDraggingRightSlidesTheViewRight()
    {
        var c = Make();
        Pan(c, 50, 0);
        Assert.True(c.Target.X < 0);
        Assert.True(System.Math.Abs(c.Target.Z) < 1e-6);
        // targetDistance = sqrt(34)*tan(25 deg) = 2.719039; d = 2*50*2.719039/800 = 0.339880
        Assert.Equal(-0.339880, c.Target.X, 5);
        Assert.Equal(0, c.Target.Y, 9);
        Assert.Equal(-0.339880, c.Position.X, 5);
        Assert.Equal(3, c.Position.Y, 9);
        Assert.Equal(5, c.Position.Z, 9);
    }

    [Fact]
    public void PanDistanceScalesWithTheDistanceToTheTarget()
    {
        var near = Make();
        var far = Make();
        for (var i = 0; i < 20; ++i) far.Wheel(+1);
        Pan(near, 50, 0);
        Pan(far, 50, 0);
        Assert.True(System.Math.Abs(far.Target.X) > System.Math.Abs(near.Target.X));
    }

    [Fact]
    public void PoseRoundTripPreservesTheCamera()
    {
        var c = new OrbitControls();
        c.Pose = Pose;
        var got = c.Pose;
        Assert.Equal(Pose.Position.X, got.Position.X, 5);
        Assert.Equal(Pose.Position.Y, got.Position.Y, 5);
        Assert.Equal(Pose.Position.Z, got.Position.Z, 5);
        Assert.Equal(Pose.FovY, got.FovY);
    }

    [Fact]
    public void WheelIsIgnoredWhileDragging()
    {
        var c = Make();
        var before = c.GetDistance();
        Assert.True(c.BeginDrag(OrbitButton.Left, OrbitModifiers.None, 10, 10));
        c.Wheel(+1);
        Assert.Equal(before, c.GetDistance(), 9);
        c.EndDrag();
        c.Wheel(+1);
        Assert.Equal(before / 0.95, c.GetDistance(), 9);
    }

    [Fact]
    public void MiddleDragDolliesOneStepPerMoveBySign()
    {
        var c = Make();
        var before = c.GetDistance();
        Assert.True(c.BeginDrag(OrbitButton.Middle, OrbitModifiers.None, 100, 100));
        c.Move(100, 400);          // far down: still one step
        Assert.Equal(before / 0.95, c.GetDistance(), 9);
        c.Move(100, 400);          // dy = 0: nothing
        Assert.Equal(before / 0.95, c.GetDistance(), 9);
        c.Move(100, 399);          // one pixel up: one step in
        Assert.Equal(before, c.GetDistance(), 9);
        c.EndDrag();
    }

    [Theory]
    [InlineData(OrbitModifiers.Shift)]
    [InlineData(OrbitModifiers.Ctrl)]
    [InlineData(OrbitModifiers.Meta)]
    public void LeftWithModifierPans(OrbitModifiers mods)
    {
        var c = Make();
        Drag(c, OrbitButton.Left, 50, 0, mods);
        Assert.Equal(OrbitState.None, c.State);
        Assert.Equal(-0.339880, c.Target.X, 5);
    }

    [Theory]
    [InlineData(OrbitModifiers.Shift)]
    [InlineData(OrbitModifiers.Ctrl)]
    [InlineData(OrbitModifiers.Meta)]
    public void RightWithModifierRotates(OrbitModifiers mods)
    {
        var c = Make();
        Drag(c, OrbitButton.Right, 100, 0, mods);
        Assert.Equal(Vec3.Zero, c.Target);
        Assert.Equal(-3.535534, c.Position.X, 5);
    }

    [Fact]
    public void MiddleIgnoresModifiers()
    {
        var c = Make();
        Assert.True(c.BeginDrag(OrbitButton.Middle, OrbitModifiers.Shift, 0, 0));
        Assert.Equal(OrbitState.Dolly, c.State);
        c.EndDrag();
    }

    [Fact]
    public void DisabledControlsIgnoreInput()
    {
        var c = Make();
        c.Enabled = false;
        Assert.False(c.BeginDrag(OrbitButton.Left, OrbitModifiers.None, 0, 0));
        c.Move(100, 100);
        c.Wheel(+1);
        Assert.Equal(Pose.Position, c.Position);
    }

    [Fact]
    public void UpdateReportsWhetherTheCameraMoved()
    {
        var c = Make();
        Assert.False(c.Update());
        Assert.True(c.BeginDrag(OrbitButton.Left, OrbitModifiers.None, 0, 0));
        var fired = 0;
        c.Changed += () => fired++;
        c.Move(10, 0);
        Assert.Equal(1, fired);
        c.EndDrag();
    }

    [Fact]
    public void TwoFingerTouchDolliesByDistanceRatioAndPansByMidpoint()
    {
        var c = Make();
        var before = c.GetDistance();
        c.TouchBegin(1, 100, 100);
        c.TouchBegin(2, 200, 100);           // distance 100
        Assert.Equal(OrbitState.TouchDollyPan, c.State);
        c.TouchMove(2, 300, 100);            // distance 200 -> dollyOut(2) = scale /= 2: spreading zooms IN; midpoint moved +50 px
        Assert.Equal(before / 2, c.GetDistance(), 6);
        Assert.True(c.Target.X < 0);
        c.TouchEnd(2);
        c.TouchEnd(1);
        Assert.Equal(OrbitState.None, c.State);
    }

    [Fact]
    public void BasisIsTheLookAtOfThreeAndLooksDownNegativeZ()
    {
        var c = Make();
        var b = c.Basis;
        AssertOrthonormal(b);
        var back = (c.Position - c.Target).Normalized();
        Assert.Equal(back.X, b.Col2.X, 12);
        Assert.Equal(back.Y, b.Col2.Y, 12);
        Assert.Equal(back.Z, b.Col2.Z, 12);
        Assert.True(Vec3.Dot(b.Col1, Vec3.UnitY) > 0);
        // forward (-col2) from the position reaches the target
        var hit = c.Position + (-b.Col2) * c.GetDistance();
        Assert.Equal(0, (hit - c.Target).Length, 9);
    }

    [Fact]
    public void TurntableKeepsRadiusAndElevation()
    {
        var path = new TurntableCamera(Pose, 1);
        var p0 = path.PoseAt(0, 100);
        Assert.Equal(Pose.Position.X, p0.Position.X, 9);
        Assert.Equal(Pose.Position.Z, p0.Position.Z, 9);
        var half = path.PoseAt(50, 100);
        Assert.Equal(-Pose.Position.Z, half.Position.Z, 9);
        Assert.Equal(Pose.Position.Y, half.Position.Y, 9);
        Assert.Equal(Pose.Distance, half.Distance, 9);
    }

    private static void AssertOrthonormal(Basis3 b)
    {
        Assert.Equal(1, b.Col0.Length, 9);
        Assert.Equal(1, b.Col1.Length, 9);
        Assert.Equal(1, b.Col2.Length, 9);
        Assert.Equal(0, Vec3.Dot(b.Col0, b.Col1), 9);
        Assert.Equal(0, Vec3.Dot(b.Col1, b.Col2), 9);
        Assert.Equal(0, Vec3.Dot(b.Col0, b.Col2), 9);
        Assert.Equal(1, b.Determinant, 9);
    }
}

/// <summary>
/// The same pointer script replayed in the web app (three.js OrbitControls, via tools/visual/cameraprobe.mjs) and
/// through this port must end at the same pose. The expected pose is the web's own output, recorded in the fixture.
/// </summary>
public class CameraParityTests
{
    [Fact]
    public void ScriptedDragsEndWhereTheWebCameraEnds()
    {
        var script = File.ReadAllText(Fixtures.Path("camera-script.json"));
        var expected = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Fixtures.Path("camera-script.web-pose.json"))).RootElement.GetProperty("pose");
        var c = new OrbitControls(CameraPose.Default) { MinDistance = 0.1, MaxDistance = 250 };
        GodotFrontend.Core.Cli.EventScript.Replay(script, c);
        void Same(string what, Vec3 got)
        {
            var e = expected.GetProperty(what);
            Assert.Equal(e.GetProperty("x").GetDouble(), got.X, 9);
            Assert.Equal(e.GetProperty("y").GetDouble(), got.Y, 9);
            Assert.Equal(e.GetProperty("z").GetDouble(), got.Z, 9);
        }
        Same("position", c.Position);
        Same("target", c.Target);
    }
}
