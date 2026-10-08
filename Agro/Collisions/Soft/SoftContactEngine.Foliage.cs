using System.Numerics;

namespace Agro.Collisions;

public sealed partial class SoftContactEngine
{
    /// <summary>Remembered side of a touching pair: the direction that pushes A off B.</summary>
    internal readonly record struct SoftContactMemory(Vector3 Normal, int FeatureA, int FeatureB, long Seen);
    static readonly bool TraceCrossings = Environment.GetEnvironmentVariable("AGRO_TRACE_SOFT_CROSSINGS") != null;
    static readonly bool TraceDeep = Environment.GetEnvironmentVariable("AGRO_TRACE_SOFT_DEEP") != null;
    static int TracedCrossings, TracedDeep;

    void VisitOwn(PlantFormation2 plant)
    {
        var ag = plant.AG; var work = Work[plant]; var pairs = work.OwnPairs;
        pairs.Clear();
        ag.CollisionBvh.GetOverlappingPairs(pairs, collisionsOnly: false);
        var retained = 0;
        for (int k = 0; k < pairs.Count; ++k)
        {
            var (a, b) = pairs[k];
            if (CollisionWorld.Connected(ag, a, b)) continue;
            pairs[retained++] = ag.GetOrganId(a) > ag.GetOrganId(b) ? (b, a) : (a, b);
        }
        pairs.RemoveRange(retained, pairs.Count - retained);
        // Stable-ID order makes the accumulation independent of the hierarchy's layout.
        pairs.Sort(work.PairOrder);
        CollisionWorld.UniquePairs(pairs);
        for (int k = 0; k < pairs.Count; ++k)
        {
            var (a, b) = pairs[k];
            VisitPair(plant, a, plant, b, work);
        }
    }

    void VisitCross(CrossJob job)
    {
        var pa = job.A.Plant; var pb = job.B.Plant;
        var pairs = job.Pairs;
        pairs.Clear();
        pa.AG.CollisionBvh.GetOverlappingPairs(pb.AG.CollisionBvh, pa.AG.FieldOrigin, pb.AG.FieldOrigin, pairs);
        pairs.Sort(job.PairOrder);
        CollisionWorld.UniquePairs(pairs);
        for (int k = 0; k < pairs.Count; ++k)
        {
            var (i, j) = pairs[k];
            VisitPair(pa, i, pb, j, job.A);
        }
    }

