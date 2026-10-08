using I = Agro.Mechanics.ShellContinuousCollision.Interval;
using V = Agro.Mechanics.ShellContinuousCollision.VectorInterval;

namespace Agro.Mechanics;

public readonly record struct ShellPetioleAngles(double Y0, double Z0, double Y1, double Z1, double Y2, double Z2)
{
    internal double this[int i] => i switch { 0 => Y0, 1 => Z0, 2 => Y1, 3 => Z1, 4 => Y2, 5 => Z2, _ => throw new ArgumentOutOfRangeException(nameof(i)) };
    public static ShellPetioleAngles From(ReadOnlySpan<double> angles)
    {
        if (angles.Length != PetioleBeam.DofCount) throw new ArgumentException("Six petiole angles are required.");
        return new(angles[0], angles[1], angles[2], angles[3], angles[4], angles[5]);
    }
}

/// <summary>Outward enclosure of an exact real-valued motion, in metres.</summary>
public readonly record struct ShellMotionBounds(ShellVector Minimum, ShellVector Maximum);

/// <summary>
/// Trial-path kinematics, held in worker/caller scratch, not persistent organ state.
/// Origin, base rotation vector, rod length, rod Y/Z rotation vectors and local
/// blade vertices interpolate linearly in the continuation parameter. Their
/// composition is curved. Default is identity, suitable for a static obstacle.
/// Does not represent arbitrary quaternion slerps or an unspecified ancestor path.
/// </summary>
public readonly record struct ShellSupportMotion(ShellVertexMotion Origin, ShellVertexMotion BaseRotation,
    double StartLength, double EndLength, ShellPetioleAngles StartAngles, ShellPetioleAngles EndAngles)
{
    public static ShellSupportMotion ForPetiole(PetioleBeam beam, ReadOnlySpan<double> start, ReadOnlySpan<double> end)
        => new(default, default, beam.Length, beam.Length, ShellPetioleAngles.From(start), ShellPetioleAngles.From(end));

    public ShellSupportMotion AtStart => new(new(Origin.Start, Origin.Start), new(BaseRotation.Start, BaseRotation.Start),
        StartLength, StartLength, StartAngles, StartAngles);

    internal bool Valid
    {
        get
        {
            if (!(StartLength >= 0) || !(EndLength >= 0) || !double.IsFinite(StartLength + EndLength)) return false;
            if (!Finite(Origin.Start) || !Finite(Origin.End) || !Finite(BaseRotation.Start) || !Finite(BaseRotation.End)) return false;
            for (var i = 0; i < 6; ++i)
                if (!double.IsFinite(StartAngles[i]) || !double.IsFinite(EndAngles[i])) return false;
            return true;
        }
    }
    static bool Finite(ShellVector v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    /// <summary>
    /// Enclose a transported blade vertex for the whole parameter interval.
    /// Returns false on unsupported numerical range; never substitutes a chord.
    /// Each rotation-vector norm must remain at most 4 rad in the interval bound.
    /// This is a numerical domain, not a botanical flexibility allowance.
    /// </summary>
    public bool TryEnclose(in ShellVertexMotion local, double from, double to, out ShellMotionBounds bounds)
    {
        if (!(from >= 0 && to >= from && to <= 1)) throw new ArgumentOutOfRangeException(nameof(from));
        bounds = default; var time = new I(from, to);
        if (!TryTransform(time, out var transform)) return false;
        var point = transform.Point(V.Lerp(local, time));
        if (!point.Finite) return false;
        bounds = new(new(point.X.Low, point.Y.Low, point.Z.Low), new(point.X.High, point.Y.High, point.Z.High));
        return true;
    }

    // All elementary arithmetic is rounded outward. No Math.Sin/Cos error
    // assumption is required: degree-18 Taylor polynomials use interval
    // coefficients and an explicit remainder for squared angle in [0,16].
    // For sinc the remainder <= 16^19/39! < 2^-77; cosc's is smaller.
    static readonly I[] SincCoefficients = Coefficients(false), CoscCoefficients = Coefficients(true);
    static I[] Coefficients(bool cosc)
    {
        var c = new I[19]; c[0] = I.Point(cosc ? .5 : 1);
        for (var k = 1; k < c.Length; ++k)
            c[k] = (I.Point(0) - c[k - 1]).DividePositive((2 * k + (cosc ? 1 : 0)) * (2 * k + (cosc ? 2 : 1)));
        return c;
    }
    static I Polynomial(I squared, I[] coefficients)
    {
        var p = coefficients[^1];
        for (var k = coefficients.Length - 2; k >= 0; --k) p = p * squared + coefficients[k];
        var error = Math.ScaleB(1.0, -77);
        return p + new I(-error, error);
    }

    internal readonly record struct Transform(V X, V Y, V Z, V Translation)
    {
        internal static Transform Identity => new(V.Point(new(1, 0, 0)), V.Point(new(0, 1, 0)), V.Point(new(0, 0, 1)), V.Point(default));
        internal V Vector(V p) => X * p.X + Y * p.Y + Z * p.Z;
        internal V Point(V p) => Translation + Vector(p);
        internal Transform Compose(in Transform b) => new(Vector(b.X), Vector(b.Y), Vector(b.Z), Point(b.Translation));
    }

    static bool Rotation(V q, out Transform rotation)
    {
        rotation = default;
        if (!q.Finite) return false;
        if (q.X.Magnitude == 0 && q.Y.Magnitude == 0 && q.Z.Magnitude == 0) { rotation = Transform.Identity; return true; }
        var squared = q.X.Square() + q.Y.Square() + q.Z.Square();
        // Squared norms are nonnegative even when directed addition expands zero.
        squared = new(Math.Max(0, squared.Low), squared.High);
        if (!(squared.High <= 16)) return false;
        var s = Polynomial(squared, SincCoefficients); var c = Polynomial(squared, CoscCoefficients);
        var one = I.Point(1);
        var xy = q.X * q.Y * c; var xz = q.X * q.Z * c; var yz = q.Y * q.Z * c;
        var sx = q.X * s; var sy = q.Y * s; var sz = q.Z * s;
        rotation = new(new(one - (q.Y.Square() + q.Z.Square()) * c, xy + sz, xz - sy),
            new(xy - sz, one - (q.X.Square() + q.Z.Square()) * c, yz + sx),
            new(xz + sy, yz - sx, one - (q.X.Square() + q.Y.Square()) * c), V.Point(default));
        return true;
    }

    internal bool TryTransform(I time, out Transform transform)
    {
        transform = default; if (!Valid) return false;
        var noBaseRotation = BaseRotation == default;
        var basis = Transform.Identity;
        if (!noBaseRotation && !Rotation(V.Lerp(BaseRotation, time), out basis)) return false;
        var length = I.Lerp(StartLength, EndLength, time);
        var fractions = PetioleBeam.HingeFractions;
        var rod = Transform.Identity;
        if (StartAngles == default && EndAngles == default)
            rod = rod with { Translation = new(length, I.Point(0), I.Point(0)) };
        // Nested from tip towards base, matching PetioleBeam.Directions exactly.
        else for (var i = 2; i >= 0; --i)
        {
            var angle = new V(I.Point(0), I.Lerp(StartAngles[2 * i], EndAngles[2 * i], time),
                I.Lerp(StartAngles[2 * i + 1], EndAngles[2 * i + 1], time));
            var span = length * I.Difference(i == 2 ? 1 : fractions[i + 1], fractions[i]);
            rod = rod with { Translation = rod.Translation + new V(span, I.Point(0), I.Point(0)) };
            if (!angle.Y.IsZero || !angle.Z.IsZero)
            {
                if (!Rotation(angle, out var hinge)) return false;
                rod = hinge.Compose(rod);
            }
        }
        transform = noBaseRotation ? rod : basis.Compose(rod);
        transform = transform with { Translation = transform.Translation + V.Lerp(Origin, time) };
        return transform.X.Finite && transform.Y.Finite && transform.Z.Finite && transform.Translation.Finite;
    }

    internal static void RemoveCommonBase(ref ShellSupportMotion a, ref ShellSupportMotion b)
    {
        if (a.Origin != b.Origin) return;
        a = a with { Origin = default }; b = b with { Origin = default };
        if (a.BaseRotation != b.BaseRotation) return;
        a = a with { BaseRotation = default }; b = b with { BaseRotation = default };
    }
}
