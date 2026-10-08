using System.Numerics;
using System.Diagnostics;
using AgentsSystem;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    internal readonly record struct ContactKey(PlantFormation2 A, ulong OrganA, PlantFormation2 B, ulong OrganB);
    readonly record struct RememberedContact(Vector3 Normal, int FeatureA, int FeatureB);
    // Dual contact reactions are rebuilt from the unloaded equilibrium each tick.
    // Retaining them during relaxation permits negative increments to release an
    // over-corrected constraint, rather than accumulating an irreversible fold.
    readonly record struct ElasticContact(float Force, float Mobility);
    readonly record struct ElasticStaticContact(float Force,float Mobility,Vector3 Point,Vector3 Normal);

    void ResetElasticContacts()
    {
        foreach(var work in Work.Values) { work.Elastic?.Clear();work.StaticElastic?.Clear();work.HardCorrectionDepth=0; }
        foreach(var job in CrossWorkByPair.Values) job.Elastic?.Clear();
    }

    /// <summary>Largest estimated unilateral reaction at the last bounded solve, in N.</summary>
    public float MaximumElasticContactForceNewtons
    {
        get
        {
            var force=0f;
            foreach(var work in Work.Values)
            {
                if(work.Elastic!=null)foreach(var c in work.Elastic.Values)force=MathF.Max(force,c.Force);
                if(work.StaticElastic!=null)foreach(var c in work.StaticElastic.Values)force=MathF.Max(force,c.Force);
            }
            foreach(var job in CrossWorkByPair.Values)
                if(job.Elastic!=null)foreach(var c in job.Elastic.Values)force=MathF.Max(force,c.Force);
            return force;
        }
    }
    /// <summary>Maximum remaining endpoint penetration in the last foliage validation, metres.</summary>
    public float FoliageResidualMetres => Work.Count == 0 ? 0 : Work.Values.Max(w=>w.FoliageResidual);
    /// <summary>Largest solid-contact trial correction this tick, metres; not final penetration.</summary>
    public float MaximumHardTrialCorrectionMetres => Work.Count == 0 ? 0 : Work.Values.Max(w=>w.HardCorrectionDepth);
    /// <summary>Read after a tick. Stable organ IDs and estimated unilateral forces in N;
    /// clamped/rolled-back trials are not a converged pressure measurement.</summary>
    public IEnumerable<(int PlantA, ulong OrganA, int PlantB, ulong OrganB, string Kind, float ForceNewtons)> ContactReactions()
    {
        foreach(var (plant,work) in Work)
        {
            if(work.Elastic!=null)foreach(var (key,c) in work.Elastic)
                if(c.Force>0)yield return (PlantIndices[key.A],key.OrganA,PlantIndices[key.B],key.OrganB,"foliage",c.Force);
            if(work.StaticElastic!=null)foreach(var (key,c) in work.StaticElastic)
                if(c.Force>0)yield return (PlantIndices[plant],key.Item1,-1,0,key.Item2.ToString(),c.Force);
        }
        foreach(var job in CrossWorkByPair.Values)
            if(job.Elastic!=null)foreach(var (key,c) in job.Elastic)
                if(c.Force>0)yield return (PlantIndices[key.A],key.OrganA,PlantIndices[key.B],key.OrganB,"foliage",c.Force);
    }
    internal static void UniquePairs(List<(int, int)> pairs)
    {
        var retained=0;
        for(var i=0;i<pairs.Count;++i)
            if(retained==0 || pairs[i]!=pairs[retained-1]) pairs[retained++]=pairs[i];
        pairs.RemoveRange(retained,pairs.Count-retained);
    }
    readonly record struct PairQueryKey(ulong A, PlantFormation2 B, ulong OrganB, bool Previous);
    readonly record struct PairQuery(ContactShape A, ContactShape B, OrganPose Junction, float Search, FoliageContact Hit, bool WholeBody);
    struct PairCacheEntry { public bool Used; public PairQueryKey Key; public PairQuery Query; }
    sealed class PairCache
    {
        public PairCacheEntry[] Entries = new PairCacheEntry[64];
        public void Reserve(int pairs)
        {
            var size=Entries.Length;
            while(size<2048 && size<pairs*4) size*=2;
            if(size>Entries.Length) Entries=new PairCacheEntry[size];
        }
    }
    readonly Dictionary<PlantFormation2,PairCache> PairQueries = [];
    readonly Dictionary<PlantFormation2,HashSet<int>> CrossStages = [];
    readonly List<int> PairHits = [];
    readonly Bvh PlantBounds = new();
    readonly List<(int, Bvh.BoundingBox)> PlantLeaves = [];
    readonly List<int> TrialPlants = [];
    readonly List<int> TrialOrgans = [];
    readonly List<int> TrialAffected = [];
    readonly List<(int Index, float Length, float Radius)> PostRollbackFloralGrowth = [];
    readonly Dictionary<PlantFormation2, HashSet<int>> UnresolvedPlants = [];
    public long RejectedMotions { get; private set; }
    public int ActiveFoliageContacts => Work.Values.Sum(w => w.Contacts.Count);
    /// <summary>Contacts already present in the preceding geometry that could not be repaired.
    /// New movement is restored before this diagnostic is incremented.</summary>
    public int UnresolvedFoliageContacts { get; private set; }
    bool ExpandRejection;
    bool ExtendedRelaxation;
    readonly HashSet<PlantFormation2> ExtendedPlants = [];
    int[] ContactGroups = [];
    bool[] GroupEnabled = [], GroupUnresolved = [];

    int ContactGroup(int i)
    {
        while(ContactGroups[i]!=i){ContactGroups[i]=ContactGroups[ContactGroups[i]];i=ContactGroups[i];}
        return i;
    }

    bool SelectUnresolvedGroups(List<PlantFormation2> plants)
    {
        var count=plants.Count;
        if(ContactGroups.Length<count){ContactGroups=new int[count];GroupEnabled=new bool[count];GroupUnresolved=new bool[count];}
        Array.Clear(GroupEnabled);Array.Clear(GroupUnresolved);ExtendedPlants.Clear();
        for(var i=0;i<count;++i)ContactGroups[i]=i;
        foreach(var work in Work.Values)foreach(var (key,memory) in work.Contacts)
            if(memory.Seen==ContactEpoch && PlantIndices.TryGetValue(key.A,out var a) && PlantIndices.TryGetValue(key.B,out var b))
            {var rootA=ContactGroup(a);var rootB=ContactGroup(b);ContactGroups[Math.Max(rootA,rootB)]=Math.Min(rootA,rootB);}
        for(var i=0;i<count;++i)
        {var root=ContactGroup(i);GroupEnabled[root]|=plants[i].AG.HasContactDeformation;GroupUnresolved[root]|=Work[plants[i]].Found;}
        foreach(var job in CrossWorkByPair.Values)if(job.Found)
        {GroupUnresolved[ContactGroup(PlantIndices[job.A.Plant])]=true;GroupUnresolved[ContactGroup(PlantIndices[job.B.Plant])]=true;}
        for(var i=0;i<count;++i){var root=ContactGroup(i);if(GroupEnabled[root] && GroupUnresolved[root])ExtendedPlants.Add(plants[i]);}
        return ExtendedPlants.Count>0;
    }
    static readonly bool TraceContacts = Environment.GetEnvironmentVariable("AGRO_TRACE_CONTACTS") != null;
    static readonly bool TraceSlowPairs = Environment.GetEnvironmentVariable("AGRO_TRACE_SLOW_PAIRS") != null;

    internal static ContactShape Shape(PlantSubFormation<AboveGroundAgent> ag, int i) => new(ag.GetContactPose(i), ag.GetLaminaTemplate(i));
    internal static ContactShape Previous(PlantSubFormation<AboveGroundAgent> ag, int i, ContactShape current)
        => i < ag.PreviousGeometry.Length && ag.PreviousGeometry[i].Exists ? current with { Pose = ag.PreviousGeometry[i].Pose } : current;

    internal static bool Connected(PlantSubFormation<AboveGroundAgent> ag, int a, int b)
        => ag.GetParent(a) == b || ag.GetParent(b) == a;

    internal static int CommonAncestor(PlantSubFormation<AboveGroundAgent> ag, int a, int b)
    {
        var da = ag.GetAbsDepth(a); var db = ag.GetAbsDepth(b);
        while (a >= 0 && b >= 0 && a != b)
        {
            if (da >= db) { a = ag.GetParent(a); --da; }
            else { b = ag.GetParent(b); --db; }
        }
        return a == b ? a : -1;
    }

    bool VisitPairs(List<PlantFormation2> plants, bool correct, bool swept)
    {
        var broadStart = Stopwatch.GetTimestamp();
        RefreshNeighbours(plants);
        RefreshSchedule();
        foreach (var plant in plants) Work[plant].Capture();
        BroadPhaseTicks += Stopwatch.GetTimestamp() - broadStart;
        long discovery = 0, queries = 0;
        void PrepareOwn(PlantFormation2 plant)
        {
            var ag = plant.AG;
            var work = Work[plant]; var pairs = work.OwnPairs;
            var start = Stopwatch.GetTimestamp();
            if (work.OwnRevision != ag.CollisionGeometryRevision)
            {
                pairs.Clear();
                ag.CollisionBvh.GetOverlappingPairs(pairs, collisionsOnly: false);
                var retained = 0;
                for (int k = 0; k < pairs.Count; ++k)
                {
                    var (a, b) = pairs[k];
                    if (Connected(ag, a, b)) continue;
                    pairs[retained++] = ag.GetOrganId(a) > ag.GetOrganId(b) ? (b, a) : (a, b);
                }
                pairs.RemoveRange(retained, pairs.Count - retained);
                if (correct && ag.HasContactDeformation && work.Elastic != null)
                    foreach(var (key,reaction) in work.Elastic)
                        if(reaction.Force>0) {var a=ag.FindOrgan(key.OrganA);var b=ag.FindOrgan(key.OrganB);if(a>=0 && b>=0)pairs.Add((a,b));}
                pairs.Sort(work.PairOrder); UniquePairs(pairs);
                work.OwnRevision = ag.CollisionGeometryRevision;
            }
            PairQueries[plant].Reserve(pairs.Count); work.ReserveEvaluations(pairs.Count);
            Interlocked.Add(ref discovery, Stopwatch.GetTimestamp() - start);
        }
        // The CPU subdivision budget is part of the current acceptance policy. A
        // stronger device sweep certificate could release a tip that CPU conservatively
        // suspends. Keep those decisions on CPU; offload endpoint correction checks.
        var devicePhase = CudaAvailable && !swept && !plants.Any(p=>p.AG.HasContactDeformation);
        void VisitOwn(PlantFormation2 plant)
        {
            if(correct && ExtendedRelaxation && !ExtendedPlants.Contains(plant)){Work[plant].Found=false;return;}
            if (!devicePhase) PrepareOwn(plant);
            var work = Work[plant]; var pairs = work.OwnPairs;
            var afterDiscovery = Stopwatch.GetTimestamp();
            work.Found = false;work.FoliageResidual=0;
            var elastic=correct && plant.AG.HasContactDeformation ? work.Elastic ??= [] : null;
            MeasureJob(work, () =>
            {
                for (int k = 0; k < pairs.Count; ++k)
                {
                    if (devicePhase && CudaAvailable && work.Certified.Length > k && work.Certified[k]) { ++work.Candidates; continue; }
                    var (a, b) = pairs[k];
                    work.Found |= VisitPair(plant, a, plant, b, correct, swept, elastic);
                }
            });
            // One reduction per job, rather than one contended atomic per candidate.
            Interlocked.Add(ref queries, Stopwatch.GetTimestamp() - afterDiscovery);
        }
        if (devicePhase)
        {
            RunJobs(plants, plants.Sum(p => (long)p.AG.Count), PrepareOwn);
            CertifyOwnBatch(plants, swept);
        }
        RunJobs(plants, plants.Sum(p => (long)p.AG.Count), VisitOwn);
        var found = plants.Any(p => Work[p].Found);
        void PrepareCross(CrossWork job)
        {
            var pa = job.A.Plant; var pb = job.B.Plant;
            var a = pa.AG; var b = pb.AG;
            var start = Stopwatch.GetTimestamp();
            if (job.RevisionA != a.CollisionGeometryRevision || job.RevisionB != b.CollisionGeometryRevision)
            {
                job.Pairs.Clear();
                a.CollisionBvh.GetOverlappingPairs(b.CollisionBvh, a.FieldOrigin, b.FieldOrigin, job.Pairs);
                if(correct && job.Elastic != null)
                    foreach(var (key,reaction) in job.Elastic)
                        if(reaction.Force>0) {var i=a.FindOrgan(key.OrganA);var j=b.FindOrgan(key.OrganB);if(i>=0 && j>=0)job.Pairs.Add((i,j));}
                job.Pairs.Sort(job.PairOrder); UniquePairs(job.Pairs);
                job.RevisionA = a.CollisionGeometryRevision; job.RevisionB = b.CollisionGeometryRevision;
            }
            Interlocked.Add(ref discovery, Stopwatch.GetTimestamp() - start);
        }
        void VisitCross(CrossWork job)
        {
            var pa = job.A.Plant; var pb = job.B.Plant;
            var afterDiscovery = Stopwatch.GetTimestamp();
            job.Found = false;
            if(correct && ExtendedRelaxation && !ExtendedPlants.Contains(pa) && !ExtendedPlants.Contains(pb))return;
            var elastic=correct && (pa.AG.HasContactDeformation || pb.AG.HasContactDeformation) ? job.Elastic ??= [] : null;
            MeasureJob(job.A, () =>
            {
                for (int k = 0; k < job.Pairs.Count; ++k)
                {
                    if (elastic == null && !swept && CudaAvailable && job.Certified.Length > k && job.Certified[k]) { ++job.A.Candidates; continue; }
                    var (i, j) = job.Pairs[k];
                    job.Found |= VisitPair(pa, i, pb, j, correct, swept, elastic);
                }
            });
            Interlocked.Add(ref queries, Stopwatch.GetTimestamp() - afterDiscovery);
        }
        if (CollisionSettings.InterPlantPass)
            foreach (var batch in ScheduledBatches)
            {
                RunJobs(batch, batch.Sum(j => (long)j.A.Plant.AG.Count + j.B.Plant.AG.Count), PrepareCross);
                CertifyCrossBatch(batch, swept);
                RunJobs(batch, batch.Sum(j => (long)Math.Max(j.Pairs.Count, j.A.Plant.AG.Count + j.B.Plant.AG.Count)), VisitCross);
                foreach (var job in batch) found |= job.Found;
            }
        PairDiscoveryTicks += discovery; PairQueryTicks += queries;
        return found;
    }

    bool VisitPair(PlantFormation2 pa, int i, PlantFormation2 pb, int j, bool correct, bool swept,
        Dictionary<ContactKey,ElasticContact>? elastic = null)
    {
        var a = pa.AG; var b = pb.AG;
        if (!a.IsCollisionEligiblePublic(i) || !b.IsCollisionEligiblePublic(j)) return false;
        var work = Work[pa]; ++work.Candidates;
        var sa = Shape(a, i); var sb = Shape(b, j);
        var oldA = Previous(a, i, sa); var oldB = Previous(b, j, sb);
        var reach = sa.Pose.Radius + sb.Pose.Radius;
        var stop = pa == pb ? CommonAncestor(a, i, j) : -1;
        var started = TraceSlowPairs ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var key = new ContactKey(pa, a.GetOrganId(i), pb, b.GetOrganId(j));
        var reaction = elastic != null && elastic.TryGetValue(key,out var prior) ? prior : default;
        var search = reach + FoliageGeometry.Skin + reaction.Force * reaction.Mobility;
        var evaluationKey = new EvaluationKey(key.OrganA, pb, key.OrganB);
        ref var evaluated = ref work.Evaluations[evaluationKey.GetHashCode() & (work.Evaluations.Length - 1)];
        var versionA = work.Geometry[i].Revision; var versionB = Work[pb].Geometry[j].Revision;
        var commonVersion = stop < 0 ? 0 : work.Geometry[stop].Revision;
        var commonId = stop < 0 ? 0UL : a.GetOrganId(stop);
        FoliageContact hit; bool crossing;
        if (elastic == null && evaluated.Used && evaluated.Key == evaluationKey && evaluated.Swept == swept
            && evaluated.A == versionA && evaluated.B == versionB && evaluated.Common == commonVersion && evaluated.CommonId == commonId)
        {
            hit = evaluated.Hit; crossing = evaluated.Crossing; ++work.Reused;
        }
        else
        {
            hit = CachedPairDistance(pa,i,pb,j,stop,sa,sb,search,false);
            var oldReach = oldA.Pose.Radius + oldB.Pose.Radius;
            var oldContact = swept ? oldA==sa && oldB==sb ? hit
                : CachedPairDistance(pa,i,pb,j,stop,oldA,oldB,oldReach+FoliageGeometry.Skin,true) : default;
            var initialOverlap = swept && oldContact.Distance < oldReach - FoliageGeometry.Skin;
            var movement = oldA.MotionBound(sa)+oldB.MotionBound(sb);
            var sweptReach = MathF.Max(oldA.Pose.Radius,sa.Pose.Radius)+MathF.Max(oldB.Pose.Radius,sb.Pose.Radius);
            var separatedPath = stop<0 && oldContact.Distance >= sweptReach+movement+FoliageGeometry.Skin;
            // A long 4-hour scape increment can exhaust the CCD interval budget even
            // when shorter sweeps over the same path each certify clear. Retry only
            // that failed query, and only for a floral meristem within one crown.
            var subdivideScape = pa == pb && (a.GetOrgan(i) == OrganTypes.FlowerMeristem
                || b.GetOrgan(j) == OrganTypes.FlowerMeristem);
            crossing = swept && !initialOverlap && !separatedPath
                && !RelativeSweepClear(a, stop, oldA, sa, oldB, sb, certifiedSubsteps: subdivideScape);
            evaluated = new() { Used = true, Key = evaluationKey, Swept = swept, A = versionA, B = versionB,
                Common = commonVersion, CommonId = commonId, Hit = hit, Crossing = crossing };
        }
        work.FoliageResidual=MathF.Max(work.FoliageResidual,MathF.Max(0,reach-hit.Distance));
        var touching = hit.Distance < reach + FoliageGeometry.Skin;
        if (TraceSlowPairs)
        {
            var ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(ms>5)Console.WriteLine($"SLOW_PAIR t={pa.World.Timestep} {pa.Parameters.Name} {i}:{a.GetOrgan(i)} / {pb.Parameters.Name} {j}:{b.GetOrgan(j)} common={stop} swept={swept} d={hit.Distance} reach={reach} motion={oldA.MotionBound(sa)+oldB.MotionBound(sb)} crossing={crossing} ms={ms:F2}");
        }
        if (!touching && !crossing && reaction.Force<=0) return false;
        var normal = ContactNormal(hit, oldA.Pose.Base - oldB.Pose.Base,
            work.Contacts.TryGetValue(key, out var memory) ? memory.Value.Normal : null);
        if (touching)
        {
            work.Contacts[key] = new(new(normal, hit.FeatureA, hit.FeatureB), ContactEpoch);
        }
        var penetration = hit.Distance < reach - FoliageGeometry.Skin;
        if (!penetration && !crossing && reaction.Force<=0) return false;
        if (TraceContacts && InterPlantCandidates < 2000)
            Console.WriteLine($"CONTACT t={pa.World.Timestep} {i}:{a.GetOrgan(i)} / {j}:{b.GetOrgan(j)} d={hit.Distance} reach={reach} swept={crossing}");
        if (!correct)
        {
            var rootA = RejectionRoot(a, i, stop); var rootB = RejectionRoot(b, j, stop);
            Reject(pa, rootA); Reject(pb, rootB);
            Work[pa].Normals.TryAdd(rootA, normal); Work[pb].Normals.TryAdd(rootB, -normal);
            return true;
        }
        var depth = elastic == null ? MathF.Max(FoliageGeometry.Skin, reach-hit.Distance+FoliageGeometry.Skin)
            : reach-hit.Distance+FoliageGeometry.Skin;
        if (hit.Distance < 1e-7f)
            depth = MathF.Max(depth, reach - Vector3.Dot(sa.Support(normal) - hit.B, normal));
        var responseNormal=depth<0?-normal:normal;
        var ma = a.ContactMobility(i, hit.A, responseNormal, stop);
        var mb = b.ContactMobility(j, hit.B, -responseNormal, stop);
        var shared = a.SharedShapeMobility(stop,i,hit.A,j,hit.B,responseNormal);
        var sum = ma + mb + shared;
        if (sum > 1e-20 && double.IsFinite(sum))
        {
            var force = (float)(depth/sum);
            if(elastic != null)
            {
                var limit=MathF.Min(a.ContactForceStepLimit(i,hit.A,responseNormal,stop),b.ContactForceStepLimit(j,hit.B,-responseNormal,stop));
                force=Math.Clamp(MathF.Max(-reaction.Force,force),-limit,limit);
                elastic[key]=new(MathF.Max(0,reaction.Force+force),(float)sum);
                depth=force*(float)sum;
            }
            if (ma > 0) a.QueueContactCorrection(i, hit.A, normal, (float)(depth * ma / sum), stop);
            if (mb > 0) b.QueueContactCorrection(j, hit.B, -normal, (float)(depth * mb / sum), stop);
            if (shared > 0) a.QueueSharedShapeCorrection(stop,i,hit.A,j,hit.B,normal,(float)(depth/sum));
        }
        else
        {
            a.QueueContactCorrection(i, hit.A, normal, depth * 0.5f, stop);
            b.QueueContactCorrection(j, hit.B, -normal, depth * 0.5f, stop);
        }
        ++work.ContactsCount;
        return true;
    }

    static Vector3 ContactNormal(in FoliageContact hit, Vector3 previousBaseDelta, Vector3? remembered)
    {
        // A nonzero closest-point vector identifies the current side, including after
        // sliding around an edge. History resolves coincident sheets; use the bases
        // only when history is absent, never to override an already matching normal.
        if (hit.Distance >= FoliageGeometry.Skin) return hit.Normal;
        return Vector3.Dot(hit.Normal, remembered ?? previousBaseDelta) < 0 ? -hit.Normal : hit.Normal;
    }

    FoliageContact CachedPairDistance(PlantFormation2 pa,int i,PlantFormation2 pb,int j,int common,
        ContactShape a,ContactShape b,float search,bool previous)
    {
        var ag=pa.AG;
        var junction=common<0?default:previous && ag.PreviousGeometry[common].Exists
            ?ag.PreviousGeometry[common].Pose:ag.GetContactPose(common);
        var frame=junction with { Base=junction.Tip, Length=0 };
        if(common>=0)
        {
            a=InFrame(a,frame);b=InFrame(b,frame);
            junction=junction with{Base=Vector3.Zero,Rotation=Quaternion.Identity,Length=0};
        }
        var cache=PairQueries[pa].Entries;
        var key=new PairQueryKey(ag.GetOrganId(i),pb,pb.AG.GetOrganId(j),previous);
        ref var entry=ref cache[key.GetHashCode() & (cache.Length-1)];
        ref readonly var query=ref entry.Query;
        if(entry.Used && entry.Key==key && query.A==a && query.B==b
            && query.Junction==junction && query.Search==search)
            return Result(query.Hit);
        if(entry.Used && entry.Key==key && query.WholeBody
            && query.A.Lamina==a.Lamina && query.B.Lamina==b.Lamina)
        {
            // Distance is 1-Lipschitz under bounded body motion. A previous clearance
            // proves separation until translation, rotation and growth consume that gap.
            // Never use a clipped attachment distance for this whole-body certificate.
            var lower=query.Hit.Distance-query.A.MotionBound(a)-query.B.MotionBound(b);
            if(lower>=search+FoliageGeometry.Skin)
            {
                var separated=query.Hit with { Distance=lower };
                return Result(separated);
            }
        }
        var whole=common<0 || !NearAttachment(a,b,junction.Tip,
            MathF.Max(.002f,2f*junction.Radius+a.Pose.Radius+b.Pose.Radius));
        var hit=whole ? DetailedDistance(a,b,search+(common<0?.005f:0f),pa.World.CollisionGeometryToleranceM,Work[pa])
            : LocalPairDistance(a,b,search,junction,true);
        // Bounded, direct-mapped storage survives worker migration. Evict one entry on a
        // hash collision, not every cached pair when a crowded crown exceeds the capacity.
        // Full key and geometry equality are required, so eviction changes only runtime.
        entry=new() { Used=true,Key=key,Query=new(a,b,junction,search,hit,whole) };
        return Result(hit);

        FoliageContact Result(FoliageContact value)
        {
            // Cached clearance can exceed a fresh query's search radius. Exposing that
            // extra distance let a warm cache skip a bounded sweep which a resumed run
            // could conservatively reject. Cache history may change cost, never growth.
            // Also normalize the unused feature/normal when outside the requested range:
            // a swept crossing can use the endpoint result as its rejection direction.
            if(value.Distance>=search) value=new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
            return common>=0?OutOfFrame(value,frame):value;
        }
    }

    static bool RelativeSweepClear(PlantSubFormation<AboveGroundAgent> ag, int common,
        ContactShape oldA, ContactShape a, ContactShape oldB, ContactShape b,
        bool parentMoved = true, bool certifiedSubsteps = false)
    {
        // Endpoint overlap is checked separately. Identical geometry has no swept crossing.
        if (oldA == a && oldB == b) return true;
        var junction = false;
        var radius = 0f;
        OrganPose beforeJunction = default, afterJunction = default;
        if (common >= 0 && ag.PreviousGeometry[common].Exists)
        {
            afterJunction = ag.GetContactPose(common);
            beforeJunction = parentMoved ? ag.PreviousGeometry[common].Pose : afterJunction;
            radius = MathF.Max(.002f, 2f*MathF.Min(beforeJunction.Radius,afterJunction.Radius)
                + MathF.Min(oldA.Pose.Radius+oldB.Pose.Radius,a.Pose.Radius+b.Pose.Radius));
            junction = NearAttachment(oldA,oldB,beforeJunction.Tip,radius)
                || NearAttachment(a,b,afterJunction.Tip,radius);
            // Remove shared-ancestor transport before certifying relative motion.
            oldA = InFrame(oldA, beforeJunction with { Base=beforeJunction.Tip, Length=0 });
            oldB = InFrame(oldB, beforeJunction with { Base=beforeJunction.Tip, Length=0 });
            a = InFrame(a, afterJunction with { Base=afterJunction.Tip, Length=0 });
            b = InFrame(b, afterJunction with { Base=afterJunction.Tip, Length=0 });
        }
        var result = junction
            ? FoliageGeometry.AttachmentSweep(oldA,a,oldB,b,Vector3.Zero,Vector3.Zero,radius)
            : FoliageGeometry.Sweep(oldA,a,oldB,b);
        if (result == FoliageGeometry.MotionResult.Clear) return true;
        var fixedAttachment = !junction || beforeJunction.Radius == afterJunction.Radius
            && oldA.Pose.Radius == a.Pose.Radius && oldB.Pose.Radius == b.Pose.Radius;
        if (result == FoliageGeometry.MotionResult.Intersecting && fixedAttachment) return false;
        if (junction)
        {
            // The attachment neighbourhood grows with its junction and the two
            // organs. The whole-sweep minimum above is safe but can falsely block
            // a scape that is clear outside the larger neighbourhood later in the
            // interval. For each shorter segment use the minimum of its endpoint
            // radii: biological radius interpolation is linear, so this remains a
            // conservative bound at every intermediate instant.
            for (var segments = 2; segments <= 16; segments *= 2)
            {
                var certified = true;
                for (var step = 1; step <= segments; ++step)
                {
                    var t0 = (step - 1f) / segments;
                    var t1 = step / (float)segments;
                    var a0 = oldA.Interpolate(a,t0); var a1 = oldA.Interpolate(a,t1);
                    var b0 = oldB.Interpolate(b,t0); var b1 = oldB.Interpolate(b,t1);
                    var joint0 = beforeJunction.Interpolate(afterJunction,t0);
                    var joint1 = beforeJunction.Interpolate(afterJunction,t1);
                    var localRadius = MathF.Max(.002f, 2f*MathF.Min(joint0.Radius,joint1.Radius)
                        + MathF.Min(a0.Pose.Radius+b0.Pose.Radius,a1.Pose.Radius+b1.Pose.Radius));
                    var subResult = FoliageGeometry.AttachmentSweep(a0,a1,b0,b1,
                        Vector3.Zero,Vector3.Zero,localRadius);
                    if (subResult == FoliageGeometry.MotionResult.Clear) continue;
                    // A smaller interval may enlarge the justified attachment
                    // region; it must still check any observed outside collision.
                    if (subResult == FoliageGeometry.MotionResult.Intersecting && fixedAttachment) return false;
                    certified = false; break;
                }
                if (certified) return true;
            }
            return false;
        }
        if (!certifiedSubsteps) return false;
        // A false result may mean the conservative CCD work budget was exhausted,
        // not that the path intersects. Every shorter segment must independently
        // certify; a genuine crossing therefore remains rejected.
        const int steps = 4; // Mechanical certification is independent of biological tick duration.
        for (var step = 1; step <= steps; ++step)
        {
            var t0 = (step - 1f) / steps;
            var t1 = step / (float)steps;
            var a0 = oldA.Interpolate(a,t0); var a1 = oldA.Interpolate(a,t1);
            var b0 = oldB.Interpolate(b,t0); var b1 = oldB.Interpolate(b,t1);
            if (junction ? !FoliageGeometry.AttachmentSweepClear(a0,a1,b0,b1,Vector3.Zero,Vector3.Zero,radius)
                : !FoliageGeometry.SweepClear(a0,a1,b0,b1)) return false;
        }
        return true;
    }

    static bool NearAttachment(in ContactShape a, in ContactShape b, Vector3 point, float radius)
    {
        var pad = new Vector3(radius); var box = new Bvh.BoundingBox(point-pad,point+pad);
        return a.Bounds.Intersects(box) && b.Bounds.Intersects(box);
    }

    static FoliageContact PairDistance(PlantSubFormation<AboveGroundAgent> ag, int common,
        in ContactShape a, in ContactShape b, float search, bool previous = false)
    {
        if (common >= 0)
        {
            var junction = previous && ag.PreviousGeometry[common].Exists ? ag.PreviousGeometry[common].Pose : ag.GetContactPose(common);
            var frame=junction with { Base=junction.Tip, Length=0 };
            var hit=LocalPairDistance(InFrame(a,frame),InFrame(b,frame),search,
                junction with{Base=Vector3.Zero,Rotation=Quaternion.Identity,Length=0},true);
            return OutOfFrame(hit,frame);
        }
        return FoliageGeometry.Distance(a,b,search);
    }

    static FoliageContact LocalPairDistance(in ContactShape a,in ContactShape b,float search,OrganPose junction,bool attached)
    {
        if(attached)
        {
            var radius=MathF.Max(.002f,2f*junction.Radius+a.Pose.Radius+b.Pose.Radius);
            if(NearAttachment(a,b,junction.Tip,radius))
                return FoliageGeometry.OutsideAttachment(a,b,search,junction.Tip,radius);
        }
        return FoliageGeometry.Distance(a,b,search);
    }

    static FoliageContact OutOfFrame(FoliageContact hit,OrganPose frame)=>hit with
    {
        A=frame.Base+Vector3.Transform(hit.A,frame.Rotation),
        B=frame.Base+Vector3.Transform(hit.B,frame.Rotation),
        Normal=Vector3.Transform(hit.Normal,frame.Rotation)
    };

    static ContactShape InFrame(ContactShape shape, OrganPose frame)
    {
        var inverse = Quaternion.Conjugate(frame.Rotation);
        return shape with { Pose = shape.Pose with { Base = Vector3.Transform(shape.Pose.Base - frame.Base, inverse),
            Rotation = Quaternion.Normalize(inverse * shape.Pose.Rotation) } };
    }

    // One bounded ordering hint, never a cached clearance result. Stable IDs guard
    // against removal/reindexing; every candidate still runs the full certificate.
    (PlantFormation2 A, int I, ulong IdA, PlantFormation2 B, int J, ulong IdB)? LastTrialFailure;

    internal bool GrowthTrialClear(PlantFormation2 plant, int root, bool radiusOnly = false)
    {
        var hint = LastTrialFailure;
        LastTrialFailure = null; // a solid failure must not inherit an old foliage reaction
        var ag = plant.AG; var affected = TrialAffected;
        affected.Clear(); affected.Add(root);
        // An earlier development can add organs after this tick's hard pass sized its
        // clearance arrays. Subsequent support trials must include those new organs.
        ag.EnsureClearanceCapacity(ag.Count);
        RefreshPlantBounds(plant);
        // A pure radius retry changes only this organ's collision surface. Child
        // attachment offsets depend on length and orientation, never radius, so
        // RebaseAndRefit leaves descendant poses bit-identical. They were already
        // certified by the final fallback; keep their full check for every other trial.
        if (!radiusOnly)
            for (int i = 0; i < affected.Count; ++i) affected.AddRange(ag.GetChildren(affected[i]));
        foreach (var i in affected)
        {
            if (!StaticAdmissible(plant, i)) return false;
            if (!plant.World.SoftCollisions) continue;
            var a = Shape(ag, i); var oldA = Previous(ag, i, a);
            var bounds = oldA.SweptBounds(a);
            TrialPlants.Clear(); PlantBounds.QueryOverlaps(bounds, TrialPlants);
            TrialPlants.Sort();
            foreach (var pi in TrialPlants)
            {
                var other = ActivePlants[pi].AG;
                TrialOrgans.Clear(); other.QueryWorldBounds(bounds.Min, bounds.Max, TrialOrgans);
                TrialOrgans.Sort(Work[other.Plant].OrganOrder);
                // Reorder only candidates already returned by the normal broad phase.
                // The complete certificate is unchanged; a remembered failure is never
                // itself a reason to reject a new geometry.
                if (hint is { } failed && failed.A == plant && failed.I == i
                    && failed.IdA == ag.GetOrganId(i) && failed.B == other.Plant
                    && failed.J < other.Count && failed.IdB == other.GetOrganId(failed.J))
                {
                    var at = TrialOrgans.IndexOf(failed.J);
                    if (at > 0) { TrialOrgans.RemoveAt(at); TrialOrgans.Insert(0, failed.J); }
                }
                foreach (var j in TrialOrgans)
                {
                    if (!other.IsCollisionEligiblePublic(j) || other == ag && (i == j || Connected(ag, i, j))) continue;
                    // Continuation certifies every changed body once. A pair of
                    // changed bodies therefore needs only its canonical visit;
                    // stationary neighbours still receive the complete check here.
                    if (ContinuingGrowth && radiusOnly && other.HasContinuationWaypoint
                        && (pi < PlantIndices[plant] || pi == PlantIndices[plant] && j < i)
                        && j < other.PreviousGeometry.Length && other.PreviousGeometry[j].Exists
                        && other.GetContactPose(j) != other.PreviousGeometry[j].Pose) continue;
                    var b = Shape(other, j);
                    // Continuation starts after the original motion is certified.
                    // Unchanged neighbours stay at that accepted endpoint. Replaying
                    // their earlier motion can invent an old penetration exemption
                    // or reject a feasible path. Joined participants have their own
                    // last certified waypoint for the actual coupled sweep.
                    var oldB = ContinuingGrowth && !other.HasContinuationWaypoint
                        ? b : Previous(other, j, b);
                    var common = ag == other ? CommonAncestor(ag, i, j) : -1;
                    var reach = a.Pose.Radius + b.Pose.Radius;
                    // Match VisitPairs: the finite CCD work budget can make an
                    // equivalent A/B traversal certify while B/A cannot. Keep
                    // current and previous queries in the same stable order,
                    // including after organ removal/reindexing. This also reuses
                    // the main solver's geometry memo entries during retries.
                    var ownFirst = ag == other ? ag.GetOrganId(i) < other.GetOrganId(j)
                        : PlantIndices[plant] < pi;
                    var first = ownFirst ? a : b; var firstOld = ownFirst ? oldA : oldB;
                    var second = ownFirst ? b : a; var secondOld = ownFirst ? oldB : oldA;
                    var firstAG = ownFirst ? ag : other;
                    var hit = PairDistance(firstAG,common,first,second,reach + FoliageGeometry.Skin);
                    if (CollectingContinuationContacts)
                    {
                        if (hit.Distance < reach + FoliageGeometry.Skin)
                            RememberContinuationContact(plant, i, other.Plant, j, hit.Distance < reach - FoliageGeometry.Skin);
                        continue;
                    }
                    if (hit.Distance < reach - FoliageGeometry.Skin)
                    { LastTrialFailure = (plant, i, ag.GetOrganId(i), other.Plant, j, other.GetOrganId(j)); return false; }
                    var oldReach = oldA.Pose.Radius + oldB.Pose.Radius;
                    var previous = PairDistance(firstAG,common,firstOld,secondOld,oldReach + FoliageGeometry.Skin,previous:true);
                    if (previous.Distance < oldReach - FoliageGeometry.Skin) continue;
                    // Use the same bounded CCD continuation policy as VisitPair;
                    // rollback retries must not reject a scape solely because
                    // the equivalent main-pass query had a larger work budget.
                    var subdivideScape = other == ag && (ag.GetOrgan(i) == OrganTypes.FlowerMeristem
                        || other.GetOrgan(j) == OrganTypes.FlowerMeristem);
                    if (!RelativeSweepClear(firstAG, common, firstOld, first, secondOld, second, certifiedSubsteps: subdivideScape))
                    { LastTrialFailure = (plant, i, ag.GetOrganId(i), other.Plant, j, other.GetOrganId(j)); return false; }
                }
            }
        }
        return true;
    }

    internal void RefreshPlantBounds(PlantFormation2 plant)
    {
        if (PlantIndices.TryGetValue(plant, out var index) && plant.AG.TryGetWorldBounds(out var lo,out var hi))
            PlantBounds.RefitLeaf(index,new(lo,hi));
    }

    // A newly developing organ is not in the live graph yet. Its contact load still acts
    // through the existing support chain; use those joints before changing its emergence.
    internal bool DeflectDevelopment(PlantSubFormation<AboveGroundAgent> ag, int support,
        OrganPose pose, LaminaTemplate? template, ulong[] retired)
    {
        if (support<0 || ag.IsMechanicallyAnchored(support)) return false;
        var shape=new ContactShape(pose,template);
        Vector3 point=default,normal=default; float depth=0;
        var hit=CollisionSettings.ObstaclePass && Static.ShapeContact(shape,ag.GetContactPose(support).Tip,
            StaticSurface.Obstacle,out point,out normal,out depth);
        if (!hit && ag.Plant.World.SoftCollisions)
        {
            TrialPlants.Clear(); PlantBounds.QueryOverlaps(shape.Bounds, TrialPlants); TrialPlants.Sort();
            foreach(var pi in TrialPlants)
            {
                var plant = ActivePlants[pi];
                var other=plant.AG;
                PairHits.Clear();other.QueryWorldBounds(shape.Bounds.Min,shape.Bounds.Max,PairHits);
                PairHits.Sort(Work[plant].OrganOrder);
                foreach(var j in PairHits)
                {
                    if(other==ag && (j==support || retired.Contains(other.GetOrganId(j)))) continue;
                    var neighbour=Shape(other,j);
                    var common=other==ag?CommonAncestor(ag,support,j):-1;
                    var reach=pose.Radius+neighbour.Pose.Radius;
                    var contact=PairDistance(ag,common,shape,neighbour,reach+FoliageGeometry.Skin);
                    if(contact.Distance>=reach-FoliageGeometry.Skin) continue;
                    point=contact.A;normal=contact.Normal;depth=reach-contact.Distance+FoliageGeometry.Skin;hit=true;break;
                }
                if(hit) break;
            }
        }
        if(!hit) return false;
        ag.BeginContactSweep();
        var queued=ag.QueueContactCorrection(support,point,normal,depth+CollisionSettings.ContactTolerance);
        return ag.EndContactSweep(CollisionSettings.MaxCorrectionRadians) && queued;
    }

    int RejectionRoot(PlantSubFormation<AboveGroundAgent> ag, int index, int common)
    {
        if (!ExpandRejection) return index;
        // A shared ancestor transports both bodies together and cannot change their relative
        // intersection. Restore only the two contacting branches below it. For cross-plant
        // contact, stop at a rooted anchor rather than rolling back other crowns of the plant.
        return ag.ChangedContactRoot(index, common);
    }

    void Reject(PlantFormation2 plant, int organ)
    {
        Work[plant].Rejected.Add(organ);
    }

    void ResolveVegetation(List<PlantFormation2> plants)
    {
        CaptureGrowthProposals(plants);
        ResolveCapturedVegetation(plants);
    }

    void CaptureGrowthProposals(List<PlantFormation2> plants)
    {
        // Save physiological proposals before coupled correction can roll back an
        // organ that was never itself named by a contact pair. Reindexing can move
        // descendants before ancestors in the array, so order trials by graph depth
        // and stable ID rather than current array index.
        foreach (var plant in plants)
        {
            var ag = plant.AG;
            var support = Work[plant].SampledSupportRadius;
            var foliage = Work[plant].SampledOrganGrowth;
            support.Clear();
            foliage.Clear();
            for (var i = 0; i < ag.Count; ++i)
            {
                // A restored history has only its live prefix, whereas warm arrays
                // can have spare capacity. Organs inserted before the first hard
                // pass have no sampled previous dimensions in either case.
                if (i >= ag.PreviousGeometry.Length) continue;
                var old = ag.PreviousGeometry[i];
                if (!old.Exists || ag.GetIsRizome(i) || ag.HasPendingDevelopment(i)) continue;
                var organ = ag.GetOrgan(i);
                var floralAxis = organ is OrganTypes.FlowerMeristem or OrganTypes.FlowerStem or OrganTypes.FlowerPedicel;
                if (!floralAxis && organ is not (OrganTypes.Stem or OrganTypes.Meristem or OrganTypes.Petiole or OrganTypes.Leaf)) continue;
                var length = ag.GetLength(i);
                var radius = ag.GetBaseRadius(i);
                var lengthGrew = length > old.Pose.Length + 1e-7f;
                var radiusGrew = radius > old.Radius + 1e-7f;
                if (!lengthGrew && !radiusGrew) continue;
                var depth = 0;
                for (var parent = ag.GetParent(i); parent >= 0; parent = ag.GetParent(parent)) ++depth;
                var id = ag.GetOrganId(i);
                if (radiusGrew && (floralAxis || organ is (OrganTypes.Stem or OrganTypes.Meristem or OrganTypes.Petiole)))
                    support.Add((i, radius, depth, id));
                if (floralAxis || organ is OrganTypes.Petiole or OrganTypes.Leaf)
                    foliage.Add((i, length, radius, depth, id));
            }
            support.Sort(static (a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Id.CompareTo(b.Id));
            foliage.Sort(static (a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Id.CompareTo(b.Id));
        }
    }

    void ResolveCapturedVegetation(List<PlantFormation2> plants)
    {
        ++ContactEpoch;
        LastCorrectionRounds = LastValidationRounds = LastFallbackRounds = 0;
        LastContinuationTrials = LastContinuationSteps = LastContinuationCorrections = 0;
        LastContinuationCoupledSets = 0;
        LastContinuationEvasionStages = 0;
        LastContinuationContactRows = 0;
        LastContinuationDualResidualMetres = double.NaN;
        LastContinuationActiveBendLimits = LastContinuationDirectSolves = 0;
        ExtendedRelaxation=false;
        var rounds=plants.Any(p=>p.AG.HasContactDeformation)?16:CollisionSettings.HardRounds;
        for (int round = 0; round < rounds; ++round)
        {
            if(round>=CollisionSettings.HardRounds)
            {
                if(!SelectUnresolvedGroups(plants))break;
                ExtendedRelaxation=true;
            }
            ++LastCorrectionRounds;
            foreach (var p in plants) p.AG.BeginContactSweep();
            var contact = VisitPairs(plants, correct: true, swept: false);
            var moved = false;
            foreach (var p in plants) moved |= p.AG.EndContactSweep(CollisionSettings.MaxCorrectionRadians);
            if (!contact || !moved) break;
            ResolveHardPlants(plants);
        }
        ExtendedRelaxation=false;
        // Validate every affected pair, including paths whose endpoints are separated. A rollback
        // changes its neighbours' relative paths, so repeat discovery after each rollback.
        ExpandRejection = false;
        for (int pass = 0; pass < 8; ++pass)
        {
            ++LastValidationRounds;
            ExpandRejection = pass > 0;
            ClearRejections();
            if (!VisitPairs(plants, correct: false, swept: true)) break;
            foreach (var plant in plants)
            {
                if (!UnresolvedPlants.TryGetValue(plant, out var rejected) || rejected.Count == 0) continue;
                // Stable IDs make accepted trials independent of HashSet order.
                if (pass == 0) PostRollbackFloralGrowth.Clear();
                foreach (var i in rejected.OrderBy(plant.AG.GetOrganId).ToArray())
                {
                    if (pass == 0)
                    {
                        var old = plant.AG.PreviousGeometry[i];
                        if (plant.AG.GetOrgan(i) == OrganTypes.FlowerMeristem && old.Exists
                            && !plant.AG.HasPendingDevelopment(i)
                            && (plant.AG.GetLength(i) > old.Pose.Length + 1e-7f
                                || plant.AG.GetBaseRadius(i) > old.Radius + 1e-7f))
                            PostRollbackFloralGrowth.Add((i, plant.AG.GetLength(i), plant.AG.GetBaseRadius(i)));
                        Work[plant].Normals.TryGetValue(i, out var normal);
                        bool Accept() => GrowthTrialClear(plant, i);
                        if (plant.AG.TryRedirectGrowth(i, normal, Accept)) { rejected.Remove(i); continue; }
                        if (plant.AG.TryShortenGrowth(i, Accept)) rejected.Remove(i);
                    }
                    plant.AG.SetObstruction(i, true); ++RejectedMotions;
                }
                plant.AG.RestorePreviousGeometry(rejected);
                if (pass == 0)
                {
                    // Contact correction can turn a neighbouring petiole into an
                    // otherwise clear scape growth path. Once the coupled proposals
                    // are restored, retry each sampled scape increment serially.
                    // The complete descendant motion must pass static and foliage
                    // endpoint and swept tests before it is retained.
                    foreach (var (i, length, radius) in PostRollbackFloralGrowth)
                    {
                        bool AcceptGrowth() => GrowthTrialClear(plant, i);
                        if (plant.AG.TryGrowthAfterRollback(i, length, radius, AcceptGrowth))
                            rejected.Remove(i);
                    }
                    // A contact-rejected blade and its neighbours can have several
                    // simultaneous growth proposals. First restore those proposals;
                    // then let a stationary petiole yield as a separate certified
                    // support motion. Its descendants move with it and retry growth
                    // on their normal biological schedule.
                    foreach (var i in rejected.OrderBy(plant.AG.GetOrganId))
                    {
                        if (plant.AG.GetOrgan(i) != OrganTypes.Petiole
                            || plant.AG.IsMechanicallyAnchored(i)) continue;
                        Work[plant].Normals.TryGetValue(i, out var normal);
                        bool AcceptSupport() => GrowthTrialClear(plant, i);
                        var moved = plant.AG.TryRedirectGrowth(i, normal, AcceptSupport,
                            stationaryPetioleSupport: true)
                            || plant.AG.TryRedirectGrowth(i, Vector3.UnitY, AcceptSupport,
                                stationaryPetioleSupport: true);
                        if (moved)
                        {
                            plant.AG.WakeGrowth(i);
                            foreach (var child in plant.AG.GetChildren(i))
                                plant.AG.WakeGrowth(child);
                        }
                    }
                }
            }
            ResolveHardPlants(plants);
        }
        ExpandRejection = false;
        // Exhausting relaxation is not permission to accept an unchecked pose. Expand only
        // through supports that actually moved, and re-discover neighbours after each rollback.
        UnresolvedFoliageContacts = 0;
        var restoredComponents=new HashSet<(PlantFormation2,ulong)>();
        for (int pass=0, limit=plants.Sum(p=>p.AG.Count)+1; pass<limit; ++pass)
        {
            ++LastFallbackRounds;
            ClearRejections();
            VisitPairs(plants,correct:false,swept:true);
            foreach (var plant in plants)
                for (int i=0; i<plant.AG.Count; ++i)
                    if (!StaticAdmissible(plant,i)) Reject(plant,i);
            if (!HasRejections) break;
            var changed = false;
            foreach (var plant in plants)
            {
                if (!UnresolvedPlants.TryGetValue(plant,out var rejected) || rejected.Count == 0) continue;
                // Revert the contacting increments as well as their moving supports.
                // Keeping only the farthest support can leave the leaf's own growth
                // penetrating after the support returns to its previous orientation.
                var roots = rejected.SelectMany(plant.AG.ChangedContactComponent).Distinct().ToArray();
                foreach (var i in roots)
                {
                    changed |= restoredComponents.Add((plant,plant.AG.GetOrganId(i)));
                    plant.AG.SetObstruction(i,true);
                }
                plant.AG.RestorePreviousGeometry(roots);
            }
            // The fallback is a monotone removal of proposals. Running the elastic solver
            // here could move a restored support again and recreate the foliage crossing.
            // Revalidate both constraint sets on the next pass instead of proposing turns.
            if (!changed)
            {
                // These are candidates from BEFORE the last restoration. Revalidating
                // after it avoids reporting a repaired contact as an unresolved one.
                ClearRejections();
                VisitPairs(plants, correct:false, swept:true);
                UnresolvedFoliageContacts=UnresolvedPlants.Sum(p=>p.Value.Count);
                break;
            }
        }
        if (restoredComponents.Count>0)
            foreach (var plant in plants) RefreshContactLoads(plant);
        foreach (var work in Work.Values)
        {
            work.Expired.Clear();
            foreach (var (key, memory) in work.Contacts)
                if (memory.Seen != ContactEpoch) work.Expired.Add(key);
            foreach (var key in work.Expired)
            {
                work.Contacts.Remove(key);
                var a = key.A.AG.FindOrgan(key.OrganA); var b = key.B.AG.FindOrgan(key.OrganB);
                if (a >= 0) key.A.AG.WakeGrowth(a);
                if (b >= 0) key.B.AG.WakeGrowth(b);
            }
        }
        // Do this after the LAST fallback: an earlier certified increment can be
        // discarded again when a neighbouring branch is restored. All remaining
        // proposals were actually sampled by physiology. Pure thickening checks
        // the changed support when its descendants are already certified; every
        // length or blade retry checks its complete moving subtree and swept path.
        foreach (var plant in plants)
        {
            var ag = plant.AG;
            var changed = false;
            foreach (var (i, radius, _, _) in Work[plant].SampledSupportRadius)
            {
                if (ag.GetBaseRadius(i) >= radius - 1e-7f) continue;
                // This retry passes the current length back unchanged. Only the
                // support cylinder thickens, and its own complete endpoint and
                // swept certificate still runs. An unresolved fallback retains the
                // full subtree trial because its descendants are not yet certified.
                bool AcceptRadius() => GrowthTrialClear(plant, i,
                    radiusOnly: UnresolvedFoliageContacts == 0);
                changed |= ag.TryGrowthAfterRollback(i, ag.GetLength(i), radius, AcceptRadius);
            }
            // Coupled rollback can discard a blade, petiole or floral increment
            // that fits after its neighbours settle. Retry each lost sampled increment
            // once in support-before-leaf order. A complete subtree endpoint and
            // swept certificate remains mandatory; failure retains the rollback.
            foreach (var (i, sampledLength, sampledRadius, _, _) in Work[plant].SampledOrganGrowth)
            {
                var currentLength = ag.GetLength(i);
                var currentRadius = ag.GetBaseRadius(i);
                var length = MathF.Max(currentLength, sampledLength);
                var radius = MathF.Max(currentRadius, sampledRadius);
                if (length <= currentLength + 1e-7f && radius <= currentRadius + 1e-7f) continue;
                bool AcceptFoliage() => GrowthTrialClear(plant, i);
                var floralAxis = ag.GetOrgan(i) is OrganTypes.FlowerMeristem or OrganTypes.FlowerStem or OrganTypes.FlowerPedicel;
                var recovered = ag.TryGrowthAfterRollback(i, length, radius, AcceptFoliage, allowPartial: floralAxis);
                if (floralAxis && (ag.GetLength(i) < length - 1e-7f || ag.GetBaseRadius(i) < radius - 1e-7f))
                    recovered |= TryCoupledGrowth(plant, i, length, radius);
                if (!recovered) continue;
                changed = true;
                if (ag.GetOrgan(i) == OrganTypes.Petiole && length > currentLength + 1e-7f)
                    foreach (var child in ag.GetChildren(i)) ag.WakeGrowth(child);
            }
            if (changed) RefreshContactLoads(plant);
        }
    }

    internal bool AcceptDevelopmentPose(PlantSubFormation<AboveGroundAgent> ag, int owner, OrganPose pose,
        LaminaTemplate? template, ulong[]? retired = null, bool replacingOwner = false,
        HashSet<ulong>? moving = null, OrganPose? previousPose = null, bool attachedToOwner = true,
        bool previousPoseInCurrentFrame = false)
    {
        var shape = new ContactShape(pose, template);
        if (previousPose.HasValue && new ContactShape(previousPose.Value,template).MotionBound(shape) < 1e-7f) return true;
        // Expansion of a new blade can cross another sheet even when its final surface
        // is clear. Existing proposals also need their swept bounds during discovery.
        var from = new ContactShape(previousPose ?? pose with { Length=0, Width=0, Radius=0 },template);
        var trusted = owner >= 0 ? ag.GetBaseCenterWorld(owner) : ag.Plant.Position + ag.FieldOrigin;
        if (CollisionSettings.ObstaclePass && Static.ShapeContact(shape, trusted, StaticSurface.Obstacle, out _, out _, out _)) return false;
        if (CollisionSettings.ObstaclePass && !Static.ShapeSweepClear(from, shape, StaticSurface.Obstacle)) return false;
        if (CollisionSettings.SoilPass && pose.Tip.Y <= pose.Base.Y && !Static.InsideSoil(pose.Base, 1f)
            && Static.ShapeContact(shape, trusted, StaticSurface.Soil, out _, out _, out _)) return false;
        if (!ag.Plant.World.SoftCollisions) return true;
        var bounds = from.SweptBounds(shape);
        TrialPlants.Clear(); PlantBounds.QueryOverlaps(bounds, TrialPlants); TrialPlants.Sort();
        foreach (var pi in TrialPlants)
        {
            var plant = ActivePlants[pi];
            var other = plant.AG;
            if (!other.TryGetWorldBounds(out var lo, out var hi) || !bounds.Intersects(new(lo, hi))) continue;
            PairHits.Clear(); other.QueryWorldBounds(bounds.Min, bounds.Max, PairHits);
            PairHits.Sort(Work[plant].OrganOrder);
            foreach (var j in PairHits)
            {
                if (other == ag)
                {
                    if (j == owner && attachedToOwner || retired?.Contains(ag.GetOrganId(j)) == true || moving?.Contains(ag.GetOrganId(j)) == true) continue;
                    if (replacingOwner && ag.GetParent(j) == owner) continue;
                    if (replacingOwner && ag.GetParent(owner) == j) continue;
                }
                var neighbour=Shape(other,j);
                var common=other==ag && owner>=0 ? CommonAncestor(ag,owner,j) : -1;
                var reach=pose.Radius+neighbour.Pose.Radius;
                var hit = PairDistance(ag,common,shape,neighbour,reach+FoliageGeometry.Skin);
                if (hit.Distance < pose.Radius + other.GetContactPose(j).Radius - FoliageGeometry.Skin)
                {
                    if (TraceContacts) Console.WriteLine($"BIRTH REJECT t={ag.Plant.World.Timestep} owner={owner} other={j}:{other.GetOrgan(j)} d={hit.Distance} pose={pose} otherPose={other.GetContactPose(j)}");
                    return false;
                }
                if (!RelativeSweepClear(ag,common,from,shape,neighbour,neighbour,
                    parentMoved:previousPose.HasValue && !previousPoseInCurrentFrame)) return false;
            }
        }
        return true;
    }

    internal static bool AdmissibleBirthPair(OrganPose a, LaminaTemplate? ta, OrganPose b, LaminaTemplate? tb, Vector3 attachment)
    {
        var reach = a.Radius + b.Radius;
        var radius = MathF.Max(0.002f, reach + 0.001f);
        var contact = FoliageGeometry.OutsideAttachment(new(a,ta),new(b,tb),reach+FoliageGeometry.Skin,attachment,radius);
        return contact.Distance >= reach-FoliageGeometry.Skin
            && FoliageGeometry.AttachmentSweepClear(new(a with {Length=0,Width=0,Radius=0},ta),new(a,ta),
                new(b with {Length=0,Width=0,Radius=0},tb),new(b,tb),attachment,attachment,radius);
    }

    internal void WriteState(BinaryWriter writer, IReadOnlyList<IFormation> formations, ContactEngineKind engine)
    {
        // V33: one tag byte, then the payload of whichever engine ran. The certified payload
        // below is unchanged from V32.
        writer.Write((byte)engine);
        if (engine == ContactEngineKind.Soft) { (Soft ??= new(this)).WriteState(writer, formations); return; }
        var indices = formations.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i);
        var contacts = Work.Values.SelectMany(w => w.Contacts).OrderBy(x => indices[x.Key.A]).ThenBy(x => x.Key.OrganA)
            .ThenBy(x => indices[x.Key.B]).ThenBy(x => x.Key.OrganB).ToArray();
        writer.Write(contacts.Length);
        foreach (var (key, value) in contacts)
        {
            writer.Write(indices[key.A]); writer.Write(key.OrganA); writer.Write(indices[key.B]); writer.Write(key.OrganB);
            // Keep the v3 payload byte layout; visit epochs are reconstructible.
            PlantSubFormation<AboveGroundAgent>.WriteValue(writer, value.Value);
        }
        writer.Write(RejectedMotions);
    }
    internal void ReadState(BinaryReader reader, IReadOnlyList<IFormation> formations, ContactEngineKind engine)
    {
        var saved = (ContactEngineKind)reader.ReadByte();
        if (saved != engine)
            throw new InvalidDataException($"This checkpoint was written by the {saved} contact engine and the request selects {engine}. "
                + "Contact state is engine-specific; resume with the engine that wrote it.");
        if (engine == ContactEngineKind.Soft) { (Soft ??= new(this)).ReadState(reader, formations); return; }
        LastTrialFailure = null;
        Work.Clear(); PairQueries.Clear(); CrossWorkByPair.Clear(); ScheduledBatches.Clear();
        PreviousNeighbours.Clear(); PlantIndices.Clear(); UnresolvedPlants.Clear();
        CrossStages.Clear(); ScheduledPairs.Clear(); RetiredPairs.Clear();
        DeviceGroups.Clear(); DeviceCopies.Clear();
        ScheduleInvalidated = PlantMembershipChanged = true;
        ContactEpoch = 0; var count = reader.ReadInt32();
        for (int i = 0; i < count; ++i)
        {
            var key = new ContactKey((PlantFormation2)formations[reader.ReadInt32()], reader.ReadUInt64(),
                (PlantFormation2)formations[reader.ReadInt32()], reader.ReadUInt64());
            if (!Work.TryGetValue(key.A, out var work)) Work.Add(key.A, work = new(key.A));
            work.Contacts.TryAdd(key, new(PlantSubFormation<AboveGroundAgent>.ReadValue<RememberedContact>(reader), -1));
        }
        RejectedMotions = reader.ReadInt64();
    }
}
