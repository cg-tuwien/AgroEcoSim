using System.Numerics;

namespace Agro;

/// <summary>
/// Cuts a lamina template into the pieces a segmented blade is drawn from.
/// </summary>
/// <remarks>
/// A blade split by <see cref="SpeciesSettings.LeafSegmentLength"/> is a chain of ordinary leaf
/// organs, and every one of them was drawn with the species' whole-leaf outline. An outline runs
/// to a point at both ends, so a chain of them pinched to nothing at every joint - the blade came
/// out as a row of separate lenses rather than one strap.
///
/// Each segment instead carries the slice of the whole outline that belongs to its own stretch of
/// the blade. Neighbouring slices are cut at the same station, so they meet along an edge of the
/// same width, and the chain reassembles into exactly the outline the species authored.
/// </remarks>
public static class LaminaMeshSlicer
{
    /// <summary>
    /// The <paramref name="index"/>-th of <paramref name="count"/> slices of <paramref name="whole"/>.
    /// </summary>
    /// <remarks>
    /// X is the blade's own axis, -1 at the junction and +1 at the tip, and a slice is rescaled to
    /// that same range because it is drawn against its own segment's half length. The outer cuts
    /// are deliberately not made: tissue reaching behind the junction - a cordate or reniform base -
    /// belongs to the first slice, and anything past the tip to the last.
    /// </remarks>
    public static MeshData Slice(MeshData whole, int index, int count)
    {
        if (count <= 1 || whole.Indices.Count < 3)
            return whole;

        index = Math.Clamp(index, 0, count - 1);
        var low = -1f + 2f * index / count;
        var high = -1f + 2f * (index + 1) / count;

        var result = new MeshData();
        //Welded, not one copy of the corners per clipped polygon. The template this is cut from is a
        //welded fan and shades smoothly; emitting every clipped polygon with its own vertices gave
        //each triangle its own normal, so a segment came out faceted - which is what the joints were
        //being blamed for, the geometry either side of one agreeing to a hundredth of a millimetre.
        //Clipping lands neighbouring polygons on bitwise identical positions (the same source vertex,
        //or the same intersection of the same edge with the same plane), so exact keys are enough and
        //a tolerance would risk welding the two margins of a blade tip together.
        var welded = WeldBuffer;
        welded.Clear();
        var vertices = whole.Positions;
        Span<Vector3> polygon = stackalloc Vector3[8];
        Span<Vector3> scratch = stackalloc Vector3[8];

        for (int t = 0; t + 2 < whole.Indices.Count; t += 3)
        {
            polygon[0] = vertices[whole.Indices[t]];
            polygon[1] = vertices[whole.Indices[t + 1]];
            polygon[2] = vertices[whole.Indices[t + 2]];
            var n = 3;

            if (index > 0)
                n = ClipHalfSpace(polygon, n, scratch, low, keepAbove: true);
            if (n >= 3 && index < count - 1)
                n = ClipHalfSpace(polygon, n, scratch, high, keepAbove: false);
            if (n < 3)
                continue;

            //A slice spans 2/count of the blade and is drawn against its own half length, so the
            //cut window maps back onto the full [-1, 1] the template is authored in.
            for (int i = 0; i < n; ++i)
            {
                var p = polygon[i];
                polygon[i] = new Vector3((p.X - low) * count - 1f, p.Y, p.Z);
            }

            //Clipping routinely produces slivers where a triangle only grazes the cut, and a
            //zero-area face contributes nothing but a NaN normal. The surviving faces are found
            //before anything is appended, so a polygon that is all sliver leaves no orphan
            //vertices behind either.
            var kept = 0;
            for (int i = 1; i + 1 < n; ++i)
                if (Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]).LengthSquared() >= 1e-18f)
                    ++kept;
            if (kept == 0)
                continue;

            Span<int> ids = stackalloc int[8];
            for (int i = 0; i < n; ++i)
            {
                if (!welded.TryGetValue(polygon[i], out var id))
                {
                    id = result.Positions.Count;
                    result.Positions.Add(polygon[i]);
                    welded.Add(polygon[i], id);
                }
                ids[i] = id;
            }

            for (int i = 1; i + 1 < n; ++i)
            {
                if (Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]).LengthSquared() < 1e-18f)
                    continue;
                result.Indices.Add(ids[0]);
                result.Indices.Add(ids[i]);
                result.Indices.Add(ids[i + 1]);
            }
        }

        return result.Indices.Count >= 3 ? result : new MeshData();
    }

    /// <summary>
    /// Where an edge crosses the plane, computed so that the answer does not depend on which way
    /// the edge is walked.
    /// </summary>
    /// <remarks>
    /// This is what makes the weld actually weld. The template is a fan, so every interior edge is
    /// shared by two triangles that traverse it in OPPOSITE directions, and Lerp(a, b, k) and
    /// Lerp(b, a, 1-k) are not the same float. Clipping each triangle independently therefore
    /// produced two vertices a few ulps apart at every crossing; an exact-key weld kept both, and
    /// the surface was left full of T-junctions - hairline cracks along every cut, which is what a
    /// blade's internodes were showing. Ordering the endpoints canonically first makes the two
    /// agree bit for bit, and pinning X to the plane removes the residual error in that component.
    /// </remarks>
    static Vector3 Intersect(Vector3 a, Vector3 b, float x)
    {
        if (b.X < a.X || (b.X == a.X && (b.Y < a.Y || (b.Y == a.Y && b.Z < a.Z))))
            (a, b) = (b, a);

        var span = b.X - a.X;
        //The two ends straddle the plane, so the span cannot be zero; the guard is for the denormal
        //case rather than a real one.
        var k = MathF.Abs(span) > 1e-12f ? (x - a.X) / span : 0f;
        var p = Vector3.Lerp(a, b, Math.Clamp(k, 0f, 1f));
        return new Vector3(x, p.Y, p.Z);
    }

    [ThreadStatic] static Dictionary<Vector3, int>? WeldBufferStorage;
    static Dictionary<Vector3, int> WeldBuffer => WeldBufferStorage ??= new();

    /// <summary>
    /// Sutherland-Hodgman against a plane normal to X, in place. Returns the new vertex count.
    /// </summary>
    static int ClipHalfSpace(Span<Vector3> polygon, int count, Span<Vector3> scratch, float x, bool keepAbove)
    {
        var written = 0;
        for (int i = 0; i < count; ++i)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % count];
            var currentIn = keepAbove ? current.X >= x : current.X <= x;
            var nextIn = keepAbove ? next.X >= x : next.X <= x;

            if (currentIn)
                scratch[written++] = current;

            if (currentIn != nextIn)
                scratch[written++] = Intersect(current, next, x);
        }

        for (int i = 0; i < written; ++i)
            polygon[i] = scratch[i];
        return written;
    }
}
