namespace Agro;

public partial class PlantFormation2
{
    float? WoodyGroundHeightCache;

    /// <summary>
    /// Soil-line height in this plant's field coordinates, independent of seed burial.
    /// Derived once from immutable scene geometry, so checkpoints rebuild it identically.
    /// A planting point outside the soil or without an upward crossing uses its origin.
    /// </summary>
    internal float WoodyGroundHeight
    {
        get
        {
            if (WoodyGroundHeightCache.HasValue)
                return WoodyGroundHeightCache.Value;
            var origin = Soil.GetFieldOrigin(SoilIndex);
            var point = Position + origin;
            var scene = World.Collisions.StaticScene;
            // A caller may inspect the plant before the static scene is prepared.
            // Do not make that temporary absence a permanent planting-depth datum.
            if (scene.IsEmpty)
                return Position.Y;
            var height = Position.Y;
            if ((scene.InsideSoil(point, 1f) || scene.ClosestSoilSurface(point, 1e-4f, out _))
                && scene.TrySoilSurfaceAbove(point, out var worldHeight))
                height = worldHeight - origin.Y;
            WoodyGroundHeightCache = height;
            return height;
        }
    }
}
