using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;

namespace GodotFrontend.Core.Model;

/// <summary>What the viewport shows and how: the data mapping, the visibility toggles and the lighting switches.</summary>
public sealed class ViewState
{
    public VisualMapping Mapping = VisualMapping.Natural;
    public bool DebugBoxes;
    public bool ShowLeaves = true, ShowTerrain = true, ShowRoots = true, ShowObstacles = true, ShowSeeds = true;
    public bool PerformPicking = true;
    public LightingOptions Lighting = LightingOptions.Off;
    /// <summary>The web's obstacleMapping select ("none", "lai", ...); carried through sessions for the deferred panel.</summary>
    public string ObstacleMapping = "none";
}
