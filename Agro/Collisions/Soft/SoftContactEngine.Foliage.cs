using System.Numerics;

namespace Agro.Collisions;

public sealed partial class SoftContactEngine
{
    /// <summary>Remembered side of a touching pair: the direction that pushes A off B.</summary>
    internal readonly record struct SoftContactMemory(Vector3 Normal, int FeatureA, int FeatureB, long Seen);
    static readonly bool TraceCrossings = Environment.GetEnvironmentVariable("AGRO_TRACE_SOFT_CROSSINGS") != null;
    static readonly bool TraceDeep = Environment.GetEnvironmentVariable("AGRO_TRACE_SOFT_DEEP") != null;
    static readonly bool TracePairCost = Environment.GetEnvironmentVariable("AGRO_TRACE_SOFT_PAIRCOST") != null;
    static int TracedCrossings, TracedDeep;

    /// <summary>Per-pair counters, accumulated per chunk so the totals do not depend on how the
    /// chunks were spread over threads.</summary>
    struct PairStats
    {
        public long Candidates, Contacts, NewSides, Crossings, SuspectedTunnels, DeepPenetrations, PenetrationCount, StaticPairs, StaticTouching;
        public long BoundsTicks, ConvexTicks, ExactTicks, SampledTicks, BoundsOut, ConvexOut, Exact, Sampled, TouchingTicks, SeparatedTicks;
        public long ApproximationQueries, ApproximationSeparations;
        public double PenetrationSum;
        public float MaxPenetration;
        public void Add(in PairStats o)
        {
            Candidates += o.Candidates; Contacts += o.Contacts; NewSides += o.NewSides; Crossings += o.Crossings; StaticPairs += o.StaticPairs; StaticTouching += o.StaticTouching;
            BoundsTicks += o.BoundsTicks; ConvexTicks += o.ConvexTicks; ExactTicks += o.ExactTicks; SampledTicks += o.SampledTicks; BoundsOut += o.BoundsOut; ConvexOut += o.ConvexOut; Exact += o.Exact; Sampled += o.Sampled; TouchingTicks += o.TouchingTicks; SeparatedTicks += o.SeparatedTicks;
            SuspectedTunnels += o.SuspectedTunnels; DeepPenetrations += o.DeepPenetrations; PenetrationCount += o.PenetrationCount;
            ApproximationQueries += o.ApproximationQueries; ApproximationSeparations += o.ApproximationSeparations;
            PenetrationSum += o.PenetrationSum; MaxPenetration = MathF.Max(MaxPenetration, o.MaxPenetration);
        }
    }

    /// <summary>What one pair's geometric evaluation yields; the response is queued from it afterwards.</summary>
    struct PairRecord
    {
        public int I, J, Stop;
        public FoliageContact Hit;
        public Vector3 Normal;
        public float Weight, Depth, Residual;
        public bool Touching, Crossing;
        public CollisionWorld.ContactKey Key;
    }

