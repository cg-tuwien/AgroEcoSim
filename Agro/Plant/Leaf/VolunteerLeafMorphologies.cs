using System.Numerics;

namespace Agro;

/// <summary>Authored volunteer-herb silhouettes shared by playback, light and contact geometry.</summary>
public static class VolunteerLeafMorphologies
{
    // Stations suffice for the entire Persicaria blade. Lobed blades below use a
    // perimeter that can turn back around a hook. All asymmetry is deterministic.
    readonly record struct Station(float Along, float Left, float Right);

    /// <summary>Unblotched, entire, narrowly lanceolate small water-pepper blade.</summary>
    /// <remarks>BSBI: https://fermanagh.bsbi.org/persicaria-minor-opiz .</remarks>
    public static LeafMorphology PersicariaMinor { get; } = Create(
        LeafDimensions.Create(.06f, .007f, .00018f, .003f, .12f),
        LeafMarginType.Entire, LeafBaseType.Cuneate, 0, .025f,
        [new(0, .04f, .04f), new(.05f, .24f, .22f), new(.12f, .53f, .49f),
         new(.22f, .80f, .76f), new(.38f, .98f, .96f), new(.55f, 1, 1),
         new(.70f, .91f, .92f), new(.82f, .73f, .76f), new(.91f, .47f, .49f),
         new(.97f, .20f, .19f), new(1, 0, 0)]);

    /// <summary>Lobed prickly lettuce: clasping basal ears, rounded sinuses and three broad hooked lobes.</summary>
    /// <remarks>
    /// Three lateral lobe pairs, an elongated terminal lobe and fine marginal teeth. The species
    /// also has unlobed forms; this preset represents the lobed form, not every genotype.
    /// https://w3.biosci.utexas.edu/prc/K12/pages/Lactuca%20serriola.html
    /// </remarks>
    public static LeafMorphology LactucaSerriola { get; } = PricklyLettuce();

    /// <summary>Basal dandelion blade with five deep, backward-pointing lobe pairs.</summary>
    /// <remarks>
    /// A representative common-dandelion aggregate silhouette: long winged base, narrow sinuses
    /// and a triangular terminal lobe. The aggregate includes less deeply divided leaves too.
    /// https://www.nzflora.info/factsheet/taxon/Taraxacum-officinale.html
    /// </remarks>
    public static LeafMorphology TaraxacumOfficinale { get; } = Dandelion();

    static LeafMorphology PricklyLettuce()
    {
        // Photograph: Institute of Botany CAS, Pavel Vesely, Lactuca_serriola14.jpg,
        // https://pladias.cz/taxon/pictures/Lactuca%20serriola . The lobed form has
        // curved U-shaped incisions, not a repeated triangular width waveform.
        var left = new Perimeter();
        left.Curve(.015f, .02f, .018f, .12f, .012f, .31f, 3);
        left.Curve(.045f, .49f, .085f, .50f, .12f, .35f, 4, teeth: true);
        left.Curve(.16f, .20f, .18f, .11f, .215f, .18f, 4);
        left.Curve(.225f, .38f, .18f, .68f, .17f, .82f, 4);
        left.Curve(.24f, .80f, .27f, .64f, .30f, .49f, 4, teeth: true);
        left.Curve(.345f, .18f, .36f, .13f, .405f, .19f, 5);
        left.Curve(.44f, .31f, .40f, .73f, .385f, 1f, 4);
        left.Curve(.45f, .98f, .49f, .76f, .515f, .51f, 4, teeth: true);
        left.Curve(.55f, .20f, .59f, .14f, .635f, .22f, 5);
        left.Curve(.67f, .34f, .66f, .64f, .645f, .78f, 4);
        left.Curve(.715f, .77f, .75f, .53f, .785f, .32f, 4, teeth: true);
        left.Curve(.825f, .16f, .855f, .20f, .88f, .25f, 4);
        left.Curve(.925f, .25f, .965f, .11f, 1f, 0f, 5, teeth: true);

        var right = new Perimeter();
        right.Curve(.008f, .07f, .005f, .23f, .018f, .42f, 3);
        right.Curve(.055f, .51f, .10f, .45f, .15f, .28f, 4, teeth: true);
        right.Curve(.18f, .14f, .20f, .12f, .25f, .19f, 4);
        right.Curve(.275f, .32f, .245f, .59f, .235f, .76f, 4);
        right.Curve(.30f, .73f, .34f, .58f, .365f, .40f, 4, teeth: true);
        right.Curve(.39f, .20f, .43f, .13f, .47f, .21f, 5);
        right.Curve(.50f, .35f, .475f, .79f, .455f, 1f, 4);
        right.Curve(.53f, .94f, .57f, .70f, .595f, .45f, 4, teeth: true);
        right.Curve(.62f, .21f, .655f, .17f, .69f, .22f, 5);
        right.Curve(.715f, .36f, .70f, .56f, .69f, .67f, 4);
        right.Curve(.75f, .64f, .79f, .43f, .825f, .30f, 4, teeth: true);
        right.Curve(.86f, .23f, .875f, .29f, .91f, .24f, 3);
        right.Curve(.95f, .19f, .98f, .06f, 1f, 0f, 4, teeth: true);
        return Lobed(LeafDimensions.Create(.18f, .06f, .00025f, .002f, .25f),
            LeafMarginType.Spiny, LeafBaseType.Truncate, 6, left, right, .065f, .025f);
    }

