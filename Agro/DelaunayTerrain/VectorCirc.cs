using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro.DelunayTerrain;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{PX} {PY} {PZ} + {VX} {VY} {VZ} / {D} o- {RDD}")]
public readonly struct VectorCirc
{
    public readonly BigInteger PDVX;
    public readonly BigInteger PDVY;
    public readonly BigInteger PDVZ;
    public readonly BigInteger D;
    public readonly BigInteger RDD;

    public VectorCirc(Vector3int p, Vector3big v, BigInteger det, BigInteger r)
    {
        PDVX = p.X * det + v.X;
        PDVY = p.Y * det + v.Y;
        PDVZ = p.Z * det + v.Z;
        D = det;
        //in general
        //d = x/w * x/w + y/w * y/w + z/w * z/w

        //a*a + a*b - a*c + b*a + b*b - b*c - c*a - c*b + c*c = a² + b² + c² +2ab -2ac -2bc

        //in our case
        //d = (zx + cx/w - px)² + (zy + cy/w - py)² + (zy + cz/w - pz)²
        //d = zx² + cx²/w² + px² + 2*zx*cx/w - 2*zx*px - 2*cx*px/w ...
        //dw² = zx²w² + cx² + px²w² + 2*zx*w - 2*zx*px*w² - 2*cx*px*w  ...
        //dw² = (zx*w + cx - px*w)² ...

        //d*w*w = x*x + y*y + z*z

        //return d <= R
        //return d*w*w <= R*w*w
        RDD = r;
    }

    internal bool TestOwnPoint(Vector3int point)
    {
        var d = point.X * D - PDVX;
        d *= d;
        //Debug.WriteLine($"T: {tetraIndex} P: {point} = {d - circumsphere.W} {d + (circumsphere.Y - point.Y) * (circumsphere.Y - point.Y) - circumsphere.W} {d + (circumsphere.Y - point.Y) * (circumsphere.Y - point.Y) + (circumsphere.Z - point.Z) * (circumsphere.Z - point.Z)- circumsphere.W}");
        if (d > RDD) return false;
        var dy = point.Y * D - PDVY;
        d += dy * dy;
        if (d > RDD) return false;
        var dz = point.Z * D - PDVZ;
        return d + dz * dz == RDD;
    }
}