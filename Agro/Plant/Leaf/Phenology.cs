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
            .Add(1.00f, ColorUtils.OkLabFromCieLab(72f, 6f, 28f)))); // withered tan

    internal RGB8 WireColor(float season, float adulcy, float stress, float senescence, float exposure, float pathStress = 0f,
        float seasonWeight = 1f)
        => ColorUtils.OklabToWire(Color.Evaluate(adulcy, season, stress, senescence, pathStress, seasonWeight), exposure);

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

    /// <summary>
    /// How far a leaf takes on its healthy seasonal tint, from 0 to 1, given its light relative to
    /// the plant's best-lit leaf (yesterday's gathered resources over the plant's maximum).
    /// </summary>
    /// <remarks>
    /// Winter reddening of evergreen leaves is anthocyanin made under cold AND strong light - the
    /// favoured explanation is photoprotection (Hughes 2011) - so it is strongest on the sun-exposed
    /// leaves and the shaded inner ones stay green: a Bergenia in January is deep red on top, bronze
    /// and dark gold below, green underneath. A shaded leaf keeps a quarter of the tint.
    /// </remarks>
    public static float SeasonWeight(float relativeLight)
    {
        var t = Math.Clamp((relativeLight - 0.15f) / 0.55f, 0f, 1f);
        return 0.25f + 0.75f * t * t * (3f - 2f * t);
    }


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
    /// <param name="seasonWeight">How far this leaf takes on the healthy seasonal tint, from 0 to 1
    /// (<see cref="SeasonWeight"/>); 1 is the curve as authored.</param>
    public Vector3 Evaluate(float adulcy, float season, float stress, float senescence, float pathStress = 0f, float seasonWeight = 1f)
    {
        var result = LifecycleCurve.Evaluate(adulcy * 0.5f);

        if (HealthySeasonCurve != null)
        {
            //The season drifts along the species' own pigment direction (a Bergenia goes
            //burgundy in the cold). This applied the dry direction, so every seasonal tint
            //went yellow-brown whatever the preset asked for.
            var seasonModifier = HealthySeasonCurve?.Evaluate(season) ?? default;
            result = ApplySeasonModifier(result, WeightSeasonModifier(seasonModifier, seasonWeight));
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
    /// A season modifier taken part of the way: the lightness shift and the push along the pigment
    /// direction scale with the weight, the loss of the base colour's own chroma with its square.
    /// </summary>
    /// <remarks>
    /// The square is what makes a partly tinted leaf a colour rather than a grey. Scaling all three
    /// linearly moves a leaf in a straight line from green to burgundy, through a dull olive at
    /// chroma 15; keeping the base chroma longer bends the path through gold and copper, which is
    /// what a half-reddened Bergenia leaf looks like. At weight 1 the modifier is unchanged.
    /// </remarks>
    public static Vector3 WeightSeasonModifier(Vector3 modifier, float weight)
    {
        if (weight >= 1f) return modifier;
        weight = MathF.Max(weight, 0f);
        return new(modifier.X * weight, 1f + (modifier.Y - 1f) * weight * weight, modifier.Z * weight);
    }

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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(54f, -24f, 51f)) // very young: light, yellowish
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -27f, 43f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(40f, -27f, 37f)) // mature (adulcy 1 reads this key): the lightness and hue of Munsell 5GY 4/4, chroma as a canopy shows it
        .Add(1.00f, ColorUtils.OkLabFromCieLab(40f, -27f, 37f)); // not reached while Evaluate reads adulcy * 0.5

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
    // Every path ends on a mid brown (the lightness and hue of Munsell 10YR 5/4 - 7.5YR 4/4,
    // CIELAB L* 42-50, chroma 30-40): never darker, since dry tissue reflects more than a wet
    // one, and never a grey tan - the phenolics that make it brown stay. Black is frost, rot or
    // fire. See Agro/ORGAN_COLORS.md.
    public static ColorCurve DefaultSeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(66f, 0f, 64f)) // yellow: the carotenoids unmasked
        .Add(0.35f, ColorUtils.OkLabFromCieLab(60f, 10f, 57f)) // ochre-gold
        .Add(0.70f, ColorUtils.OkLabFromCieLab(51f, 16f, 44f)) // light brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(46f, 14f, 35f)); // dry brown, the hue of Munsell 10YR 5/4 - 7.5YR 4/4

    // Death by desiccation skips most of the resorption: the leaf dries with part of its
    // chlorophyll still in it, like hay - dull olive, then khaki, then brown.
    public static ColorCurve DefaultStressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(45f, -6f, 36f)) // dull olive: dried with chlorophyll still in it
        .Add(0.45f, ColorUtils.OkLabFromCieLab(45f, 6f, 35f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(45f, 11f, 31f)); // dry brown

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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -27f, 42f)) // fresh: lighter glossy green
        .Add(0.20f, ColorUtils.OkLabFromCieLab(41f, -27f, 36f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(36f, -25f, 30f)) // mature: deep glossy green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(36f, -25f, 30f)); // full adult

    /// <summary>
    /// The winter colour of the leaves that stay: green until the frosts, then dark gold and
    /// bronze, copper-red and by January deep red on the sun-exposed leaves, back to green as
    /// the new leaves come in April. Season fractions from 1 March: 0.69 is 7 November, 0.86
    /// 8 January, 0.16 28 April. Per leaf the tint is scaled by its light
    /// (<see cref="ColorModel.SeasonWeight"/>), so the shaded leaves stop at gold or stay green.
    /// </summary>
    /// <remarks>
    /// The curve shipped before only pushed the green 0.024 along a magenta axis, which left a
    /// January leaf at CIELAB (34, -23, 28): green. A green that turns red has to lose its
    /// own chroma on the way (the chroma scale) while the shift supplies the red, and a winter
    /// leaf is darker (anthocyanin over chlorophyll). At full light: November (34, 4, 20) dark
    /// gold, December (31, 23, 16) copper-red, January (29, 33, 13) deep red; with half the tint
    /// January is (33, 0, 29), dark gold, and three quarters (31, 16, 22), copper. A first
    /// version ended on a magenta-leaning burgundy (28, 34, 6) that read as wine in the viewer;
    /// a grower's January plant is dark red and dark gold.
    /// </remarks>
    public static ColorCurve BergeniaHealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3(-0.032f, 0.18f, 0.105f)) // 1 March: still winter red
        .Add(0.08f, new Vector3(-0.012f, 0.55f, 0.050f)) // end of March: fading to bronze
        .Add(0.16f, new Vector3( 0.000f, 1.00f, 0.000f)) // end of April: green
        .Add(0.62f, new Vector3( 0.000f, 1.00f, 0.000f)) // mid October: still green
        .Add(0.69f, new Vector3(-0.010f, 0.55f, 0.050f)) // early November: dark gold, bronze
        .Add(0.77f, new Vector3(-0.025f, 0.30f, 0.090f)) // early December: copper-red
        .Add(0.86f, new Vector3(-0.035f, 0.15f, 0.110f)) // January: deep red
        .Add(1.00f, new Vector3(-0.032f, 0.18f, 0.105f)); // end of February

    /// <summary>Towards red in OKLab: anthocyanin over what is left of the green.</summary>
    public static readonly Vector2 HealthySeasonDirection =
        Vector2.Normalize(new Vector2(1.00f, 0.20f));

    public static ColorCurve Stress => ColorModel.CoolAnthocyanicStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(42f, 14f, 31f)) // dull bronze-olive
        .Add(0.35f, ColorUtils.OkLabFromCieLab(40f, 33f, 27f)) // reddish bronze
        .Add(0.70f, ColorUtils.OkLabFromCieLab(40f, 25f, 28f)) // warm brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(43f, 17f, 28f)); // dead leathery brown, the darkest of the presets

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(40f, 0f, 28f)) // dull stressed bronze
        .Add(0.50f, ColorUtils.OkLabFromCieLab(41f, 14f, 28f)) // reddish brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(43f, 15f, 32f)); // dry brown
}