    /// <summary>
    /// One endpoint distance query, a side decision, and a compliance-weighted correction queued
    /// on both bodies. Nothing here is re-evaluated this tick.
    /// </summary>
    void VisitPair(PlantFormation2 pa, int i, PlantFormation2 pb, int j, SoftWork owner)
    {
        var a = pa.AG; var b = pb.AG;
        if (!a.IsCollisionEligiblePublic(i) || !b.IsCollisionEligiblePublic(j)) return;
        ++owner.Candidates;
        var sa = CollisionWorld.Shape(a, i); var sb = CollisionWorld.Shape(b, j);
        var reach = sa.Pose.Radius + sb.Pose.Radius;
        var skin = CollisionSettings.FoliageSkin;
        var band = reach + skin;
        var key = new CollisionWorld.ContactKey(pa, a.GetOrganId(i), pb, b.GetOrganId(j));
        var hasMemory = owner.Contacts.TryGetValue(key, out var memory)
            && memory.Seen >= ContactEpoch - 1 - CollisionSettings.ContactMemoryGraceEpochs;
        // A remembered pair is queried a little beyond the band, so one that has just separated
        // still reports a witness normal and a reversed one can be counted.
        var search = hasMemory ? band + skin : band;
        var stop = pa == pb ? CollisionWorld.CommonAncestor(a, i, j) : -1;
        var hit = PairDistance(sa, sb, search, pa.World.CollisionGeometryToleranceM, owner);
        var touching = hit.Distance < band;
        var weight = 1f;
        if (stop >= 0)
        {
            // Siblings and descendants of one node are exempt right at the junction, where every
            // organ overlaps by construction, and fully weighted twice that far out. The ramp is
            // what keeps the response continuous as a tip grows past the junction region.
            var junction = a.GetContactPose(stop);
            var rAtt = MathF.Max(.002f, 2f * junction.Radius + sa.Pose.Radius + sb.Pose.Radius);
            weight = touching ? AttachmentWeight(junction.Tip, rAtt, hit.A, hit.B) : 0f;
            // Two rods leaving one junction always have their nearest points at that junction,
            // where the exemption is total; sample along the rods instead.
            if (sa.Lamina == null && sb.Lamina == null && weight < 1f
                && TryRodSample(sa.Pose, sb.Pose, junction.Tip, rAtt, band, weight * (band - hit.Distance), out var sampled, out var sampledWeight))
            {
                hit = sampled; weight = sampledWeight; touching = hit.Distance < band;
            }
        }
        if (!touching)
        {
            if (hasMemory && hit.Distance < search && Vector3.Dot(hit.Normal, memory.Normal) < -.5f) ++owner.SuspectedTunnels;
            return;
        }
        ++owner.ContactsCount;
        // Overlap inside the attachment exemption is by construction (every organ of a node
        // shares the junction) and is not reported; only weighted pairs count as penetration.
        var residual = weight > 0f ? MathF.Max(0f, reach - hit.Distance) : 0f;
        if (residual > 0f)
        {
            ++owner.PenetrationCount;
            owner.PenetrationSum += residual;
            owner.MaxPenetration = MathF.Max(owner.MaxPenetration, residual);
            if (residual > CollisionSettings.DeepPenetrationThreshold)
            {
                ++owner.DeepPenetrations;
                if (TraceDeep && Interlocked.Increment(ref TracedDeep) <= 400)
                    Console.WriteLine($"SOFT_DEEP t={pa.World.Timestep} {i}:{a.GetOrgan(i)} id={a.GetOrganId(i)} L={a.GetLength(i):F4} / {j}:{b.GetOrgan(j)} id={b.GetOrganId(j)} L={b.GetLength(j):F4} d={hit.Distance * 1000:F3}mm reach={reach * 1000:F3}mm w={weight:F2} stop={stop} fA={hit.FeatureA} fB={hit.FeatureB}");
            }
        }
        var normal = SideNormal(sa, sb, a, i, b, j, hit, hasMemory, memory.Normal, reach, search, owner, out var crossing);
        owner.Contacts[key] = new(normal, hit.FeatureA, hit.FeatureB, ContactEpoch);
        if (weight <= 0f) return;
        var depth = band - hit.Distance;
        if (crossing || hit.Distance < 1e-7f)
        {
            // The witnesses sit on the wrong side or coincide: measure how far the deepest
            // material of either body reaches past the other's witness instead.
            depth = MathF.Max(depth, band - Vector3.Dot(sa.Support(normal) - hit.B, normal));
            depth = MathF.Max(depth, band - Vector3.Dot(hit.A - sb.Support(-normal), normal));
        }
        depth *= CollisionSettings.ContactRelaxation * weight;
        if (depth <= 0f) return;
        var cap = CollisionSettings.MaxCorrectionDisplacement > 0f ? CollisionSettings.MaxCorrectionDisplacement : float.PositiveInfinity;
        // Split by mobility: a scape against a petiole moves the petiole, a leaf against a leaf
        // moves both. The walk stops at the common ancestor, so shared supports never move.
        var ma = a.ContactMobility(i, hit.A, normal, stop);
        var mb = b.ContactMobility(j, hit.B, -normal, stop);
        var shared = a.SharedShapeMobility(stop, i, hit.A, j, hit.B, normal);
        var sum = ma + mb + shared;
        if (sum > 1e-20 && double.IsFinite(sum))
        {
            if (ma > 0) a.QueueContactCorrection(i, hit.A, normal, (float)(depth * ma / sum), stop, cap);
            if (mb > 0) b.QueueContactCorrection(j, hit.B, -normal, (float)(depth * mb / sum), stop, cap);
            if (shared > 0) a.QueueSharedShapeCorrection(stop, i, hit.A, j, hit.B, normal, (float)(depth / sum));
        }
        else
        {
            a.QueueContactCorrection(i, hit.A, normal, depth * .5f, stop, cap);
            b.QueueContactCorrection(j, hit.B, -normal, depth * .5f, stop, cap);
        }
        // A pair that stays deeply overlapped is pressed as far as its joints bend: the normal
        // correction is swallowed by the cumulative elastic limit every tick. Two real blades in
        // that state slide along each other until an edge clears, so add a tangential step that
        // separates the bodies' centres. Stateless on purpose: it keys on the residual overlap,
        // not on history, and the usual caps bound it.
        if (residual > CollisionSettings.DeepPenetrationThreshold * .5f)
        {
            var apart = (sa.Pose.Base + sa.Pose.Tip - sb.Pose.Base - sb.Pose.Tip) * .5f;
            var tangent = apart - normal * Vector3.Dot(apart, normal);
            if (tangent.LengthSquared() < 1e-12f) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            tangent = Vector3.Normalize(tangent);
            var ta = a.ContactMobility(i, hit.A, tangent, stop);
            var tb = b.ContactMobility(j, hit.B, -tangent, stop);
            var tangentSum = ta + tb;
            if (tangentSum > 1e-20 && double.IsFinite(tangentSum))
            {
                var slide = residual * CollisionSettings.ContactRelaxation * weight;
                if (ta > 0) a.QueueContactCorrection(i, hit.A, tangent, (float)(slide * ta / tangentSum), stop, cap);
                if (tb > 0) b.QueueContactCorrection(j, hit.B, -tangent, (float)(slide * tb / tangentSum), stop, cap);
                ++owner.Slides;
            }
        }
    }

