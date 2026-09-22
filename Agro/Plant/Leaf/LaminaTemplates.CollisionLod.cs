using System.Numerics;
using Agro.Collisions;

namespace Agro;

public sealed partial class LaminaTemplate
{
    readonly record struct CollisionLevel(LaminaTemplate Template, float ReliefError);
    readonly Lazy<CollisionLevel[]>? CollisionLevels;

    /// <summary>Collision-only interior simplification. The displayed boundary is unchanged;
    /// unsupported surfaces, petals and segmented blades retain the exact template. Error is
    /// bounded over the complete swept scale, including growing out-of-plane relief.</summary>
    public LaminaTemplate GetCollisionApproximation(in OrganPose from, in OrganPose to,
        float tolerance, out float error)
    {
        error = 0;
        if (!(tolerance > 0) || CollisionLevels == null) return this;
        var width = MathF.Max(from.Width, to.Width);
        // Five percent of the narrowest nonzero width during the interval protects seedlings.
        var narrow = MathF.Min(from.Width, to.Width);
        var budget = MathF.Min(.0005f, MathF.Min(tolerance, MathF.Max(0, narrow) * .1f));
        if (!(budget > 0)) return this;
        var selected = this;
        foreach (var level in CollisionLevels.Value)
        {
            var candidateError = level.ReliefError * ReliefScale(width);
            if (!float.IsFinite(candidateError) || candidateError > budget) continue;
            selected = level.Template; error = candidateError;
        }
        return selected;
    }

    readonly record struct Face(int A, int B, int C)
    {
        public bool Has(int v) => A == v || B == v || C == v;
        public Face Replace(int from, int to) => new(A == from ? to : A, B == from ? to : B, C == from ? to : C);
        public bool Degenerate => A == B || A == C || B == C;
    }
    readonly record struct Edge(int A, int B)
    {
        public static Edge Of(int a, int b) => a < b ? new(a, b) : new(b, a);
    }
    readonly record struct Point(double X, double Y);
    static double Cross(Point a, Point b, Point c) => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    Point XY(int vertex) => new(Vertices[vertex].X, Vertices[vertex].Y);
    double Area(Face f) => Cross(XY(f.A), XY(f.B), XY(f.C));

    CollisionLevel[] BuildCollisionLevels(float referenceLength)
    {
        if (Indices.Length < 96) return [];
        var original = new List<Face>(Indices.Length / 3);
        for (int i = 0; i < Indices.Length; i += 3) original.Add(new(Indices[i], Indices[i+1], Indices[i+2]));
        var sign = Math.Sign(Area(original[0]));
        // Folded or vertical surfaces cannot use an XY height certificate.
        if (sign == 0 || original.Any(f => Area(f) * sign <= 1e-12)) return [];
        var faces = new List<Face>(original);
        var levels = new List<CollisionLevel>();
        var threshold = original.Count * 3 / 4;
        var neighbours = Enumerable.Range(0, Vertices.Length).Select(_ => new HashSet<int>()).ToArray();
        var edges = new Dictionary<Edge, int>();
        var boundary = new bool[Vertices.Length];
        var candidates = new List<(double Cost, int From, int To)>();
        var replacements = new List<Face>();
        while (faces.Count >= 12)
        {
            edges.Clear(); Array.Clear(boundary);
            foreach (var n in neighbours) n.Clear();
            foreach (var f in faces)
            { Add(f.A, f.B); Add(f.B, f.C); Add(f.C, f.A); }
            if (edges.Values.Any(n => n > 2)) return []; // Nonmanifold source: exact fallback.
            foreach (var (edge, count) in edges)
                if (count == 1) boundary[edge.A] = boundary[edge.B] = true;
            candidates.Clear();
            foreach (var (edge, count) in edges)
            {
                if (count != 2) continue;
                var delta = Vertices[edge.A] - Vertices[edge.B];
                var cost = delta.X*delta.X*referenceLength*referenceLength*.25
                    + delta.Y*delta.Y*ReferenceHalfWidth*ReferenceHalfWidth + delta.Z*delta.Z;
                if (!boundary[edge.A]) candidates.Add((cost, edge.A, edge.B));
                if (!boundary[edge.B]) candidates.Add((cost, edge.B, edge.A));
            }
            candidates.Sort(static (a,b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost)
                : a.From != b.From ? a.From.CompareTo(b.From) : a.To.CompareTo(b.To));
            var changed = false;
            foreach (var candidate in candidates)
            {
                var from = candidate.From; var to = candidate.To;
                // Link condition: exactly the two incident faces may be removed.
                var common = 0;
                foreach (var n in neighbours[from]) if (neighbours[to].Contains(n)) ++common;
                if (common != 2) continue;
                replacements.Clear(); var valid = true;
                foreach (var f in faces)
                {
                    var next = f.Replace(from, to);
                    if (next.Degenerate) continue;
                    if (next != f && Area(next) * sign <= 1e-12) { valid = false; break; }
                    replacements.Add(next);
                }
                if (!valid || replacements.Count != faces.Count - 2) continue;
                // Positive projected faces plus unchanged boundary and the link condition
                // preserve the planar domain; no hole, lost lobe or folded face is introduced.
                (faces, replacements) = (replacements, faces);
                changed = true; break;
            }
            if (!changed) break;
            if (faces.Count > threshold) continue;
            var reliefError = CertifyRelief(original, faces, sign);
            if (!float.IsFinite(reliefError)) break;
            var mesh = new MeshData(); var ids = new Dictionary<int,int>();
            foreach (var f in faces)
            {
                Append(f.A); Append(f.B); Append(f.C);
            }
            levels.Add(new(new(mesh, ReferenceHalfWidth), reliefError));
            threshold = faces.Count * 3 / 4;

            void Append(int vertex)
            {
                if (!ids.TryGetValue(vertex, out var index))
                { index = mesh.Positions.Count; ids.Add(vertex,index); mesh.Positions.Add(Vertices[vertex]); }
                mesh.Indices.Add(index);
            }
        }
        return levels.ToArray();

        void Add(int a, int b)
        {
            neighbours[a].Add(b); neighbours[b].Add(a);
            var edge = Edge.Of(a,b); edges.TryGetValue(edge, out var count); edges[edge] = count + 1;
        }
    }