    static LeafMorphology Dandelion()
    {
        // Common-dandelion leaves vary substantially within the aggregate. This is
        // the deeply runcinate form: five unequal retrorse lobes and a broad terminal
        // blade, with a narrow winged base. UW Extension photographs show this range:
        // https://hort.extension.wisc.edu/articles/dandelion-taraxacum-officinale/ .
        var left = new Perimeter();
        left.Curve(.05f, .035f, .10f, .06f, .145f, .10f, 4);
        left.Curve(.17f, .13f, .13f, .27f, .115f, .36f, 3);
        left.Curve(.175f, .36f, .20f, .22f, .23f, .12f, 4);
        left.Curve(.255f, .08f, .26f, .09f, .285f, .13f, 3);
        left.Curve(.30f, .25f, .235f, .52f, .205f, .66f, 4);
        left.Curve(.29f, .61f, .32f, .42f, .35f, .28f, 4, teeth: true);
        left.Curve(.375f, .15f, .38f, .10f, .405f, .13f, 3);
        left.Curve(.43f, .27f, .355f, .67f, .325f, .86f, 4);
        left.Curve(.405f, .82f, .46f, .48f, .485f, .30f, 4, teeth: true);
        left.Curve(.51f, .15f, .53f, .12f, .555f, .15f, 3);
        left.Curve(.575f, .32f, .49f, .79f, .46f, 1f, 4);
        left.Curve(.56f, .94f, .60f, .54f, .64f, .32f, 4, teeth: true);
        left.Curve(.66f, .18f, .68f, .15f, .70f, .18f, 3);
        left.Curve(.72f, .32f, .66f, .71f, .625f, .86f, 4);
        left.Curve(.72f, .77f, .755f, .47f, .79f, .30f, 4, teeth: true);
        left.Curve(.82f, .20f, .805f, .64f, .795f, .83f, 4);
        left.Curve(.87f, .79f, .90f, .43f, .925f, .28f, 4, teeth: true);
        left.Curve(.955f, .17f, .98f, .08f, 1f, 0f, 3);

        var right = new Perimeter();
        right.Curve(.06f, .04f, .115f, .055f, .17f, .09f, 4);
        right.Curve(.20f, .13f, .16f, .24f, .145f, .29f, 3);
        right.Curve(.21f, .30f, .24f, .15f, .27f, .11f, 4);
        right.Curve(.29f, .08f, .30f, .11f, .315f, .16f, 3);
        right.Curve(.335f, .30f, .28f, .52f, .25f, .59f, 4);
        right.Curve(.335f, .55f, .37f, .36f, .40f, .22f, 4, teeth: true);
        right.Curve(.425f, .12f, .44f, .10f, .46f, .16f, 3);
        right.Curve(.48f, .29f, .40f, .67f, .37f, .78f, 4);
        right.Curve(.46f, .72f, .505f, .40f, .54f, .25f, 4, teeth: true);
        right.Curve(.565f, .14f, .585f, .13f, .605f, .18f, 3);
        right.Curve(.625f, .34f, .55f, .80f, .525f, 1f, 4);
        right.Curve(.615f, .92f, .65f, .55f, .69f, .33f, 4, teeth: true);
        right.Curve(.715f, .19f, .73f, .15f, .75f, .21f, 3);
        right.Curve(.77f, .35f, .72f, .62f, .695f, .72f, 4);
        right.Curve(.775f, .67f, .805f, .40f, .825f, .29f, 4, teeth: true);
        right.Curve(.855f, .24f, .84f, .65f, .83f, .76f, 3);
        right.Curve(.905f, .65f, .94f, .24f, 1f, 0f, 5, teeth: true);
        return Lobed(LeafDimensions.Create(.18f, .05f, .00022f, .02f, .20f),
            LeafMarginType.Lobed, LeafBaseType.Cuneate, 10, left, right, .045f, .04f);
    }

