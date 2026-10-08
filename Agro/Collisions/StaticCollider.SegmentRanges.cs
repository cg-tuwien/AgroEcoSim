using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    // Restricted segments must remain in relative double coordinates until the
    // distance is measured. Only the returned witnesses are rounded to world floats.
    // Leave the existing unrestricted hot kernel unchanged.
    internal static double ClosestPointsBetweenSegmentRanges(
        Vector3 p0, Vector3 p1, double lowP, double highP,
        Vector3 q0, Vector3 q1, double lowQ, double highQ,
        out Vector3 onFirst, out Vector3 onSecond)
    {
        double px = (double)p1.X - p0.X, py = (double)p1.Y - p0.Y, pz = (double)p1.Z - p0.Z;
        double qx = (double)q1.X - q0.X, qy = (double)q1.Y - q0.Y, qz = (double)q1.Z - q0.Z;
        var rangeP = highP - lowP;
        var rangeQ = highQ - lowQ;
        double ux = px * rangeP, uy = py * rangeP, uz = pz * rangeP;
        double vx = qx * rangeQ, vy = qy * rangeQ, vz = qz * rangeQ;
        double rx = ((double)p0.X - q0.X) + px * lowP - qx * lowQ;
        double ry = ((double)p0.Y - q0.Y) + py * lowP - qy * lowQ;
        double rz = ((double)p0.Z - q0.Z) + pz * lowP - qz * lowQ;
        double a = ux * ux + uy * uy + uz * uz;
        double e = vx * vx + vy * vy + vz * vz;
        double f = vx * rx + vy * ry + vz * rz;
        double c = ux * rx + uy * ry + uz * rz;
        double b = ux * vx + uy * vy + uz * vz;
        double s, t;
        if (a == 0) { s = 0; t = e > 0 ? Math.Clamp(f / e, 0, 1) : 0; }
        else if (e == 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
        else
        {
            var determinant = a * e - b * b;
            if (determinant > 1e-8 * a * e)
            {
                s = Math.Clamp((b * f - c * e) / determinant, 0, 1);
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
            else
            {
                // Near parallelism, subtracting a*e-b*b is ill-conditioned. Test
                // boundary minima and a cross-product interior candidate instead.
                // Evaluate residual vectors rather than the expanded quadratic.
                s = 0; t = Math.Clamp(f / e, 0, 1);
                var best = Squared(rx, ry, rz, ux, uy, uz, vx, vy, vz, s, t);
                Consider(1, Math.Clamp((b + f) / e, 0, 1), rx, ry, rz, ux, uy, uz, vx, vy, vz, ref best, ref s, ref t);
                Consider(Math.Clamp(-c / a, 0, 1), 0, rx, ry, rz, ux, uy, uz, vx, vy, vz, ref best, ref s, ref t);
                Consider(Math.Clamp((b - c) / a, 0, 1), 1, rx, ry, rz, ux, uy, uz, vx, vy, vz, ref best, ref s, ref t);
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var crossSquared = nx * nx + ny * ny + nz * nz;
                if (crossSquared > 0)
                {
                    var interiorS = -((ry * vz - rz * vy) * nx + (rz * vx - rx * vz) * ny + (rx * vy - ry * vx) * nz) / crossSquared;
                    var interiorT = -((ry * uz - rz * uy) * nx + (rz * ux - rx * uz) * ny + (rx * uy - ry * ux) * nz) / crossSquared;
                    if (interiorS >= 0 && interiorS <= 1 && interiorT >= 0 && interiorT <= 1)
                        Consider(interiorS, interiorT, rx, ry, rz, ux, uy, uz, vx, vy, vz, ref best, ref s, ref t);
                }
            }
        }
        var parameterP = lowP + rangeP * s;
        var parameterQ = lowQ + rangeQ * t;
        onFirst = new((float)(p0.X + px * parameterP), (float)(p0.Y + py * parameterP), (float)(p0.Z + pz * parameterP));
        onSecond = new((float)(q0.X + qx * parameterQ), (float)(q0.Y + qy * parameterQ), (float)(q0.Z + qz * parameterQ));
        return Squared(rx, ry, rz, ux, uy, uz, vx, vy, vz, s, t);
    }

    static double Squared(double rx, double ry, double rz, double ux, double uy, double uz,
        double vx, double vy, double vz, double s, double t)
    {
        var x = rx + ux * s - vx * t;
        var y = ry + uy * s - vy * t;
        var z = rz + uz * s - vz * t;
        return x * x + y * y + z * z;
    }

    static void Consider(double candidateS, double candidateT, double rx, double ry, double rz,
        double ux, double uy, double uz, double vx, double vy, double vz,
        ref double best, ref double s, ref double t)
    {
        var distance = Squared(rx, ry, rz, ux, uy, uz, vx, vy, vz, candidateS, candidateT);
        if (distance >= best) return;
        best = distance; s = candidateS; t = candidateT;
    }
}