    // Both meshes have the identical XY domain. On each triangle-overlay polygon their
    // relief difference is affine, so its absolute maximum occurs at a polygon vertex.
    // This certifies every surface point, unlike vertex sampling or quadric-error heuristics.
    float CertifyRelief(List<Face> original, List<Face> coarse, int sign)
    {
        double error = 0;
        Span<Point> polygon = stackalloc Point[12];
        Span<Point> scratch = stackalloc Point[12];
        foreach (var a in original)
        {
            var minA = Vector3.Min(Vertices[a.A], Vector3.Min(Vertices[a.B], Vertices[a.C]));
            var maxA = Vector3.Max(Vertices[a.A], Vector3.Max(Vertices[a.B], Vertices[a.C]));
            foreach (var b in coarse)
            {
                var minB = Vector3.Min(Vertices[b.A], Vector3.Min(Vertices[b.B], Vertices[b.C]));
                var maxB = Vector3.Max(Vertices[b.A], Vector3.Max(Vertices[b.B], Vertices[b.C]));
                if (minA.X > maxB.X || maxA.X < minB.X || minA.Y > maxB.Y || maxA.Y < minB.Y) continue;
                polygon[0] = XY(a.A); polygon[1] = XY(a.B); polygon[2] = XY(a.C);
                var count = 3;
                count = Clip(polygon, scratch, count, XY(b.A), XY(b.B), sign);
                count = Clip(polygon, scratch, count, XY(b.B), XY(b.C), sign);
                count = Clip(polygon, scratch, count, XY(b.C), XY(b.A), sign);
                for (int i = 0; i < count; ++i)
                    error = Math.Max(error, Math.Abs(Height(a,polygon[i])-Height(b,polygon[i])));
            }
        }
        // Round outward; the templates use floats, but overlay construction and plane
        // evaluation use doubles. The additional nanometre is at reference blade width.
        return MathF.BitIncrement((float)error + 1e-9f);
    }

    static int Clip(Span<Point> polygon, Span<Point> scratch, int count, Point first, Point second, int sign)
    {
        if (count == 0) return 0;
        var previous = polygon[count - 1]; var dp = sign * Cross(first,second,previous);
        var written = 0;
        for (int i = 0; i < count; ++i)
        {
            var current = polygon[i]; var dc = sign * Cross(first,second,current);
            if ((dp >= 0) != (dc >= 0))
            {
                var t = dp / (dp-dc);
                scratch[written++] = new(previous.X+(current.X-previous.X)*t, previous.Y+(current.Y-previous.Y)*t);
            }
            if (dc >= 0) scratch[written++] = current;
            previous = current; dp = dc;
        }
        scratch[..written].CopyTo(polygon);
        return written;
    }

    double Height(Face f, Point p)
    {
        var a = XY(f.A); var b = XY(f.B); var c = XY(f.C);
        var area = Cross(a,b,c);
        var wb = Cross(a,p,c) / area; var wc = Cross(a,b,p) / area;
        return Vertices[f.A].Z + wb*(Vertices[f.B].Z-Vertices[f.A].Z) + wc*(Vertices[f.C].Z-Vertices[f.A].Z);
    }
}
