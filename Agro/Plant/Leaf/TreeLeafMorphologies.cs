using System.Numerics;

namespace Agro;
/// <summary>Species silhouettes authored once; shared by contacts, light and every export.</summary>
public static class TreeLeafMorphologies
{
    public static LeafMorphology Celtis { get; } = CeltisBlade();
    public static LeafMorphology Amelanchier { get; } = Blade(.05f, .036f, .013f, false, true);
    public static LeafMorphology Acer { get; } = Mesh(.12f, .14f, .09f, [new(0, 0), new(.05f, -.26f), new(.01f, -.60f), new(.23f, -.56f), new(.27f, -.82f), new(.38f, -.65f), new(.48f, -1), new(.55f, -.66f), new(.67f, -.70f), new(.59f, -.38f), new(.79f, -.45f), new(.76f, -.22f), new(1, 0), new(.76f, .22f), new(.79f, .45f), new(.59f, .38f), new(.67f, .70f), new(.55f, .66f), new(.48f, 1), new(.38f, .65f), new(.27f, .82f), new(.23f, .56f), new(.01f, .60f), new(.05f, .26f)], LeafMarginType.Lobed, LeafBaseType.Cordate, LeafVenationType.Palmate, 5);
    public static LeafMorphology SamaraWing { get; } = Mesh(.035f, .012f, 0, [new(0, 0), new(.2f, -.23f), new(.65f, -.43f), new(.92f, -.25f), new(1, .18f), new(.91f, .68f), new(.67f, 1), new(.36f, .68f), new(.1f, .22f)], LeafMarginType.Entire, LeafBaseType.Cuneate, LeafVenationType.Parallel, 0);

    static LeafMorphology CeltisBlade()
    {
        // Ovate-lanceolate, with unequal rounded basal shoulders and a drawn-out
        // acuminate tip. The proximal margin is entire; the small saw teeth point
        // toward the apex. These silhouette proportions are authored hypotheses,
        // guided by JRC/Flora descriptions and the supplied close-up, not a fit to
        // a perspective photograph. Physical length/width stay at75x35mm.
        Vector2[] lower = [new(0, 0), new(.02f, .36f), new(.06f, .65f), new(.13f, .87f), new(.23f, 1), new(.34f, .99f), new(.47f, .85f), new(.60f, .65f), new(.70f, .43f), new(.78f, .26f), new(.86f, .13f), new(.94f, .045f), new(1, 0)];
        Vector2[] upper = [new(0, 0), new(.06f, .10f), new(.12f, .45f), new(.20f, .74f), new(.31f, .87f), new(.43f, .82f), new(.56f, .64f), new(.68f, .44f), new(.78f, .25f), new(.86f, .12f), new(.94f, .04f), new(1, 0)];
        static float Width(Vector2[] controls, float t)
        {
            for (int i = 1; i < controls.Length; i++)
                if (t <= controls[i].X)
                {
                    var a = controls[i - 1];
                    var b = controls[i];
                    return a.Y + (b.Y - a.Y) * ((t - a.X) / (b.X - a.X));
                }
            return 0f;
        }
        static List<Vector2> Edge(Vector2[] controls, int side)
        {
            var edge = new List<Vector2>(44);
            foreach (var t in new[] { 0f, .02f, .06f, .10f })
                edge.Add(new(t, side * Width(controls, t)));
            edge.Add(new(.14f, side * Width(controls, .14f) * .985f));
            for (int tooth = 0; tooth < 18; tooth++)
            {
                var t = .14f + .04f * tooth;
                // Distally displaced apex and a short distal face, rather than
                // alternating broad notches that make the blade look crenate.
                edge.Add(new(t + .026f, side * Width(controls, t + .026f) * 1.025f));
                edge.Add(new(t + .04f, side * Width(controls, t + .04f) * .985f));
            }
            foreach (var t in new[] { .90f, .96f, 1f })
                edge.Add(new(t, side * Width(controls, t)));
            return edge;
        }
        var boundary = Edge(lower, -1);
        var returnEdge = Edge(upper, 1);
        // Keep the petiole junction and apex once each, and do not recenter the
        // unequal shoulders. Runtime whole-width correction preserves full width.
        for (int i = returnEdge.Count - 2; i > 0; i--)
            boundary.Add(returnEdge[i]);
        return Mesh(.075f, .035f, .012f, boundary.ToArray(), LeafMarginType.Serrate,
            LeafBaseType.Oblique, LeafVenationType.Pinnate, 0) with { Apex = LeafApexType.Acuminate };
    }

