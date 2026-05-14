using System.Numerics;
using Utils;

namespace Agro;

public enum LeafStage : byte { Bud, Expanding, Mature, Senescing, Abscised }

public readonly struct LeafPhenology
{
    // Expected lifespan (days) used for normalization in curves
    public float ExpectedAgeDays { get; init; }

    public ColorModel Color { get; init; }

    public LeafPhenology()
    {
        ExpectedAgeDays = 180f * 24;
        Color = new(ColorModel.DefaultLifecycle, ColorModel.DefaultStress, ColorModel.DefaultSeasonalSenescence, ColorModel.DefaultStressSenescence);
    }

    public LeafPhenology(float expectedAge, ColorModel colorModel)
    {
        ExpectedAgeDays = expectedAge;
        Color = colorModel;
    }

    internal void WriteColor(BinaryWriter writer, float season, float adulcy, float stress, float senescence)
    {
        var lab = Color.Evaluate(adulcy, season, stress, senescence);
        var rgb = ColorUtils.OklabToLinearRgb8(lab);
        writer.Write(rgb.Red);
        writer.Write(rgb.Green);
        writer.Write(rgb.Blue);
    }

    //public static LeafPhenology Default => new();
}

public readonly record struct PhenologyContext(
    int DayOfYear,       // 1..366
    float AgeDays,       // leaf age
    LeafStage Stage,
    float Stress01,      // 0..1
    float TemperatureC,  // optional for advanced phenology
    float SoilMoisture01 // optional for advanced phenology
)
{
    public float Season01 => (DayOfYear - 1) / 365f; // simple normalized season
}