    /// <summary>
    /// The direction that pushes A off B. While a pair stays in contact its remembered side wins:
    /// sliding around a finite edge turns the witness normal gradually and the memory follows it
    /// every tick, whereas passing through a sheet reverses it in one tick. A pair found already
    /// overlapping with no history takes its side from where the bodies were last tick.
    /// </summary>
    Vector3 SideNormal(in ContactShape sa, in ContactShape sb, PlantSubFormation<AboveGroundAgent> a, int i,
        PlantSubFormation<AboveGroundAgent> b, int j, in FoliageContact hit, bool hasMemory, Vector3 remembered,
        float reach, float search, SoftWork owner, out bool crossing)
    {
        crossing = false;
        var resolved = hit.Distance >= FoliageGeometry.Skin;
        if (hasMemory)
        {
            // A reversal, not merely a rotation: sliding off a blade's face onto its edge turns
            // the witness normal by about a right angle in one tick and is legitimate, whereas a
            // sheet that was passed through reverses it.
            if (Vector3.Dot(hit.Normal, remembered) < -.5f)
            {
                if (resolved)
                {
                    crossing = true; ++owner.Crossings;
                    if (TraceCrossings && Interlocked.Increment(ref TracedCrossings) <= 400)
                        Console.WriteLine($"SOFT_CROSSING t={a.Plant.World.Timestep} {i}:{a.GetOrgan(i)} id={a.GetOrganId(i)} L={a.GetLength(i):F4} / {j}:{b.GetOrgan(j)} id={b.GetOrganId(j)} L={b.GetLength(j):F4} d={hit.Distance * 1000:F3}mm reach={reach * 1000:F3}mm dot={Vector3.Dot(hit.Normal, remembered):F3} fA={hit.FeatureA} fB={hit.FeatureB}");
                }
                return remembered;
            }
            return resolved ? hit.Normal : remembered;
        }
        if (hit.Distance < reach - FoliageGeometry.Skin
            && i < a.PreviousGeometry.Length && j < b.PreviousGeometry.Length
            && a.PreviousGeometry[i].Exists && b.PreviousGeometry[j].Exists)
        {
            ++owner.NewSides;
            var previous = FoliageGeometry.DistanceUncached(sa with { Pose = a.PreviousGeometry[i].Pose },
                sb with { Pose = b.PreviousGeometry[j].Pose }, search);
            if (previous.Distance >= FoliageGeometry.Skin && previous.Distance < search)
            {
                if (!resolved) return previous.Normal;
                if (Vector3.Dot(hit.Normal, previous.Normal) < 0f) { crossing = true; ++owner.Crossings; return previous.Normal; }
                return hit.Normal;
            }
        }
        if (resolved) return hit.Normal;
        // Coincident surfaces with no history: the organ bases say which side is which.
        return Vector3.Dot(hit.Normal, sa.Pose.Base - sb.Pose.Base) < 0f ? -hit.Normal : hit.Normal;
    }

