
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Utils;

namespace Agro.DelunayTerrain;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{IA} {IB}")]
public readonly struct Hyperedge
{
    public readonly int IA;
    public readonly int IB;

    public Hyperedge(int ia, int ib)
    {
        Debug.Assert(ia < ib);
        IA = ia;
        IB = ib;
    }
}


[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{IA} {IB} {IC}")]
public readonly struct Hyperface : IEquatable<Hyperface>
{
    public readonly int IA;
    public readonly int IB;
    public readonly int IC;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Hyperface(int ia, int ib, int ic)
    {
        Debug.Assert(ia < ib && ib < ic);
        IA = ia;
        IB = ib;
        IC = ic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Hyperface other) => IA == other.IA && IB == other.IB && IC == other.IC;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Hyperface other && Equals(other);

    // Faster than HashCode.Combine for hot loops (and alloc-free)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        unchecked
        {
            // classic multiplicative mixing on ints
            int h = IA * 73856093;
            h ^= IB * 19349663;
            h ^= IC * 83492791;
            return h;
        }
    }

    internal IEnumerable<Hyperedge> GetEdges() => [new(IA, IB), new(IA, IC), new(IB, IC)];
}

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{A} {B} {C} {D}")]
public readonly struct SimplexIndexes
{
    public readonly int A;
    public readonly int B;
    public readonly int C;
    public readonly int D;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SimplexIndexes(int ia, int ib, int ic, int id)
    {
        Debug.Assert(ia < ib && ib < ic && ic < id);
        A = ia;
        B = ib;
        C = ic;
        D = id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasSupervertex(int minIndex) => A >= minIndex || B >= minIndex || C >= minIndex || D >= minIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool Contains(int index) => index <= B ? (index == A || index == B) : (index == C || index == D);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]Hyperface ABC() => new(A, B, C);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]Hyperface ABD() => new(A, B, D);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]Hyperface ACD() => new(A, C, D);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]Hyperface BCD() => new(B, C, D);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ToggleFaces(HashSet<Hyperface> set)
    {
        var face = ABC(); if (!set.Add(face)) set.Remove(face);
        face = ABD(); if (!set.Add(face)) set.Remove(face);
        face = ACD(); if (!set.Add(face)) set.Remove(face);
        face = BCD(); if (!set.Add(face)) set.Remove(face);
    }

    internal FacesSharing Shares(FacesSharing input, SimplexIndexes other)
    {
        //tetrahedrons share at most one face, hence else branches can be used extensivelly

        //ABC:
        // (A == X && B == Y & C == Z) || [0]
        // (A == X && B == Y & C == W) || [1]
        // (A == X && B == Z & C == W) || [2]
        // (A == Y && B == Z & C == W) || [3]
        //ABD:
        // (A == X && B == Y & D == Z) || [4]
        // (A == X && B == Y & D == W) || [5]
        // (A == X && B == Z & D == W) || [6]
        // (A == Y && B == Z & D == W) || [7]
        //ACD:
        // (A == X && C == Y & D == Z) || [8]
        // (A == X && C == Y & D == W) || [9]
        // (A == X && C == Z & D == W) || [10]
        // (A == Y && C == Z & D == W) || [11]
        //BCD:
        // (B == X && C == Y & D == Z) || [12]
        // (B == X && C == Y & D == W) || [13]
        // (B == X && C == Z & D == W) || [14]
        // (B == Y && C == Z & D == W)    [15]

        if (A == other.A) //[0,1,2, 4,5,6, 8,9,10]
        {
            if (B == other.B) //[0,1, 4,5]
            {
                if (C == other.C || C == other.D) return input | FacesSharing.ABC; //[0,1]
                else if (D == other.C || D == other.D) return input | FacesSharing.ABD; //[4,5]
            }
            else if (B == other.C) //[2, 6]
            {
                if (C == other.D) return input | FacesSharing.ABC; //[2]
                else if (D == other.D) return input | FacesSharing.ABD; //[6]
            }
            else if ((C == other.B && (D == other.C || D == other.D)) || (C == other.C && D == other.D)) return input | FacesSharing.ACD; //[8,9,10]
        }
        else if (A == other.B) //[3, 7, 11]
        {
            if (B == other.C) //[3, 7]
            {
                if (C == other.D) return input | FacesSharing.ABC; //[3]
                else if (D == other.D) return input | FacesSharing.ABD; //[7]
            }
            else if (C == other.C && D == other.D) return input | FacesSharing.ACD; //[11]
        }
        else if (B == other.A) //[12,13,14]
        {
            if ((C == other.B && (D == other.C || D == other.D)) || (C == other.C && D == other.D)) return input | FacesSharing.BCD; //[12,13,14]
        }
        else if (B == other.B && C == other.C && D == other.D) return input | FacesSharing.BCD; //[15]

        return input;
    }

    internal (float AntigravityPull, float Area) NeighInterface(Vector3[] points, FacesSharing conn)
    {
        Vector3 a, b;
        switch(conn)
        {
            case FacesSharing.ABC: a = points[B] - points[A]; b = points[C] - points[A]; break;
            case FacesSharing.ABD: a = points[B] - points[A]; b = points[D] - points[A]; break;
            case FacesSharing.ACD: a = points[C] - points[A]; b = points[D] - points[A]; break;
            case FacesSharing.BCD: a = points[C] - points[B]; b = points[D] - points[B]; break;
            default: return (0f, 0f);
        }

        var ortho = Vector3.Cross(a, b);
        var orthoLength = ortho.Length();
        var area = orthoLength * 0.5f;
        //DOT with DOWN vector
        var antigravityPull = ortho.Y / orthoLength;
        return (antigravityPull, area);
    }

    internal Hyperface GetFace(FacesSharing face) => face switch
    {
        FacesSharing.ABD => new (A, B, D),
        FacesSharing.ACD => new (A, C, D),
        FacesSharing.BCD => new (B, C, D),
        _ => new (A, B, C)
    };

    internal float Volume(Vector3[] points)
    {
        var a = points[A] - points[D];
        var b = points[B] - points[D];
        var c = points[C] - points[D];

        //see https://en.wikipedia.org/wiki/Tetrahedron
        //using the scalar tripple product

        return Math.Abs(Vector3.Dot(a, Vector3.Cross(b, c))) / 6f;
    }

    internal bool PointInside(in Vector3[] points, in Vector3 center)
    {
        //see https://math.stackexchange.com/a/4431870
        var ba = points[B] - points[A];
        var da = points[D] - points[A];
        var ca = points[C] - points[A];
        var na = Vector3.Cross(ba, ca);
        var nb = Vector3.Cross(ba, da);
        var nc = Vector3.Cross(ca, da);
        var nd = Vector3.Cross(points[C] - points[B], points[D] - points[B]);

        var g = 0.25f * (points[A] + points[B] + points[C] + points[D]);
        var ga = g - points[A];
        var gb = g - points[B];
        if (Vector3.Dot(ga, na) > 0) na = -na;
        if (Vector3.Dot(ga, nb) > 0) nb = -nb;
        if (Vector3.Dot(ga, nc) > 0) nc = -nc;
        if (Vector3.Dot(gb, nd) > 0) nd = -nd;

        //now the actual computation
        var qa = center - points[A];
        return !(Vector3.Dot(qa, na) > 0 || Vector3.Dot(qa, nb) > 0 || Vector3.Dot(qa, nc) > 0 || Vector3.Dot(center - points[B], nd) > 0);
    }

    internal Vector3 GetRandomPoint(Vector3[] points, Pcg rnd, float minDepth, float maxDepth)
    {
        var maxY = Math.Max( Math.Max(points[A].Y, points[B].Y), Math.Max(points[C].Y, points[D].Y) );
        var minY = Math.Min( Math.Min(points[A].Y, points[B].Y), Math.Min(points[C].Y, points[D].Y) );
        var depth = Math.Clamp(maxY - rnd.NextFloat(minDepth, maxDepth), minY, maxY);

        //cut all edges at the given height (three of them will have an actual intersection in the interval)
        var triangle = new List<Vector3>();
        if (TryGetHorizontalCut(points[A], points[B], depth, out var p)) triangle.Add(p);
        if (TryGetHorizontalCut(points[A], points[C], depth, out p)) triangle.Add(p);
        if (TryGetHorizontalCut(points[A], points[D], depth, out p)) triangle.Add(p);
        if (TryGetHorizontalCut(points[B], points[C], depth, out p)) triangle.Add(p);
        if (TryGetHorizontalCut(points[B], points[D], depth, out p)) triangle.Add(p);
        if (TryGetHorizontalCut(points[C], points[D], depth, out p)) triangle.Add(p);

        if (triangle.Count == 0)
        {
            if (points[A].Y == maxY) return points[A];
            else if (points[B].Y == maxY) return points[B];
            else if (points[C].Y == maxY) return points[C];
            else return points[D];
        }
        else if (triangle.Count == 1)
            return triangle[0];
        else if (triangle.Count == 2)
        {
            var t = rnd.NextFloat();
            return triangle[0] * t + triangle[1] * (1f - t);
        }
        else
        {
            var s = rnd.NextFloat();
            var t = rnd.NextFloat();
            return (triangle[0] * s + triangle[1] * (1f - s)) * t + triangle[2] * (1f - t);
        }
    }

    static bool TryGetHorizontalCut(in Vector3 a, in Vector3 b, float h, out Vector3 intersection)
    {
        var dy = b.Y - a.Y;
        if (Math.Abs(dy) < 0)
        {
            //deliberately ignoring the case when the whole edge lies in the plane since that will be handled by the remaining edges, it would only produce unnecessary duplicates
            intersection = default;
            return false;
        }

        var t = (h - a.Y) / dy;
        if (t >= 0f && t <= 1f)
        {
            intersection = a + t * (b - a);
            return true;
        }
        else
        {
            intersection = default;
            return false;
        }
    }

    internal static float Height(Vector3[] points)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        for(int i = 0; i < points.Length; ++i)
        {
            if (points[i].Y < min) min = points[i].Y;
            if (points[i].Y > max) max = points[i].Y;
        }
        return max - min;
    }
}

[Flags] internal enum FacesSharing : byte { None = 0, ABC = 1, ABD = 2, ACD = 4, BCD = 8, All = ABC | ABD | ACD | BCD };