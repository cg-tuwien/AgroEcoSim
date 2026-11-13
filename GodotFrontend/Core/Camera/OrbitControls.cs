using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Camera;

public enum OrbitButton { Left, Middle, Right, Other }

[Flags]
public enum OrbitModifiers { None = 0, Ctrl = 1, Meta = 2, Shift = 4 }

/// <summary>three's MOUSE.ROTATE / DOLLY / PAN plus "no action" (-1 in the original).</summary>
public enum MouseAction { Rotate, Dolly, Pan, None }

/// <summary>three's TOUCH.ROTATE / PAN / DOLLY_PAN / DOLLY_ROTATE plus "no action".</summary>
public enum TouchAction { Rotate, Pan, DollyPan, DollyRotate, None }

public enum OrbitState { None = -1, Rotate = 0, Dolly = 1, Pan = 2, TouchRotate = 3, TouchPan = 4, TouchDollyPan = 5, TouchDollyRotate = 6 }

/// <summary>
/// A line-by-line port of three.js r152 <c>OrbitControls</c> (examples/jsm/controls/OrbitControls.js), the
/// camera ThreeFrontend's WebGL backend uses with every setting at its default. The web app never enables
/// damping, auto-rotation or keyboard panning, so those are not ported; everything the pointer and the wheel
/// can do is reproduced exactly, including the quirks (rotation divides both axes by the viewport HEIGHT,
/// the wheel takes one 0.95 step per event whatever its magnitude, a middle-drag dolly takes one step per
/// move event by the sign of dy, and modifiers turn the left button into pan and the right button into
/// rotate).
///
/// Everything is double precision and engine-free so the behaviour can be pinned by unit tests; the Godot
/// side only copies <see cref="Position"/> and <see cref="Basis"/> into a Camera3D.
///
/// Pixel inputs are whatever the host's "client" coordinates are; <see cref="SetViewport"/> supplies the
/// element height the formulas divide by (the browser's <c>clientHeight</c>).
/// </summary>
public sealed class OrbitControls
{
    public const double Eps = 0.000001;

    // ---- settings (three's defaults) ----
    public bool Enabled = true;
    public double MinDistance = 0;
    public double MaxDistance = double.PositiveInfinity;
    public double MinPolarAngle = 0;
    public double MaxPolarAngle = System.Math.PI;
    public double MinAzimuthAngle = double.NegativeInfinity;
    public double MaxAzimuthAngle = double.PositiveInfinity;
    public bool EnableZoom = true;
    public double ZoomSpeed = 1.0;
    public bool EnableRotate = true;
    public double RotateSpeed = 1.0;
    public bool EnablePan = true;
    public double PanSpeed = 1.0;
    /// <summary>true pans in screen space (three's default); false pans orthogonal to the world up.</summary>
    public bool ScreenSpacePanning = true;
    public MouseAction LeftButton = MouseAction.Rotate;
    public MouseAction MiddleButton = MouseAction.Dolly;
    public MouseAction RightButton = MouseAction.Pan;
    public TouchAction OneTouch = TouchAction.Rotate;
    public TouchAction TwoTouches = TouchAction.DollyPan;

    // ---- the camera ----
    public Vec3 Position;
    public Vec3 Target;
    public Vec3 Up;
    public double FovY;
    public double Near;
    public double Far;

    /// <summary>
    /// The camera's rotation as of the last <see cref="Update"/>: columns are right, up and back in world space,
    /// i.e. the first three columns of three's <c>object.matrix</c>, which is what <c>pan()</c> reads.
    /// </summary>
    public Basis3 Basis { get; private set; } = Basis3.Identity;
    public Quat Quaternion { get; private set; } = Quat.Identity;

    public OrbitState State => state;

    public event Action? Changed;
    public event Action? Started;
    public event Action? Ended;