/// <summary>
/// Why these colors: foliage is semi-evergreen / aromatic gray-green and develops red / bronze / purple-red autumn tones.
/// https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?taxonid=280850
/// https://www.rhs.org.uk/plants/7891/geranium-macrorrhizum/details
/// </summary>
public static class GeraniumMacrorrhizumPreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(54f, -26f, 47f)) // young: light
        .Add(0.20f, ColorUtils.OkLabFromCieLab(48f, -26f, 41f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(44f, -25f, 36f)) // mature: matte soft green (hairy, aromatic), a little lighter and less saturated than the glossy species
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, -25f, 36f)); // full adult, stable


    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(56f, 14f, 57f)) // gold-orange
        .Add(0.35f, ColorUtils.OkLabFromCieLab(48f, 37f, 46f)) // orange-red (RHS: autumn colour orange red)
        .Add(0.70f, ColorUtils.OkLabFromCieLab(42f, 38f, 35f)) // russet
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 20f, 32f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(44f, -4f, 35f)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromCieLab(44f, 7f, 34f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 13f, 31f)); // dry brown

    /// <summary>
    /// The leaves that overwinter are "attractively tinted with red" (Cornell), red, bronze and
    /// amber (Gardenia.net): from November the sunlit ones go amber, then bronze-red, the shaded
    /// ones keep their grey-green, and the new leaves of April are green again. Weaker than
    /// Bergenia's and warmer. At full light: November (42, 0, 26) olive-amber, December (39, 15, 22)
    /// bronze, January (37, 24, 20) bronze-red; with three quarters of the tint January is
    /// (39, 10, 29), amber. A first version stopped at (39, 21, 20) and, over the soil, the plant
    /// read as dry brown rather than red-tinted. Season fractions from 1 March.
    /// </summary>
    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3(-0.036f, 0.33f, 0.090f)) // 1 March: still red-tinted
        .Add(0.08f, new Vector3(-0.012f, 0.65f, 0.040f)) // end of March: fading
        .Add(0.16f, new Vector3( 0.000f, 1.00f, 0.000f)) // end of April: green
        .Add(0.62f, new Vector3( 0.000f, 1.00f, 0.000f)) // mid October: green
        .Add(0.68f, new Vector3(-0.012f, 0.62f, 0.045f)) // early November: amber
        .Add(0.76f, new Vector3(-0.028f, 0.42f, 0.075f)) // early December: bronze
        .Add(0.86f, new Vector3(-0.040f, 0.30f, 0.095f)) // January: bronze-red
        .Add(1.00f, new Vector3(-0.036f, 0.33f, 0.090f)); // end of February

    /// <summary>Towards bronze-red in OKLab: red with some of the yellow kept.</summary>
    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(1.00f, 0.25f));
}