/// <summary>
/// Generic model that outputs color (linear RGB 0..1) using a curve over time/age/stress.
/// </summary>
public readonly record struct ColorModel
{

    // Lifecycle color: based on age & stage (young leaves lighter, senescence yellow/brown)
    public ColorCurve LifecycleCurve { get; init; }


    // Optional healthy seasonal modifier curve: (deltaL, chromaScale, shiftAmount)
    public ColorCurve? HealthySeasonCurve { get; init; }

    // Direction of healthy seasonal pigment drift in OKLab a/b space
    public Vector2 HealthySeasonDirection { get; init; } = Vector2.Zero;


    // Stress color: chlorosis/drought/browning; you can swap this model later (deltaL, chromaScale, shiftAmount)
    public ColorCurve StressCurve { get; init; }

    /// <summary>
    /// Seasonal color: normal seasonal / developmental senescence path in OKLab
    /// </summary>
    public ColorCurve SeasonalSenescenceCurve { get; init; }

    /// <summary>
    /// Optional stress-induced senescence path in OKLab.
    /// If null, SeasonalSenescenceCurve is used for both.
    /// </summary>
    public ColorCurve? StressSenescenceCurve { get; init; }

    public Vector2 DryDirection { get; init; } = Vector2.Normalize(new Vector2(0.24f, 0.97f));

    /// <summary>
    /// How strongly stress compresses the senescence color path.
    /// 0 = no timing change, 1 = strong acceleration of early brown/dry stages.
    /// Recommended range: 0.3 .. 0.8
    /// </summary>
    public float StressPathCompression { get; init; } = 0.55f;

    /// <summary>
    /// Additional late-stage drying: chroma loss.
    /// </summary>
    public float LateDesaturation { get; init; } = 0.30f;

    /// <summary>
    /// Additional late-stage drying: lightness loss.
    /// </summary>
    public float LateDarkening { get; init; } = 0.05f;


    public ColorModel(ColorCurve age, ColorCurve stress, ColorCurve senescenceSeason, ColorCurve? senescenceStress = null)
    {
        SeasonalSenescenceCurve = senescenceSeason;
        StressSenescenceCurve = senescenceStress;
        LifecycleCurve = age;
        StressCurve = stress;
    }

    /// <summary>Blend weights for combining curve outputs.</summary>
    public Vector2 Weights { get; init; } = new(0.6f, 0.3f);

    public Vector3 Evaluate(float adulcy, float season, float stress, float senescence)
    {
        var result = LifecycleCurve.Evaluate(adulcy * 0.5f);

        if (HealthySeasonCurve != null)
        {
            var seasonModifier = HealthySeasonCurve?.Evaluate(season) ?? default;
            result = ApplyStressModifier(result, seasonModifier);
        }

        if (stress > 0f)
        {
            var stressModifier = StressCurve.Evaluate(stress);
            result = ApplyStressModifier(result, stressModifier);
        }

        if (senescence > 0f)
            result = ApplySenescence(result, stress, senescence);

        return result;
        //return Vector3.Clamp(result, Vector3.Zero, Vector3.One);
    }


    private Vector3 ApplySenescence(Vector3 baseLab, float stress, float senescence)
    {
        // // Convert base to OKLCh so we can manipulate chroma more intuitively.
        // Vector3 baseLch = ColorUtils.OklabToOklch(baseLab);

        // // --------------------------------------------------------------------
        // // Phase timings
        // // --------------------------------------------------------------------
        // // With low stress, the leaf spends more time in a yellow/golden phase.
        // // With high stress, the yellow phase is compressed and browning starts earlier.
        // yellowBrow

        // var timing =
        // float yellowEnd   = Lerp(0.68f, 0.32f, stress);
        // float brownStart  = Lerp(0.52f, 0.18f, stress);
        // float dryStart    = Lerp(0.72f, 0.38f, stress);

        // float tYellow = SmoothStep(0.00f, yellowEnd,  senescence);
        // float tBrown  = SmoothStep(brownStart, 1.00f, senescence);
        // float tDry    = SmoothStep(dryStart,   1.00f, senescence);

        // // --------------------------------------------------------------------
        // // Stage targets
        // // --------------------------------------------------------------------
        // // Mid target:
        // // - low stress -> more golden/yellow autumn look
        // // - high stress -> already somewhat pulled toward brown
        // Vector3 midTarget = Vector3.Lerp(
        //     SenescenceYellowLab,
        //     NearAbscissionLab,
        //     0.55f * stress);

        // // End target:
        // // - low stress -> normal brown near shedding
        // // - high stress -> darker, drier brown
        // Vector3 endTarget = Vector3.Lerp(
        //     NearAbscissionLab,
        //     StressBrownLab,
        //     stress);

        // // --------------------------------------------------------------------
        // // 1) Early / mid senescence:
        // // Start from base and drift toward the senescing mid target.
        // // We intentionally keep this partial because the leaf should still
        // // look like "this leaf, but senescing", not an abrupt recolor.
        // // --------------------------------------------------------------------
        // var c = Vector3.Lerp(baseLab, midTarget, 0.78f * tYellow);

        // // --------------------------------------------------------------------
        // // 2) Browning / terminal phase:
        // // Move increasingly toward the terminal brown target.
        // // --------------------------------------------------------------------
        // c = Vector3.Lerp(c, endTarget, tBrown);

        // // --------------------------------------------------------------------
        // // 3) Additional drying / desaturation pass:
        // // Late senescence loses chroma and gets a bit darker.
        // // High-stress senescence dries more aggressively.
        // // --------------------------------------------------------------------
        // Vector3 lch = OklabUtil.OklabToOklch(c);

        // var chromaScale = 1f - Lerp(0.18f, 0.55f, stress) * tDry;
        // var lightnessDrop = Lerp(0.015f, 0.090f, stress) * tDry;

        // lch.Y *= chromaScale;   // reduce chroma
        // lch.X -= lightnessDrop; // darken slightly

        // c = OklabUtil.OklchToOklab(lch);

        // return c;

        // ------------------------------------------------------------
        // Path timing
        // ------------------------------------------------------------
        // Low stress: follow the normal path more literally.
        // High stress: move earlier along the path (compressed progression).
        //
        // Example:
        // - autumn senescence: green -> yellow -> orange -> red -> brown
        // - stress senescence: reach brownish/dry stages earlier
        // ------------------------------------------------------------
        float compression = Lerp(1.0f, 2.2f, stress * StressPathCompression);

        // identity-like progress for the seasonal path
        float tSeasonal = senescence;

        // accelerated progress for the stress path
        float tStress = 1f - MathF.Pow(1f - senescence, compression);

        // Evaluate both candidate paths
        var seasonalTarget = SeasonalSenescenceCurve.Evaluate(tSeasonal);
        var stressTarget   = StressCurve.Evaluate(tStress);

        // Blend the two path targets by onset stress.
        // Low stress -> mostly seasonal path
        // High stress -> mostly stress path
        var targetLab = Vector3.Lerp(seasonalTarget, stressTarget, stress);

        // ------------------------------------------------------------
        // Blend from the frozen base into the target senescence path.
        // This keeps the leaf starting from its actual onset color.
        // ------------------------------------------------------------
        var alpha = 1f - MathF.Pow(1f - senescence, Lerp(1.15f, 1.65f, stress));
        var c = Vector3.Lerp(baseLab, targetLab, alpha);

        // ------------------------------------------------------------
        // Late drying pass
        // Leaves near shedding typically get drier, duller, and often darker.
        // Even if the path already encodes color well, this adds a believable
        // "papery / dead tissue" finish.
        // ------------------------------------------------------------
        var dryStart = Lerp(0.78f, 0.45f, stress);
        var dryT = ColorUtils.SmoothStep(dryStart, 1.0f, senescence);

        var lch = ColorUtils.OklabToOklch(c);
        lch.Y *= 1f - (LateDesaturation * Lerp(0.7f, 1.25f, stress) * dryT);
        lch.X -=      LateDarkening    * Lerp(0.5f, 1.4f, stress) * dryT;

        c = ColorUtils.OklchToOklab(lch);
        return c;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    /// <summary>
    /// Applies a species-independent stress modifier to a base OKLab color.
    ///
    /// stressMod.X = deltaL       (typically <= 0)
    /// stressMod.Y = chromaScale  (1 = unchanged, <1 = duller)
    /// stressMod.Z = dryShift     (push toward dry yellow-brown axis)
    /// </summary>
    public Vector3 ApplyStressModifier(Vector3 baseLab, Vector3 modifier) => ApplyDirectionalModifier(baseLab, modifier, DryDirection);

    public Vector3 ApplySeasonModifier(Vector3 baseLab, Vector3 modifier) => ApplyDirectionalModifier(baseLab, modifier, HealthySeasonDirection);

    /// <summary>
    /// Applies a compact directional modifier in OKLab.
    ///
    /// modifier.X = deltaL
    /// modifier.Y = chromaScale
    /// modifier.Z = directional shift amount in a/b space
    ///
    /// For stress typically:
    /// stressMod.X = deltaL       (typically <= 0)
    /// stressMod.Y = chromaScale  (1 = unchanged, <1 = duller)
    /// stressMod.Z = dryShift     (push toward dry yellow-brown axis)///
    /// </summary>
    public static Vector3 ApplyDirectionalModifier(Vector3 baseLab, Vector3 modifier, Vector2 direction)
    {
        float L = baseLab.X + modifier.X;
        float chromaScale = modifier.Y;
        float shift = modifier.Z;

        Vector2 ab = new(baseLab.Y, baseLab.Z);

        // Scale existing chroma
        ab *= chromaScale;

        // Push along species-specific pigment direction
        if (direction.LengthSquared() > 1e-8f)
            ab += Vector2.Normalize(direction) * shift;

        return new Vector3(L, ab.X, ab.Y);
    }

    public static Vector3 BlendBaseOklab(Vector3 seasonLab, float seasonWeight, Vector3 ageLab, float ageWeight)
    {
        float w = seasonWeight + ageWeight;
        if (w <= 1e-6f)
            return Vector3.Zero;

        return (seasonLab * seasonWeight + ageLab * ageWeight) / w;
    }

    // Some useful defaults (tweak per species)
    // public static ColorCurve DefaultSeasonal => new ColorCurve()
    //     .Add(0.00f, new Vector3(0.875482f, -0.047827f,  0.049368f)) // late winter/early spring
    //     .Add(0.25f, new Vector3(0.781846f, -0.093386f,  0.066909f)) // spring/summer
    //     .Add(0.55f, new Vector3(0.753553f, -0.101204f,  0.072953f)) // late summer
    //     .Add(0.72f, new Vector3(0.815685f, -0.012736f,  0.092925f)) // early autumn yellowing
    //     .Add(0.82f, new Vector3(0.686969f,  0.058088f,  0.085581f)) // autumn browning
    //     .Add(1.00f, new Vector3(0.875482f, -0.047827f,  0.049368f));

    public static ColorCurve DefaultLifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(175, 225, 160)) // very young, lighter
        .Add(0.18f, ColorUtils.OkLabFromSrgb8(135, 195, 125)) // expanding
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(105, 170, 100)) // near mature
        .Add(0.75f, ColorUtils.OkLabFromSrgb8( 95, 160,  92)) // mature
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90, 155,  88)); // full adult, stable

    public static ColorCurve DefaultHealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.010f, 1.03f, 0.000f)) // early spring flush: a bit lighter/fresher
        .Add(0.20f, new Vector3( 0.000f, 1.00f, 0.000f)) // normal spring
        .Add(0.55f, new Vector3( 0.000f, 1.00f, 0.000f)) // summer: no change
        .Add(0.72f, new Vector3(-0.005f, 0.98f, 0.004f)) // late summer: tiny warm shift
        .Add(0.85f, new Vector3(-0.015f, 0.95f, 0.010f)) // autumn: warmer / slightly duller
        .Add(1.00f, new Vector3(-0.020f, 0.93f, 0.012f)); // late winter: muted

    public static Vector2 DefaultHealthySeasonDirection = Vector2.Normalize(new Vector2(0.20f, 0.98f));

    public static ColorCurve DefaultStress => new ColorCurve()
        .Add(0.00f, new Vector3( 0.00f, 1.00f, 0.000f)) // no change
        .Add(0.35f, new Vector3(-0.01f, 0.93f, 0.004f)) // mild dulling
        .Add(0.60f, new Vector3(-0.03f, 0.84f, 0.011f)) // visible stress
        .Add(0.85f, new Vector3(-0.07f, 0.62f, 0.030f)) // yellowing / browning
        .Add(1.00f, new Vector3(-0.12f, 0.45f, 0.055f)); // strong necrosis / dry

    public static ColorCurve DefaultSeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(190, 180,  70)) // yellow-gold
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(175, 125,  50)) // warm ochre
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(120,  85,  45)) // brown onset
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 82,  60,  38)); // dry brown

    public static ColorCurve DefaultStressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(120, 120,  70)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(115,  90,  50)) // yellow-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 80,  58,  36)); // dark brown

    //Optional milder stress modifier for naturally purple / anthocyanic foliage.
    public static ColorCurve CoolAnthocyanicStress => new ColorCurve()
        .Add(0.00f, new Vector3( 0.00f, 1.00f, 0.000f))
        .Add(0.35f, new Vector3(-0.01f, 0.95f, 0.002f))
        .Add(0.60f, new Vector3(-0.03f, 0.88f, 0.006f))
        .Add(0.85f, new Vector3(-0.06f, 0.72f, 0.016f))
        .Add(1.00f, new Vector3(-0.10f, 0.56f, 0.032f));

    // public static ColorCurve MapleLikeSeasonalSenescence => new ColorCurve()
    //     .Add(0.00f, LeafColorAuthoring.LabFromLinearRgb(0.78f, 0.72f, 0.18f)) // yellow
    //     .Add(0.30f, LeafColorAuthoring.LabFromLinearRgb(0.82f, 0.46f, 0.10f)) // orange
    //     .Add(0.65f, LeafColorAuthoring.LabFromLinearRgb(0.66f, 0.14f, 0.10f)) // red
    //     .Add(1.00f, LeafColorAuthoring.LabFromLinearRgb(0.38f, 0.24f, 0.12f)); // brown
}

