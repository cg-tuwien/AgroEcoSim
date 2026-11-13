using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Picking;

/// <summary>The renderer's gizmo, if it offers one (ThreeRenderer does; the WebGPU backend did not).</summary>
public interface IGizmoHost
{
    bool HasGizmo { get; }
    void AttachGizmo(SelectableObject target);
    void DetachGizmo(SelectableObject target);
}

/// <summary>
/// Base for the editable objects in the scene: seeds, obstacles and terrain items (helpers/BaseRequestObject.ts).
/// Holds position, the selection state and the FSM entry points; whatever is mounted draws something at that
/// position in the colour the selection state specifies.
/// </summary>
public class SelectableObject : ISelectable
{
    public double Px, Py, Pz;
    /// <summary>Index of the tray the position is relative to.</summary>
    public int FieldIndex;
    public SelectionState State { get; private set; } = SelectionState.None;
    /// <summary>Forces direct cursor dragging even when a gizmo is available (historically always false).</summary>
    public bool RespondToMove = false;
    public bool Movable = true;

    /// <summary>Raised whenever something about the object that is drawn changed (appstate.needsRender).</summary>
    public event Action? Changed;

    private IGizmoHost? host;
    private Vec3? grabOffset;

    public SelectableObject(double x, double y, double z, int fieldIndex = 0)
    {
        Px = x; Py = y; Pz = z;
        FieldIndex = fieldIndex;
    }

    public Vec3 LocalPosition => new(Px, Py, Pz);

    public void SetGizmoHost(IGizmoHost? gizmoHost) => host = gizmoHost;

    /// <summary>True when no gizmo is mounted, so the object handles dragging itself by projecting the cursor onto its own height.</summary>
    private bool DragsDirectly => RespondToMove || host == null || !host.HasGizmo;

    protected void NotifyChanged() => Changed?.Invoke();

    public void Hover()
    {
        State = SelectionState.Hover;
        NotifyChanged();
    }

    public void Unhover()
    {
        State = SelectionState.None;
        if (Movable) host?.DetachGizmo(this);
        NotifyChanged();
    }

    public void Select()
    {
        State = SelectionState.Select;
        NotifyChanged();
    }

    public void SelectHover()
    {
        State = SelectionState.SelectHover;
        if (Movable) host?.AttachGizmo(this);
        NotifyChanged();
    }

    /// <summary>Called by a gizmo with the object's new WORLD position; subclasses undo the offsets they draw with.</summary>
    public virtual void TransformMove(double x, double y, double z)
    {
        if (!Movable) return;
        Px = x; Py = y; Pz = z;
        NotifyChanged();
    }

    public void Grab(Ray ray)
    {
        if (!Movable || !DragsDirectly) return;
        var start = ray.IntersectPlaneY(Py);
        if (start is null) return;
        State = SelectionState.Grab;
        grabOffset = new Vec3(Px - start.Value.X, Py - start.Value.Y, Pz - start.Value.Z);
        NotifyChanged();
    }

    public void Move(Ray ray)
    {
        if (!Movable || !DragsDirectly || grabOffset is null) return;
        var p = ray.IntersectPlaneY(Py);
        if (p is null) return;
        Px = grabOffset.Value.X + p.Value.X;
        Py = grabOffset.Value.Y + p.Value.Y;
        Pz = grabOffset.Value.Z + p.Value.Z;
        NotifyChanged();
    }

    public void Ungrab(SelectionState state)
    {
        if (!DragsDirectly) return;
        State = state;
        grabOffset = null;
        NotifyChanged();
    }

    public bool IsSelected => State is SelectionState.Select or SelectionState.SelectHover;
}
