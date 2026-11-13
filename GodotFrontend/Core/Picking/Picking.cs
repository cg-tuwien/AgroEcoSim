using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Picking;

/// <summary>The pointer event that produced a pick; RendererHost maps mousemove/mousedown/mouseup/dblclick onto these.</summary>
public enum Clicks { None, Down, Up, Double }

/// <summary>Per-object selection state; the string union of helpers/Selection.ts.</summary>
public enum SelectionState { None, Hover, Select, SelectHover, Grab }

/// <summary>The subset of a selectable object the FSM driver needs (Picking.ts ISelectable).</summary>
public interface ISelectable
{
    SelectionState State { get; }
    void Hover();
    void Unhover();
    void Select();
    void SelectHover();
    void Grab(Ray ray);
    void Move(Ray ray);
    void Ungrab(SelectionState state);
}

/// <summary>Identity of a plant primitive: the plant's index in the frame and the primitive's index in the plant.</summary>
public readonly record struct PlantIndex(int Entity, int Primitive)
{
    /// <summary>helpers/Plant.ts EncodePlantName.</summary>
    public string Encode() => $"{Entity}_{Primitive}";

    public static PlantIndex? Decode(string name)
    {
        var a = name.Split('_');
        if (a.Length != 2 || !int.TryParse(a[0], out var e) || !int.TryParse(a[1], out var p)) return null;
        return new PlantIndex(e, p);
    }
}

/// <summary>What a raycast hit (Picking.ts DataRef).</summary>
public abstract record PickRef;
public sealed record SeedRef(ISelectable Seed) : PickRef;
public sealed record ObstacleRef(ISelectable Obstacle) : PickRef;
public sealed record PlantRef(PlantIndex Index) : PickRef;
public sealed record TerrainRef(ISelectable? Terrain, int Index) : PickRef;

/// <summary>The slice of application state the pick driver reads and publishes to.</summary>
public interface IPickStore
{
    IReadOnlyList<ISelectable> Seeds { get; }
    IReadOnlyList<ISelectable> Obstacles { get; }
    IReadOnlyList<ISelectable> Terrain { get; }
    /// <summary>Encoded "entity_primitive" of the hovered plant organ, or "" (appstate.plantPick).</summary>
    string PlantPick { set; }
    /// <summary>Index of the picked seed or -1 (appstate.seedPick).</summary>
    int SeedPick { set; }
    /// <summary>Index of the picked terrain item or -1 (appstate.terrainPick).</summary>
    int TerrainPick { set; }
}

/// <summary>
/// The selection state machine driver, lifted verbatim from render/core/Picking.ts and the clear* helpers of
/// appstate.ts. The transition table is the behaviour users are used to, so it is reproduced rather than tidied.
/// </summary>
public static class PickingLogic
{
    /// <summary>Drives one object's selection transitions for a single interaction (pickingLogic).</summary>
    public static void Apply(Clicks clicks, ISelectable target, Ray ray)
    {
        if (clicks == Clicks.Double)
        {
            if (target.State != SelectionState.Select) target.SelectHover();
        }
        else if (clicks == Clicks.Down)
        {
            if (target.State == SelectionState.SelectHover) target.Grab(ray);
        }
        else if (clicks == Clicks.Up)
        {
            if (target.State == SelectionState.Grab) target.Ungrab(SelectionState.SelectHover);
        }
        else
        {
            switch (target.State)
            {
                case SelectionState.None: target.Hover(); break;
                case SelectionState.Select: target.SelectHover(); break;
            }
        }
    }

    /// <summary>
    /// Applies a resolved pick: drives the FSM for the hit object, clears every other object's transient state
    /// and publishes the pick values (applyPick). Synchronous on purpose: move/down/up must apply in order.
    /// </summary>
    public static void ApplyPick(PickRef? hit, Clicks clicks, Ray ray, IPickStore store)
    {
        ISelectable? seedPick = null;
        ISelectable? obstaclePick = null;
        ISelectable? terrainPick = null;
        var plantPick = "";

        switch (hit)
        {
            case SeedRef s:
                seedPick = s.Seed;
                Apply(clicks, seedPick, ray);
                break;
            case ObstacleRef o:
                obstaclePick = o.Obstacle;
                Apply(clicks, obstaclePick, ray);
                break;
            case PlantRef p:
                // Plants are not selectable objects; only a plain hover reports one.
                if (clicks == Clicks.None) plantPick = p.Index.Encode();
                break;
            case TerrainRef t:
                terrainPick = t.Terrain;
                if (terrainPick != null) Apply(clicks, terrainPick, ray);
                break;
        }

        ClearHovers(store.Seeds, seedPick);
        ClearHovers(store.Obstacles, obstaclePick);
        ClearHovers(store.Terrain, terrainPick);
        if (clicks == Clicks.Double)
        {
            ClearSelects(store.Seeds, seedPick);
            ClearSelects(store.Obstacles, obstaclePick);
            ClearSelects(store.Terrain, terrainPick);
        }
        if (clicks == Clicks.Up)
        {
            ClearGrabs(store.Seeds, seedPick);
            ClearGrabs(store.Obstacles, obstaclePick);
        }

        store.PlantPick = plantPick;
        store.SeedPick = plantPick.Length == 0 && seedPick != null ? IndexOf(store.Seeds, seedPick) : -1;
        store.TerrainPick = terrainPick != null ? IndexOf(store.Terrain, terrainPick) : -1;
    }

    public static void ClearHovers(IReadOnlyList<ISelectable> src, ISelectable? except)
    {
        foreach (var x in src)
        {
            if (ReferenceEquals(x, except)) continue;
            switch (x.State)
            {
                case SelectionState.Hover: x.Unhover(); break;
                case SelectionState.SelectHover: x.Select(); break;
            }
        }
    }

    public static void ClearSelects(IReadOnlyList<ISelectable> src, ISelectable? except)
    {
        foreach (var s in src)
        {
            if (ReferenceEquals(s, except)) continue;
            if (s.State is SelectionState.Select or SelectionState.SelectHover) s.Unhover();
        }
    }

    public static void ClearGrabs(IReadOnlyList<ISelectable> src, ISelectable? except)
    {
        foreach (var s in src)
        {
            if (ReferenceEquals(s, except)) continue;
            if (s.State == SelectionState.Grab) s.Ungrab(SelectionState.Select);
        }
    }

    private static int IndexOf(IReadOnlyList<ISelectable> list, ISelectable item)
    {
        for (var i = 0; i < list.Count; ++i)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }
}
