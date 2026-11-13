using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Color;

/// <summary>The visual data mappings (render/core/PlantColors.ts VisualMappingOptions).</summary>
public enum VisualMapping { Natural, Water, Auxins, Cytokinins, Energy, Irradiance, Resource, Production }

/// <summary>A primitive's shading inputs: Natural fills Color (lit), every other mapping fills Emissive with Color black (unlit readout).</summary>
public readonly record struct OrganColor(RGB Color, RGB Emissive);

/// <summary>
/// The per-organ colour rules of render/core/PlantColors.ts over a <see cref="DecodedScene"/>. The renderer uses the
/// natural colour per frame and evaluates the readout ramps on the GPU; this CPU version is the reference for tests,
/// for the hover readout and for exports.
/// </summary>
public static class PlantColors
{
    public static string MappingName(VisualMapping m) => m switch
    {
        VisualMapping.Natural => "natural", VisualMapping.Water => "water", VisualMapping.Auxins => "auxins",
        VisualMapping.Cytokinins => "cytokinins", VisualMapping.Energy => "energy", VisualMapping.Irradiance => "irradiance",
        VisualMapping.Resource => "resource", VisualMapping.Production => "production", _ => "natural",
    };

    public static VisualMapping ParseMapping(string? s) => s switch
    {
        "water" => VisualMapping.Water, "auxins" => VisualMapping.Auxins, "cytokinins" => VisualMapping.Cytokinins,
        "energy" => VisualMapping.Energy, "irradiance" => VisualMapping.Irradiance, "resource" => VisualMapping.Resource,
        "production" => VisualMapping.Production, _ => VisualMapping.Natural,
    };

    /// <summary>Cylinder organs whose colour the backend decides rather than the wood ramp.</summary>
    public static bool IsBackendColoredCylinder(OrganType o) =>
        o is OrganType.Petiole or OrganType.FlowerStem or OrganType.FlowerMeristem or OrganType.FlowerPedicel or OrganType.FlowerBud;

    private static readonly RGB CeltisBark = ColorMath.FromHex("#929087");

    public static RGB? NaturalBark(string species) => species == "Celtis australis" ? CeltisBark : null;

    private static OrganColor? Shared(VisualMapping mapping, ReadOnlySpan<float> stats)
    {
        return mapping switch
        {
            VisualMapping.Water => new OrganColor(RGB.Black, Ramps.Water(stats[Stat.Water])),
            VisualMapping.Energy => new OrganColor(RGB.Black, Ramps.Energy(stats[Stat.Energy])),
            VisualMapping.Auxins => new OrganColor(RGB.Black, Ramps.Auxins(stats[Stat.Auxins])),
            VisualMapping.Cytokinins => new OrganColor(RGB.Black, Ramps.Cytokinins(stats[Stat.Cytokinins])),
            VisualMapping.Resource => new OrganColor(RGB.Black, Ramps.Efficiency(stats[Stat.RelResource])),
            VisualMapping.Production => new OrganColor(RGB.Black, Ramps.Efficiency(stats[Stat.RelProduction])),
            _ => null,
        };
    }

    private static readonly OrganColor NeutralColor = new(Theme.Neutral, RGB.Black);

    private static RGB Wire(DecodedScene s, int i) =>
        new(SrgbLut.ToLinear(s.Rgb[i * 3]), SrgbLut.ToLinear(s.Rgb[i * 3 + 1]), SrgbLut.ToLinear(s.Rgb[i * 3 + 2]));

    /// <summary>The natural (lit) colour of a primitive, independent of the mapping.</summary>
    public static RGB Natural(DecodedScene s, int i)
    {
        var o = s.Organ[i];
        switch (s.Kind[i])
        {
            case PrimitiveType.Rectangle:
            case PrimitiveType.Ellipsoid:
                return s.HasColor(i)
                    ? Wire(s, i)
                    : Palette.GreenColors[s.IndexInPlant(i) % Palette.GreenColors.Length];
            case PrimitiveType.Cylinder:
            {
                if (IsBackendColoredCylinder(o) && s.HasColor(i))
                    return Wire(s, i);
                var wood = s.Stats is null ? 0 : s.Stats[i * Stat.Count + Stat.IrradianceOrWood];
                var bark = (o == OrganType.Stem || o == OrganType.Meristem) ? NaturalBark(s.Species[s.PlantSpecies[s.Plant[i]]]) : null;
                return ColorMath.LerpHSL(Ramps.Green, bark ?? Ramps.Wood, wood);
            }
            case PrimitiveType.Box:
            {
                var wood = s.Stats is null ? 0 : s.Stats[i * Stat.Count + Stat.IrradianceOrWood];
                return ColorMath.LerpHSL(Ramps.Grey, Ramps.Wood, wood);
            }
            default:
                return Theme.Neutral;
        }
    }

    /// <summary>The web's leafColor/stemColor/rootColor dispatch for one primitive under a mapping.</summary>
    public static OrganColor Of(DecodedScene s, int i, VisualMapping mapping)
    {
        if (s.Stats is null) return NeutralColor; // BuildScene: primitives without stats are neutral
        var stats = s.StatsOf(i);
        var o = s.Organ[i];
        switch (s.Kind[i])
        {
            case PrimitiveType.Rectangle:
            case PrimitiveType.Ellipsoid:
                if (mapping == VisualMapping.Natural) return new OrganColor(Natural(s, i), RGB.Black);
                if (mapping == VisualMapping.Irradiance)
                    return o is OrganType.Fruit or OrganType.FruitWing
                        ? NeutralColor
                        : new OrganColor(RGB.Black, Ramps.Irradiance(stats[Stat.IrradianceOrWood] * 1e-3));
                return Shared(mapping, stats) ?? NeutralColor;
            case PrimitiveType.Cylinder:
            case PrimitiveType.Box:
                if (mapping == VisualMapping.Natural) return new OrganColor(Natural(s, i), RGB.Black);
                return Shared(mapping, stats) ?? NeutralColor;
            default:
                return NeutralColor;
        }
    }
}