    // Along is a fraction of blade length; Across is a fraction of half width.
    // Unlike a width-at-X profile, a perimeter may turn backwards around a lobe.
    sealed class Perimeter
    {
        public readonly List<Vector2> Points = [Vector2.Zero];

        public void Curve(float x1, float y1, float x2, float y2, float x3, float y3,
            int steps, bool teeth = false)
        {
            var a = Points[^1];
            var b = new Vector2(x1, y1); var c = new Vector2(x2, y2); var d = new Vector2(x3, y3);
            for (var i = 1; i <= steps; ++i)
            {
                var t = i / (float)steps; var u = 1f - t;
                var point = u*u*u*a + 3f*u*u*t*b + 3f*u*t*t*c + t*t*t*d;
                // Sparse secondary teeth are much smaller than a lobe. Leave sinuses
                // smooth and do not serrate the narrow attachment or the terminal tip.
                if (teeth && i < steps && point.Y > .22f)
                {
                    var previous = Points[^1];
                    var tangent = point - previous;
                    var normal = Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
                    Points.Add((previous + point) * .5f + new Vector2(normal.X * .002f, normal.Y * .018f));
                }
                Points.Add(point);
            }
        }
    }

    static LeafMorphology Lobed(LeafDimensions dimensions, LeafMarginType margin,
        LeafBaseType leafBase, int lobes, Perimeter left, Perimeter right, float fold, float twist)
    {
        // Triangulate the two sides independently so the midrib remains a real mesh
        // seam. A global fan would fill the hooked sinuses with overlapping triangles.
        // All of this work runs once per preset, never per leaf or simulation tick.
        var flat = new List<Vector2>(); var indices = new List<int>();
        var vertices = new Dictionary<Vector2, int>();
        AddHalf(left.Points, 1f); AddHalf(right.Points, -1f);
        var coarse = Surface(flat, indices, dimensions.MaxWidthM, fold, twist);
        var finePoints = new List<Vector2>(flat); var fineIndices = new List<int>();
        var edges = new Dictionary<(int, int), int>();
        for (var i = 0; i < indices.Count; i += 3)
        {
            var a = indices[i]; var b = indices[i+1]; var c = indices[i+2];
            var ab = Midpoint(a,b); var bc = Midpoint(b,c); var ca = Midpoint(c,a);
            fineIndices.AddRange([a,ab,ca, ab,b,bc, ca,bc,c, ab,bc,ca]);
        }
        var fine = Surface(finePoints, fineIndices, dimensions.MaxWidthM, fold, twist);
        return new()
        {
            Dimensions = dimensions, Margin = margin, Base = leafBase,
            Venation = LeafVenationType.Pinnate, Apex = LeafApexType.Acute, LobeCount = lobes,
            LowResVertices = coarse.Positions, LowResIndices = coarse.Indices,
            HighResVertices = fine.Positions, HighResIndices = fine.Indices,
        };

        int Add(Vector2 p)
        {
            if (vertices.TryGetValue(p, out var index)) return index;
            index = flat.Count; flat.Add(p); vertices.Add(p,index); return index;
        }

        void AddHalf(List<Vector2> boundary, float side)
        {
            var polygon = boundary.Select(p => Add(new(p.X, side*p.Y))).ToList();
            for (var i = 9; i > 0; --i) polygon.Add(Add(new(i*.1f, 0f)));
            if (side < 0) polygon.Reverse();
            var triangles = Triangulate(flat, polygon);
            ImproveTriangles(flat, triangles, 2f*dimensions.LengthM/dimensions.MaxWidthM);
            foreach (var (a,b,c) in triangles)
            {
                // Put a curvature sample inside broad tissue, but avoid tessellating
                // tiny teeth and sinus edges. The boundary and its area stay exact.
                if (Math.Abs(Cross(flat[a],flat[b],flat[c])) > .018)
                {
                    var centre = Add((flat[a]+flat[b]+flat[c])/3f);
                    indices.AddRange([a,b,centre, b,c,centre, c,a,centre]);
                }
                else indices.AddRange([a,b,c]);
            }
        }

        int Midpoint(int a, int b)
        {
            var edge = a < b ? (a,b) : (b,a);
            if (edges.TryGetValue(edge,out var index)) return index;
            index = finePoints.Count; finePoints.Add((finePoints[a]+finePoints[b])*.5f);
            edges.Add(edge,index); return index;
        }
    }

