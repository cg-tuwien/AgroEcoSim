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

    /// <summary>
    /// The wire colour of an organ on this phenology: the evaluated appearance as an
    /// sRGB-encoded albedo for a surface lit at <paramref name="exposure"/>.
    /// </summary>
    /// <remarks>
    /// This used to write the linear value times 255 while both viewers decode the bytes as
    /// sRGB, so every phenology colour was darkened twice: the authored dead-leaf brown
    /// (82, 60, 38) was shown as (44, 28, 15), which is the black old leaves were reported as.
    /// <paramref name="pathStress"/> only chooses between the seasonal and the stress
    /// senescence path; <paramref name="stress"/> is the live modifier on a living organ.
    /// </remarks>
    /// <summary>A white corolla: the colour of a flower that states none.</summary>
    public static LeafPhenology WhitePetals => new(24f * 18f, new ColorModel(
        new ColorCurve()
            .Add(0.00f, ColorUtils.OkLabFromCieLab(95f, -2f, 6f))
            .Add(1.00f, ColorUtils.OkLabFromCieLab(95f, -1f, 4f)),
        ColorModel.DefaultStress,
        new ColorCurve()
            .Add(0.00f, ColorUtils.OkLabFromCieLab(90f, 0f, 12f)) // cream
            .Add(1.00f, ColorUtils.OkLabFromCieLab(72f, 4f, 20f)))); // withered tan

    internal RGB8 WireColor(float season, float adulcy, float stress, float senescence, float exposure, float pathStress = 0f)
        => ColorUtils.OklabToWire(Color.Evaluate(adulcy, season, stress, senescence, pathStress), exposure);

    internal void WriteColor(BinaryWriter writer, float season, float adulcy, float stress, float senescence, float exposure, float pathStress = 0f)
        => Write(writer, WireColor(season, adulcy, stress, senescence, exposure, pathStress));

    /// <summary>The three colour bytes of a record.</summary>
    internal static void Write(BinaryWriter writer, RGB8 rgb)
    {
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
    /// Additional late-stage drying: chroma loss as the last carotenoids go. Scaled by 0.7 on the
    /// seasonal path and by 1.25 on the stress path.
    /// </summary>
    /// <remarks>
    /// Kept small: the brown of a dead leaf is its oxidised phenolics, which stay. At 0.2 a dead
    /// leaf read as a grey tan, which is what the user reported as "almost grey"; the curves now
    /// carry the end colour themselves.
    /// </remarks>
    public float LateDesaturation { get; init; } = 0.08f;

    /// <summary>
    /// Additional late-stage drying: lightness loss.
    /// </summary>
    /// <remarks>
    /// Zero by default. Dry dead tissue is brighter than green tissue across the visible -
    /// chlorophyll and then the carotenoids are gone and air replaces water in the cells -
    /// so the terminal colour of a senescence path is a tan or a light brown, and darkening
    /// it further is what a wet, rotting or frosted leaf does, not a dry one.
    /// </remarks>
    public float LateDarkening { get; init; } = 0f;


    public ColorModel(ColorCurve age, ColorCurve stress, ColorCurve senescenceSeason, ColorCurve? senescenceStress = null)
    {
        SeasonalSenescenceCurve = senescenceSeason;
        StressSenescenceCurve = senescenceStress;
        LifecycleCurve = age;
        StressCurve = stress;
    }

    /// <summary>Blend weights for combining curve outputs.</summary>
    public Vector2 Weights { get; init; } = new(0.6f, 0.3f);

    /// <param name="stress">Live stress of a living organ: dulls and dries the healthy colour.</param>
    /// <param name="pathStress">Which way a senescing organ dies: 0 follows the seasonal path
    /// (resorption: yellow, then brown), 1 the stress path (desiccation: dull olive, then tan).
    /// It does not touch the colour of an organ that is not senescing.</param>
    public Vector3 Evaluate(float adulcy, float season, float stress, float senescence, float pathStress = 0f)
    {
        var result = LifecycleCurve.Evaluate(adulcy * 0.5f);

        if (HealthySeasonCurve != null)
        {
            //The season drifts along the species' own pigment direction (a Bergenia goes
            //burgundy in the cold). This applied the dry direction, so every seasonal tint
            //went yellow-brown whatever the preset asked for.
            var seasonModifier = HealthySeasonCurve?.Evaluate(season) ?? default;
            result = ApplySeasonModifier(result, seasonModifier);
        }

        if (stress > 0f)
        {
            var stressModifier = StressCurve.Evaluate(stress);
            result = ApplyStressModifier(result, stressModifier);
        }

        if (senescence > 0f)
            result = ApplySenescence(result, Math.Clamp(MathF.Max(stress, pathStress), 0f, 1f), senescence);

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
        //The stress path is a colour path of its own. StressCurve is a modifier triple
        //(lightness shift, chroma scale, dry shift), and reading it as an OKLab colour sent
        //every stressed senescence towards black.
        var seasonalTarget = SeasonalSenescenceCurve.Evaluate(tSeasonal);
        var stressTarget   = (StressSenescenceCurve ?? SeasonalSenescenceCurve).Evaluate(tStress);

        // Blend the two path targets by onset stress.
        // Low stress -> mostly seasonal path
        // High stress -> mostly stress path
        var targetLab = Vector3.Lerp(seasonalTarget, stressTarget, stress);

        // ------------------------------------------------------------
        // Blend from the frozen base into the target senescence path.
        // This keeps the leaf starting from its actual onset color.
        // ------------------------------------------------------------
        // The leaf leaves its own colour quickly and then follows the authored path: by half way
        // it is 85 % on the path. A slower blend (exponent 1.15) kept a dying leaf half green, so
        // a strawberry's strong red stage only ever reached a dull red-brown.
        var alpha = 1f - MathF.Pow(1f - senescence, Lerp(2.2f, 2.8f, stress));
        var c = Vector3.Lerp(baseLab, targetLab, alpha);

        // ------------------------------------------------------------
        // Late drying pass
        // Leaves near shedding get drier and duller: the last pigments go and the
        // colour greys. They do not get darker - see LateDarkening.
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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(54f, -16f, 36f)) // very young: light, yellowish
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -18f, 32f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(40f, -18f, 28f)) // mature (adulcy 1 reads this key): Munsell 5GY 4/4, as measured on mature leaves
        .Add(1.00f, ColorUtils.OkLabFromCieLab(40f, -18f, 28f)); // not reached while Evaluate reads adulcy * 0.5

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

    // Senescence paths. Chlorophyll goes first and unmasks the carotenoids (yellow); those go
    // next; what is left is the brown of oxidised, polymerised phenolics over pale cell wall.
    // Every path ends on a mid brown (Munsell 10YR 5/4 - 7.5YR 4/4, CIELAB L* 42-50): never
    // darker, since dry tissue reflects more than a wet one, and never a grey tan - the
    // phenolics that make it brown stay. Black is frost, rot or fire. See Agro/ORGAN_COLORS.md.
    public static ColorCurve DefaultSeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(66f, 0f, 58f)) // yellow: the carotenoids unmasked
        .Add(0.35f, ColorUtils.OkLabFromCieLab(60f, 9f, 50f)) // ochre-gold
        .Add(0.70f, ColorUtils.OkLabFromCieLab(51f, 12f, 34f)) // light brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(46f, 10f, 25f)); // dry brown, between Munsell 10YR 5/4 and 7.5YR 4/4

    // Death by desiccation skips most of the resorption: the leaf dries with part of its
    // chlorophyll still in it, like hay - dull olive, then khaki, then brown.
    public static ColorCurve DefaultStressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(45f, -4f, 26f)) // dull olive: dried with chlorophyll still in it
        .Add(0.45f, ColorUtils.OkLabFromCieLab(45f, 4f, 25f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(45f, 8f, 22f)); // dry brown

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
/// Why these colors: Bergenia cordifolia is an evergreen perennial with large,
/// leathery, rounded leaves. Healthy warm-season foliage is medium to dark glossy
/// green. Cold weather commonly causes healthy leaves to develop red, bronze,
/// purplish-bronze or burgundy pigmentation, so that winter shift belongs in the
/// healthy seasonal curve rather than in senescence.
///
/// True senescence is modeled separately as bronze / reddish-brown -> brown.
///
/// Sources:
/// - RHS: Bergenia are evergreen with large leathery leaves, often tinged red in winter.
/// - Missouri Botanical Garden: B. cordifolia foliage turns purplish-bronze in winter.
/// - Cambridge University Botanic Garden: large leaves become brownish-red in winter.
/// </summary>
public static class BergeniaCordifoliaPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -18f, 28f)) // fresh: lighter glossy green
        .Add(0.20f, ColorUtils.OkLabFromCieLab(41f, -18f, 24f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(36f, -17f, 20f)) // mature: deep glossy green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(36f, -17f, 20f)); // full adult

    public static ColorCurve BergeniaHealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.000f, 1.00f, 0.000f)) // early spring
        .Add(0.25f, new Vector3( 0.000f, 1.00f, 0.000f)) // spring / summer
        .Add(0.60f, new Vector3( 0.000f, 1.00f, 0.000f)) // still green
        .Add(0.75f, new Vector3(-0.008f, 1.04f, 0.008f)) // early autumn: red-bronze begins
        .Add(0.88f, new Vector3(-0.020f, 1.10f, 0.018f)) // late autumn: bronze / plum
        .Add(1.00f, new Vector3(-0.030f, 1.14f, 0.024f)); // winter: burgundy-plum

    public static readonly Vector2 HealthySeasonDirection =
        Vector2.Normalize(new Vector2(0.72f, -0.69f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(42f, 10f, 22f)) // dull bronze-olive
        .Add(0.35f, ColorUtils.OkLabFromCieLab(40f, 24f, 20f)) // reddish bronze
        .Add(0.70f, ColorUtils.OkLabFromCieLab(40f, 18f, 20f)) // warm brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(43f, 12f, 20f)); // dead leathery brown, the darkest of the presets

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(40f, 0f, 20f)) // dull stressed bronze
        .Add(0.50f, ColorUtils.OkLabFromCieLab(41f, 10f, 20f)) // reddish brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(43f, 10f, 20f)); // dry brown
}

