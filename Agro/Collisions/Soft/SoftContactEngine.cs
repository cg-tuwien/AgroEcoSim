using System.Diagnostics;
using System.Numerics;
using AgentsSystem;

namespace Agro.Collisions;

/// <summary>
/// The single-pass ("soft") contact engine. Selected by <see cref="SimulationRequest.ContactEngine"/>;
/// the certified engine in <see cref="CollisionWorld"/> stays the default and is left untouched.
/// </summary>
/// <remarks>
/// <para>
/// One narrow-phase evaluation per candidate pair per tick, a Jacobi response through the plant's
/// existing joint compliance (<c>ContactMobility</c>, <c>QueueContactCorrection</c>,
/// <c>EndContactSweep</c>), then a bounded push against soil and obstacles. Nothing is certified,
/// so nothing is rolled back, retried or deferred: a pair that still overlaps after this tick's
/// correction is corrected again next tick, and births reach Census the tick they are sampled.
/// Residual overlap is measured and reported, never asserted. The one place growth is taken
/// away is <see cref="ClipElongation"/>, which shortens this tick's elongation of an organ that
/// grew into a solid, and only down to last tick's length.
/// </para>
/// <para>
/// Determinism: no random numbers. Per plant, contributions accumulate first from its own pairs,
/// sorted by stable organ IDs, then from the cross-plant batches in sequence; a plant belongs to
/// exactly one job per batch (greedy edge colouring), so the accumulation order is the same for
/// any worker cap.
/// </para>
/// <para>
/// Cost and memory: the broad phase (swept organ boxes, plant-pair hierarchy) and every
/// geometric kernel are the certified engine's, reused unchanged. What this engine does not do is
/// repeat them: no correction rounds, no swept certificates, no fallback passes, no memo tables.
/// Per plant it keeps one pair list and one sparse dictionary of touching pairs.
/// </para>
/// </remarks>
public sealed partial class SoftContactEngine
{
    readonly CollisionWorld World;
    StaticCollider Static => World.StaticScene;
    readonly List<PlantFormation2> Plants = [];
    readonly List<PlantFormation2> PreviousPlants = [];
    readonly Dictionary<PlantFormation2, SoftWork> Work = [];
    readonly List<PlantFormation2> RetiredPlants = [];
    readonly Dictionary<(PlantFormation2, PlantFormation2), CrossJob> CrossJobs = [];
    readonly List<(PlantFormation2, PlantFormation2)> RetiredPairs = [];
    readonly List<List<CrossJob>> Batches = [];
    readonly List<(PlantFormation2 A, PlantFormation2 B)> PreviousNeighbours = [];
    bool ScheduleInvalidated = true;
    bool FoliageActive;
    /// <summary>With fewer plants than workers the parallelism has to come from inside a plant.</summary>
    bool ChunkOwnPairs;
    long ContactEpoch;
    long TotalOrgans;
    readonly CollisionWorld.WeightedSchedule PlantSchedule = new(), CrossSchedule = new();
    long NeighbourTicks, PrepareTicks, CrossTicks;
    static readonly bool TraceTiming = Environment.GetEnvironmentVariable("AGRO_COLLISION_TIMING") != null;

    internal SoftContactEngine(CollisionWorld world) => World = world;

    /// <summary>Per-plant state: a reused pair list, the sparse contact memory and tick counters.</summary>
    sealed class SoftWork
    {
        public readonly PlantFormation2 Plant;
        public readonly Dictionary<CollisionWorld.ContactKey, SoftContactMemory> Contacts = [];
        public readonly List<CollisionWorld.ContactKey> Expired = [];
        public readonly List<(int, int)> OwnPairs = [];
        /// <summary>Cross-plant batches this plant is already in, one bit per stage.</summary>
        public ulong[] StageMask = new ulong[1];
        /// <summary>Last tick's measured cost of the two per-plant jobs, for the scheduler.</summary>
        public long LastPrepareTicks, LastFinishTicks, LastRespondTicks, LastEvalTicks;
        public int LastChunks;
        public readonly Comparison<(int, int)> PairOrder;
        public readonly Comparison<int> OrganOrder;
        /// <summary>Residual obstacle penetration per organ after this tick's pushes; the thigmomorphogenesis hook.</summary>
        public float[] SolidConfinement = [];
        public long Seen;
        public PairStats Stats;
        public PairStats[] ChunkStats = [];
        public PairRecord[] Records = [];
        public double ClippedMetres;
        public float MaxSolidPenetration;
        public long Clips, Obstacle, Soil, Queries, Skips, Slides;