    static MeshData Surface(List<Vector2> points, List<int> indices, float width, float fold, float twist)
    {
        var mesh = new MeshData();
        foreach (var p in points)
        {
            var arch = MathF.Sin(MathF.PI*p.X);
            // Shallow longitudinal bow, cupping about the midrib, a broad raised
            // rib and restrained torsion. Relief remains metres at reference width
            // and is scaled by LaminaTemplates for expanding/seedling blades.
            var rib = .009f*MathF.Exp(-p.Y*p.Y/.04f)*arch;
            var z = width*(.045f*arch - .035f*p.X*p.X*p.X
                + fold*p.Y*p.Y + rib
                + twist*p.Y*MathF.Sin(2.3f*MathF.PI*p.X)*arch);
            mesh.Positions.Add(new(2f*p.X-1f, p.Y, z));
        }
        mesh.Indices.AddRange(indices);
        return mesh;
    }

    static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
        ((double)b.X-a.X)*(c.Y-a.Y)-((double)b.Y-a.Y)*(c.X-a.X);

    static void ImproveTriangles(List<Vector2> points, List<(int A,int B,int C)> triangles, float aspect)
    {
        // Ear clipping preserves concavities but can leave long fans whose interpolation
        // makes a smooth rib look corrugated. Flip only interior diagonals of convex
        // pairs to improve the worst triangle, retaining the perimeter and midrib seam.
        // This small, bounded optimization runs once at preset initialization.
        for (var pass = 0; pass < 64; ++pass)
        {
            var edges = new Dictionary<(int,int),(int Triangle,int Opposite)>();
            var touched = new bool[triangles.Count]; var changed = false;
            for (var i = 0; i < triangles.Count; ++i)
            {
                var (a,b,c) = triangles[i];
                Visit(a,b,c,i); Visit(b,c,a,i); Visit(c,a,b,i);
            }
            if (!changed) break;

            void Visit(int a, int b, int c, int current)
            {
                var edge = a < b ? (a,b) : (b,a);
                if (!edges.TryGetValue(edge, out var other))
                { edges.Add(edge,(current,c)); return; }
                if (touched[current] || touched[other.Triangle]) return;
                var d = other.Opposite;
                if (Cross(points[c],points[d],points[a]) * Cross(points[c],points[d],points[b]) >= -1e-14) return;
                var before = Math.Min(Quality(a,b,c),Quality(a,b,d));
                var after = Math.Min(Quality(c,d,a),Quality(c,d,b));
                if (after <= before + 1e-6) return;
                triangles[current] = Clockwise(c,d,a);
                triangles[other.Triangle] = Clockwise(c,d,b);
                touched[current] = touched[other.Triangle] = true; changed = true;
            }
        }

        // Judge triangle shape at the reference blade's physical aspect ratio.
        double DistanceSquared(int a, int b)
        {
            var delta = points[a]-points[b];
            return aspect*aspect*delta.X*delta.X + delta.Y*delta.Y;
        }
        double Quality(int a, int b, int c) => Math.Abs(Cross(points[a],points[b],points[c])) /
            (DistanceSquared(a,b) + DistanceSquared(b,c) + DistanceSquared(c,a));
        (int,int,int) Clockwise(int a, int b, int c) =>
            Cross(points[a],points[b],points[c]) < 0 ? (a,b,c) : (a,c,b);
    }