/// <summary>
/// Simple keyframe curve in [0..1]
///  -> OKLab (linear) for colors
///  -> (lignthessShift, chromaScale, dryShift) with (0,1,0) being identity
/// </summary>
public readonly struct ColorCurve
{
    public ColorCurve() { }
    public List<ColorKey> Keys { get; init; } = [];
    public ColorCurve Add(float t, Vector3 colorLinear)
    {
        Keys.Add(new ColorKey(Math.Clamp(t, 0f, 1f), colorLinear));
        Keys.Sort((a, b) => a.T.CompareTo(b.T));
        return this;
    }

    public Vector3 Evaluate(float t)
    {
        if (Keys.Count == 0)
            return Vector3.Zero;

        t = Math.Clamp(t, 0f, 1f);

        if (t <= Keys[0].T)
            return Keys[0].Lab;
        if (t >= Keys[^1].T)
            return Keys[^1].Lab;

        for (int i = 0; i < Keys.Count - 1; i++)
        {
            var a = Keys[i];
            var b = Keys[i + 1];
            if (t >= a.T && t <= b.T)
            {
                var u = (t - a.T) / Math.Max(1e-6f, b.T - a.T);
                return Vector3.Lerp(a.Lab, b.Lab, SmoothStep(u));
            }
        }

        return Keys[^1].Lab;
    }

    private static float SmoothStep(float u) => u * u * (3f - 2f * u);
}

