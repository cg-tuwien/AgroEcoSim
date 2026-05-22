
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Agro.DelunayTerrain;

//nice explanation
//https://gorillasun.de/blog/bowyer-watson-algorithm-for-delaunay-triangulation

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{IA} {IB} {IC} {ID} {CircumradiusSqr}")]
///Indexes are always sorted
public class TetrahedronsSoA
{
    int mCapacity;
    internal SimplexIndexes[] Indices; //IA, IB, IC, ID
    VectorCirc[] Circumsphere; //Circumcenter + CircumradiusSqr
    readonly int SuperIndex;

    readonly List<int>[] LocalBadTetrahedrons = new List<int>[ProcessorCount];

    public int Count = 0;
    const int BlockPower = 6;
    public int Capacity
    {
        get => mCapacity;

        internal set
        {
            if (value > mCapacity)
                SetCapacity(value);
        }
    }

#if DEBUG
    Dictionary<Hyperface, int> DebugFaceCounts = [];
    readonly Dictionary<Hyperedge, List<Hyperface>> DebugEdgeCounts = [];
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetCapacity(int value)
    {
        var target = ((value >> BlockPower) + 1) << BlockPower;
        mCapacity = target;
        Array.Resize(ref Indices, target);
        Array.Resize(ref Circumsphere, target);
    }

