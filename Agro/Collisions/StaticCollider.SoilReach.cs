using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    /// <summary>
    /// How far above the soil an organ has to stay before no soil query can find it, beyond its own
    /// radius: the 5 mm band <see cref="Contact"/> searches, the contact tolerance and the skins
    /// the support and lamina queries add to a radius, and half a millimetre for the rounding of
    /// distances that the queries compute their own way.
    /// </summary>
    const float SoilQueryReach = 0.005f + CollisionSettings.ContactTolerance + 4f * FoliageGeometry.Skin + 0.0005f;

    /// <summary>The flat top of a regular field: every soil triangle lies in one horizontal plane.</summary>
    bool FlatOpenSoil => OpenSoil && SoilTriangles > 0 && SoilMin.Y == SoilMax.Y;

    /// <summary>A height no point of the shape's collision proxy lies below (its surface excluded
    /// for a rod, included for a blade).</summary>
    static float Lowest(in ContactShape shape) => shape.Lamina != null
        ? shape.Bounds.Min.Y
        : MathF.Min(shape.Pose.Base.Y, shape.Pose.Tip.Y) - MathF.Abs(shape.Pose.Across.Y);

    /// <summary>
    /// Whether the shape stands so far above a regular field that a soil contact or support query
    /// about it finds nothing.
    /// </summary>
    /// <remarks>
    /// A crown is metres above the ground, and the solid pass still asked the soil about every
    /// organ several times a tick: the query built its shape, hashed it into a memo and walked to
    /// the hierarchy's root before answering "nothing" - a fifth of a mature tree's tick. On a
    /// regular field the soil is one horizontal face at <see cref="SoilMax"/>, so the vertical gap
    /// to it is a lower bound on every distance those queries measure. When the gap exceeds the
    /// radius plus <see cref="SoilQueryReach"/>, <see cref="ShapeContactUncached"/> and
    /// <see cref="Contact"/> find no triangle in their band and the support queries find none in
    /// theirs, so their answer is the one returned here. Deformed rods are approximated by spans
    /// whose ends this bound does not cover, and imported troughs have walls: both take the
    /// queries as before.
    /// </remarks>
    internal bool AboveSoilReach(in ContactShape shape)
        => FlatOpenSoil && !shape.Pose.CurvedRod && shape.Feature < 0
            && Lowest(shape) - shape.Pose.Radius - SoilQueryReach > SoilMax.Y;

    /// <summary>
    /// Whether the shape, its previous pose and the path between them stay so far above a regular
    /// field that the soil half of the admissibility test passes: no contact now, the previous
    /// base outside the soil, and a clear sweep.
    /// </summary>
    /// <remarks>
    /// The sweep certifies a path against a triangle when both ends keep at least the larger
    /// radius plus the arc bound from its plane (<see cref="SweepTriangle"/>,
    /// <see cref="SweepLaminaTriangle"/>). Against a horizontal face that is a statement about
    /// heights, and it holds whenever the lower of the two poses clears the face by both arc
    /// bounds on top of the contact reach - so the sweep returns clear at its first test for every
    /// soil triangle, which is what this returns without asking.
    /// </remarks>
    internal bool AboveSoilReach(in ContactShape previous, in ContactShape shape)
    {
        if (!FlatOpenSoil || shape.Pose.CurvedRod || previous.Pose.CurvedRod || shape.Feature >= 0 || previous.Feature >= 0)
            return false;
        var soil = SoilMax.Y;
        if (!(previous.Pose.Base.Y > soil) || !(shape.Pose.Base.Y > soil)) return false;
        var reach = MathF.Max(previous.Pose.Radius, shape.Pose.Radius)
            + previous.ArcBound(shape) + ArcBound(previous.Pose, shape.Pose) + SoilQueryReach;
        return MathF.Min(Lowest(previous), Lowest(shape)) - reach > soil;
    }
}