public readonly record struct ColorKey(float T, Vector3 Lab);

/// <summary>
/// Why these colors: Bergenia is evergreen and winter foliage is often described as
/// purple / reddish-bronze / plum rather than classic yellow-orange autumn senescence.
/// So the lifecycle stays mostly green, while the senescence path goes through plum → muted purple-brown → brown.
/// https://www.rhs.org.uk/plants/2209/bergenia-cordifolia/details
/// https://www.gardendesign.com/perennials/bergenia.html
/// </summary>
public static class BergeniaCordifoliaPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(160, 210, 140)) // fresh light green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8( 95, 160, 100)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 82, 145,  88)) // near adult
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 75, 135,  82)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 70, 125,  75)); // full adult, stable

    public static ColorCurve BergeniaHealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.000f, 1.00f, 0.000f)) // early spring
        .Add(0.25f, new Vector3( 0.000f, 1.00f, 0.000f)) // spring/summer unchanged
        .Add(0.60f, new Vector3( 0.000f, 1.00f, 0.000f)) // still neutral
        .Add(0.75f, new Vector3(-0.010f, 1.03f, 0.010f)) // early autumn: hint of plum
        .Add(0.88f, new Vector3(-0.025f, 1.08f, 0.022f)) // late autumn: clear purple/plum
        .Add(1.00f, new Vector3(-0.035f, 1.10f, 0.028f)); // winter: deep plum, slightly darker

    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(0.72f, -0.69f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8( 95,  55, 105)) // plum-purple
        .Add(0.35f, ColorUtils.OkLabFromSrgb8( 80,  45,  90)) // deeper winter plum
        .Add(0.70f, ColorUtils.OkLabFromSrgb8( 85,  60,  70)) // muted purple-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 95,  70,  40)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8( 80,  55,  85)) // dull plum
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 85,  65,  60)) // plum-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90,  70,  45)); // brown
}