        public SoftWork(PlantFormation2 plant)
        {
            Plant = plant;
            OrganOrder = (a, b) => plant.AG.GetOrganId(a).CompareTo(plant.AG.GetOrganId(b));
            PairOrder = (a, b) =>
            {
                var first = OrganOrder(a.Item1, b.Item1);
                return first != 0 ? first : OrganOrder(a.Item2, b.Item2);
            };
        }

        public void ResetCounters()
        {
            Stats = default;
            ClippedMetres = 0;
            MaxSolidPenetration = 0;
            Clips = Obstacle = Soil = Queries = Skips = Slides = 0;
        }
    }

    /// <summary>One neighbouring plant pair. Its memories live in <c>A</c>'s dictionary.</summary>
    sealed class CrossJob
    {
        public readonly SoftWork A, B;
        public readonly List<(int, int)> Pairs = [];
        public readonly Comparison<(int, int)> PairOrder;
        public long Seen, LastTicks;
        public CrossJob(SoftWork a, SoftWork b)
        {
            A = a; B = b;
            PairOrder = (x, y) =>
            {
                var first = a.OrganOrder(x.Item1, y.Item1);
                return first != 0 ? first : b.OrganOrder(x.Item2, y.Item2);
            };
        }
    }

    public void Tick(AgroWorld world, uint timestep, IList<IFormation> formations)
    {
        var t0 = Stopwatch.GetTimestamp();
        Plants.Clear();
        TotalOrgans = 0;
        for (int i = 0; i < formations.Count; ++i)
            if (formations[i] is PlantFormation2 p && p.AG.Count > 0) { Plants.Add(p); TotalOrgans += p.AG.Count; }
        PrepareWork();
        var afterPrepare = Stopwatch.GetTimestamp();
        FoliageActive = world.SoftCollisions;
        ChunkOwnPairs = Plants.Count * 2 <= CollisionWorld.WorkerLimit;
        if (FoliageActive) ResolveFoliage();
        var afterFoliage = Stopwatch.GetTimestamp();
        // Everything after the last cross-plant batch is plant-local, so it is one job per plant:
        // the correction, the memory expiry, the solid pass and the epilogue, with no barrier
        // between them and no serial loop after them.
        PlantSchedule.Run(Plants, TotalOrgans, p => Work[p].LastFinishTicks > 0 ? Work[p].LastFinishTicks : p.AG.Count, FinishPlant);
        var afterHard = Stopwatch.GetTimestamp();
        FlushCounters();
        FoliageTicks += afterFoliage - afterPrepare;
        HardTicks += afterHard - afterFoliage;
        Ticks += Stopwatch.GetTimestamp() - t0;
        ++TickCount;
        if (TraceTiming)
        {
            var scale = 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"SOFT_CONTACT_TIMING step={timestep} foliageMs={(afterFoliage - afterPrepare) * scale:F2} finishMs={(afterHard - afterFoliage) * scale:F2}"
                + $" neighboursMs={NeighbourTicks * scale:F2} prepareMs={PrepareTicks * scale:F2} respondMs={RespondTicksSum() * scale:F2} crossMs={CrossTicks * scale:F2} batches={Batches.Count} neighbours={World.Neighbours.Count}"
                + (Plants.Count == 1 ? $" evalMs={Work[Plants[0]].LastEvalTicks * scale:F2} chunks={Work[Plants[0]].LastChunks}" : "")
                + $" plants={Plants.Count} organs={TotalOrgans} pairs={LastCandidates} contacts={LastContacts} newSides={LastNewSides} crossings={LastCrossings}"
                + $" tunnels={LastSuspectedTunnels} maxPenMm={MaxFoliagePenetrationMetres * 1000:F3} meanPenMm={MeanFoliagePenetrationMetres * 1000:F3} deep={LastDeepPenetrations}"
                + $" clips={LastClips} clippedMm={LastClippedMetres * 1000:F3} maxSolidMm={MaxSolidPenetrationMetres * 1000:F3}");
        }
    }

    void PrepareWork()
    {
        ++ContactEpoch;
        var changed = PreviousPlants.Count != Plants.Count;
        for (int i = 0; !changed && i < Plants.Count; ++i) changed = !ReferenceEquals(PreviousPlants[i], Plants[i]);
        foreach (var plant in Plants)
        {
            if (!Work.TryGetValue(plant, out var work)) Work.Add(plant, work = new(plant));
            work.Seen = ContactEpoch;
        }
        if (!changed) return;
        // The plant-pair hierarchy is the certified engine's; tell it the leaves changed meaning.
        World.PlantMembershipChanged = true;
        ScheduleInvalidated = true;
        PreviousPlants.Clear(); PreviousPlants.AddRange(Plants);
        RetiredPlants.Clear();
        foreach (var (plant, work) in Work) if (work.Seen != ContactEpoch) RetiredPlants.Add(plant);
        foreach (var plant in RetiredPlants) Work.Remove(plant);
        RetiredPairs.Clear();
        foreach (var (pair, _) in CrossJobs)
            if (!Work.ContainsKey(pair.Item1) || !Work.ContainsKey(pair.Item2)) RetiredPairs.Add(pair);
        foreach (var pair in RetiredPairs) CrossJobs.Remove(pair);
    }

    void ResolveFoliage()
    {
        var t0 = Stopwatch.GetTimestamp();
        World.RefreshNeighbours(Plants);
        RefreshSchedule();
        NeighbourTicks = Stopwatch.GetTimestamp() - t0;
        PlantSchedule.Run(Plants, TotalOrgans, p => Work[p].LastPrepareTicks > 0 ? Work[p].LastPrepareTicks : p.AG.Count, PreparePlant);
        PrepareTicks = Stopwatch.GetTimestamp() - t0 - NeighbourTicks;
        foreach (var batch in Batches)
        {
            if (batch.Count == 0) continue;
            long work = 0;
            foreach (var job in batch) work += job.A.Plant.AG.Count + job.B.Plant.AG.Count;
            CrossSchedule.Run(batch, work, j => j.LastTicks > 0 ? j.LastTicks : j.A.Plant.AG.Count + j.B.Plant.AG.Count, VisitCrossTimed);
        }
        CrossTicks = Stopwatch.GetTimestamp() - t0 - NeighbourTicks - PrepareTicks;
    }

    /// <summary>The per-plant work before the cross-plant batches: clear the turns, complete the
    /// lamina cache while this thread owns the plant, then its own pairs.</summary>
    void PreparePlant(PlantFormation2 plant)
    {
        var started = Stopwatch.GetTimestamp();
        plant.AG.BeginContactSweep();
        plant.AG.WarmLaminaCache();
        VisitOwn(plant);
        Work[plant].LastPrepareTicks = Stopwatch.GetTimestamp() - started;
    }

    void VisitCrossTimed(CrossJob job)
    {
        var started = Stopwatch.GetTimestamp();
        VisitCross(job);
        job.LastTicks = Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// The per-plant work after the cross-plant batches: one Jacobi application of everything
    /// queued this tick (capped per joint, then a refit), memory expiry, the solid pass, and the
    /// same epilogue as the certified engine minus development admission - nothing is staged
    /// under this engine. Construction escrow settles against the accepted volume (a solid clip
    /// refunds its share), new organs get their first recorded pose, and rhizome-apex arrests (a
    /// plant-side mechanism) are released on their retry.
    /// </summary>
    void FinishPlant(PlantFormation2 plant)
    {
        var started = Stopwatch.GetTimestamp();
        if (FoliageActive)
        {
            plant.AG.EndContactSweep(CollisionSettings.MaxCorrectionRadians, CollisionSettings.SoftJointSumRule);
            ExpireContacts(plant);
        }
        ResolveHard(plant);
        plant.AG.SettleConstruction();
        plant.AG.SealNewGeometry();
        plant.AG.CompleteGrowthAttempts();
        Work[plant].LastFinishTicks = Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// Greedy edge colouring of the neighbour pairs into batches with no plant twice in a batch.
    /// Recomputed only when the neighbour list changes.
    /// </summary>
    void RefreshSchedule()
    {
        var neighbours = World.Neighbours;
        if (!ScheduleInvalidated && SameNeighbours(neighbours)) return;
        ScheduleInvalidated = false;
        PreviousNeighbours.Clear(); PreviousNeighbours.AddRange(neighbours);
        foreach (var batch in Batches) batch.Clear();
        foreach (var plant in Plants) Array.Clear(Work[plant].StageMask);
        foreach (var (a, b) in neighbours)
        {
            var wa = Work[a]; var wb = Work[b];
            var stage = FirstFreeStage(wa.StageMask, wb.StageMask);
            while (Batches.Count <= stage) Batches.Add([]);
            if (!CrossJobs.TryGetValue((a, b), out var job)) CrossJobs.Add((a, b), job = new(wa, wb));
            job.Seen = ContactEpoch;
            Batches[stage].Add(job); SetStage(ref wa.StageMask, stage); SetStage(ref wb.StageMask, stage);
        }
        RetiredPairs.Clear();
        foreach (var (pair, job) in CrossJobs) if (job.Seen != ContactEpoch) RetiredPairs.Add(pair);
        foreach (var pair in RetiredPairs) CrossJobs.Remove(pair);
    }

    long RespondTicksSum() { long sum = 0; foreach (var plant in Plants) sum += Work[plant].LastRespondTicks; return sum; }

    static int FirstFreeStage(ulong[] a, ulong[] b)
    {
        for (int w = 0; ; ++w)
        {
            var used = (w < a.Length ? a[w] : 0UL) | (w < b.Length ? b[w] : 0UL);
            if (used != ulong.MaxValue) return w * 64 + System.Numerics.BitOperations.TrailingZeroCount(~used);
        }
    }

    static void SetStage(ref ulong[] mask, int stage)
    {
        var w = stage >> 6;
        if (w >= mask.Length) Array.Resize(ref mask, w + 1);
        mask[w] |= 1UL << (stage & 63);
    }

    bool SameNeighbours(List<(PlantFormation2 A, PlantFormation2 B)> neighbours)
    {
        if (neighbours.Count != PreviousNeighbours.Count) return false;
        for (int i = 0; i < neighbours.Count; ++i)
            if (!ReferenceEquals(neighbours[i].A, PreviousNeighbours[i].A) || !ReferenceEquals(neighbours[i].B, PreviousNeighbours[i].B))
                return false;
        return true;
    }

    /// <summary>Forgets a pair's side once it has stayed apart for the grace period.</summary>
    void ExpireContacts(PlantFormation2 plant)
    {
        var work = Work[plant];
        var limit = ContactEpoch - CollisionSettings.ContactMemoryGraceEpochs;
        work.Expired.Clear();
        foreach (var (key, memory) in work.Contacts) if (memory.Seen < limit) work.Expired.Add(key);
        foreach (var key in work.Expired) work.Contacts.Remove(key);
    }
}