    /// <summary>Pairs per chunk of the parallel own-pair evaluation. Fixed, so the chunking - and
    /// with it the order in which the per-chunk counters are summed - never depends on the worker cap.</summary>
    const int OwnPairChunk = 32;

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
        // Stable-ID order makes the accumulation independent of the hierarchy's layout. The keys
        // are materialised once: a delegate comparison that looked both ids up per compare was
        // most of the serial time left in a single plant's tick.
        SortByOrganIds(ag, pairs, ref work.SortKeys);
        CollisionWorld.UniquePairs(pairs);
        var n = pairs.Count;
        var chunks = (n + OwnPairChunk - 1) / OwnPairChunk;
        if (work.ChunkStats.Length < chunks) Array.Resize(ref work.ChunkStats, chunks);
        if (ChunkOwnPairs && chunks >= 2)
        {
            // Few plants, many pairs: the geometry of this plant's pairs - the expensive half - is
            // evaluated in parallel over fixed chunks, and only the response, which writes into the
            // plant, is replayed serially in pair order. Bit-identical to the serial path.
            if (work.Records.Length < n) Array.Resize(ref work.Records, Math.Max(n, work.Records.Length * 2));
            var records = work.Records; var stats = work.ChunkStats; var epoch = ContactEpoch;
            var evalStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (work.ChunkTicks.Length < chunks) { work.ChunkTicks = new long[chunks]; work.WorstPairTicks = new long[chunks]; work.WorstPairIndex = new int[chunks]; }
            var chunkTicks = work.ChunkTicks; var gc0 = GC.CollectionCount(0);
            Parallel.For(0, chunks, c =>
            {
                var chunkStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                var local = default(PairStats);
                var last = Math.Min(n, (c + 1) * OwnPairChunk);
                long worst = 0; int worstK = -1;
                var b0 = FoliageGeometry.StageBoundsTicks; var c0 = FoliageGeometry.StageConvexTicks; var e0 = FoliageGeometry.StageExactTicks; var s0 = FoliageGeometry.StageSampledTicks;
                var bo0 = FoliageGeometry.StageBoundsOut; var co0 = FoliageGeometry.StageConvexOut; var ex0 = FoliageGeometry.StageExact; var sm0 = FoliageGeometry.StageSampled;
                for (int k = c * OwnPairChunk; k < last; ++k)
                {
                    var pairStarted = TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    var (a, b) = pairs[k];
                    EvaluatePair(plant, a, plant, b, epoch, work.Contacts, ref local, out records[k]);
                    if (TraceTiming)
                    {
                        var pt = System.Diagnostics.Stopwatch.GetTimestamp() - pairStarted;
                        if (records[k].Touching) local.TouchingTicks += pt; else local.SeparatedTicks += pt;
                        if (TracePairCost && pt > worst) { worst = pt; worstK = k; }
                    }
                }
                local.BoundsTicks += FoliageGeometry.StageBoundsTicks - b0; local.ConvexTicks += FoliageGeometry.StageConvexTicks - c0; local.ExactTicks += FoliageGeometry.StageExactTicks - e0; local.SampledTicks += FoliageGeometry.StageSampledTicks - s0;
                local.BoundsOut += FoliageGeometry.StageBoundsOut - bo0; local.ConvexOut += FoliageGeometry.StageConvexOut - co0; local.Exact += FoliageGeometry.StageExact - ex0; local.Sampled += FoliageGeometry.StageSampled - sm0;
                stats[c] = local;
                chunkTicks[c] = System.Diagnostics.Stopwatch.GetTimestamp() - chunkStarted;
                if (TracePairCost) { work.WorstPairTicks[c] = worst; work.WorstPairIndex[c] = worstK; }
            });
            var evaluated = System.Diagnostics.Stopwatch.GetTimestamp();
            work.LastEvalTicks = evaluated - evalStarted; work.LastChunks = chunks;
            long cmax = 0, csum = 0; for (int c = 0; c < chunks; ++c) { cmax = Math.Max(cmax, chunkTicks[c]); csum += chunkTicks[c]; }
            work.LastChunkMaxTicks = cmax; work.LastChunkSumTicks = csum; work.LastGcs = GC.CollectionCount(0) - gc0;
            if (TracePairCost && plant.World.Timestep % 50 == 0)
            {
                var order = Enumerable.Range(0, chunks).OrderByDescending(c => work.WorstPairTicks[c]).Take(6);
                foreach (var c in order)
                {
                    var k = work.WorstPairIndex[c]; if (k < 0) continue; var (a, b) = pairs[k];
                    var sa = CollisionWorld.Shape(ag, a); var sb = CollisionWorld.Shape(ag, b);
                    Console.WriteLine($"SOFT_PAIRCOST t={plant.World.Timestep} {work.WorstPairTicks[c] * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F3}ms {ag.GetOrgan(a)}/{ag.GetOrgan(b)} lamina={sa.Lamina != null}/{sb.Lamina != null} tri={sa.Lamina?.Indices.Length / 3}/{sb.Lamina?.Indices.Length / 3} L={ag.GetLength(a) * 100:F1}/{ag.GetLength(b) * 100:F1}cm d={records[k].Hit.Distance * 1000:F2}mm touching={records[k].Touching} chunkMs={chunkTicks[c] * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F2}");
                }
            }
            for (int c = 0; c < chunks; ++c) work.Stats.Add(stats[c]);
            for (int k = 0; k < n; ++k)
                if (records[k].Touching) RespondPair(plant, plant, in records[k], work);
            work.LastRespondTicks = System.Diagnostics.Stopwatch.GetTimestamp() - evaluated;
        }
        else
        {
            for (int c = 0; c < chunks; ++c)
            {
                var local = default(PairStats);
                var last = Math.Min(n, (c + 1) * OwnPairChunk);
                for (int k = c * OwnPairChunk; k < last; ++k)
                {
                    var (a, b) = pairs[k];
                    if (EvaluatePair(plant, a, plant, b, ContactEpoch, work.Contacts, ref local, out var record))
                        RespondPair(plant, plant, in record, work);
                }
                work.Stats.Add(local);
            }
        }
    }

    void VisitCross(CrossJob job)
    {
        var pa = job.A.Plant; var pb = job.B.Plant;
        var pairs = job.Pairs;
        pairs.Clear();
        pa.AG.CollisionBvh.GetOverlappingPairs(pb.AG.CollisionBvh, pa.AG.FieldOrigin, pb.AG.FieldOrigin, pairs);
        SortByOrganIds(pa.AG, pb.AG, pairs, ref job.SortKeys);
        CollisionWorld.UniquePairs(pairs);
        var local = default(PairStats);
        for (int k = 0; k < pairs.Count; ++k)
        {
            var (i, j) = pairs[k];
            if (EvaluatePair(pa, i, pb, j, ContactEpoch, job.A.Contacts, ref local, out var record))
                RespondPair(pa, pb, in record, job.A);
        }
        job.A.Stats.Add(local);
    }

    /// <summary>
    /// One endpoint distance query and a side decision. Reads the two plants, last tick's memory
    /// and the previous poses; writes nothing into either plant, so any number of these can run
    /// at once. Returns whether the pair touches, in which case <paramref name="record"/> holds
    /// what <see cref="RespondPair"/> needs.
    /// </summary>
    static bool EvaluatePair(PlantFormation2 pa, int i, PlantFormation2 pb, int j, long epoch,
        Dictionary<CollisionWorld.ContactKey, SoftContactMemory> memories, ref PairStats stats, out PairRecord record)
    {
        record = default;
        var a = pa.AG; var b = pb.AG;
        if (!a.IsCollisionEligiblePublic(i) || !b.IsCollisionEligiblePublic(j)) return false;
        ++stats.Candidates;
        var sa = CollisionWorld.Shape(a, i); var sb = CollisionWorld.Shape(b, j);
        if (sa.Lamina != null && sa.Pose.Length < CollisionSettings.SoftTinyLaminaLength) sa = AsCapsule(sa);
        if (sb.Lamina != null && sb.Pose.Length < CollisionSettings.SoftTinyLaminaLength) sb = AsCapsule(sb);
        var reach = sa.Pose.Radius + sb.Pose.Radius;
        var skin = CollisionSettings.FoliageSkin;
        var band = reach + skin;
        var key = new CollisionWorld.ContactKey(pa, a.GetOrganId(i), pb, b.GetOrganId(j));
        var hasMemory = memories.TryGetValue(key, out var memory)
            && memory.Seen >= epoch - 1 - CollisionSettings.ContactMemoryGraceEpochs;
        // A remembered pair is queried a little beyond the band, so one that has just separated
        // still reports a witness normal and a reversed one can be counted.
        var search = hasMemory ? band + skin : band;
        var stop = pa == pb ? CollisionWorld.CommonAncestor(a, i, j) : -1;
        var hit = PairDistance(sa, sb, search, pa.World.CollisionGeometryToleranceM, ref stats);
        var touching = hit.Distance < band;
        if (i < a.PreviousGeometry.Length && j < b.PreviousGeometry.Length && a.PreviousGeometry[i].Exists && b.PreviousGeometry[j].Exists
            && a.PreviousGeometry[i].Pose == CollisionWorld.Shape(a, i).Pose && b.PreviousGeometry[j].Pose == CollisionWorld.Shape(b, j).Pose)
        { ++stats.StaticPairs; if (touching) ++stats.StaticTouching; }
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
            if (hasMemory && hit.Distance < search && Vector3.Dot(hit.Normal, memory.Normal) < -.5f) ++stats.SuspectedTunnels;
            return false;
        }
        ++stats.Contacts;
        // Overlap inside the attachment exemption is by construction (every organ of a node
        // shares the junction) and is not reported; only weighted pairs count as penetration.
        var residual = weight > 0f ? MathF.Max(0f, reach - hit.Distance) : 0f;
        if (residual > 0f)
        {
            ++stats.PenetrationCount;
            stats.PenetrationSum += residual;
            stats.MaxPenetration = MathF.Max(stats.MaxPenetration, residual);
            if (residual > CollisionSettings.DeepPenetrationThreshold)
            {
                ++stats.DeepPenetrations;
                if (TraceDeep && Interlocked.Increment(ref TracedDeep) <= 400)
                    Console.WriteLine($"SOFT_DEEP t={pa.World.Timestep} {i}:{a.GetOrgan(i)} id={a.GetOrganId(i)} L={a.GetLength(i):F4} / {j}:{b.GetOrgan(j)} id={b.GetOrganId(j)} L={b.GetLength(j):F4} d={hit.Distance * 1000:F3}mm reach={reach * 1000:F3}mm w={weight:F2} stop={stop} fA={hit.FeatureA} fB={hit.FeatureB}");
            }
        }
        var normal = SideNormal(sa, sb, a, i, b, j, hit, hasMemory, memory.Normal, reach, search, ref stats, out var crossing);
        var depth = 0f;
        if (weight > 0f)
        {
            depth = band - hit.Distance;
            if (crossing || hit.Distance < 1e-7f)
            {
                // The witnesses sit on the wrong side or coincide: measure how far the deepest
                // material of either body reaches past the other's witness instead.
                depth = MathF.Max(depth, band - Vector3.Dot(sa.Support(normal) - hit.B, normal));
                depth = MathF.Max(depth, band - Vector3.Dot(hit.A - sb.Support(-normal), normal));
            }
            if (CollisionSettings.SoftBandLaw)
            {
                // Gain fades to zero at the band edge: a pair settles at the band instead of being
                // pushed across it at full gain and pulled back by gravity next tick. Unchanged at
                // and beyond physical contact, where the deficit is the whole skin or more.
                var x = Math.Clamp((band - hit.Distance) / skin, 0f, 1f);
                depth *= x * (2f - x);
            }
            depth *= CollisionSettings.ContactRelaxation * weight;
        }
        record = new()
        {
            I = i, J = j, Stop = stop, Hit = hit, Normal = normal, Weight = weight, Depth = depth,
            Residual = residual, Touching = true, Crossing = crossing, Key = key,
        };
        return true;
    }

    /// <summary>Sorts the pairs by (stable id of the first organ, stable id of the second): the same
    /// order the delegate comparison produced, from keys looked up once per pair.</summary>
    static void SortByOrganIds(PlantSubFormation<AboveGroundAgent> a, PlantSubFormation<AboveGroundAgent> b, List<(int, int)> pairs, ref (ulong, ulong)[] keys)
    {
        var n = pairs.Count;
        if (keys.Length < n) keys = new (ulong, ulong)[Math.Max(n, keys.Length * 2)];
        var items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pairs);
        for (int k = 0; k < n; ++k) keys[k] = (a.GetOrganId(items[k].Item1), b.GetOrganId(items[k].Item2));
        keys.AsSpan(0, n).Sort(items);
    }
    static void SortByOrganIds(PlantSubFormation<AboveGroundAgent> ag, List<(int, int)> pairs, ref (ulong, ulong)[] keys) => SortByOrganIds(ag, ag, pairs, ref keys);

    /// <summary>A tiny blade as a capsule of its own half width: a rod the distance kernels answer in
    /// under a microsecond, against a template query that could take a millisecond.</summary>
    static ContactShape AsCapsule(in ContactShape s)
        => new(s.Pose with { Width = 0f, Radius = MathF.Max(s.Pose.Radius, s.Pose.Width * .5f), Rounded = true, Deformation = default }, null);

    /// <summary>
    /// The side memory and the compliance-weighted correction queued on both bodies from one
    /// evaluated pair. This is the part that writes into the plants, so for a given plant it runs
    /// on one thread at a time, in pair order.
    /// </summary>
    void RespondPair(PlantFormation2 pa, PlantFormation2 pb, in PairRecord r, SoftWork owner)
    {
        var a = pa.AG; var b = pb.AG;
        var i = r.I; var j = r.J; var stop = r.Stop; var hit = r.Hit; var normal = r.Normal; var weight = r.Weight;
        owner.Contacts[r.Key] = new(normal, hit.FeatureA, hit.FeatureB, ContactEpoch);
        if (weight <= 0f) return;
        var depth = r.Depth;
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
            if (ma > 0) a.QueueContactCorrection(i, hit.A, normal, (float)(depth * ma / sum), stop, cap, ma);
            if (mb > 0) b.QueueContactCorrection(j, hit.B, -normal, (float)(depth * mb / sum), stop, cap, mb);
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
        var residual = r.Residual;
        var slideThreshold = CollisionSettings.DeepPenetrationThreshold * (CollisionSettings.SoftSlideRamp ? .25f : .5f);
        if (residual > slideThreshold)
        {
            var sa = CollisionWorld.Shape(a, i); var sb = CollisionWorld.Shape(b, j);
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
                if (CollisionSettings.SoftSlideRamp)
                    slide *= SmoothStep(slideThreshold, CollisionSettings.DeepPenetrationThreshold * .5f, residual);
                if (ta > 0) a.QueueContactCorrection(i, hit.A, tangent, (float)(slide * ta / tangentSum), stop, cap, ta);
                if (tb > 0) b.QueueContactCorrection(j, hit.B, -tangent, (float)(slide * tb / tangentSum), stop, cap, tb);
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
    static Vector3 SideNormal(in ContactShape sa, in ContactShape sb, PlantSubFormation<AboveGroundAgent> a, int i,
        PlantSubFormation<AboveGroundAgent> b, int j, in FoliageContact hit, bool hasMemory, Vector3 remembered,
        float reach, float search, ref PairStats stats, out bool crossing)
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
                    crossing = true; ++stats.Crossings;
                    if (TraceCrossings && Interlocked.Increment(ref TracedCrossings) <= 400)
                        Console.WriteLine($"SOFT_CROSSING t={a.Plant.World.Timestep} {i}:{a.GetOrgan(i)} id={a.GetOrganId(i)} L={a.GetLength(i):F4} / {j}:{b.GetOrgan(j)} id={b.GetOrganId(j)} L={b.GetLength(j):F4} d={hit.Distance * 1000:F3}mm reach={reach * 1000:F3}mm dot={Vector3.Dot(hit.Normal, remembered):F3} fA={hit.FeatureA} fB={hit.FeatureB}");
                }
                return remembered;
            }
            if (!resolved) return remembered;
            // Follow the witness through a low-pass: face and edge witnesses alternate on a sliding
            // blade and would otherwise turn the push by a right angle from one tick to the next.
            var tau = CollisionSettings.SoftNormalFilterHours;
            if (tau <= 0f) return hit.Normal;
            var alpha = 1f - MathF.Exp(-a.Plant.World.HoursPerTick / tau);
            var filtered = Vector3.Lerp(remembered, hit.Normal, alpha);
            var length = filtered.Length();
            return length > 1e-3f ? filtered / length : hit.Normal;
        }
        if (hit.Distance < reach - FoliageGeometry.Skin
            && i < a.PreviousGeometry.Length && j < b.PreviousGeometry.Length
            && a.PreviousGeometry[i].Exists && b.PreviousGeometry[j].Exists)
        {
            ++stats.NewSides;
            var previous = FoliageGeometry.DistanceUncached(sa with { Pose = a.PreviousGeometry[i].Pose },
                sb with { Pose = b.PreviousGeometry[j].Pose }, search);
            if (previous.Distance >= FoliageGeometry.Skin && previous.Distance < search)
            {
                if (!resolved) return previous.Normal;
                if (Vector3.Dot(hit.Normal, previous.Normal) < 0f) { crossing = true; ++stats.Crossings; return previous.Normal; }
                return hit.Normal;
            }
        }
        if (resolved) return hit.Normal;
        // Coincident surfaces with no history: the organ bases say which side is which.
        return Vector3.Dot(hit.Normal, sa.Pose.Base - sb.Pose.Base) < 0f ? -hit.Normal : hit.Normal;
    }

    /// <summary>Endpoint distance with the certified engine's level-of-detail rejection prefilter, uncached.</summary>
    static FoliageContact PairDistance(in ContactShape a, in ContactShape b, float search, float tolerance, ref PairStats stats)
    {
        float ea = 0, eb = 0;
        var ta = a.Lamina?.GetCollisionApproximation(a.Pose, a.Pose, tolerance, out ea);
        var tb = b.Lamina?.GetCollisionApproximation(b.Pose, b.Pose, tolerance, out eb);
        if (ta != a.Lamina || tb != b.Lamina)
        {
            ++stats.ApproximationQueries;
            var inflated = search + ea + eb + FoliageGeometry.Skin;
            var coarse = FoliageGeometry.DistanceSampled(a with { Lamina = ta }, b with { Lamina = tb }, inflated, CollisionSettings.SoftExactPairBudget);
            if (coarse.Distance >= inflated)
            {
                ++stats.ApproximationSeparations;
                return new(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
            }
        }
        var hit = FoliageGeometry.DistanceSampled(a, b, search, CollisionSettings.SoftExactPairBudget);
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
