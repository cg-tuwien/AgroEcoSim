namespace GodotFrontend.Core.Color;

/// <summary>
/// render/core/Theme.ts with the values the web page's light theme actually computes (--fgcolor #444, --bgcolor
/// #fafafa), and the neutral primitive colour as their HSL blend at 0.1, as helpers/Plant.ts computes it.
/// </summary>
public static class Theme
{
    public static readonly RGB NeutralColor = ColorMath.FromHex("#444444");
    /// <summary>The viewport's clear colour: sRGB 250 grey.</summary>
    public static readonly RGB BackgroundColor = ColorMath.FromHex("#fafafa");
    /// <summary>Colour of primitives without stats and the fallback of every mapping's default branch.</summary>
    public static readonly RGB Neutral = ColorMath.LerpHSL(NeutralColor, BackgroundColor, 0.1);
    /// <summary>Soil colour of trays and the default field box (helpers/Terrain.ts).</summary>
    public static readonly RGB Terrain = ColorMath.FromHex("#573600");
    /// <summary>Palette colour of unpainted obstacles (helpers/Obstacle.ts).</summary>
    public static readonly RGB Obstacle = ColorMath.FromHex("#999");
}