    static LeafMorphology Blade(float length, float width, float petiole, bool asymmetric, bool rounded)
    {
        var points = new List<Vector2>
        {
            Vector2.Zero
        };
        for (int side = -1; side <= 1; side += 2)
        {
            for (int n = 1; n < 24; n++)
            {
                var t = side < 0 ? n / 24f : 1f - n / 24f;
                var w = rounded ? MathF.Pow(MathF.Sin(MathF.PI * t), .55f) : MathF.Sin(MathF.PI * MathF.Pow(t, .7f));
                if (asymmetric)
                    w *= side < 0 ? 1f - .18f * (1 - t) : .88f + .12f * t;
                if ((!rounded || t > .45f) && (n & 1) == 0)
                    w *= .90f;
                points.Add(new(t, w * side));
            }

            if (side < 0)
                points.Add(new(1, 0));
        }

        return Mesh(length, width, petiole, points.ToArray(), LeafMarginType.Serrate, asymmetric ? LeafBaseType.Oblique : LeafBaseType.Rounded, LeafVenationType.Pinnate, 0);
    }

    static LeafMorphology Mesh(float length, float width, float petiole, Vector2[] boundary, LeafMarginType margin, LeafBaseType basis, LeafVenationType veins, int lobes)
    {
        // Ear clipping respects deep maple sinuses; a triangle fan fills them in.
        var vertices = boundary.Select(p => new Vector3(2 * p.X - 1, p.Y, width * .018f * p.Y * p.Y)).ToList();
        var remaining = Enumerable.Range(0, boundary.Length).ToList();
        var indices = new List<int>((boundary.Length - 2) * 3);
        float area = 0;
        for (int i = 0; i < boundary.Length; i++)
            area += Cross(boundary[i], boundary[(i + 1) % boundary.Length]);
        if (area < 0)
            remaining.Reverse();
        while (remaining.Count > 3)
        {
            var found = false;
            for (int j = 0; j < remaining.Count; j++)
            {
                var a = remaining[(j + remaining.Count - 1) % remaining.Count];
                var b = remaining[j];
                var c = remaining[(j + 1) % remaining.Count];
                var va = boundary[a];
                var vb = boundary[b];
                var vc = boundary[c];
                if (Cross(vb - va, vc - vb) <= 1e-8f)
                    continue;
                var inside = false;
                foreach (var q in remaining)
                {
                    if (q == a || q == b || q == c)
                        continue;
                    var p = boundary[q];
                    if (Cross(vb - va, p - va) >= -1e-8f && Cross(vc - vb, p - vb) >= -1e-8f && Cross(va - vc, p - vc) >= -1e-8f)
                    {
                        inside = true;
                        break;
                    }
                }

                if (inside)
                    continue;
                indices.AddRange([a, b, c]);
                remaining.RemoveAt(j);
                found = true;
                break;
            }

            if (!found)
                throw new InvalidOperationException("Invalid authored tree lamina outline.");
        }

        indices.AddRange(remaining);
        return new()
        {
            Dimensions = LeafDimensions.Create(length, width, .0002f, petiole, .08f),
            Margin = margin,
            Base = basis,
            Venation = veins,
            LobeCount = lobes,
            Apex = lobes > 0 ? LeafApexType.Acuminate : LeafApexType.Acute,
            LowResVertices = vertices,
            HighResVertices = vertices,
            LowResIndices = indices,
            HighResIndices = indices
        };
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
