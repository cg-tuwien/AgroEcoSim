namespace Agro.Mechanics;

/// <summary>
/// Conservative strain-domain check for linearly moving shell vertices. Positive
/// Bernstein bounds test the whole interval, not just its endpoint. Failure to
/// bound an interval is a numerical rejection, not proof of physical confinement.
/// This does not certify contact clearance or a curved support trajectory.
/// </summary>
internal static class ShellStrainPath
{
    readonly record struct Quadratic(double A, double B, double C)
    {
        internal Quadratic Offset(double value) => new(A + value, B, C);
        internal Quadratic Negated => new(-A, -B, -C);
    }

    internal static bool Check(ShellVector u, ShellVector v, ShellVector du, ShellVector dv,
        double rest00, double rest01, double rest11, double lower, double upper)
    {
        var a = new Quadratic(u.Dot(u), 2 * u.Dot(du), du.Dot(du));
        var b = new Quadratic(u.Dot(v), u.Dot(dv) + du.Dot(v), du.Dot(dv));
        var c = new Quadratic(v.Dot(v), 2 * v.Dot(dv), dv.Dot(dv));
        // Base the roundoff allowance on the operands BEFORE subtracting rest
        // metrics or determinant products. A nearly cancelled determinant is
        // not an appropriate estimate of the arithmetic's absolute error.
        var scale00 = Math.Abs(a.A) + Math.Abs(a.B) + Math.Abs(a.C) + upper * Math.Abs(rest00);
        var scale01 = Math.Abs(b.A) + Math.Abs(b.B) + Math.Abs(b.C) + upper * Math.Abs(rest01);
        var scale11 = Math.Abs(c.A) + Math.Abs(c.B) + Math.Abs(c.C) + upper * Math.Abs(rest11);
        var leadingMargin = 1e-12 * scale00;
        var determinantMargin = 1e-12 * (scale00 * scale11 + scale01 * scale01);
        return PositiveMetric(a.Offset(-lower * rest00), b.Offset(-lower * rest01), c.Offset(-lower * rest11), leadingMargin, determinantMargin)
            && PositiveMetric(a.Negated.Offset(upper * rest00), b.Negated.Offset(upper * rest01), c.Negated.Offset(upper * rest11), leadingMargin, determinantMargin);
    }

    static bool PositiveMetric(Quadratic a, Quadratic b, Quadratic c, double leadingMargin, double determinantMargin)
    {
        // Sylvester's criterion for the symmetric 2x2 metric in its rest basis.
        var minimum = Math.Min(a.A, a.A + a.B + a.C);
        if (a.C > 0 && a.B < 0 && -a.B < 2 * a.C)
            minimum = Math.Min(minimum, a.A - a.B * a.B / (4 * a.C));
        if (!(minimum > leadingMargin)) return false;
        Span<double> power = stackalloc double[5]
        {
            a.A * c.A - b.A * b.A,
            a.A * c.B + a.B * c.A - 2 * b.A * b.B,
            a.A * c.C + a.B * c.B + a.C * c.A - b.B * b.B - 2 * b.A * b.C,
            a.B * c.C + a.C * c.B - 2 * b.B * b.C,
            a.C * c.C - b.C * b.C
        };
        Span<double> bernstein = stackalloc double[5]
        {
            power[0],
            power[0] + power[1] / 4,
            power[0] + power[1] / 2 + power[2] / 6,
            power[0] + 3 * power[1] / 4 + power[2] / 2 + power[3] / 4,
            power[0] + power[1] + power[2] + power[3] + power[4]
        };
        // A scale-relative roundoff margin also applies at every subdivision.
        // Near the boundary we return uncertified and let continuation shorten.
        return PositiveBernstein(bernstein, determinantMargin, 20);
    }

    static bool PositiveBernstein(ReadOnlySpan<double> coefficients, double margin, int remaining)
    {
        var minimum = double.PositiveInfinity;
        foreach (var value in coefficients)
        {
            if (!double.IsFinite(value)) return false;
            minimum = Math.Min(minimum, value);
        }
        if (minimum > margin) return true;
        if (remaining == 0 || coefficients[0] <= margin || coefficients[4] <= margin) return false;
        Span<double> work = stackalloc double[5]; coefficients.CopyTo(work);
        Span<double> left = stackalloc double[5]; Span<double> right = stackalloc double[5];
        left[0] = work[0]; right[4] = work[4];
        for (var level = 1; level <= 4; ++level)
        {
            for (var i = 0; i < 5 - level; ++i) work[i] = (work[i] + work[i + 1]) * .5;
            left[level] = work[0]; right[4 - level] = work[4 - level];
        }
        return PositiveBernstein(left, margin, remaining - 1) && PositiveBernstein(right, margin, remaining - 1);
    }
}
