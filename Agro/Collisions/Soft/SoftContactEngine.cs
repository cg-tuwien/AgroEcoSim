using System.Diagnostics;
using System.Numerics;
using AgentsSystem;

namespace Agro.Collisions;

/// <summary>
/// The single-pass ("soft") contact engine. Selected by <see cref="SimulationRequest.ContactEngine"/>;
/// the certified engine in <see cref="CollisionWorld"/> is left untouched. The mixed engine, the
/// default, is this engine with the certified engine's solid constraints run after its pass
/// (<see cref="AgroWorld.CertifiedSolids"/>): foliage stays soft, obstacles and soil do not.
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
    /// <summary>Mixed engine: solids are certified constraints, enforced after the pushes.</summary>
    bool CertifiedSolids;
    readonly CollisionWorld.WeightedSchedule PlantSchedule = new(), CrossSchedule = new();
    long NeighbourTicks, PrepareTicks, CrossTicks;
    long LastStaticQueries, LastClearanceSkips, LastSupportSkips, LastObstacleContacts, LastSoilContacts;
    static readonly bool TraceTiming = Environment.GetEnvironmentVariable("AGRO_COLLISION_TIMING") != null;
    static readonly bool TraceSolids = Environment.GetEnvironmentVariable("AGRO_TRACE_SOLID_ENFORCE") != null;

    internal SoftContactEngine(CollisionWorld world) { World = world; FoliageGeometry.StageClocks = TraceTiming; }

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
        public long LastPrepareTicks, LastFinishTicks, LastRespondTicks, LastEvalTicks, LastChunkMaxTicks, LastChunkSumTicks, LastApplyTicks, LastHardTicks;
        public int LastChunks, LastGcs;
        public long[] ChunkTicks = [], WorstPairTicks = [];
        public (ulong, ulong)[] SortKeys = [];
        public int[] WorstPairIndex = [];
        public readonly Comparison<(int, int)> PairOrder;
        public readonly Comparison<int> OrganOrder;
        /// <summary>Residual obstacle penetration per organ after this tick's pushes; the thigmomorphogenesis hook.</summary>
        public float[] SolidConfinement = [];
        public StaticTouch[] ObstacleTouch = [], SoilTouch = [];
        /// <summary>The mixed engine's memo for the certified admissibility pass.</summary>
        public readonly CollisionWorld.StaticAdmission Admission = new();
        public long Seen;
        public PairStats Stats, LastStats;
        public PairStats[] ChunkStats = [];
        public PairRecord[] Records = [];
        public double ClippedMetres;
        public float MaxSolidPenetration;
        public long Clips, Obstacle, Soil, Queries, Skips, SupportSkips, Slides;
        /// <summary>Mixed engine: ticks in which the admissibility pass had to redirect, shorten or roll something back.</summary>
        public long Enforced;
        /// <summary>Mixed engine: organs the admissibility pass left inside a solid this tick, because
        /// the only configuration to fall back to was already inside it.</summary>
        public int UnresolvedSolids;
        public long HardAnchorTicks, HardClearTicks, HardObstacleTicks, HardSoilEligibleTicks, HardSoilTicks, HardSweepTicks, HardClipTicks, HardAdmitTicks, HardLoadTicks, HardRounds, ExactRounds;

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
            LastStats = Stats; Stats = default;
            ClippedMetres = 0;
            MaxSolidPenetration = 0;
            Clips = Obstacle = Soil = Queries = Skips = SupportSkips = Slides = Enforced = 0;
            HardAnchorTicks = HardClearTicks = HardObstacleTicks = HardSoilEligibleTicks = HardSoilTicks = HardSweepTicks = HardClipTicks = HardAdmitTicks = HardLoadTicks = HardRounds = ExactRounds = 0;
        }
    }

    /// <summary>One neighbouring plant pair. Its memories live in <c>A</c>'s dictionary.</summary>
    sealed class CrossJob
    {
        public readonly SoftWork A, B;
        public readonly List<(int, int)> Pairs = [];
        public readonly Comparison<(int, int)> PairOrder;
        public (ulong, ulong)[] SortKeys = [];
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
        CertifiedSolids = world.CertifiedSolids;
        ChunkOwnPairs = Plants.Count * 2 <= CollisionWorld.WorkerLimit;
        if (FoliageActive) ResolveFoliage();
        var afterFoliage = Stopwatch.GetTimestamp();
        // Everything after the last cross-plant batch is plant-local, so it is one job per plant:
        // the correction, the memory expiry, the solid pass and the epilogue, with no barrier
        // between them and no serial loop after them.
        PlantSchedule.Run(Plants, TotalOrgans, p => Work[p].LastFinishTicks > 0 ? Work[p].LastFinishTicks : p.AG.Count, FinishPlant);
        var afterHard = Stopwatch.GetTimestamp();
        LastStaticQueries = StaticQueries; LastClearanceSkips = ClearanceSkips; LastSupportSkips = SupportSkips; LastObstacleContacts = ObstacleContacts; LastSoilContacts = SoilContacts;
        var hardStages = TraceTiming ? HardStages() + " " + CollisionWorld.AdmissionStages() : "";
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
                + $" finishCpu(apply/hard/all)Ms={FinishTicksSum().apply * scale:F1}/{FinishTicksSum().hard * scale:F1}/{FinishTicksSum().finish * scale:F1} staticQueries={StaticQueries - LastStaticQueries} skips={ClearanceSkips - LastClearanceSkips} supportSkips={SupportSkips - LastSupportSkips} {hardStages} obstacleContacts={ObstacleContacts - LastObstacleContacts} soilContacts={SoilContacts - LastSoilContacts}"
                + (Plants.Count == 1 ? $" evalMs={Work[Plants[0]].LastEvalTicks * scale:F2} chunks={Work[Plants[0]].LastChunks} chunkSumMs={Work[Plants[0]].LastChunkSumTicks * scale:F2} chunkMaxMs={Work[Plants[0]].LastChunkMaxTicks * scale:F3} gcs={Work[Plants[0]].LastGcs}" : "")
                + (Plants.Count == 1 ? $" | stages: boundsOut={Work[Plants[0]].LastStats.BoundsOut} ({Work[Plants[0]].LastStats.BoundsTicks * scale:F1}ms) convexOut={Work[Plants[0]].LastStats.ConvexOut} ({Work[Plants[0]].LastStats.ConvexTicks * scale:F1}ms) exact={Work[Plants[0]].LastStats.Exact} ({Work[Plants[0]].LastStats.ExactTicks * scale:F1}ms) sampled={Work[Plants[0]].LastStats.Sampled} ({Work[Plants[0]].LastStats.SampledTicks * scale:F1}ms) touchingMs={Work[Plants[0]].LastStats.TouchingTicks * scale:F1} separatedMs={Work[Plants[0]].LastStats.SeparatedTicks * scale:F1}" : "")
                + $" plants={Plants.Count} organs={TotalOrgans} pairs={LastCandidates} contacts={LastContacts} staticPairs={LastStaticPairs} staticTouching={LastStaticTouching} newSides={LastNewSides} crossings={LastCrossings}"
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
        var applied = Stopwatch.GetTimestamp();
        ResolveHard(plant);
        var hard = Stopwatch.GetTimestamp();
        plant.AG.SettleConstruction();
        plant.AG.SealNewGeometry();
        plant.AG.CompleteGrowthAttempts();
        var work = Work[plant];
        work.LastFinishTicks = Stopwatch.GetTimestamp() - started;
        work.LastApplyTicks = applied - started; work.LastHardTicks = hard - applied;
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
    string HardStages()
    {
        long a = 0, c = 0, o = 0, se = 0, s = 0, sw = 0, cl = 0, ad = 0, l = 0, r = 0, e = 0, x = 0;
        foreach (var plant in Plants) { var w = Work[plant]; a += w.HardAnchorTicks; c += w.HardClearTicks; o += w.HardObstacleTicks; se += w.HardSoilEligibleTicks; s += w.HardSoilTicks; sw += w.HardSweepTicks; cl += w.HardClipTicks; ad += w.HardAdmitTicks; l += w.HardLoadTicks; r += w.HardRounds; e += w.Enforced; x += w.ExactRounds; }
        var k = 1000.0 / Stopwatch.Frequency;
        return $"hardCpuMs: anchors={a * k:F0} clear={c * k:F0} obstacle={o * k:F0} soilEligible={se * k:F0} soil={s * k:F0} sweep={sw * k:F0} clip={cl * k:F0} admit={ad * k:F0} loads={l * k:F0} rounds={r} exactRounds={x} enforced={e}";
    }
    (long apply, long hard, long finish) FinishTicksSum() { long a = 0, h = 0, f = 0; foreach (var plant in Plants) { var w = Work[plant]; a += w.LastApplyTicks; h += w.LastHardTicks; f += w.LastFinishTicks; } return (a, h, f); }

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
