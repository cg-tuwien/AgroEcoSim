using Godot;
using GodotFrontend.Core.Camera;

namespace GodotFrontend.App;

/// <summary>
/// The SubViewportContainer that shows the 3D world and routes pointer input to the orbit controls (and, later,
/// to the gizmo and the picker). It overrides _GuiInput, so a press that lands on the side panel never reaches
/// it, and Godot's GUI focus binds a press to this control until the release even outside the window - the
/// equivalent of the browser's setPointerCapture.
///
/// Units: <c>e.Position</c> is local to this control and <c>Size.Y</c> is its height in the same (content-scaled)
/// pixels, the analogue of the browser's clientX/clientY against clientHeight. ScreenRelative and the window
/// size are deliberately not used; they are in physical pixels and would change the rotation speed on HiDPI.
/// </summary>
public partial class InputRouter : SubViewportContainer
{
    public CameraRig Rig { get; set; } = null!;

    /// <summary>Sum wheel factors and step per whole unit instead of one step per event (an opt-in deviation).</summary>
    public bool WheelAccumulate;
    private double wheelAccumulator;

    /// <summary>Raised with the local pointer position on every move, press, release and double click (for picking).</summary>
    public event Action<Vector2, PointerPhase>? Pointer;

    public enum PointerPhase { Move, Down, Up, Double, Leave }

    private bool pressedLeftWasDoubleClick;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        Stretch = true;
        Resized += () => Rig?.SetViewportSize(Size.X, Size.Y);
        MouseExited += () =>
        {
            if (Rig != null && Rig.Controls.State == OrbitState.None) Pointer?.Invoke(Vector2.Zero, PointerPhase.Leave);
        };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (Rig == null) return;
        var controls = Rig.Controls;
        controls.SetViewport(Size.X, Size.Y);

        switch (e)
        {
            case InputEventMouseButton mb:
            {
                switch (mb.ButtonIndex)
                {
                    case MouseButton.WheelUp:
                    case MouseButton.WheelDown:
                        if (!mb.Pressed) { AcceptEvent(); return; }
                        HandleWheel(mb.ButtonIndex == MouseButton.WheelDown ? +1 : -1, mb.Factor);
                        AcceptEvent();
                        return;
                    case MouseButton.Left:
                    case MouseButton.Middle:
                    case MouseButton.Right:
                    {
                        var button = mb.ButtonIndex switch
                        {
                            MouseButton.Left => OrbitButton.Left,
                            MouseButton.Middle => OrbitButton.Middle,
                            _ => OrbitButton.Right,
                        };
                        if (mb.Pressed)
                        {
                            GrabFocus();
                            var mods = OrbitModifiers.None;
                            if (mb.CtrlPressed) mods |= OrbitModifiers.Ctrl;
                            if (mb.MetaPressed) mods |= OrbitModifiers.Meta;
                            if (mb.ShiftPressed) mods |= OrbitModifiers.Shift;
                            if (button == OrbitButton.Left) pressedLeftWasDoubleClick = mb.DoubleClick;
                            // three registers OrbitControls' pointerdown before the picker's mousedown, so the orbit
                            // state is entered first and the pick sees the same event afterwards.
                            controls.BeginDrag(button, mods, mb.Position.X, mb.Position.Y);
                            Pointer?.Invoke(mb.Position, PointerPhase.Down);
                        }
                        else
                        {
                            if (controls.State != OrbitState.None) controls.EndDrag();
                            Pointer?.Invoke(mb.Position, PointerPhase.Up);
                            // The browser fires dblclick after the second mouseup.
                            if (button == OrbitButton.Left && pressedLeftWasDoubleClick)
                            {
                                pressedLeftWasDoubleClick = false;
                                Pointer?.Invoke(mb.Position, PointerPhase.Double);
                            }
                        }
                        AcceptEvent();
                        return;
                    }
                    default:
                        return; // X buttons: ignored, as three's mouseAction = -1
                }
            }
            case InputEventMouseMotion mm:
                if (controls.State != OrbitState.None) controls.Move(mm.Position.X, mm.Position.Y);
                Pointer?.Invoke(mm.Position, PointerPhase.Move);
                AcceptEvent();
                return;
            case InputEventPanGesture pan:
                // macOS trackpad two-finger scroll arrives as a pan gesture; browsers deliver it as wheel events.
                if (pan.Delta.Y != 0) HandleWheel(System.Math.Sign(pan.Delta.Y), System.Math.Abs(pan.Delta.Y));
                AcceptEvent();
                return;
            case InputEventMagnifyGesture mag:
                // Pinch: browsers deliver it as ctrl+wheel, one step per event.
                HandleWheel(mag.Factor > 1 ? -1 : +1, 1);
                AcceptEvent();
                return;
        }
    }

    private void HandleWheel(int sign, float factor)
    {
        var controls = Rig.Controls;
        if (!WheelAccumulate)
        {
            // One 0.95 step per event, whatever the magnitude: OrbitControls.handleMouseWheel.
            controls.Wheel(sign);
            return;
        }
        wheelAccumulator += sign * (factor > 0 ? factor : 1);
        while (System.Math.Abs(wheelAccumulator) >= 1)
        {
            var s = System.Math.Sign(wheelAccumulator);
            controls.Wheel(s);
            wheelAccumulator -= s;
        }
    }
}
