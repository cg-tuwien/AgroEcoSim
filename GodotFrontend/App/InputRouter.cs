using Godot;
using GodotFrontend.Core.Camera;

namespace GodotFrontend.App;

/// <summary>
/// The control that shows the 3D world and routes pointer input to the orbit controls (and, later,
/// to the gizmo and the picker). It overrides _GuiInput, so a press that lands on the side panel never reaches
/// it, and Godot's GUI focus binds a press to this control until the release even outside the window - the
/// equivalent of the browser's setPointerCapture.
///
/// Units: <c>e.Position</c> is local to this control and <c>Size.Y</c> is its height in the same (content-scaled)
/// pixels, the analogue of the browser's clientX/clientY against clientHeight. ScreenRelative and the window
/// size are deliberately not used; they are in physical pixels and would change the rotation speed on HiDPI.
///
/// <para>It draws the 3D viewport's texture itself rather than being a SubViewportContainer, because a container
/// resizes its viewport the moment the layout changes: the render target is reallocated, and in low-processor mode
/// the next frame may show it before anything has rendered into it (measured: the Godot boot splash after F10, noise
/// after a window resize). Here the viewport keeps its size, and its last image stays on screen at the same vertical
/// scale, centred (<see cref="HoldRect"/>), until the panel has kept one size for <see cref="SettleSeconds"/>; then the
/// viewport is resized and renders before the window does in the same frame. The camera keeps the vertical field of
/// view, so the held image is exactly the middle of what the new size will show: only the margins are missing.</para>
/// </summary>
public partial class InputRouter : Control
{
    public CameraRig Rig { get; set; } = null!;

    /// <summary>The 3D viewport this control shows; a child of it.</summary>
    public SubViewport? View { get; set; }

    /// <summary>True while the stereo checkerboard is shown: the held image is then offset by whole pixel pairs.</summary>
    public bool KeepPixelParity { get; set; }

    /// <summary>Raised whenever the panel changed size, so the app keeps drawing until the change has settled.</summary>
    public event Action? SizeChanging;

    /// <summary>How long the panel has to keep one size before the viewport is reallocated to it.</summary>
    public const double SettleSeconds = 0.12;

    private Vector2I pendingSize;
    private ulong pendingSinceUs;
    private bool sized;

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
        Resized += () =>
        {
            Rig?.SetViewportSize(Size.X, Size.Y);
            pendingSize = PixelSize();
            pendingSinceUs = Time.GetTicksUsec();
            QueueRedraw();
            SizeChanging?.Invoke();
        };
        MouseExited += () =>
        {
            if (Rig != null && Rig.Controls.State == OrbitState.None) Pointer?.Invoke(Vector2.Zero, PointerPhase.Leave);
        };
    }

    private Vector2I PixelSize() => new(System.Math.Max(1, (int)MathF.Round(Size.X)), System.Math.Max(1, (int)MathF.Round(Size.Y)));

    public override void _Process(double delta)
    {
        if (View == null) return;
        var target = PixelSize();
        if (View.Size == target) return;
        // The first size is applied at once (there is nothing to hold yet); later ones once the layout has settled.
        if (sized && Time.GetTicksUsec() - pendingSinceUs < (ulong)(SettleSeconds * 1e6)) return;
        sized = true;
        View.Size = target;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (View == null) return;
        var rect = HoldRect(View.Size, Size, KeepPixelParity);
        if (rect.Position != Vector2.Zero || rect.Size != Size)
            DrawRect(new Rect2(Vector2.Zero, Size), Render.LightingRig.BackgroundColor);
        DrawTextureRect(View.GetTexture(), rect, false);
    }

    /// <summary>
    /// Where a viewport image rendered at <paramref name="render"/> is drawn in a panel of <paramref name="panel"/>:
    /// the whole panel when the sizes agree; otherwise scaled to the panel's height and centred, which is how the
    /// camera (vertical field of view) will frame the panel once the viewport has been resized. With
    /// <paramref name="keepParity"/> an unscaled image moves by an even number of pixels, so the stereo interleave
    /// keeps every pixel on its eye while it is held.
    /// </summary>
    public static Rect2 HoldRect(Vector2I render, Vector2 panel, bool keepParity)
    {
        if (render.X == (int)MathF.Round(panel.X) && render.Y == (int)MathF.Round(panel.Y)) return new Rect2(Vector2.Zero, panel);
        var scale = panel.Y / System.Math.Max(1, render.Y);
        var width = render.X * scale;
        var x = (panel.X - width) * 0.5f;
        x = keepParity && render.Y == (int)MathF.Round(panel.Y) ? 2 * MathF.Round(x * 0.5f) : MathF.Round(x);
        return new Rect2(x, 0, width, panel.Y);
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