/// <summary>
/// Why these colors: foliage is semi-evergreen / aromatic gray-green and develops red / bronze / purple-red autumn tones.
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=280850
/// https://www.rhs.org.uk/plants/7891/geranium-macrorrhizum/details
/// </summary>
public static class GeraniumMacrorrhizumPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(175, 220, 150)) // young light green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(115, 175, 110)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(100, 160,  95)) // mature gray-green
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 95, 152,  90)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90, 145,  85)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(160, 145,  80)) // olive-gold
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(150, 100,  55)) // bronze-copper
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(130,  65,  60)) // red-bronze
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 85,  60,  35)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(120, 120,  70)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(110,  85,  45)) // stress brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 80,  55,  35)); // dark brown
}

/// <summary>
/// Why these colors: foliage is glossy light green and turns reddish-bronze / red in autumn.
/// https://www.rhs.org.uk/plants/92273/geranium-cantabrigiense/details
/// https://www.gardenia.net/plant/geranium-cantabrigiense
/// </summary>
public static class GeraniumXCantabrigiensePreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(175, 225, 160)) // fresh light green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(110, 180, 110)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 95, 165,  95)) // mature glossy green
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 90, 157,  90)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 85, 150,  85)); // full adult, stable

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(165, 150,  85)) // yellow-green
        .Add(0.35f, ColorUtils.OkLabFromSrgb8(160, 110,  70)) // bronze
        .Add(0.70f, ColorUtils.OkLabFromSrgb8(140,  85,  70)) // reddish bronze
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90,  65,  40)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(122, 122,  72)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromSrgb8(112,  88,  48)) // tan-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 82,  58,  38)); // dark brown
}

