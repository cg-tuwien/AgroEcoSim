using Godot;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Packing;
using AgroTheme = GodotFrontend.Core.Color.Theme;

namespace GodotFrontend.App.Render;

/// <summary>
/// The shader materials, one per bucket variant plus the terrain/obstacle one, and the uniforms they share:
/// the active data mapping with its ramp, the hemisphere light and the translucency. Godot has no uniform block
/// shared by user shaders, so the set is pushed to every material; there are five of them.
/// </summary>
public sealed class MaterialSet
{
    private readonly ShaderMaterial foliage, foliageFolded, laminaOpaque, fruit, opaque, terrain, obstacle, seed;
    private readonly ShaderMaterial[] all;

    public MaterialSet()
    {
        var foliageShader = GD.Load<Shader>("res://App/Shaders/plant_foliage.gdshader");
        var opaqueShader = GD.Load<Shader>("res://App/Shaders/plant_opaque.gdshader");
        var terrainShader = GD.Load<Shader>("res://App/Shaders/terrain.gdshader");
        foliage = new ShaderMaterial { Shader = foliageShader, ResourceName = "foliage" };
        foliageFolded = new ShaderMaterial { Shader = foliageShader, ResourceName = "foliage-folded" };
        laminaOpaque = new ShaderMaterial { Shader = foliageShader, ResourceName = "lamina-opaque" };
        laminaOpaque.SetShaderParameter("foliage", 0.0f);
        fruit = new ShaderMaterial { Shader = foliageShader, ResourceName = "fruit" };
        fruit.SetShaderParameter("foliage", 0.0f);
        opaque = new ShaderMaterial { Shader = opaqueShader, ResourceName = "opaque" };
        terrain = new ShaderMaterial { Shader = terrainShader, ResourceName = "terrain" };
        terrain.SetShaderParameter("albedo_color", ToG(AgroTheme.Terrain));
        obstacle = new ShaderMaterial { Shader = terrainShader, ResourceName = "obstacle" };
        obstacle.SetShaderParameter("albedo_color", ToG(AgroTheme.Obstacle));
        obstacle.SetShaderParameter("depth_bias", 2e-5f);
        // seeds are drawn with their selection colour under every mapping, so they are not in `all` for SetMapping
        seed = new ShaderMaterial { Shader = opaqueShader, ResourceName = "seed" };
        all = new[] { foliage, foliageFolded, laminaOpaque, fruit, opaque, terrain, obstacle };
        SetMapping(VisualMapping.Natural);
        // The hemisphere light the web scene has always had: HemisphereLight(0xffffff, 0x707070, 3.175).
        SetHemisphere(ColorMath.FromHex("#ffffff") * 3.175f, ColorMath.FromHex("#707070") * 3.175f, analytic: true);
        foreach (var m in all) m.SetShaderParameter("neutral_color", ToG(AgroTheme.Neutral));
    }

    public ShaderMaterial For(BucketVariant v) => v switch
    {
        BucketVariant.Foliage => foliage,
        BucketVariant.FoliageFolded => foliageFolded,
        BucketVariant.LaminaOpaque => laminaOpaque,
        BucketVariant.Fruit => fruit,
        _ => opaque,
    };

    public ShaderMaterial Terrain => terrain;
    public ShaderMaterial Seed => seed;
    public ShaderMaterial Obstacle => obstacle;

    /// <summary>Selects the readout ramp; the per-organ exceptions live in the shader (agro_mapping_input).</summary>
    public void SetMapping(VisualMapping mapping)
    {
        var (a, b, hsl) = mapping switch
        {
            VisualMapping.Water => (Ramps.WaterEmpty, Ramps.WaterFull, false),
            VisualMapping.Energy => (Ramps.WaterEmpty, Ramps.EnergyFull, true),
            VisualMapping.Auxins => (Ramps.WaterEmpty, Ramps.AuxinsFull, true),
            VisualMapping.Cytokinins => (Ramps.WaterEmpty, Ramps.CytokininsFull, true),
            VisualMapping.Irradiance => (Ramps.IrrEmpty, Ramps.IrrFull, false),
            VisualMapping.Resource => (Ramps.EffEmpty, Ramps.EffFull, false),
            VisualMapping.Production => (Ramps.EffEmpty, Ramps.EffFull, false),
            _ => (RGB.Black, RGB.Black, false),
        };
        foreach (var m in all)
        {
            m.SetShaderParameter("mapping", (int)mapping);
            m.SetShaderParameter("ramp_a", ToG(a));
            m.SetShaderParameter("ramp_b", ToG(b));
            m.SetShaderParameter("ramp_hsl", hsl);
        }
    }

    public void SetHemisphere(RGB sky, RGB ground, bool analytic)
    {
        foreach (var m in all.Append(seed))
        {
            m.SetShaderParameter("hemi_sky", ToG(sky));
            m.SetShaderParameter("hemi_ground", ToG(ground));
            m.SetShaderParameter("analytic_hemi", analytic ? 1.0f : 0.0f);
        }
    }

    public void SetTranslucency(float t)
    {
        foliage.SetShaderParameter("leaf_translucency", t);
        foliageFolded.SetShaderParameter("leaf_translucency", t);
        laminaOpaque.SetShaderParameter("leaf_translucency", t);
        fruit.SetShaderParameter("leaf_translucency", t);
    }

    public void SetSun(Vector3 dirWorld, float casterOffset)
    {
        foreach (var m in new[] { foliage, foliageFolded, laminaOpaque, fruit })
        {
            m.SetShaderParameter("sun_dir_world", dirWorld);
            m.SetShaderParameter("shadow_caster_offset", casterOffset);
        }
    }

    public static Vector3 ToG(RGB c) => new(c.R, c.G, c.B);
}