    // ---- internals, named as in the original ----
    private OrbitState state = OrbitState.None;
    private double sphTheta, sphPhi, sphRadius;       // `spherical`
    private double deltaTheta, deltaPhi;              // `sphericalDelta`
    private double scale = 1;
    private Vec3 panOffset;
    private Vec2 rotateStart, rotateEnd, panStart, panEnd, dollyStart, dollyEnd;
    private Vec3 lastPosition;
    private Quat lastQuaternion = Quat.Identity;
    private readonly Quat quat, quatInverse;          // object.up -> +Y, captured once as three does
    private double viewportWidth = 1, viewportHeight = 1;

    // pointers: three keeps the pointerdown event per pointer (its position never updates) and a separate
    // map of tracked positions that moves update.
    private const int MousePointerId = int.MinValue;
    private readonly List<int> pointers = new();
    private readonly Dictionary<int, Vec2> pointerDown = new();
    private readonly Dictionary<int, Vec2> pointerPositions = new();

    public OrbitControls() : this(CameraPose.Default) { }

    public OrbitControls(CameraPose pose)
    {
        Position = pose.Position;
        Target = pose.Target;
        Up = pose.Up;
        FovY = pose.FovY;
        Near = pose.Near;
        Far = pose.Far;
        // three computes this once, in the constructor's closure, from object.up as it was then.
        quat = Quat.FromUnitVectors(Up, Vec3.UnitY);
        quatInverse = quat.Conjugate();
        Update(); // "force an update at start"
    }

    public CameraPose Pose
    {
        get => new(Position, Target, Up, FovY, Near, Far);
        set
        {
            // ThreeRenderer.setCameraPose: copy everything, then controls.update().
            Position = value.Position;
            Target = value.Target;
            Up = value.Up;
            FovY = value.FovY;
            Near = value.Near;
            Far = value.Far;
            Update();
        }
    }

    /// <summary>The element size the pixel formulas divide by (the browser's clientWidth/clientHeight).</summary>
    public void SetViewport(double width, double height)
    {
        viewportWidth = width;
        viewportHeight = height;
    }

    public double ViewportWidth => viewportWidth;
    public double ViewportHeight => viewportHeight;
    public double Aspect => viewportHeight > 0 ? viewportWidth / viewportHeight : 1;

    public double GetPolarAngle() => sphPhi;
    public double GetAzimuthalAngle() => sphTheta;
    public double GetDistance() => (Position - Target).Length;

    // ---------------------------------------------------------------- helpers

    private double GetZoomScale() => System.Math.Pow(0.95, ZoomSpeed);

    private void RotateLeft(double angle) => deltaTheta -= angle;
    private void RotateUp(double angle) => deltaPhi -= angle;

    private void PanLeft(double distance)
    {
        panOffset += Basis.Col0 * -distance;
    }

    private void PanUp(double distance)
    {
        var v = ScreenSpacePanning ? Basis.Col1 : Vec3.Cross(Up, Basis.Col0);
        panOffset += v * distance;
    }

    /// <summary>Perspective pan: two half-heights of the view at the target's distance per element height.</summary>
    private void Pan(double deltaX, double deltaY)
    {
        var targetDistance = (Position - Target).Length;
        targetDistance *= System.Math.Tan((FovY / 2) * System.Math.PI / 180.0);
        PanLeft(2 * deltaX * targetDistance / viewportHeight);
        PanUp(2 * deltaY * targetDistance / viewportHeight);
    }

    private void DollyOut(double dollyScale) => scale /= dollyScale;
    private void DollyIn(double dollyScale) => scale *= dollyScale;

    // ---------------------------------------------------------------- mouse

