using System.Diagnostics;
using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    // All these caches are derived. A worker owns a plant exclusively within a batch;
    // no dictionary below is mutated concurrently by two jobs.
    readonly Dictionary<PlantFormation2, PlantWork> Work = [];
    readonly Dictionary<PlantFormation2, int> PlantIndices = [];
    readonly Dictionary<(PlantFormation2, PlantFormation2), CrossWork> CrossWorkByPair = [];
    readonly List<List<CrossWork>> ScheduledBatches = [];
    readonly List<(int, int)> PlantPairs = [];
    readonly List<(PlantFormation2 A, PlantFormation2 B)> PreviousNeighbours = [];
    readonly HashSet<(PlantFormation2, PlantFormation2)> ScheduledPairs = [];
    readonly List<(PlantFormation2, PlantFormation2)> RetiredPairs = [];
    long ContactEpoch;
    bool PlantMembershipChanged;
    bool ScheduleInvalidated;

    readonly record struct ContactMemory(RememberedContact Value, long Seen);
    struct GeometryStamp
    {
        public ContactShape Current, Previous;
        public ulong Id;
        public long Revision;
        public int Parent;
        public bool Eligible;
    }
    readonly record struct EvaluationKey(ulong A, PlantFormation2 B, ulong OrganB);
    struct Evaluation
    {
        public bool Used, Swept, Crossing;
        public EvaluationKey Key;
        public long A, B, Common;
        public ulong CommonId;
        public FoliageContact Hit;
    }
    sealed class PlantWork
    {
        public readonly PlantFormation2 Plant;
        public readonly Dictionary<ContactKey, ContactMemory> Contacts = [];
        public readonly List<ContactKey> Expired = [];
        public readonly HashSet<int> Rejected = [];
        public readonly Dictionary<int, Vector3> Normals = [];
        public readonly List<(int, int)> OwnPairs = [];
        // Biological sizes sampled before coupled contact correction; reused each tick.
        // Depth then stable ID is independent of array positions after organ reindexing.
        public readonly List<(int Index, float Radius, int Depth, ulong Id)> SampledSupportRadius = [];
        public readonly List<(int Index, float Length, float Radius, int Depth, ulong Id)> SampledOrganGrowth = [];
        public Dictionary<ContactKey,ElasticContact>? Elastic;
        public Dictionary<(ulong,StaticSurface),ElasticStaticContact>? StaticElastic;
        public bool[] Certified = [];
        public readonly Comparison<(int, int)> PairOrder;
        public readonly Comparison<int> OrganOrder;
        public GeometryStamp[] Geometry = [];
        public StaticEvaluation[] StaticEvaluations = [];
        public Evaluation[] Evaluations = new Evaluation[64];
        public long GeometryRevision = -1, TopologyRevision = -1, NextStamp, OwnRevision = -1;
        public bool Found;
        public float FoliageResidual, HardCorrectionDepth;
        public long Candidates, ContactsCount, Queries, Skips, Obstacle, Soil, Reused;
        public long Intervals, Triangles, StaticReused;
        public long ApproximationQueries, ApproximationSeparations;

        public PlantWork(PlantFormation2 plant)
        {
            Plant = plant;
            OrganOrder = (a, b) => plant.AG.GetOrganId(a).CompareTo(plant.AG.GetOrganId(b));
            PairOrder = (a, b) =>
            {
                var first = OrganOrder(a.Item1, b.Item1);
                return first != 0 ? first : OrganOrder(a.Item2, b.Item2);
            };
        }
        public void Capture()
        {
            var ag = Plant.AG;
            if (GeometryRevision == ag.CollisionGeometryRevision && TopologyRevision == ag.TopologyRevision) return;
            if (Geometry.Length < ag.Count) Array.Resize(ref Geometry, Math.Max(ag.Count, Geometry.Length * 3 / 2 + 8));
            var topology = TopologyRevision != ag.TopologyRevision;
            for (int i = 0; i < ag.Count; ++i)
            {
                ref var state = ref Geometry[i];
                var current = Shape(ag, i); var previous = Previous(ag, i, current);
                var id = ag.GetOrganId(i); var parent = ag.GetParent(i); var eligible = ag.IsCollisionEligiblePublic(i);
                if (topology || state.Current != current || state.Previous != previous || state.Id != id
                    || state.Parent != parent || state.Eligible != eligible)
                    state = new() { Current = current, Previous = previous, Id = id, Parent = parent,
                        Eligible = eligible, Revision = ++NextStamp };
            }
            GeometryRevision = ag.CollisionGeometryRevision;
            TopologyRevision = ag.TopologyRevision;
        }
        public void ReserveEvaluations(int count)
        {
            var size = Evaluations.Length;
            while (size < 1024 && size < count * 2) size *= 2;
            if (size > Evaluations.Length) Evaluations = new Evaluation[size];
        }
    }
    sealed class CrossWork
    {
        public readonly PlantWork A, B;
        public readonly List<(int, int)> Pairs = [];
        public Dictionary<ContactKey,ElasticContact>? Elastic;
        public readonly Comparison<(int, int)> PairOrder;
        public long RevisionA = -1, RevisionB = -1;
        public bool Found;
        public bool[] Certified = [];
        public CrossWork(PlantWork a, PlantWork b)
        {
            A = a; B = b;
            PairOrder = (x, y) =>
            {
                var first = a.OrganOrder(x.Item1, y.Item1);
                return first != 0 ? first : b.OrganOrder(x.Item2, y.Item2);
            };
        }
    }

    /// <summary>Bounds on parallelism affect cost only, never candidate order or physics.</summary>
    static readonly int WorkerLimit = int.TryParse(Environment.GetEnvironmentVariable("AGRO_CollisionWorkers"), out var n)
        ? Math.Clamp(n, 1, Environment.ProcessorCount) : Environment.ProcessorCount;
    static readonly ParallelOptions[] Workers = Enumerable.Range(1, WorkerLimit)
        .Select(n => new ParallelOptions { MaxDegreeOfParallelism = n }).ToArray();
    const int MinimumParallelWork = 256;
    static void RunJobs<T>(List<T> jobs, long work, Action<T> action)
    {
        var concurrency = (int)Math.Min(WorkerLimit, Math.Max(1, (work + MinimumParallelWork - 1) / MinimumParallelWork));
        if (jobs.Count < 2 || concurrency == 1)
        { foreach (var job in jobs) action(job); return; }
        // Contiguous coarse ranges avoid a delegate/partitioner invocation per tiny pair.
        // Cap active workers as well as partition size. Four small plants measured
        // faster with two workers than with 8/16/32; a global on/off threshold alone
        // still oversubscribed these short phases on machines with many cores.
        var partitions = Math.Min(jobs.Count, concurrency * 2);
        Parallel.For(0, partitions, Workers[concurrency - 1], part =>
        {
            var end = (part + 1) * jobs.Count / partitions;
            for (int i = part * jobs.Count / partitions; i < end; ++i) action(jobs[i]);
        });
    }
    int PhaseThread;
    long AttributedMainIntervals, AttributedMainTriangles;
    void MeasureJob(PlantWork work, Action action)
    {
        var intervals = FoliageGeometry.IntervalCount; var triangles = FoliageGeometry.TriangleCount;
        action();
        var di = FoliageGeometry.IntervalCount - intervals;
        var dt = FoliageGeometry.TriangleCount - triangles;
        work.Intervals += di; work.Triangles += dt;
        if (Environment.CurrentManagedThreadId == PhaseThread)
        { AttributedMainIntervals += di; AttributedMainTriangles += dt; }
    }
    public long ReusedPairChecks { get; private set; }
    public long SweepIntervals { get; private set; }
    public long TriangleTests { get; private set; }
    public long ReusedStaticChecks { get; private set; }
    public long BroadPhaseTicks { get; private set; }
    public long PairDiscoveryTicks { get; private set; }
    public long PairQueryTicks { get; private set; }
    public int LastCorrectionRounds { get; private set; }
    public int LastValidationRounds { get; private set; }
    public int LastFallbackRounds { get; private set; }

    void PrepareWork(List<PlantFormation2> plants)
    {
        PlantMembershipChanged = PlantIndices.Count != plants.Count;
        for (int i = 0; i < plants.Count; ++i)
        {
            var plant = plants[i];
            PlantMembershipChanged |= !PlantIndices.TryGetValue(plant, out var old) || old != i;
            if (!Work.ContainsKey(plant)) Work.Add(plant, new(plant));
            UnresolvedPlants[plant] = Work[plant].Rejected;
            if (!PairQueries.ContainsKey(plant)) PairQueries.Add(plant, new());
        }
        if (PlantMembershipChanged)
        {
            // The ordering hint must not retain a retired plant graph.
            LastTrialFailure = null;
            PlantIndices.Clear();
            for (int i = 0; i < plants.Count; ++i) PlantIndices.Add(plants[i], i);
            foreach (var retired in Work.Keys.Where(p => !PlantIndices.ContainsKey(p)).ToArray())
            {
                Work.Remove(retired); UnresolvedPlants.Remove(retired);
                PairQueries.Remove(retired); CrossStages.Remove(retired);
            }
            foreach (var pair in CrossWorkByPair.Keys.Where(p => !PlantIndices.ContainsKey(p.Item1)
                || !PlantIndices.ContainsKey(p.Item2)).ToArray()) CrossWorkByPair.Remove(pair);
            ScheduleInvalidated = true;
            PreviousNeighbours.Clear();
        }
    }
    void FlushCounters()
    {
        foreach (var work in Work.Values)
        {
            InterPlantCandidates += work.Candidates; InterPlantContacts += work.ContactsCount;
            StaticQueries += work.Queries; ClearanceSkips += work.Skips;
            ObstacleContacts += work.Obstacle; SoilContacts += work.Soil;
            ReusedPairChecks += work.Reused; SweepIntervals += work.Intervals; TriangleTests += work.Triangles;
            ReusedStaticChecks += work.StaticReused; work.StaticReused = 0;
            ApproximationQueries += work.ApproximationQueries; ApproximationSeparations += work.ApproximationSeparations;
            work.Candidates = work.ContactsCount = work.Queries = work.Skips = work.Obstacle = work.Soil = work.Reused = 0;
            work.Intervals = work.Triangles = 0;
            work.ApproximationQueries = work.ApproximationSeparations = 0;
        }
    }
    void ClearRejections()
    {
        foreach (var work in Work.Values) { work.Rejected.Clear(); work.Normals.Clear(); }
    }
    bool HasRejections => Work.Values.Any(w => w.Rejected.Count != 0);
    void RefreshSchedule()
    {
        if (!ScheduleInvalidated && Neighbours.SequenceEqual(PreviousNeighbours)) return;
        ScheduleInvalidated = false;
        PreviousNeighbours.Clear(); PreviousNeighbours.AddRange(Neighbours);
        ScheduledPairs.Clear();
        foreach (var pair in Neighbours) ScheduledPairs.Add(pair);
        RetiredPairs.Clear();
        foreach (var pair in CrossWorkByPair.Keys)
            if (!ScheduledPairs.Contains(pair)) RetiredPairs.Add(pair);
        foreach (var pair in RetiredPairs) CrossWorkByPair.Remove(pair);
        RetiredPairs.Clear();
        foreach (var stages in CrossStages.Values) stages.Clear();
        foreach (var batch in ScheduledBatches) batch.Clear();
        foreach (var plant in ActivePlants)
            if (!CrossStages.ContainsKey(plant)) CrossStages.Add(plant, []);
        foreach (var pair in Neighbours)
        {
            var a = CrossStages[pair.A]; var b = CrossStages[pair.B]; var stage = 0;
            while (a.Contains(stage) || b.Contains(stage)) ++stage;
            while (ScheduledBatches.Count <= stage) ScheduledBatches.Add([]);
            if (!CrossWorkByPair.TryGetValue(pair, out var job))
                CrossWorkByPair.Add(pair, job = new(Work[pair.A], Work[pair.B]));
            ScheduledBatches[stage].Add(job); a.Add(stage); b.Add(stage);
        }
    }
}