    static List<(int A,int B,int C)> Triangulate(List<Vector2> points, List<int> polygon)
    {
        var result = new List<(int,int,int)>();
        while (polygon.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < polygon.Count; ++i)
            {
                var a = polygon[(i+polygon.Count-1)%polygon.Count];
                var b = polygon[i]; var c = polygon[(i+1)%polygon.Count];
                if (Cross(points[a],points[b],points[c]) >= -1e-10) continue;
                var occupied = false;
                foreach (var other in polygon)
                    if (other != a && other != b && other != c
                        && Cross(points[a],points[b],points[other]) <= 1e-10
                        && Cross(points[b],points[c],points[other]) <= 1e-10
                        && Cross(points[c],points[a],points[other]) <= 1e-10)
                    { occupied = true; break; }
                if (occupied) continue;
                result.Add((a,b,c)); polygon.RemoveAt(i); clipped = true; break;
            }
            if (!clipped) throw new InvalidOperationException("Leaf perimeter must be simple and nondegenerate.");
        }
        if (Cross(points[polygon[0]],points[polygon[1]],points[polygon[2]]) >= -1e-10)
            throw new InvalidOperationException("Degenerate leaf triangulation.");
        result.Add((polygon[0],polygon[1],polygon[2]));
        return result;
    }

    static LeafMorphology Create(LeafDimensions dimensions, LeafMarginType margin,
        LeafBaseType leafBase, int lobes, float edgeFold, Station[] stations)
    {
        // Built only once when the presets are initialized. Direct meshes follow the same
        // contract as the existing species: X in [-1,1], full transverse span 2, Z in metres
        // at the reference width. LaminaTemplates scales relief with the developing blade.
        var coarse = Build(stations, dimensions.MaxWidthM, edgeFold, 1);
        var fine = Build(stations, dimensions.MaxWidthM, edgeFold, 3);
        return new()
        {
            Dimensions = dimensions,
            Margin = margin,
            Venation = LeafVenationType.Pinnate,
            Apex = LeafApexType.Acute,
            Base = leafBase,
            LobeCount = lobes,
            LowResVertices = coarse.Positions,
            LowResIndices = coarse.Indices,
            HighResVertices = fine.Positions,
            HighResIndices = fine.Indices,
        };
    }

    static MeshData Build(Station[] stations, float referenceWidth, float edgeFold, int subdivisions)
    {
        var mesh = new MeshData();
        var vertices = new Dictionary<Vector3, int>();
        int[]? previous = null;
        for (var interval = 0; interval < stations.Length - 1; ++interval)
        {
            var start = stations[interval];
            var end = stations[interval + 1];
            var last = interval == stations.Length - 2 ? subdivisions : subdivisions - 1;
            for (var part = 0; part <= last; ++part)
            {
                var fraction = part / (float)subdivisions;
                var s = start.Along + (end.Along - start.Along) * fraction;
                var left = start.Left + (end.Left - start.Left) * fraction;
                var right = start.Right + (end.Right - start.Right) * fraction;
                var x = 2f * s - 1f;
                var arch = MathF.Sin(MathF.PI * s);
                // A raised midrib, shallow transverse fold and restrained asymmetric edge
                // relaxation give the blade relief without changing its authored outline.
                var midrib = referenceWidth * .025f * arch;
                var row = new[]
                {
                    Add(new(x, -left, midrib - referenceWidth * edgeFold * left * arch)),
                    Add(new(x, 0, midrib)),
                    Add(new(x, right, midrib - referenceWidth * edgeFold * right * arch * .9f)),
                };
                if (previous != null)
                    for (var side = 0; side < 2; ++side)
                    {
                        Triangle(previous[side], previous[side + 1], row[side]);
                        Triangle(previous[side + 1], row[side + 1], row[side]);
                    }
                previous = row;
            }
        }
        return mesh;

        int Add(Vector3 point)
        {
            if (vertices.TryGetValue(point, out var index)) return index;
            index = mesh.Positions.Count;
            vertices.Add(point, index);
            mesh.Positions.Add(point);
            return index;
        }

        void Triangle(int a, int b, int c)
        {
            // At a pointed tip the three row vertices coincide. Weld it once and omit
            // collapsed facets; all remaining triangles have clockwise winding in X/Y.
            if (a == b || b == c || c == a) return;
            mesh.Indices.AddRange([a, b, c]);
        }
    }
}