    /// <summary>
    /// onPointerDown + onMouseDown for a mouse pointer. Returns whether a drag state was entered; the caller
    /// is expected to capture the pointer either way, as three does on the first pointer.
    /// </summary>
    public bool BeginDrag(OrbitButton button, OrbitModifiers modifiers, double x, double y)
    {
        if (!Enabled) return false;
        AddPointer(MousePointerId, x, y);

        var mouseAction = button switch
        {
            OrbitButton.Left => LeftButton,
            OrbitButton.Middle => MiddleButton,
            OrbitButton.Right => RightButton,
            _ => MouseAction.None,
        };
        var modified = (modifiers & (OrbitModifiers.Ctrl | OrbitModifiers.Meta | OrbitModifiers.Shift)) != 0;
        var p = new Vec2(x, y);

        switch (mouseAction)
        {
            case MouseAction.Dolly:
                if (!EnableZoom) return false;
                dollyStart = p;
                state = OrbitState.Dolly;
                break;
            case MouseAction.Rotate:
                if (modified)
                {
                    if (!EnablePan) return false;
                    panStart = p;
                    state = OrbitState.Pan;
                }
                else
                {
                    if (!EnableRotate) return false;
                    rotateStart = p;
                    state = OrbitState.Rotate;
                }
                break;
            case MouseAction.Pan:
                if (modified)
                {
                    if (!EnableRotate) return false;
                    rotateStart = p;
                    state = OrbitState.Rotate;
                }
                else
                {
                    if (!EnablePan) return false;
                    panStart = p;
                    state = OrbitState.Pan;
                }
                break;
            default:
                state = OrbitState.None;
                break;
        }

        if (state != OrbitState.None) Started?.Invoke();
        return state != OrbitState.None;
    }

    /// <summary>onPointerMove + onMouseMove with absolute client coordinates.</summary>
    public void Move(double x, double y)
    {
        if (!Enabled) return;
        var p = new Vec2(x, y);
        switch (state)
        {
            case OrbitState.Rotate:
                if (!EnableRotate) return;
                rotateEnd = p;
                {
                    var d = (rotateEnd - rotateStart) * RotateSpeed;
                    RotateLeft(2 * System.Math.PI * d.X / viewportHeight); // yes, height
                    RotateUp(2 * System.Math.PI * d.Y / viewportHeight);
                }
                rotateStart = rotateEnd;
                Update();
                break;
            case OrbitState.Dolly:
                if (!EnableZoom) return;
                dollyEnd = p;
                {
                    var d = dollyEnd - dollyStart;
                    if (d.Y > 0) DollyOut(GetZoomScale());
                    else if (d.Y < 0) DollyIn(GetZoomScale());
                }
                dollyStart = dollyEnd;
                Update();
                break;
            case OrbitState.Pan:
                if (!EnablePan) return;
                panEnd = p;
                {
                    var d = (panEnd - panStart) * PanSpeed;
                    Pan(d.X, d.Y);
                }
                panStart = panEnd;
                Update();
                break;
        }
    }

    /// <summary>onPointerUp for the mouse pointer: always ends the interaction, as three does.</summary>
    public void EndDrag()
    {
        RemovePointer(MousePointerId);
        Ended?.Invoke();
        state = OrbitState.None;
    }

    /// <summary>
    /// onMouseWheel: one zoom step per event by the sign of deltaY (positive = scroll down = dolly out),
    /// ignored while any drag is in progress.
    /// </summary>
    public void Wheel(double deltaY)
    {
        if (!Enabled || !EnableZoom || state != OrbitState.None) return;
        Started?.Invoke();
        if (deltaY < 0) DollyIn(GetZoomScale());
        else if (deltaY > 0) DollyOut(GetZoomScale());
        Update();
        Ended?.Invoke();
    }

    // ---------------------------------------------------------------- touch