    public TetrahedronsSoA(int superIndex)
    {
        var c = 1 << BlockPower;
        Indices = new SimplexIndexes[c];
        Circumsphere = new VectorCirc[c];
        SuperIndex = superIndex;

        for(int i = 0; i < ProcessorCount; ++i)
            LocalBadTetrahedrons[i] = [];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool InCircumsphere(int tetraIndex, Vector3int point)
    {
        //see the math in VectorCirc
        var circumsphere = Circumsphere[tetraIndex];
        var d = point.X * circumsphere.D - circumsphere.PDVX;
        d *= d;
        if (d > circumsphere.RDD) return false;
        var dy = point.Y * circumsphere.D - circumsphere.PDVY;
        d += dy * dy;
        if (d > circumsphere.RDD) return false;
        var dz = point.Z * circumsphere.D - circumsphere.PDVZ;
        return d + dz * dz <= circumsphere.RDD;
    }

    static readonly SimplexIndexes[] InitialTetrahedra = [
        new(0, 1, 3, 7),
        new(0, 1, 5, 7),
        new(0, 2, 3, 7),
        new(0, 2, 6, 7),
        new(0, 4, 5, 7),
        new(0, 4, 6, 7),
    ];

    public void CreateInitial(Vector3int[] points)
    {
        foreach(var tetrahedron in InitialTetrahedra)
        {
            var ia = SuperIndex + tetrahedron.A;
            var ib = SuperIndex + tetrahedron.B;
            var ic = SuperIndex + tetrahedron.C;
            var id = SuperIndex + tetrahedron.D;
            //https://en.wikipedia.org/wiki/Tetrahedron#Circumcenter
            var p = points[ia];
            var s = points[ib] - p;
            var t = points[ic] - p;
            var u = points[id] - p;

            var tuCross = Vector3big.Cross(t, u);

            var det = Vector3big.Dot(s, tuCross) << 1;
            if (det != 0)
            {
                var circumcenterOffset = u.LengthSquared() * Vector3big.Cross(s, t) + t.LengthSquared() * Vector3big.Cross(u, s) + s.LengthSquared() * tuCross;
                Indices[Count] = new(ia, ib, ic, id);
                //c = p + o/d
                //r = ||o/d||
                //r² = (ox/det)² + ...
                //r²det² = ox² + oy² ...
                Circumsphere[Count] = new(p, circumcenterOffset, det, circumcenterOffset.LengthSquared());
                Debug.Assert(Circumsphere[Count].TestOwnPoint(points[ia]));
                Debug.Assert(Circumsphere[Count].TestOwnPoint(points[ib]));
                Debug.Assert(Circumsphere[Count].TestOwnPoint(points[ic]));
                Debug.Assert(Circumsphere[Count].TestOwnPoint(points[id]));
                ++Count;
            }
            else
                throw new Exception("Degenerate initial tetrahedron detected! There is something wrong with the input data, possibly there are less than 4 points or they are all coplanar");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ToggleFaces(List<int> badTetrahedrons, HashSet<Hyperface> target)
    {
        var limit = badTetrahedrons.Count;
        for (int bt = 0; bt < limit; ++bt) //find boundary of the polyhedral hole
            Indices[badTetrahedrons[bt]].ToggleFaces(target);
    }

    static readonly int ProcessorCount = Environment.ProcessorCount;
    static readonly int ParallelThreshold = ProcessorCount << 12;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool CheckBad(Vector3int point, List<int> result)
    {
        result.Clear();

        if (Count <= ParallelThreshold)
        {
            for(int t = 0; t < Count; ++t)
                if (InCircumsphere(t, point))
                    result.Add(t);
            return false;
        }
        else
        {
            var badBatch = Count / ProcessorCount;
            Parallel.For(0, ProcessorCount, bb => {
                var limit = bb == ProcessorCount - 1 ? Count : (bb + 1) * badBatch;
                     var target = LocalBadTetrahedrons[bb];
                     target.Clear();
                     for (int t = bb * badBatch; t < limit; ++t)
                         if (InCircumsphere(t, point))
                            target.Add(t);
                 });

                 for(int i = 0; i < Environment.ProcessorCount; ++i)
                     result.AddRange(LocalBadTetrahedrons[i]);
            return true;
        }
    }

    readonly List<SimplexIndexes> TmpIndices = [];
    readonly List<VectorCirc> TmpCircumspheres = [];

    //re-tetrahedralize the star-shaped polyhedral hole
    //by creatng new tetrahedra by combining each face of the polyhedron with the inserted vertex
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryCreate(HashSet<Hyperface> polyhedron, Vector3int[] points, int pointIndex, List<int> badTetrahedrons, Queue<int> postpone)
    {
#if DEBUG
        //Check for topological defects
        DebugEdgeCounts.Clear();
        foreach(var face in polyhedron)
            foreach(var edge in face.GetEdges())
                if (DebugEdgeCounts.TryGetValue(edge, out var list))
                    list.Add(face);
                else
                    DebugEdgeCounts[edge] = [face];

        foreach(var item in DebugEdgeCounts)
        {
            Dictionary<Hyperface, List<SimplexIndexes>> tets = null;
            var closestIndex = -1;
            var closestDistSqr = (BigInteger)UInt128.MaxValue;
            var insideSuper = true;
            if (item.Value.Count > 2)
            {
                tets = [];
                foreach(var bt in badTetrahedrons)
                {
                    var faces = GetFaces(bt);
                    foreach(var f in item.Value)
                        if (faces.Contains(f))
                            if (tets.TryGetValue(f, out var list))
                                list.Add(Indices[bt]);
                            else
                                tets[f] = [Indices[bt]];
                }

                for(int i = 0; i < pointIndex; ++i)
                {
                    var d = (points[i] - points[pointIndex]).LengthSquared();
                    if (d < closestDistSqr)
                    {
                        closestIndex = i;
                        closestDistSqr = d;
                    }
                }

                insideSuper = DebugSuperTetrahedron(points, pointIndex);
            }


            Debug.Assert(item.Value.Count <= 2);
        }
#endif

        TmpIndices.Clear();
        TmpCircumspheres.Clear();
        if (TmpIndices.Capacity < polyhedron.Count)
        {
            TmpIndices.Capacity = polyhedron.Count;
            TmpCircumspheres.Capacity = polyhedron.Count;
        }

        var btCount = badTetrahedrons.Count;

        int validCount = 0;
        foreach(var face in polyhedron)
        {
            // Face indices are already sorted: IA <= IB <= IC, now sort in pointIndex
            int f0, f1, f2, f3;
            if (pointIndex < face.IB)
            {
                f2 = face.IB;
                f3 = face.IC;
                if (pointIndex < face.IA)
                {
                    f0 = pointIndex;
                    f1 = face.IA;
                }
                else
                {
                    f0 = face.IA;
                    f1 = pointIndex;
                }
            }
            else
            {
                f0 = face.IA;
                f1 = face.IB;
                if (pointIndex > face.IC)
                {
                    f2 = face.IC;
                    f3 = pointIndex;
                }
                else
                {
                    f2 = pointIndex;
                    f3 = face.IC;
                }
            }
            Debug.Assert(f0 < f1 && f1 < f2 && f2 < f3);
            //https://en.wikipedia.org/wiki/Tetrahedron#Circumcenter
            var point = points[f0];
            var s = points[f1] - point;
            var t = points[f2] - point;
            var u = points[f3] - point;

            var tuCross = Vector3big.Cross(t, u);
            var det = Vector3big.Dot(s, tuCross) * 2;
            Debug.Assert(det != 0);
            if (det != 0)
            {
                Debug.Assert(!s.IsZero());
                Debug.Assert(!t.IsZero());
                Debug.Assert(!u.IsZero());

                var circumcenterOffset = u.LengthSquared() * Vector3big.Cross(s, t) + t.LengthSquared() * Vector3big.Cross(u, s) + s.LengthSquared() * tuCross;
                var circumRadiusSqr = circumcenterOffset.LengthSquared();
                Debug.Assert(circumRadiusSqr > 0);

                TmpIndices.Add(new(f0, f1, f2, f3));
                TmpCircumspheres.Add(new(point, circumcenterOffset, det, circumRadiusSqr));
                TmpCircumspheres[^1].TestOwnPoint(points[f0]);
                TmpCircumspheres[^1].TestOwnPoint(points[f1]);
                TmpCircumspheres[^1].TestOwnPoint(points[f2]);
                TmpCircumspheres[^1].TestOwnPoint(points[f3]);
            }
            else
            {
                postpone.Enqueue(pointIndex);
                break;
            }

            ++validCount;
        }

        if (validCount == polyhedron.Count)
        {
            if (validCount < badTetrahedrons.Count) //added less than removed
            {
                badTetrahedrons.Sort();
                for (int f = 0; f < validCount; ++f)
                {
                    var ti = badTetrahedrons[f];
                    Indices[ti] = TmpIndices[f];
                    Circumsphere[ti] = TmpCircumspheres[f];
                }

                //remove those not overwritten from the data structure
                int bt = btCount - 1;
                for(; bt >= validCount && badTetrahedrons[bt] == Count - 1; --bt)
                    --Count;

                for(; bt >= validCount; --bt)
                {
                    var ti = badTetrahedrons[bt];
                    Indices[ti] = Indices[--Count]; //move the last element to the position of the one to be removed
                    Circumsphere[ti] = Circumsphere[Count];
                }
            }
            else
            {
                for (int f = 0; f < btCount; ++f)
                {
                    var ti = badTetrahedrons[f];
                    Indices[ti] = TmpIndices[f];
                    Circumsphere[ti] = TmpCircumspheres[f];
                }

                var remainingLength = validCount - btCount;
                if (remainingLength > 0)
                {
                    var newCount = Count + remainingLength;
                    if (newCount >= Capacity)
                        SetCapacity(newCount);

                    TmpIndices.CopyTo(btCount, Indices, Count, remainingLength);
                    TmpCircumspheres.CopyTo(btCount, Circumsphere, Count, remainingLength);
                    Count = newCount;
                }
            }

            return true;
        }
        else
            return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RemoveSupertetras()
    {
        int t = Count - 1;
        //remove those at tail
        for (; t >= 0 && Indices[t].HasSupervertex(SuperIndex); --t)
            --Count;

        for (; t >= 0; --t) //this could be made faster in special cases when several entires for removal come one after each other -- but that does not happen often I think
            if (Indices[t].HasSupervertex(SuperIndex)) //vertex from the original super-tetrahedron
            {
                Indices[t] = Indices[--Count];
                Circumsphere[t] = Circumsphere[Count];
            }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Hyperface[] GetFaces(int i) => GetFaces(Indices, i);

    internal static Hyperface[] GetFaces(IList<SimplexIndexes> src, int i)
    {
        var result = new Hyperface[4];
        var tetra = src[i];
        result[0] = new(tetra.A, tetra.B, tetra.C);
        result[1] = new(tetra.A, tetra.B, tetra.D);
        result[2] = new(tetra.A, tetra.C, tetra.D);
        result[3] = new(tetra.B, tetra.C, tetra.D);

        return result;
    }

    #if DEBUG
    public bool DebugSuperTetrahedron(IList<Vector3int> points, int pointIndex = -1) =>
        InitialTetrahedra.Any(t => DebugSuperTetrahedron(points, pointIndex, t));

    public bool DebugSuperTetrahedron(IList<Vector3int> points, int pointIndex, SimplexIndexes idx)
    {
        var a = points[SuperIndex + idx.A];
        var b = points[SuperIndex + idx.B];
        var c = points[SuperIndex + idx.C];
        var d = points[SuperIndex + idx.D];
        var abc = Vector3big.Cross(a - c, b - c);
        var adb = Vector3big.Cross(a - b, d - b);
        var acd = Vector3big.Cross(a - d, c - d);
        var bdc = Vector3big.Cross(b - c, d - c);

        var oABC = DebugOrientSuper(abc, c, d); // orientation of tetrahedron
        if (oABC == BigInteger.Zero)
            return false; // degenerate tetrahedron

        var result = true;
        var limit = pointIndex < 4 ? points.Count : pointIndex;
        for (int i = Math.Max(pointIndex, 4); i < limit && result; ++i)
        {
            var p = points[i];
            // Orientations of P relative to each face
            var oPABC = DebugOrientSuper(abc, c, p);
            var oPADB = DebugOrientSuper(adb, b, p);
            var oPACD = DebugOrientSuper(acd, d, p);
            var oPBDC = DebugOrientSuper(bdc, c, p);

            // All must have the same sign as the tetrahedron
            result = BigIntegerSign(oPABC) == BigIntegerSign(oABC)
                && BigIntegerSign(oPADB) == BigIntegerSign(oABC)
                && BigIntegerSign(oPACD) == BigIntegerSign(oABC)
                && BigIntegerSign(oPBDC) == BigIntegerSign(oABC);
        }

        return result;
    }

    static BigInteger DebugOrientSuper(in Vector3big abc, in Vector3int c, in Vector3int p) => Vector3big.Dot(p - c, abc);
    static int BigIntegerSign(BigInteger value) => value > BigInteger.Zero ? 1 : (value == BigInteger.Zero ? 0 : -1);
    #endif
}