/// <summary>
/// Why these colors: foliage ranges from olive / bronze-green to deep purple, with beet-red / amethyst-purple undersides;
/// in hotter sun it can fade toward bronze-green.
/// https://www.gardenia.net/plant/heuchera-palace-purple-coral-bells
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=259276
/// </summary>
public static class HeucheraPalacePurplePreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(125, 120,  95)) // olive-bronze juvenile
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(110,  90, 100)) // bronzy purple juvenile
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 95,  70, 100)) // maturing purple
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 82,  58,  92)) // adult purple
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 75,  50,  85)); // full adult, stable deep purple

    public static ColorCurve HeucheraHealthySeason => new ColorCurve()
    .Add(0.00f, new Vector3( 0.000f, 1.00f, 0.000f)) // neutral
    .Add(0.35f, new Vector3( 0.000f, 1.00f, 0.000f)) // neutral
    .Add(0.70f, new Vector3(-0.005f, 1.02f, 0.004f)) // slightly richer in cool season
    .Add(0.88f, new Vector3(-0.015f, 1.05f, 0.008f)) // deeper purple in late season
    .Add(1.00f, new Vector3(-0.020f, 1.06f, 0.010f)); // winter deepening

    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(0.78f, -0.62f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8( 85,  55,  95)) // amethyst plum
        .Add(0.35f, ColorUtils.OkLabFromSrgb8( 75,  45,  88)) // deep plum
        .Add(0.70f, ColorUtils.OkLabFromSrgb8( 85,  62,  72)) // muted plum-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 95,  72,  42)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8( 90,  70,  85)) // dull plum
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 95,  75,  65)) // bronze-plum
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 95,  72,  45)); // brown
}