/// <summary>
/// Why these colors: foliage is semi-evergreen / aromatic gray-green and develops red / bronze / purple-red autumn tones.
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=280850
/// https://www.rhs.org.uk/plants/7891/geranium-macrorrhizum/details
/// </summary>
public static class GeraniumMacrorrhizumPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(54f, -14f, 30f)) // young: light
        .Add(0.20f, ColorUtils.OkLabFromCieLab(48f, -14f, 26f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(44f, -13f, 22f)) // mature: matte grey-green (hairy, aromatic)
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, -13f, 22f)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(56f, 12f, 50f)) // gold-orange
        .Add(0.35f, ColorUtils.OkLabFromCieLab(48f, 32f, 40f)) // orange-red (RHS: autumn colour orange red)
        .Add(0.70f, ColorUtils.OkLabFromCieLab(42f, 30f, 28f)) // russet
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 14f, 23f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(44f, -3f, 25f)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromCieLab(44f, 5f, 24f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 9f, 22f)); // dry brown
}

/// <summary>
/// Why these colors: foliage is glossy light green and turns reddish-bronze / red in autumn.
/// https://www.rhs.org.uk/plants/92273/geranium-cantabrigiense/details
/// https://www.gardenia.net/plant/geranium-cantabrigiense
/// </summary>
public static class GeraniumXCantabrigiensePreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(52f, -20f, 34f)) // fresh
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -19f, 30f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, -18f, 27f)) // mature: glossy mid green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, -18f, 27f)); // full adult, stable

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(55f, 14f, 50f)) // gold
        .Add(0.35f, ColorUtils.OkLabFromCieLab(45f, 40f, 36f)) // red to coppery orange
        .Add(0.70f, ColorUtils.OkLabFromCieLab(41f, 30f, 26f)) // coppery red-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(45f, 14f, 22f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(44f, -3f, 25f)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromCieLab(44f, 5f, 24f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 9f, 22f)); // dry brown
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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(52f, -17f, 32f)) // fresh
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -17f, 28f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, -17f, 25f)) // mature: deep green blade
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, -17f, 25f)); // full adult, stable

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(60f, -6f, 40f)) // yellow-green
        .Add(0.40f, ColorUtils.OkLabFromCieLab(60f, 2f, 34f)) // straw-green
        .Add(0.75f, ColorUtils.OkLabFromCieLab(57f, 5f, 28f)) // straw
        .Add(1.00f, ColorUtils.OkLabFromCieLab(55f, 6f, 26f)); // dead sedge blade: straw-tan, the lightest end on purpose

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -6f, 28f)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromCieLab(53f, 3f, 26f)) // khaki
        .Add(1.00f, ColorUtils.OkLabFromCieLab(55f, 5f, 25f)); // straw-tan
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
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(200, 178, 125)) // straw
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(185, 162, 118)); // straw-tan

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(220, 215, 180)) // dull cream
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(200, 180, 130)) // straw-buff
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(185, 162, 120)); // straw-tan
}

