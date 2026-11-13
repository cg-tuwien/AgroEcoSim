using Godot;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;

namespace GodotFrontend.App.Render;

/// <summary>
/// The lighting switches mapped onto Godot's pipeline. The default look is the web's exact hemisphere term computed
/// in the shaders; occlusion and indirect light move the hemisphere into Godot's sky ambient (the sky's radiance
/// pass is the two-tone hemisphere) so SSAO and SDFGI can act on it. The sun follows the simulated clock; the sky
/// background is the simulation's own dome. Godot's effects only correspond to the web's GTAO and cone tracing in
/// role, not pixel for pixel.
/// </summary>
public sealed class LightingRig
{
    private readonly Godot.Environment env;
    private readonly DirectionalLight3D sun;
    private readonly ShaderMaterial skyMaterial;
    private readonly MaterialSet materials;
    private static readonly Color Background = Color.Color8(250, 250, 250);
    /// <summary>The ground tint of the dome below the horizon: DAY_GROUND normalised by its luma.</summary>
    private static readonly Vector3 GroundTint;

    static LightingRig()
    {
        var g = LightingResolver.DayGround;
        var l = 0.2126f * g.R + 0.7152f * g.G + 0.0722f * g.B;
        GroundTint = new Vector3(g.R / l, g.G / l, g.B / l);
    }

    public LightingRig(Node parent, MaterialSet materials)
    {
        this.materials = materials;
        skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://App/Shaders/sky.gdshader") };
        skyMaterial.SetShaderParameter("ground_tint", GroundTint);
        env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Background,
            Sky = new Sky { SkyMaterial = skyMaterial, ProcessMode = Sky.ProcessModeEnum.Automatic, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            SsaoRadius = 0.3f,
            SsaoIntensity = 2.0f,
            SdfgiMinCellSize = 0.08f,
            SdfgiUseOcclusion = true,
        };
        parent.AddChild(new WorldEnvironment { Name = "Environment", Environment = env });
        sun = new DirectionalLight3D
        {
            Name = "Sun",
            Visible = false,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 40,
            DirectionalShadowBlendSplits = true,
            ShadowBias = 0.02f,
            ShadowNormalBias = 0.5f,
            ShadowBlur = 1.0f,
            ShadowOpacity = 0.9f,
            LightSpecular = 0,
        };
        parent.AddChild(sun);
    }

    public void Apply(FrameLighting f, SkyDomeSample dome, float coverage)
    {
        materials.SetHemisphere(f.HemiSky, f.HemiGround, f.Analytic);
        materials.SetTranslucency(f.LeafTranslucency);
        skyMaterial.SetShaderParameter("hemi_sky", MaterialSet.ToG(f.HemiSky));
        skyMaterial.SetShaderParameter("hemi_ground", MaterialSet.ToG(f.HemiGround));
        env.AmbientLightSource = f.Analytic ? Godot.Environment.AmbientSource.Disabled : Godot.Environment.AmbientSource.Sky;
        env.AmbientLightSkyContribution = 1;
        env.AmbientLightEnergy = 1;
        env.SsaoEnabled = f.AmbientOcclusion;
        env.SdfgiEnabled = f.Indirect;
        env.SdfgiBounceFeedback = f.Bounces >= 2 ? 0.5f : 0f;

        env.BackgroundMode = f.Sky ? Godot.Environment.BGMode.Sky : Godot.Environment.BGMode.Color;
        skyMaterial.SetShaderParameter("sun_dir", new Vector3((float)dome.SunX, (float)dome.SunY, (float)dome.SunZ));
        skyMaterial.SetShaderParameter("clear_scale", (float)dome.ClearScale);
        skyMaterial.SetShaderParameter("overcast_scale", (float)dome.OvercastScale);
        skyMaterial.SetShaderParameter("sun_air_mass", (float)dome.SunAirMass);
        skyMaterial.SetShaderParameter("ground_radiance", (float)dome.GroundRadiance);
        skyMaterial.SetShaderParameter("coverage", coverage);

        var lit = f.Shadows && (f.SunColor.R > 0 || f.SunColor.G > 0 || f.SunColor.B > 0);
        sun.Visible = lit;
        var dir = new Vector3((float)f.SunX, (float)f.SunY, (float)f.SunZ);
        if (lit)
        {
            // the light shines along its -Z: point -Z away from the sun, toward the scene
            var up = System.Math.Abs(dir.Y) > 0.999f ? Vector3.Forward : Vector3.Up;
            sun.Basis = Basis.LookingAt(-dir, up);
            var mx = System.Math.Max(f.SunColor.R, System.Math.Max(f.SunColor.G, f.SunColor.B));
            sun.LightColor = new Color(f.SunColor.R / mx, f.SunColor.G / mx, f.SunColor.B / mx).LinearToSrgb();
            sun.LightEnergy = mx;
        }
        if (OS.IsDebugBuild() && OS.GetCmdlineUserArgs().Contains("--debug-lighting")) GD.Print($"LIGHT sun={dir} lit={lit} energy={sun.LightEnergy} color={sun.LightColor} analytic={f.Analytic}");
        // caster-side offset against leaf self-shadowing, about 2.5 mm per texel of bias
        materials.SetSun(dir, (float)(f.ShadowBiasTexels * 0.0025));
    }
}