    public void TouchBegin(int pointerId, double x, double y)
    {
        if (!Enabled) return;
        AddPointer(pointerId, x, y);
        TrackPointer(pointerId, x, y);
        switch (pointers.Count)
        {
            case 1:
                switch (OneTouch)
                {
                    case TouchAction.Rotate:
                        if (!EnableRotate) return;
                        rotateStart = TouchCentroidAtDown();
                        state = OrbitState.TouchRotate;
                        break;
                    case TouchAction.Pan:
                        if (!EnablePan) return;
                        panStart = TouchCentroidAtDown();
                        state = OrbitState.TouchPan;
                        break;
                    default:
                        state = OrbitState.None;
                        break;
                }
                break;
            case 2:
                switch (TwoTouches)
                {
                    case TouchAction.DollyPan:
                        if (!EnableZoom && !EnablePan) return;
                        if (EnableZoom) TouchStartDolly();
                        if (EnablePan) panStart = TouchCentroidAtDown();
                        state = OrbitState.TouchDollyPan;
                        break;
                    case TouchAction.DollyRotate:
                        if (!EnableZoom && !EnableRotate) return;
                        if (EnableZoom) TouchStartDolly();
                        if (EnableRotate) rotateStart = TouchCentroidAtDown();
                        state = OrbitState.TouchDollyRotate;
                        break;
                    default:
                        state = OrbitState.None;
                        break;
                }
                break;
            default:
                state = OrbitState.None;
                break;
        }
        if (state != OrbitState.None) Started?.Invoke();
    }

    public void TouchMove(int pointerId, double x, double y)
    {
        if (!Enabled) return;
        TrackPointer(pointerId, x, y);
        var p = new Vec2(x, y);
        switch (state)
        {
            case OrbitState.TouchRotate:
                if (!EnableRotate) return;
                TouchMoveRotate(pointerId, p);
                Update();
                break;
            case OrbitState.TouchPan:
                if (!EnablePan) return;
                TouchMovePan(pointerId, p);
                Update();
                break;
            case OrbitState.TouchDollyPan:
                if (!EnableZoom && !EnablePan) return;
                if (EnableZoom) TouchMoveDolly(pointerId, p);
                if (EnablePan) TouchMovePan(pointerId, p);
                Update();
                break;
            case OrbitState.TouchDollyRotate:
                if (!EnableZoom && !EnableRotate) return;
                if (EnableZoom) TouchMoveDolly(pointerId, p);
                if (EnableRotate) TouchMoveRotate(pointerId, p);
                Update();
                break;
            default:
                state = OrbitState.None;
                break;
        }
    }

    public void TouchEnd(int pointerId)
    {
        RemovePointer(pointerId);
        Ended?.Invoke();
        state = OrbitState.None;
    }