/// <summary>
/// Best approach: use two color models:
/// Center stripe / blade green
/// Margin / cream stripe
/// Because the plant is described as deep green with creamy-white margins,
/// a single color model will only give you a muddy average.
/// https://www.rhs.org.uk/plants/99270/carex-morrowii-variegata-%28v%29/details
/// </summary>
public static class CarexMorrowiiVariegataCenterPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(150, 200, 140)) // fresh center green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(100, 155, 100)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 90, 145,  90)) // mature center green
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 88, 140,  88)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 85, 135,  85)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(175, 175,  95)) // yellow-green
        .Add(0.40f, ColorUtils.OkLabFromSrgb8(170, 150,  85)) // straw-green
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(145, 120,  70)) // straw
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(110,  88,  50)); // tan-brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(125, 125,  75)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(115,  98,  58)) // tan
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 90,  72,  42)); // brown
}

/// <summary>
/// Why these colors: foliage is described as medium / mid-green and semi-evergreen to evergreen in mild winters,
/// with no strong documented red/orange autumn pathway, so a restrained green→olive→brown senescence is the best fit.
/// https://www.rhs.org.uk/plants/99270/carex-morrowii-variegata-%28v%29/details
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=244641&%20cv=2%20
/// </summary>
public static class CarexMorrowiiVariegataMarginPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(235, 240, 215)) // pale cream juvenile
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(230, 235, 210)) // expanding cream
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(225, 230, 205)) // mature cream margin
        .Add(0.80f, ColorUtils.OkLabFromSrgb8(225, 230, 205)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(225, 230, 205)); // full adult, stable


    // Use the default stress modifier if you want simplicity;
    // if the margin browns too aggressively, swap in a milder one later.
    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(230, 225, 180)) // cream
        .Add(0.40f, ColorUtils.OkLabFromSrgb8(220, 205, 150)) // pale straw
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(185, 160, 105)) // straw
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(130, 100,  60)); // tan-brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(220, 215, 180)) // dull cream
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(190, 165, 110)) // straw-buff
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(135, 105,  65)); // brown
}

/// <summary>
/// Why these colors: foliage is described as medium / mid-green and semi-evergreen to evergreen in mild winters,
/// with no strong documented red/orange autumn pathway, so a restrained green→olive→brown senescence is the best fit.
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=278835&qt=Display
/// https://www.gardenia.net/plant/campanula-poscharskyana-serbian-bellflower
/// </summary>
public static class CampanulaPoscharskyanaPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(170, 220, 160)) // fresh green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(110, 170, 110)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 95, 155,  95)) // mature green
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 90, 150,  95)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 85, 145,  90)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(175, 180, 100)) // yellow-green
        .Add(0.40f, ColorUtils.OkLabFromSrgb8(160, 145,  85)) // olive-tan
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(130, 105,  60)) // tan-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 92,  70,  42)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(120, 122,  72)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(108,  86,  48)) // brown onset
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 82,  60,  38)); // dark brown
}

/// <summary>
/// Why these colors: alpine / woodland strawberries are described as bright/dark green, semi-evergreen / winter-green,
/// and the horticultural descriptions emphasize green foliage rather than dramatic autumn leaf colors,
/// so a green→straw→brown terminal path is a reasonable default.
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?kempercode=k290
/// https://www.rhs.org.uk/plants/42325/fragaria-vesca-semperflorens-%28f%29/details
/// https://www.lorberg.com/en-gb/article/593/fragaria-vesca-var-semperflorens-ruegen
/// </summary>
public static class FragariaRuegenPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(170, 220, 150)) // young bright green
        .Add(0.20f, ColorUtils.OkLabFromSrgb8(105, 170, 100)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromSrgb8( 90, 160,  85)) // mature green
        .Add(0.80f, ColorUtils.OkLabFromSrgb8( 85, 150,  82)) // adult
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 80, 145,  78)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(180, 185,  95)) // yellow-green
        .Add(0.40f, ColorUtils.OkLabFromSrgb8(170, 150,  80)) // warm straw
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(140, 110,  60)) // straw-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 95,  72,  42)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(122, 124,  74)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(110,  88,  48)) // tan-brown
        .Add(1.00f, ColorUtils.OkLabFromSrgb8( 82,  58,  36)); // dark brown
}

