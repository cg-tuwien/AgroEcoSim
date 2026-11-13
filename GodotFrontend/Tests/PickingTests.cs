using GodotFrontend.Core.Math;
using GodotFrontend.Core.Picking;
using Xunit;

namespace GodotFrontend.Tests;

/// <summary>Port of ThreeFrontend/tests/picking.test.ts against the FSM driver.</summary>
public class PickingTests
{
    private static readonly Ray Ray = new(new Vec3(0, 5, 0), new Vec3(0, -1, 0));

    private sealed class Store : IPickStore
    {
        public List<ISelectable> SeedList = new();
        public List<ISelectable> ObstacleList = new();
        public List<ISelectable> TerrainList = new();
        public IReadOnlyList<ISelectable> Seeds => SeedList;
        public IReadOnlyList<ISelectable> Obstacles => ObstacleList;
        public IReadOnlyList<ISelectable> Terrain => TerrainList;
        public string PlantPick { get; set; } = "";
        public int SeedPick { get; set; } = -1;
        public int TerrainPick { get; set; } = -1;
    }

    private static (Store store, SelectableObject[] seeds) Fresh(int n)
    {
        var store = new Store();
        var seeds = Enumerable.Range(0, n).Select(i => new SelectableObject(i, 0, 0)).ToArray();
        store.SeedList.AddRange(seeds);
        return (store, seeds);
    }

    [Fact]
    public void HoveringASeedSetsItsStateAndPublishesItsIndex()
    {
        var (store, seeds) = Fresh(2);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.None, Ray, store);
        Assert.Equal(SelectionState.Hover, seeds[0].State);
        Assert.Equal(0, store.SeedPick);
        Assert.Equal("", store.PlantPick);
    }

    [Fact]
    public void HoveringOneSeedClearsTheHoverOnEveryOtherSeed()
    {
        var (store, seeds) = Fresh(2);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.None, Ray, store);
        PickingLogic.ApplyPick(new SeedRef(seeds[1]), Clicks.None, Ray, store);
        Assert.Equal(SelectionState.Hover, seeds[1].State);
        Assert.Equal(SelectionState.None, seeds[0].State);
    }

    [Fact]
    public void DoubleClickSelectsAndALaterHoverElsewhereKeepsTheSelection()
    {
        var (store, seeds) = Fresh(2);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.Double, Ray, store);
        Assert.Equal(SelectionState.SelectHover, seeds[0].State);
        Assert.True(seeds[0].IsSelected);
        PickingLogic.ApplyPick(new SeedRef(seeds[1]), Clicks.None, Ray, store);
        Assert.Equal(SelectionState.Select, seeds[0].State);
        Assert.True(seeds[0].IsSelected);
    }

    [Fact]
    public void DoubleClickingADifferentSeedClearsThePreviousSelection()
    {
        var (store, seeds) = Fresh(2);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.Double, Ray, store);
        PickingLogic.ApplyPick(new SeedRef(seeds[1]), Clicks.Double, Ray, store);
        Assert.True(seeds[1].IsSelected);
        Assert.Equal(SelectionState.None, seeds[0].State);
    }

    [Fact]
    public void PickingEmptySpaceClearsHoversAndAllPickValues()
    {
        var (store, seeds) = Fresh(1);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.None, Ray, store);
        Assert.Equal(0, store.SeedPick);
        PickingLogic.ApplyPick(null, Clicks.None, Ray, store);
        Assert.Equal(SelectionState.None, seeds[0].State);
        Assert.Equal(-1, store.SeedPick);
        Assert.Equal("", store.PlantPick);
    }

    [Fact]
    public void APlantPickPublishesTheEncodedNameOnlyOnAPlainHover()
    {
        var (store, _) = Fresh(0);
        PickingLogic.ApplyPick(new PlantRef(new PlantIndex(3, 17)), Clicks.None, Ray, store);
        Assert.Equal("3_17", store.PlantPick);
        PickingLogic.ApplyPick(new PlantRef(new PlantIndex(3, 17)), Clicks.Double, Ray, store);
        Assert.Equal("", store.PlantPick);
    }

    [Fact]
    public void APlantPickSuppressesTheSeedIndex()
    {
        var (store, seeds) = Fresh(1);
        PickingLogic.ApplyPick(new SeedRef(seeds[0]), Clicks.None, Ray, store);
        Assert.Equal(0, store.SeedPick);
        PickingLogic.ApplyPick(new PlantRef(new PlantIndex(0, 0)), Clicks.None, Ray, store);
        Assert.Equal(-1, store.SeedPick);
    }

    [Fact]
    public void ObstaclesFollowTheSameFsm()
    {
        var store = new Store();
        var wall = new SelectableObject(0, 0, 0);
        store.ObstacleList.Add(wall);
        PickingLogic.ApplyPick(new ObstacleRef(wall), Clicks.None, Ray, store);
        Assert.Equal(SelectionState.Hover, wall.State);
        PickingLogic.ApplyPick(new ObstacleRef(wall), Clicks.Double, Ray, store);
        Assert.True(wall.IsSelected);
        PickingLogic.ApplyPick(null, Clicks.Double, Ray, store);
        Assert.Equal(SelectionState.None, wall.State);
    }

    [Fact]
    public void DirectDragProjectsOntoTheObjectsOwnHeightWhenNoGizmoIsMounted()
    {
        var (store, seeds) = Fresh(1);
        var s = seeds[0];
        s.Py = 1;
        PickingLogic.ApplyPick(new SeedRef(s), Clicks.Double, Ray, store);
        PickingLogic.ApplyPick(new SeedRef(s), Clicks.Down, Ray, store);
        Assert.Equal(SelectionState.Grab, s.State);
        var moved = new Ray(new Vec3(2, 5, 0), new Vec3(0, -1, 0));
        s.Move(moved);
        Assert.Equal(2, s.Px, 9);
        Assert.Equal(1, s.Py, 9);
        PickingLogic.ApplyPick(new SeedRef(s), Clicks.Up, moved, store);
        Assert.Equal(SelectionState.SelectHover, s.State);
    }
}
