using System.Numerics;
using System.Runtime.InteropServices;

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
        /// <summary>The two bodies' mobilities along the normal, when they were measured with the
        /// geometry (<see cref="MeasureMobility"/>) instead of by the response.</summary>
        public double MobilityA, MobilityB;
        public bool MobilityKnown;
    }

    /// <summary>Pairs per chunk of the parallel own-pair evaluation. Fixed, so the chunking - and
    /// with it the order in which the per-chunk counters are summed - never depends on the worker cap.</summary>
    const int OwnPairChunk = 32;

    /// <summary>Hierarchy nodes per block of the parallel own-pair search. Small enough that the
    /// few heavy nodes near the root do not decide the balance, large enough to amortise a task.</summary>
    const int OwnPairBlockNodes = 1024;
    [ThreadStatic] static Stack<(int, int)>? OwnPairStack;

    /// <summary>
    /// A plant's own candidate pairs in stable-id order, found and ordered on all cores. Returns
    /// false, leaving the list empty, when the serial search has to be used instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The serial search walked the whole hierarchy on one thread, looked two stable ids up per
    /// pair and then sorted a million pairs by them: a third of a mature tree's foliage pass. The
    /// result it produces is a set in a canonical order - every overlapping, unconnected pair,
    /// oriented and sorted by stable id, once - so any way of producing that set in that order
    /// is the same result.
    /// </para>
    /// <para>
    /// The set: each pair belongs to exactly one node of the hierarchy, so blocks of nodes are
    /// searched independently (<see cref="Bvh.CollectOverlapsUnder"/>). The order: organs are born
    /// with increasing ids and a census keeps the survivors in order, so id order is normally
    /// index order, and sorting pairs by index needs no comparison sort - they are dropped into
    /// one bucket per first organ and each small bucket is sorted by the second. That the ids
    /// ascend strictly with the index is checked on every call, not assumed; when they do not,
    /// the serial search runs exactly as before.
    /// </para>
    /// </remarks>
    /// <summary>Whether the organs' stable ids rise strictly with their index, checked in blocks
    /// side by side, each against the id before it.</summary>
    static bool IdsAscend(PlantSubFormation<AboveGroundAgent> ag, int count)
    {
        var descending = 0;
        Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, count, 8192), range =>
        {
            var previous = range.Item1 > 0 ? ag.GetOrganId(range.Item1 - 1) : 0UL;
            for (int i = range.Item1; i < range.Item2 && Volatile.Read(ref descending) == 0; ++i)
            {
                var id = ag.GetOrganId(i);
                if (id <= previous) { Volatile.Write(ref descending, 1); return; }
                previous = id;
            }
        });
        return descending == 0;
    }

    /// <summary>Ranges of first organs over which the found pairs are ordered side by side.</summary>
    const int OwnPairRanges = 64;

    /// <summary>
    /// The pairs the blocks found, as the own-pair list: by first organ, each organ's partners
    /// in ascending order and once each.
    /// </summary>
    /// <remarks>
    /// The result is fixed by the set of pairs alone, so it can be put together in any order of
    /// work: each block counts its pairs per range of first organs, a prefix over (range, block)
    /// gives each block a slice of every range, the blocks place their pairs, and each range then
    /// counts, places, sorts and deduplicates its organs' partners and writes them out after the
    /// ranges before it.
    /// </remarks>
    static void OrderOwnPairs(SoftWork work, int count, int blocks)
    {
        const int ranges = OwnPairRanges;
        var lists = work.BlockPairs;
        if (work.BlockRangeAt.Length < blocks * ranges) work.BlockRangeAt = new int[Math.Max(blocks * ranges, work.BlockRangeAt.Length * 3 / 2)];
        if (work.BucketStart.Length < count + 1)
        {
            work.BucketStart = new int[Math.Max(count + 1, work.BucketStart.Length * 2)];
            work.BucketFill = new int[work.BucketStart.Length];
        }
        var at = work.BlockRangeAt;
        CountPairsPerRange(work, count, blocks);
        var total = 0;
        for (int r = 0; r < ranges; ++r)
        {
            work.RangeStart[r] = total;
            for (int b = 0; b < blocks; ++b) { var n = at[b * ranges + r]; at[b * ranges + r] = total; total += n; }
        }
        work.RangeStart[ranges] = total;
        if (work.StagedPairs.Length < total) work.StagedPairs = new (int, int)[Math.Max(total, work.StagedPairs.Length * 3 / 2)];
        if (work.Seconds.Length < total) work.Seconds = new int[Math.Max(total, work.Seconds.Length * 2)];
        PlacePairsByRange(work, count, blocks);
        SortRanges(work, count);
        var unique = 0;
        for (int r = 0; r < ranges; ++r) { var n = work.RangeUnique[r]; work.RangeUnique[r] = unique; unique += n; }
        CollectionsMarshal.SetCount(work.OwnPairs, unique);
        WriteRanges(work, count);
    }

    static int RangeOf(int organ, int count) => (int)((long)organ * OwnPairRanges / count);
    static int RangeFirst(int range, int count) => (int)(((long)range * count + OwnPairRanges - 1) / OwnPairRanges);

    static void CountPairsPerRange(SoftWork work, int count, int blocks) => Parallel.For(0, blocks, b =>
    {
        var at = work.BlockRangeAt.AsSpan(b * OwnPairRanges, OwnPairRanges);
        at.Clear();
        foreach (var (a, _) in CollectionsMarshal.AsSpan(work.BlockPairs[b])) ++at[RangeOf(a, count)];
    });

    static void PlacePairsByRange(SoftWork work, int count, int blocks) => Parallel.For(0, blocks, b =>
    {
        var at = work.BlockRangeAt.AsSpan(b * OwnPairRanges, OwnPairRanges); var staged = work.StagedPairs;
        foreach (var pair in CollectionsMarshal.AsSpan(work.BlockPairs[b])) staged[at[RangeOf(pair.Item1, count)]++] = pair;
    });

    static void SortRanges(SoftWork work, int count) => Parallel.For(0, OwnPairRanges, r =>
    {
        var first = RangeFirst(r, count); var last = RangeFirst(r + 1, count);
        var start = work.BucketStart; var fill = work.BucketFill; var seconds = work.Seconds; var staged = work.StagedPairs;
        var from = work.RangeStart[r]; var to = work.RangeStart[r + 1];
        for (int i = first; i < last; ++i) fill[i] = 0;
        for (int k = from; k < to; ++k) ++fill[staged[k].Item1];
        var next = from;
        for (int i = first; i < last; ++i) { start[i] = next; next += fill[i]; fill[i] = start[i]; }
        for (int k = from; k < to; ++k) seconds[fill[staged[k].Item1]++] = staged[k].Item2;
        var unique = 0;
        for (int i = first; i < last; ++i)
        {
            var bucket = start[i]; var length = fill[i] - bucket;
            if (length > 1) Array.Sort(seconds, bucket, length);
            var kept = Math.Min(length, 1);
            for (int k = 1; k < length; ++k)
                if (seconds[bucket + k] != seconds[bucket + kept - 1]) seconds[bucket + kept++] = seconds[bucket + k];
            fill[i] = kept; unique += kept;
        }
        work.RangeUnique[r] = unique;
    });

    static void WriteRanges(SoftWork work, int count) => Parallel.For(0, OwnPairRanges, r =>
    {
        var first = RangeFirst(r, count); var last = RangeFirst(r + 1, count);
        var output = CollectionsMarshal.AsSpan(work.OwnPairs); var at = work.RangeUnique[r];
        for (int i = first; i < last; ++i)
        {
            var bucket = work.BucketStart[i];
            for (int k = 0; k < work.BucketFill[i]; ++k) output[at++] = (i, work.Seconds[bucket + k]);
        }
    });

    bool CollectOwnPairsParallel(PlantSubFormation<AboveGroundAgent> ag, SoftWork work)
    {
        var bvh = ag.CollisionBvh; var nodes = bvh.NodeCount; var count = ag.Count;
        var blocks = (nodes + OwnPairBlockNodes - 1) / OwnPairBlockNodes;
        if (blocks < 4) return false;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!IdsAscend(ag, count)) return false;
        if (work.BlockPairs.Length < blocks)
        {
            var old = work.BlockPairs.Length;
            Array.Resize(ref work.BlockPairs, blocks);
            for (int k = old; k < blocks; ++k) work.BlockPairs[k] = [];
        }
        var lists = work.BlockPairs;
        // Index order is id order here, and the search emits (lower index, higher index).
        Parallel.For(0, blocks, block =>
        {
            var found = lists[block]; found.Clear();
            bvh.CollectOverlapsUnder(block * OwnPairBlockNodes, Math.Min(nodes, (block + 1) * OwnPairBlockNodes), OwnPairStack ??= new(), found);
            var items = CollectionsMarshal.AsSpan(found);
            var retained = 0;
            for (int k = 0; k < items.Length; ++k)
                if (!CollisionWorld.Connected(ag, items[k].Item1, items[k].Item2)) items[retained++] = items[k];
            found.RemoveRange(retained, found.Count - retained);
        });
        var discovered = System.Diagnostics.Stopwatch.GetTimestamp();
        OrderOwnPairs(work, count, blocks);
        work.LastDiscoverTicks = discovered - started; work.LastFilterTicks = 0;
        work.LastSortTicks = System.Diagnostics.Stopwatch.GetTimestamp() - discovered;
        return true;
    }

    void VisitOwn(PlantFormation2 plant)
    {
        var ag = plant.AG; var work = Work[plant]; var pairs = work.OwnPairs;
        pairs.Clear();
        var visitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!(ChunkOwnPairs && CollectOwnPairsParallel(ag, work)))
        {
            ag.CollisionBvh.GetOverlappingPairs(pairs, collisionsOnly: false);
            var discovered = System.Diagnostics.Stopwatch.GetTimestamp();
            var retained = 0;
            for (int k = 0; k < pairs.Count; ++k)
            {
                var (a, b) = pairs[k];
                if (CollisionWorld.Connected(ag, a, b)) continue;
                pairs[retained++] = ag.GetOrganId(a) > ag.GetOrganId(b) ? (b, a) : (a, b);
            }
            pairs.RemoveRange(retained, pairs.Count - retained);
            var filtered = System.Diagnostics.Stopwatch.GetTimestamp();
            // Stable-ID order makes the accumulation independent of the hierarchy's layout. The keys
            // are materialised once: a delegate comparison that looked both ids up per compare was
            // most of the serial time left in a single plant's tick.
            SortByOrganIds(ag, pairs, ref work.SortKeys);
            CollisionWorld.UniquePairs(pairs);
            work.LastDiscoverTicks = discovered - visitStarted; work.LastFilterTicks = filtered - discovered;
            work.LastSortTicks = System.Diagnostics.Stopwatch.GetTimestamp() - filtered;
        }
        var n = pairs.Count;
        var chunks = (n + OwnPairChunk - 1) / OwnPairChunk;
        if (work.ChunkStats.Length < chunks) Array.Resize(ref work.ChunkStats, Math.Max(chunks, work.ChunkStats.Length * 3 / 2));
        if (ChunkOwnPairs && chunks >= 2)
        {
            // Few plants, many pairs: the geometry of this plant's pairs - the expensive half - is
            // evaluated in parallel over fixed chunks, and only the response, which writes into the
            // plant, is replayed serially in pair order. Bit-identical to the serial path.
            if (work.Records.Length < n) Array.Resize(ref work.Records, Math.Max(n, work.Records.Length * 2));
            var records = work.Records; var stats = work.ChunkStats; var epoch = ContactEpoch;
            var evalStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (work.ChunkTicks.Length < chunks) { work.ChunkTicks = new long[chunks]; work.WorstPairTicks = new long[chunks]; work.WorstPairIndex = new int[chunks]; }
            if (work.ChunkTouching.Length < chunks)
            {
                var capacity = Math.Max(chunks, work.ChunkTouching.Length * 3 / 2);
                work.ChunkTouching = new int[capacity]; work.ChunkTouchingMask = new uint[capacity];
            }
            var chunkTouching = work.ChunkTouching; var chunkMask = work.ChunkTouchingMask;
            var chunkTicks = work.ChunkTicks; var gc0 = GC.CollectionCount(0);
            // A plant whose organs only turn: its mobilities do not depend on what is queued.
            var rigid = !ag.HasContactDeformation;
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
                    if (EvaluatePair(plant, a, plant, b, epoch, work.Contacts, ref local, out records[k]) && rigid)
                        MeasureMobility(ag, ref records[k]);
                    if (TraceTiming)
                    {
                        var pt = System.Diagnostics.Stopwatch.GetTimestamp() - pairStarted;
                        if (records[k].Touching) local.TouchingTicks += pt; else local.SeparatedTicks += pt;
                        if (TracePairCost && pt > worst) { worst = pt; worstK = k; }
                    }
                }
                var mask = 0u;
                for (int k = c * OwnPairChunk; k < last; ++k) if (records[k].Touching) mask |= 1u << (k - c * OwnPairChunk);
                chunkMask[c] = mask; chunkTouching[c] = System.Numerics.BitOperations.PopCount(mask);
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
            if (rigid && n >= ParallelReplayMinimum && ag.Count >= TreeCacheData.ParallelWalkMinimum
                && ag.ContactJointsDescribed && Environment.ProcessorCount > 1)
                ReplayOnAllCores(plant, work, chunks);
            else
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
        var first = a.ContactOrganAt(i); var second = b.ContactOrganAt(j);
        if (!first.Eligible || !second.Eligible) return false;
        ++stats.Candidates;
        var sa = first.Shape; var sb = second.Shape;
        if (sa.Lamina != null && sa.Pose.Length < CollisionSettings.SoftTinyLaminaLength) sa = AsCapsule(sa);
        if (sb.Lamina != null && sb.Pose.Length < CollisionSettings.SoftTinyLaminaLength) sb = AsCapsule(sb);
        var reach = sa.Pose.Radius + sb.Pose.Radius;
        var skin = CollisionSettings.FoliageSkin;
        var band = reach + skin;
        var key = new CollisionWorld.ContactKey(pa, first.Id, pb, second.Id);
        var hasMemory = memories.TryGetValue(key, out var memory)
            && memory.Seen >= epoch - 1 - CollisionSettings.ContactMemoryGraceEpochs;
        // A remembered pair is queried a little beyond the band, so one that has just separated
        // still reports a witness normal and a reversed one can be counted.
        var search = hasMemory ? band + skin : band;
        var hit = PairDistance(sa, sb, search, pa.World.CollisionGeometryToleranceM, ref stats);
        var touching = hit.Distance < band;
        if (first.Unmoved && second.Unmoved) { ++stats.StaticPairs; if (touching) ++stats.StaticTouching; }
        // The common ancestor decides how a pair that touches is weighted, and lets two rods
        // that share a junction be sampled along their length. A pair with a blade that does
        // not touch needs neither, and most candidate pairs are of that kind.
        var stop = pa == pb && (touching || sa.Lamina == null && sb.Lamina == null) ? CollisionWorld.CommonAncestor(a, i, j) : -1;
        var weight = 1f;
        if (stop >= 0)
        {
            // Siblings and descendants of one node are exempt right at the junction, where every
            // organ overlaps by construction, and fully weighted twice that far out. The ramp is
            // what keeps the response continuous as a tip grows past the junction region.
            var junction = a.ContactOrganAt(stop).Shape.Pose;
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

    /// <summary>
    /// How readily each body of a touching pair yields along the contact normal: the walk from
    /// the organ to the common ancestor that <see cref="RespondPair"/> splits the correction by.
    /// </summary>
    /// <remarks>
    /// The walk reads the joints as they stand - their stiffness, their bend, where they sit -
    /// and a response only queues turns on them, which nothing reads before the sweep ends. So
    /// for a plant whose organs do not change shape under contact the mobility is the same
    /// whether it is measured here, with the geometry and on all cores, or later, one pair
    /// after another. A plant with contact deformation is left to the response: there a queued
    /// correction does change what the next pair finds.
    /// </remarks>
    static void MeasureMobility(PlantSubFormation<AboveGroundAgent> ag, ref PairRecord r)
    {
        if (r.Weight <= 0f || r.Depth <= 0f) return;
        r.MobilityA = ag.ContactMobility(r.I, r.Hit.A, r.Normal, r.Stop);
        r.MobilityB = ag.ContactMobility(r.J, r.Hit.B, -r.Normal, r.Stop);
        r.MobilityKnown = true;
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
        var ma = r.MobilityKnown ? r.MobilityA : a.ContactMobility(i, hit.A, normal, stop);
        var mb = r.MobilityKnown ? r.MobilityB : b.ContactMobility(j, hit.B, -normal, stop);
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

    /// <summary>Pairs from which the response of a large plant whose organs do not change shape
    /// under contact is replayed on several cores (<see cref="ReplayOnAllCores"/>).</summary>
    const int ParallelReplayMinimum = 16384;

    /// <summary>
    /// The response of every evaluated pair of one large rigid plant, on several cores, with the
    /// same result as <see cref="RespondPair"/> called for each pair in order.
    /// </summary>
    /// <remarks>
    /// A pair's response remembers its side and queues up to four corrections - one per body
    /// along the normal, and for a deep overlap one per body along a tangent. A correction is a
    /// walk from the organ towards the pair's common ancestor that adds a turn to each joint on
    /// the way; for a plant whose organs only turn, what it adds to a joint depends on that joint
    /// and on totals the walk takes before it turns anything, and nothing reads the turns until
    /// the sweep. So every correction is planned first, side by side
    /// (<see cref="PlantSubFormation{T}.PlanContactCorrection"/>). What then fixes the result is
    /// the order in which each joint receives its turns, a float sum: the joints are split into
    /// the partitions of <see cref="TreeCacheData.WalkPartition"/>, which a walk enters at most
    /// once each; every partition takes the corrections that reach it in pair order, starting
    /// each at the joint where it enters, adds only to its own joints, and the partitions run
    /// side by side - a joint therefore receives its turns in pair order, as in the plain replay.
    /// A single trunk partition took every long-range correction and was most of the replay,
    /// which is why the partitions have levels. The side memory is written meanwhile
    /// (<see cref="RememberSides"/>).
    /// </remarks>
    void ReplayOnAllCores(PlantFormation2 plant, SoftWork work, int chunks)
    {
        var ag = plant.AG;
        // The touching pairs, in pair order: a count per chunk of the evaluation, a prefix sum, a fill.
        var touching = 0;
        for (int c = 0; c < chunks; ++c) { var count = work.ChunkTouching[c]; work.ChunkTouching[c] = touching; touching += count; }
        if (work.TouchingPairs.Length < touching)
        {
            var capacity = Math.Max(touching, work.TouchingPairs.Length * 3 / 2);
            work.TouchingPairs = new int[capacity];
            work.PlanCounts = new byte[capacity]; work.PlanSlides = new bool[capacity];
            work.Plans = new PlantSubFormation<AboveGroundAgent>.ContactCorrection[4 * capacity];
            work.SideIsNew = new bool[capacity];
        }
        CollectTouching(work, chunks);
        // The side memory needs only the touching pairs, and is written while everything else runs.
        Parallel.Invoke(
            () => RememberSides(work, touching),
            () => ReplayCorrections(ag, work, touching));
        for (int t = 0; t < touching; ++t)
            if (work.PlanSlides[t]) ++work.Slides;
    }

    /// <summary>Touching pairs planned and routed per block, so that the routes of a block are in
    /// pair order and the blocks can be read one after another.</summary>
    const int ReplayBlock = 512;

    void ReplayCorrections(PlantSubFormation<AboveGroundAgent> ag, SoftWork work, int touching)
    {
        var owner = ag.WalkPartition(out var parts);
        var blocks = (touching + ReplayBlock - 1) / ReplayBlock;
        if (work.BlockRoutes.Length < blocks)
        {
            var old = work.BlockRoutes.Length;
            Array.Resize(ref work.BlockRoutes, Math.Max(blocks, old * 3 / 2));
            for (int b = old; b < work.BlockRoutes.Length; ++b) work.BlockRoutes[b] = [];
        }
        PlanReplay(ag, work, touching, blocks, owner);

        // Every partition's corrections, in pair order: a count, a prefix sum, a fill.
        if (work.PartStart.Length < parts + 1) { work.PartStart = new int[parts + 1]; work.PartFill = new int[parts]; }
        var start = work.PartStart; var fill = work.PartFill;
        Array.Clear(start, 0, parts + 1);
        var total = 0;
        for (int b = 0; b < blocks; ++b)
        {
            var routes = work.BlockRoutes[b];
            for (int r = 1; r < routes.Count; r += 3) ++start[routes[r] + 1];
            total += routes.Count / 3;
        }
        for (int p = 0; p < parts; ++p) { start[p + 1] += start[p]; fill[p] = start[p]; }
        if (work.PartPlans.Length < 2 * total) work.PartPlans = new int[Math.Max(2 * total, work.PartPlans.Length * 3 / 2)];
        var list = work.PartPlans;
        for (int b = 0; b < blocks; ++b)
        {
            var routes = work.BlockRoutes[b];
            for (int r = 0; r < routes.Count; r += 3)
            {
                var at = 2 * fill[routes[r + 1]]++;
                list[at] = routes[r]; list[at + 1] = routes[r + 2];
            }
        }
        ApplyReplay(ag, work, owner, parts);
    }

    /// <summary>The indices of the touching pairs, in order, from each chunk's count turned into
    /// its first position. Kept apart, as the two below, so that a closure is only allocated
    /// when it runs.</summary>
    void CollectTouching(SoftWork work, int chunks)
        => Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, chunks, 1024), range =>
        {
            for (int c = range.Item1; c < range.Item2; ++c)
            {
                var at = work.ChunkTouching[c];
                for (var mask = work.ChunkTouchingMask[c]; mask != 0; mask &= mask - 1)
                    work.TouchingPairs[at++] = c * OwnPairChunk + System.Numerics.BitOperations.TrailingZeroCount(mask);
            }
        });

    /// <summary>Plans every touching pair's corrections and the partitions each one reaches.</summary>
    void PlanReplay(PlantSubFormation<AboveGroundAgent> ag, SoftWork work, int touching, int blocks, int[] owner)
        => Parallel.For(0, blocks, b =>
        {
            var routes = work.BlockRoutes[b];
            routes.Clear();
            var last = Math.Min(touching, (b + 1) * ReplayBlock);
            for (int t = b * ReplayBlock; t < last; ++t)
            {
                PlanPair(ag, in work.Records[work.TouchingPairs[t]], work.Plans.AsSpan(4 * t, 4), out work.PlanCounts[t], out work.PlanSlides[t]);
                for (int s = 0; s < work.PlanCounts[t]; ++s)
                    ag.RouteContactCorrection(work.Plans[4 * t + s], 4 * t + s, owner, routes);
            }
        });

    void ApplyReplay(PlantSubFormation<AboveGroundAgent> ag, SoftWork work, int[] owner, int parts)
        => Parallel.For(0, parts, part =>
        {
            var plans = work.Plans; var list = work.PartPlans;
            for (int e = work.PartStart[part]; e < work.PartStart[part + 1]; ++e)
                ag.ApplyContactCorrection(plans[list[2 * e]], owner, part, list[2 * e + 1]);
        });

    /// <summary>
    /// The side memory of every touching pair, as <see cref="RespondPair"/> writes it in pair
    /// order: the same entries, and the same dictionary underneath.
    /// </summary>
    /// <remarks>
    /// Each pair has a key of its own, so each entry is written once. Overwriting the value of a
    /// key that is already there changes nothing else in a dictionary, and finding it only reads,
    /// so those are written side by side through the entry itself; the keys that are new are then
    /// added one after another in pair order - exactly the insertions, in exactly the order, that
    /// shaped the dictionary before. Its layout is never part of a result anyway (the checkpoint
    /// sorts it), but this way it does not even differ.
    /// </remarks>
    void RememberSides(SoftWork work, int touching)
    {
        OverwriteKnownSides(work, touching);
        var records = work.Records;
        for (int t = 0; t < touching; ++t)
            if (work.SideIsNew[t])
            {
                ref readonly var r = ref records[work.TouchingPairs[t]];
                work.Contacts[r.Key] = new(r.Normal, r.Hit.FeatureA, r.Hit.FeatureB, ContactEpoch);
            }
    }

    void OverwriteKnownSides(SoftWork work, int touching)
        => Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, touching, 2048), range =>
        {
            var records = work.Records; var contacts = work.Contacts; var epoch = ContactEpoch;
            for (int t = range.Item1; t < range.Item2; ++t)
            {
                ref readonly var r = ref records[work.TouchingPairs[t]];
                ref var memory = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(contacts, r.Key);
                work.SideIsNew[t] = System.Runtime.CompilerServices.Unsafe.IsNullRef(ref memory);
                if (!work.SideIsNew[t]) memory = new(r.Normal, r.Hit.FeatureA, r.Hit.FeatureB, epoch);
            }
        });

    /// <summary>
    /// The corrections <see cref="RespondPair"/> queues for one pair of a rigid plant, planned in
    /// the order it queues them, without queueing anything. Line for line the same decisions; a
    /// rigid plant has no shared shape to correct (<see cref="PlantSubFormation{T}.SharedShapeMobility"/>
    /// is zero for it).
    /// </summary>
    static void PlanPair(PlantSubFormation<AboveGroundAgent> a, in PairRecord r, Span<PlantSubFormation<AboveGroundAgent>.ContactCorrection> plans,
        out byte count, out bool slid)
    {
        count = 0; slid = false;
        if (!r.Touching) return;
        var i = r.I; var j = r.J; var stop = r.Stop; var hit = r.Hit; var normal = r.Normal; var weight = r.Weight;
        if (weight <= 0f) return;
        var depth = r.Depth;
        if (depth <= 0f) return;
        var cap = CollisionSettings.MaxCorrectionDisplacement > 0f ? CollisionSettings.MaxCorrectionDisplacement : float.PositiveInfinity;
        var ma = r.MobilityKnown ? r.MobilityA : a.ContactMobility(i, hit.A, normal, stop);
        var mb = r.MobilityKnown ? r.MobilityB : a.ContactMobility(j, hit.B, -normal, stop);
        const double shared = 0;
        var sum = ma + mb + shared;
        if (sum > 1e-20 && double.IsFinite(sum))
        {
            if (ma > 0) plans[count++] = a.PlanContactCorrection(i, hit.A, normal, (float)(depth * ma / sum), stop, cap, ma);
            if (mb > 0) plans[count++] = a.PlanContactCorrection(j, hit.B, -normal, (float)(depth * mb / sum), stop, cap, mb);
        }
        else
        {
            plans[count++] = a.PlanContactCorrection(i, hit.A, normal, depth * .5f, stop, cap, double.NaN);
            plans[count++] = a.PlanContactCorrection(j, hit.B, -normal, depth * .5f, stop, cap, double.NaN);
        }
        var residual = r.Residual;
        var slideThreshold = CollisionSettings.DeepPenetrationThreshold * (CollisionSettings.SoftSlideRamp ? .25f : .5f);
        if (residual > slideThreshold)
        {
            var sa = CollisionWorld.Shape(a, i); var sb = CollisionWorld.Shape(a, j);
            var apart = (sa.Pose.Base + sa.Pose.Tip - sb.Pose.Base - sb.Pose.Tip) * .5f;
            var tangent = apart - normal * Vector3.Dot(apart, normal);
            if (tangent.LengthSquared() < 1e-12f) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            tangent = Vector3.Normalize(tangent);
            var ta = a.ContactMobility(i, hit.A, tangent, stop);
            var tb = a.ContactMobility(j, hit.B, -tangent, stop);
            var tangentSum = ta + tb;
            if (tangentSum > 1e-20 && double.IsFinite(tangentSum))
            {
                var slide = residual * CollisionSettings.ContactRelaxation * weight;
                if (CollisionSettings.SoftSlideRamp)
                    slide *= SmoothStep(slideThreshold, CollisionSettings.DeepPenetrationThreshold * .5f, residual);
                if (ta > 0) plans[count++] = a.PlanContactCorrection(i, hit.A, tangent, (float)(slide * ta / tangentSum), stop, cap, ta);
                if (tb > 0) plans[count++] = a.PlanContactCorrection(j, hit.B, -tangent, (float)(slide * tb / tangentSum), stop, cap, tb);
                slid = true;
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