/// <summary>
/// Why these colors: foliage is glossy light green and turns reddish-bronze / red in autumn.
/// https://www.rhs.org.uk/plants/92273/geranium-cantabrigiense/details
/// https://www.gardenia.net/plant/geranium-cantabrigiense
/// </summary>
public static class GeraniumXCantabrigiensePreset
{
    public static ColorCurve Lifecycle => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(52f, -30f, 49f)) // fresh
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -29f, 43f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, -27f, 39f)) // mature: glossy mid green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, -27f, 39f)); // full adult, stable

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(55f, 16f, 57f)) // gold
        .Add(0.35f, ColorUtils.OkLabFromCieLab(45f, 45f, 41f)) // red to coppery orange
        .Add(0.70f, ColorUtils.OkLabFromCieLab(41f, 38f, 33f)) // coppery red-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(45f, 20f, 31f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(44f, -4f, 35f)) // dull olive
        .Add(0.45f, ColorUtils.OkLabFromCieLab(44f, 7f, 34f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(44f, 13f, 31f)); // dry brown

    /// <summary>
    /// 'Biokovo' keeps most of its leaves and they take "reddish tones in autumn and winter",
    /// "bronze and red", "dark green foliage changes to dark red" (nurseries; G. macrorrhizum, one
    /// parent of the cross, has the same habit). On the sunlit leaves: November (39, 1, 26) amber,
    /// December (37, 19, 19) bronze, January (34, 29, 15) dark red; at three quarters January is
    /// (36, 13, 27), copper; shaded leaves stay green; the April leaves are green. Darker and redder
    /// than G. macrorrhizum's bronze-red, lighter than Bergenia's. Season fractions from 1 March.
    /// </summary>
    public static ColorCurve HealthySeason => new ColorCurve()
        .Add(0.00f, new Vector3(-0.040f, 0.23f, 0.100f)) // 1 March: still red
        .Add(0.08f, new Vector3(-0.012f, 0.62f, 0.045f)) // end of March: fading
        .Add(0.16f, new Vector3( 0.000f, 1.00f, 0.000f)) // end of April: green
        .Add(0.62f, new Vector3( 0.000f, 1.00f, 0.000f)) // mid October: green
        .Add(0.68f, new Vector3(-0.012f, 0.60f, 0.050f)) // early November: amber
        .Add(0.76f, new Vector3(-0.030f, 0.35f, 0.085f)) // early December: bronze
        .Add(0.86f, new Vector3(-0.045f, 0.20f, 0.105f)) // January: dark red
        .Add(1.00f, new Vector3(-0.040f, 0.23f, 0.100f)); // end of February

    /// <summary>Towards red in OKLab, a little warmer than Bergenia's.</summary>
    public static readonly Vector2 HealthySeasonDirection = Vector2.Normalize(new Vector2(1.00f, 0.20f));
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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(52f, -25f, 48f)) // fresh
        .Add(0.20f, ColorUtils.OkLabFromCieLab(46f, -26f, 42f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, -25f, 38f)) // mature: deep green blade
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, -25f, 38f)); // full adult, stable

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(60f, -8f, 50f)) // yellow-green
        .Add(0.40f, ColorUtils.OkLabFromCieLab(60f, 3f, 45f)) // straw-green
        .Add(0.75f, ColorUtils.OkLabFromCieLab(57f, 7f, 39f)) // straw
        .Add(1.00f, ColorUtils.OkLabFromCieLab(55f, 8f, 36f)); // dead sedge blade: straw-tan, the lightest end on purpose

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -8f, 39f)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromCieLab(53f, 4f, 36f)) // khaki
        .Add(1.00f, ColorUtils.OkLabFromCieLab(55f, 7f, 35f)); // straw-tan
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
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(231, 226, 162)) // cream
        .Add(0.40f, ColorUtils.OkLabFromSrgb8(224, 205, 128)) // pale straw
        .Add(0.75f, ColorUtils.OkLabFromSrgb8(205, 177, 103)) // straw
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(191, 161, 99)); // straw-tan

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromSrgb8(222, 216, 166)) // dull cream
        .Add(0.50f, ColorUtils.OkLabFromSrgb8(205, 180, 109)) // straw-buff
        .Add(1.00f, ColorUtils.OkLabFromSrgb8(191, 161, 102)); // straw-tan
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
        .Add(0.00f, ColorUtils.OkLabFromCieLab(48f, -30f, 45f)) // fresh: light, clean green
        .Add(0.20f, ColorUtils.OkLabFromCieLab(40f, -30f, 37f)) // expanding
        .Add(0.50f, ColorUtils.OkLabFromCieLab(35f, -27f, 31f)) // mature: 'Ruegen' is described as dark green
        .Add(1.00f, ColorUtils.OkLabFromCieLab(35f, -27f, 31f)); // full adult: stable dark green

    public static ColorCurve Stress => ColorModel.DefaultStress;

    public static ColorCurve SeasonalSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(60f, 2f, 61f)) // yellow
        .Add(0.30f, ColorUtils.OkLabFromCieLab(47f, 44f, 47f)) // orange-red
        .Add(0.55f, ColorUtils.OkLabFromCieLab(40f, 57f, 35f)) // strong red: summer leaves load anthocyanin before they die
        .Add(0.80f, ColorUtils.OkLabFromCieLab(35f, 39f, 26f)) // red-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(40f, 20f, 28f)); // brown

    public static ColorCurve StressSenescence => new ColorCurve()
        .Add(0.00f, ColorUtils.OkLabFromCieLab(42f, -6f, 34f)) // dull olive
        .Add(0.50f, ColorUtils.OkLabFromCieLab(42f, 8f, 31f)) // khaki-brown
        .Add(1.00f, ColorUtils.OkLabFromCieLab(42f, 15f, 32f)); // dry brown

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