/// <summary>
/// Why these colors: Fragaria vesca 'Rügen' has trifoliate, toothed foliage.
/// Species references describe Fragaria vesca leaves as glossy/bright green,
/// while horticultural descriptions of 'Rügen' specifically describe its
/// winter-green foliage as dark green.
///
/// Healthy leaves are therefore modeled as a clean, fairly saturated
/// medium-to-dark green. Terminal senescence follows a muted
/// yellow-green → straw/bronze → brown path.
///
/// Sources:
/// - Kew: bright green, trifoliolate leaves with toothed margins.
/// - Missouri Botanical Garden: trifoliate, coarsely toothed green leaves.
/// - Lorberg: 'Rügen' has winter-green, dark-green, toothed foliage.
/// - RHS: Fragaria leaves are composed of three leaflets.
/// </summary>
public static class FragariaRuegenPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -20f, 34f)) // fresh: light, clean green
        .Add(0.20f, ColorUtils.OkLabFromCieLab(40f, -20f, 30f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(35f, -18f, 25f)) // mature: 'Ruegen' is described as dark green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(35f, -18f, 25f)); // full adult: stable dark green

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(60f, 2f, 54f)) // yellow
        .Add(0.30f, ColorUtils.OkLabFromCieLab(47f, 40f, 42f)) // orange-red
        .Add(0.55f, ColorUtils.OkLabFromCieLab(40f, 52f, 32f)) // strong red: summer leaves load anthocyanin before they die
        .Add(0.80f, ColorUtils.OkLabFromCieLab(35f, 30f, 20f)) // red-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(40f, 14f, 20f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(42f, -4f, 24f)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, 6f, 22f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, 10f, 20f)); // dry brown

    /// <summary>
    /// The leaves that overwinter take a bronze-red cast in the cold (anthocyanin rises in the
    /// winter leaves too, less than in the summer leaves that die), fading again in spring.
    /// </summary>
    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3( 0.000f, 0.97f, 0.020f)) // 1 March: fading
        .Add(0.12f, new Vector3( 0.000f, 1.00f, 0.000f)) // mid April: none
        .Add(0.60f, new Vector3( 0.000f, 1.00f, 0.000f)) // early October
        .Add(0.75f, new Vector3(-0.005f, 0.96f, 0.020f)) // late November
        .Add(0.90f, new Vector3(-0.010f, 0.94f, 0.032f)) // January
        .Add(1.00f, new Vector3(-0.010f, 0.95f, 0.030f)); // end of February

    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(0.85f, 0.25f));
}