    private Vec2 TouchCentroidAtDown()
    {
        // three reads pointers[i].pageX, the position recorded at that pointer's pointerdown.
        if (pointers.Count == 1) return pointerDown[pointers[0]];
        var a = pointerDown[pointers[0]];
        var b = pointerDown[pointers[1]];
        return new Vec2(0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
    }

    private void TouchStartDolly()
    {
        var a = pointerDown[pointers[0]];
        var b = pointerDown[pointers[1]];
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        dollyStart = new Vec2(0, System.Math.Sqrt(dx * dx + dy * dy));
    }

    private Vec2 SecondPointerPosition(int pointerId)
    {
        var otherId = pointers[0] == pointerId ? pointers[1] : pointers[0];
        return pointerPositions[otherId];
    }

    private Vec2 TouchPoint(int pointerId, Vec2 p)
    {
        if (pointers.Count == 1) return p;
        var o = SecondPointerPosition(pointerId);
        return new Vec2(0.5 * (p.X + o.X), 0.5 * (p.Y + o.Y));
    }

    private void TouchMoveRotate(int pointerId, Vec2 p)
    {
        rotateEnd = TouchPoint(pointerId, p);
        var d = (rotateEnd - rotateStart) * RotateSpeed;
        RotateLeft(2 * System.Math.PI * d.X / viewportHeight); // yes, height
        RotateUp(2 * System.Math.PI * d.Y / viewportHeight);
        rotateStart = rotateEnd;
    }

    private void TouchMovePan(int pointerId, Vec2 p)
    {
        panEnd = TouchPoint(pointerId, p);
        var d = (panEnd - panStart) * PanSpeed;
        Pan(d.X, d.Y);
        panStart = panEnd;
    }

    private void TouchMoveDolly(int pointerId, Vec2 p)
    {
        var o = SecondPointerPosition(pointerId);
        var dx = p.X - o.X;
        var dy = p.Y - o.Y;
        dollyEnd = new Vec2(0, System.Math.Sqrt(dx * dx + dy * dy));
        var ratio = System.Math.Pow(dollyEnd.Y / dollyStart.Y, ZoomSpeed);
        DollyOut(ratio);
        dollyStart = dollyEnd;
    }

    private void AddPointer(int id, double x, double y)
    {
        if (!pointers.Contains(id)) pointers.Add(id);
        pointerDown[id] = new Vec2(x, y);
    }

    private void RemovePointer(int id)
    {
        pointerPositions.Remove(id);
        pointerDown.Remove(id);
        pointers.Remove(id);
    }

    private void TrackPointer(int id, double x, double y) => pointerPositions[id] = new Vec2(x, y);

    // ---------------------------------------------------------------- update

    /// <summary>three's update(): applies the accumulated deltas and re-aims the camera. Returns whether the
    /// camera moved by more than EPS (when three would dispatch 'change').</summary>
    public bool Update()
    {
        var twoPi = 2 * System.Math.PI;
        var offset = Position - Target;
        // rotate offset to "y-axis-is-up" space
        offset = quat.Apply(offset);
        // angle from z-axis around y-axis (Spherical.setFromVector3)
        sphRadius = offset.Length;
        if (sphRadius == 0)
        {
            sphTheta = 0;
            sphPhi = 0;
        }
        else
        {
            sphTheta = System.Math.Atan2(offset.X, offset.Z);
            sphPhi = System.Math.Acos(System.Math.Clamp(offset.Y / sphRadius, -1, 1));
        }

        sphTheta += deltaTheta;
        sphPhi += deltaPhi;

        // restrict theta to be between desired limits
        var min = MinAzimuthAngle;
        var max = MaxAzimuthAngle;
        if (double.IsFinite(min) && double.IsFinite(max))
        {
            if (min < -System.Math.PI) min += twoPi; else if (min > System.Math.PI) min -= twoPi;
            if (max < -System.Math.PI) max += twoPi; else if (max > System.Math.PI) max -= twoPi;
            if (min <= max)
                sphTheta = System.Math.Max(min, System.Math.Min(max, sphTheta));
            else
                sphTheta = sphTheta > (min + max) / 2 ? System.Math.Max(min, sphTheta) : System.Math.Min(max, sphTheta);
        }

        // restrict phi to be between desired limits, then Spherical.makeSafe
        sphPhi = System.Math.Max(MinPolarAngle, System.Math.Min(MaxPolarAngle, sphPhi));
        sphPhi = System.Math.Max(Eps, System.Math.Min(System.Math.PI - Eps, sphPhi));

        sphRadius *= scale;
        sphRadius = System.Math.Max(MinDistance, System.Math.Min(MaxDistance, sphRadius));

        // move target to panned location
        Target += panOffset;

        // Vector3.setFromSpherical
        var sinPhiRadius = System.Math.Sin(sphPhi) * sphRadius;
        offset = new Vec3(
            sinPhiRadius * System.Math.Sin(sphTheta),
            System.Math.Cos(sphPhi) * sphRadius,
            sinPhiRadius * System.Math.Cos(sphTheta));
        // rotate offset back to "camera-up-vector-is-up" space
        offset = quatInverse.Apply(offset);

        Position = Target + offset;

        // object.lookAt(target): cameras look down -Z, so the basis is Matrix4.lookAt(position, target, up)
        Basis = Basis3.LookAt(Position, Target, Up);
        Quaternion = Quat.FromRotationMatrix(Basis);

        deltaTheta = 0;
        deltaPhi = 0;
        panOffset = Vec3.Zero;
        scale = 1;

        // update condition is: min(camera displacement, camera rotation in radians)^2 > EPS
        if (Vec3.DistanceSq(lastPosition, Position) > Eps || 8 * (1 - Quat.Dot(lastQuaternion, Quaternion)) > Eps)
        {
            Changed?.Invoke();
            lastPosition = Position;
            lastQuaternion = Quaternion;
            return true;
        }
        return false;
    }
}
