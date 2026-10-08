using System.Numerics;

namespace Agro;

/// <summary>Equal-time quadrature of sky radiance and plane irradiance in W/m².
/// The caller multiplies once by interval hours to obtain Wh/m². The backing
/// samples are shared read-only during a gather and may be reused afterwards.</summary>
public readonly struct SkyInterval
{
    readonly SkyDome[]? samples;
    readonly SkyDome single;
    readonly int count;
    public float GlobalHorizontal { get; }
    public float DiffuseHorizontal { get; }
    public bool IsLit => GlobalHorizontal > 0f;

    public static int SampleCount(float hours)
    {
        if (!float.IsFinite(hours) || hours <= 0 || hours > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(hours));
        return Math.Max(1, (int)MathF.Ceiling(hours * 6));
    }

    public SkyInterval(in SkyDome dome)
    {
        samples = null; single = dome; count = 1;
        GlobalHorizontal = dome.GlobalHorizontal; DiffuseHorizontal = dome.DiffuseHorizontal;
    }

    public SkyInterval(SkyDome[] samples, int count)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (count < 1 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
        this.samples = samples; this.count = count; single = default;
        double global = 0, diffuse = 0;
        for (int i = 0; i < count; ++i)
        { global += samples[i].GlobalHorizontal; diffuse += samples[i].DiffuseHorizontal; }
        GlobalHorizontal = (float)(global / count); DiffuseHorizontal = (float)(diffuse / count);
    }

    public float Radiance(Vector3 direction)
    {
        if (samples == null) return single.Radiance(direction);
        // The ground is isotropic; its time integral needs no per-sample lookup.
        if (direction.Y <= 0f) return SkyDome.GroundAlbedo * GlobalHorizontal / MathF.PI;
        var clearGradation = SkyDome.ClearGradation(direction.Y);
        var overcastShape = SkyDome.OvercastShape(direction.Y);
        double value = 0;
        for (int i = 0; i < count; ++i)
            if (samples[i].GlobalHorizontal > 0f) value += samples[i].Radiance(direction, clearGradation, overcastShape);
        return (float)(value / count);
    }

    public float PlaneIrradiance(Vector3 normal)
    {
        if (samples == null) return single.PlaneIrradiance(normal);
        double beam = 0;
        for (int i = 0; i < count; ++i)
            beam += samples[i].DirectNormal * MathF.Max(0f, Vector3.Dot(normal, samples[i].SunDirection));
        return (float)(beam / count)
            + DiffuseHorizontal * 0.5f * (1f + normal.Y)
            + GlobalHorizontal * SkyDome.GroundAlbedo * 0.5f * (1f - normal.Y);
    }
}
