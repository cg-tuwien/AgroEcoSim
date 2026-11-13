namespace GodotFrontend.Core.Color;

/// <summary>render/core/Ramps.ts: the data-mapping colour ramps and the fixed organ colours.</summary>
public static class Ramps
{
    public static readonly RGB WaterEmpty = ColorMath.FromHex("#909090");
    public static readonly RGB WaterFull = ColorMath.FromHex("#00BFE6");
    public static readonly RGB EnergyFull = ColorMath.FromHex("#ffc757");
    public static readonly RGB AuxinsFull = ColorMath.FromHex("#FDDE81");
    public static readonly RGB CytokininsFull = ColorMath.FromHex("#D84315");
    public static readonly RGB IrrEmpty = ColorMath.FromHex("#424d62");
    public static readonly RGB IrrFull = ColorMath.FromHex("#ffcc66");
    public static readonly RGB EffEmpty = ColorMath.FromHex("#0a95ff");
    public static readonly RGB EffFull = ColorMath.FromHex("#ff0a91");
    public static readonly RGB Wood = ColorMath.FromHex("#7f4f1f");
    public static readonly RGB Green = ColorMath.FromHex("#009900");
    public static readonly RGB Grey = ColorMath.FromHex("#898989");
    public static readonly RGB Black = RGB.Black;

    public static RGB Water(double value) => ColorMath.Lerp(WaterEmpty, WaterFull, value);
    public static RGB Energy(double value) => ColorMath.LerpHSL(WaterEmpty, EnergyFull, value);
    /// <summary>HSL lerp; the input is offset by 1 before clamping.</summary>
    public static RGB Auxins(double value) => ColorMath.LerpHSL(WaterEmpty, AuxinsFull, System.Math.Min(1, System.Math.Max(0, value - 1)));
    public static RGB Cytokinins(double value) => ColorMath.LerpHSL(WaterEmpty, CytokininsFull, value);
    /// <summary>Callers pass stats[4] * 1e-3.</summary>
    public static RGB Irradiance(double value) => ColorMath.Lerp(IrrEmpty, IrrFull, value);
    public static RGB Efficiency(double value) => ColorMath.Lerp(EffEmpty, EffFull, value);

    private static readonly RGB[] HeatColors =
    {
        ColorMath.FromHex("#003f5c"), ColorMath.FromHex("#58508d"), ColorMath.FromHex("#bc5090"),
        ColorMath.FromHex("#ff6361"), ColorMath.FromHex("#ffa600"),
    };

    /// <summary>HSL lerp across 5 stops, for the obstacle mapping.</summary>
    public static RGB Heat(double value)
    {
        var maxIdx = HeatColors.Length - 1;
        var p = System.Math.Max(0, System.Math.Min(1, value)) * maxIdx;
        var s = (int)System.Math.Floor(p);
        return s == maxIdx ? HeatColors[maxIdx] : ColorMath.LerpHSL(HeatColors[s], HeatColors[s + 1], p - s);
    }
}