    /// <summary>Endpoint distance with the certified engine's level-of-detail rejection prefilter, uncached.</summary>
    static FoliageContact PairDistance(in ContactShape a, in ContactShape b, float search, float tolerance, SoftWork work)
    {
        float ea = 0, eb = 0;
        var ta = a.Lamina?.GetCollisionApproximation(a.Pose, a.Pose, tolerance, out ea);
        var tb = b.Lamina?.GetCollisionApproximation(b.Pose, b.Pose, tolerance, out eb);
        if (ta != a.Lamina || tb != b.Lamina)
        {
            ++work.ApproximationQueries;
            var inflated = search + ea + eb + FoliageGeometry.Skin;
            var coarse = FoliageGeometry.DistanceUncached(a with { Lamina = ta }, b with { Lamina = tb }, inflated);
            if (coarse.Distance >= inflated)
            {
                ++work.ApproximationSeparations;
                return new(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
            }
        }
        var hit = FoliageGeometry.DistanceUncached(a, b, search);
        // Outside the search radius the witnesses are meaningless; normalise them so no decision
        // can depend on them.
        return hit.Distance >= search ? new(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1) : hit;
    }

    static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Zero within the attachment radius of the junction, one beyond twice that radius.</summary>
    static float AttachmentWeight(Vector3 junction, float rAtt, Vector3 a, Vector3 b)
        => SmoothStep(rAtt, 2f * rAtt, MathF.Min(Vector3.Distance(a, junction), Vector3.Distance(b, junction)));

    /// <summary>
    /// For two rods sharing a junction: the nearest points at the exemption edge and at each tip,
    /// against the other rod's axis, scored by weighted overlap. Continuous in the poses, unlike
    /// clipping the centre lines and re-applying the radii.
    /// </summary>
    static bool TryRodSample(in OrganPose a, in OrganPose b, Vector3 junction, float rAtt, float band,
        float currentScore, out FoliageContact hit, out float weight)
    {
        hit = default; weight = 0f;
        var best = currentScore;
        Span<Vector3> points = stackalloc Vector3[4];
        var count = 0;
        SamplePoints(a, 2f * rAtt, points, ref count);
        var countA = count;
        SamplePoints(b, 2f * rAtt, points, ref count);
        for (int k = 0; k < count; ++k)
        {
            var onA = k < countA;
            var p = points[k];
            var q = ClosestPointOnSegment(p, onA ? b.Base : a.Base, onA ? b.Tip : a.Tip);
            var dist = Vector3.Distance(p, q);
            var w = SmoothStep(rAtt, 2f * rAtt, MathF.Min(Vector3.Distance(p, junction), Vector3.Distance(q, junction)));
            var score = w * (band - dist);
            if (score <= best) continue;
            best = score; weight = w;
            var normal = dist > 1e-9f ? (onA ? p - q : q - p) / dist : Vector3.UnitY;
            hit = onA ? new(p, q, normal, dist, -1, -1) : new(q, p, normal, dist, -1, -1);
        }
        return weight > 0f;
    }

    static void SamplePoints(in OrganPose rod, float arc, Span<Vector3> points, ref int count)
    {
        var axis = rod.Tip - rod.Base;
        var length = axis.Length();
        if (length <= 1e-9f) { points[count++] = rod.Tip; return; }
        if (arc < length) points[count++] = rod.Base + axis * (arc / length);
        points[count++] = rod.Tip;
    }

    static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 s0, Vector3 s1)
    {
        var d = s1 - s0;
        var l2 = d.LengthSquared();
        if (l2 <= 1e-18f) return s0;
        var t = Math.Clamp(Vector3.Dot(p - s0, d) / l2, 0f, 1f);
        return s0 + d * t;
    }
}
