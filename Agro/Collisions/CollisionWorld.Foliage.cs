using System.Numerics;
using System.Diagnostics;
using AgentsSystem;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    readonly record struct ContactKey(PlantFormation2 A, ulong OrganA, PlantFormation2 B, ulong OrganB);
    readonly record struct RememberedContact(Vector3 Normal, int FeatureA, int FeatureB);
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
    readonly Dictionary<PlantFormation2, HashSet<int>> UnresolvedPlants = [];
    public long RejectedMotions { get; private set; }
    public int ActiveFoliageContacts => Work.Values.Sum(w => w.Contacts.Count);
    /// <summary>Contacts already present in the preceding geometry that could not be repaired.
    /// New movement is restored before this diagnostic is incremented.</summary>
    public int UnresolvedFoliageContacts { get; private set; }
    bool ExpandRejection;
    static readonly bool TraceContacts = Environment.GetEnvironmentVariable("AGRO_TRACE_CONTACTS") != null;
    static readonly bool TraceSlowPairs = Environment.GetEnvironmentVariable("AGRO_TRACE_SLOW_PAIRS") != null;

    static ContactShape Shape(PlantSubFormation<AboveGroundAgent> ag, int i) => new(ag.GetContactPose(i), ag.GetLaminaTemplate(i));
    static ContactShape Previous(PlantSubFormation<AboveGroundAgent> ag, int i, ContactShape current)
        => i < ag.PreviousGeometry.Length && ag.PreviousGeometry[i].Exists ? current with { Pose = ag.PreviousGeometry[i].Pose } : current;

    static bool Connected(PlantSubFormation<AboveGroundAgent> ag, int a, int b)
        => ag.GetParent(a) == b || ag.GetParent(b) == a;

    static int CommonAncestor(PlantSubFormation<AboveGroundAgent> ag, int a, int b)
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
                pairs.Sort(work.PairOrder);
                work.OwnRevision = ag.CollisionGeometryRevision;
            }
            PairQueries[plant].Reserve(pairs.Count); work.ReserveEvaluations(pairs.Count);
            Interlocked.Add(ref discovery, Stopwatch.GetTimestamp() - start);
        }
        // The CPU subdivision budget is part of the current acceptance policy. A
        // stronger device sweep certificate could release a tip that CPU conservatively
        // suspends. Keep those decisions on CPU; offload endpoint correction checks.
        var devicePhase = CudaAvailable && !swept;
        void VisitOwn(PlantFormation2 plant)
        {
            if (!devicePhase) PrepareOwn(plant);
            var work = Work[plant]; var pairs = work.OwnPairs;
            var afterDiscovery = Stopwatch.GetTimestamp();
            work.Found = false;
            MeasureJob(work, () =>
            {
                for (int k = 0; k < pairs.Count; ++k)
                {
                    if (devicePhase && CudaAvailable && work.Certified.Length > k && work.Certified[k]) { ++work.Candidates; continue; }
                    var (a, b) = pairs[k];
                    work.Found |= VisitPair(plant, a, plant, b, correct, swept);
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
                job.Pairs.Sort(job.PairOrder);
                job.RevisionA = a.CollisionGeometryRevision; job.RevisionB = b.CollisionGeometryRevision;
            }
            Interlocked.Add(ref discovery, Stopwatch.GetTimestamp() - start);
        }
        void VisitCross(CrossWork job)
        {
            var pa = job.A.Plant; var pb = job.B.Plant;
            var afterDiscovery = Stopwatch.GetTimestamp();
            job.Found = false;
            MeasureJob(job.A, () =>
            {
                for (int k = 0; k < job.Pairs.Count; ++k)
                {
                    if (!swept && CudaAvailable && job.Certified.Length > k && job.Certified[k]) { ++job.A.Candidates; continue; }
                    var (i, j) = job.Pairs[k];
                    job.Found |= VisitPair(pa, i, pb, j, correct, swept);
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

    bool VisitPair(PlantFormation2 pa, int i, PlantFormation2 pb, int j, bool correct, bool swept)
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
        var evaluationKey = new EvaluationKey(key.OrganA, pb, key.OrganB);
        ref var evaluated = ref work.Evaluations[evaluationKey.GetHashCode() & (work.Evaluations.Length - 1)];
        var versionA = work.Geometry[i].Revision; var versionB = Work[pb].Geometry[j].Revision;
        var commonVersion = stop < 0 ? 0 : work.Geometry[stop].Revision;
        var commonId = stop < 0 ? 0UL : a.GetOrganId(stop);
        FoliageContact hit; bool crossing;
        if (evaluated.Used && evaluated.Key == evaluationKey && evaluated.Swept == swept
            && evaluated.A == versionA && evaluated.B == versionB && evaluated.Common == commonVersion && evaluated.CommonId == commonId)
        {
            hit = evaluated.Hit; crossing = evaluated.Crossing; ++work.Reused;
        }
        else
        {
            hit = CachedPairDistance(pa,i,pb,j,stop,sa,sb,reach+FoliageGeometry.Skin,false);
            var oldReach = oldA.Pose.Radius + oldB.Pose.Radius;
            var oldContact = swept ? oldA==sa && oldB==sb ? hit
                : CachedPairDistance(pa,i,pb,j,stop,oldA,oldB,oldReach+FoliageGeometry.Skin,true) : default;
            var initialOverlap = swept && oldContact.Distance < oldReach - FoliageGeometry.Skin;
            var movement = oldA.MotionBound(sa)+oldB.MotionBound(sb);
            var sweptReach = MathF.Max(oldA.Pose.Radius,sa.Pose.Radius)+MathF.Max(oldB.Pose.Radius,sb.Pose.Radius);
            var separatedPath = stop<0 && oldContact.Distance >= sweptReach+movement+FoliageGeometry.Skin;
            crossing = swept && !initialOverlap && !separatedPath && !RelativeSweepClear(a, stop, oldA, sa, oldB, sb);
            evaluated = new() { Used = true, Key = evaluationKey, Swept = swept, A = versionA, B = versionB,
                Common = commonVersion, CommonId = commonId, Hit = hit, Crossing = crossing };
        }
        var touching = hit.Distance < reach + FoliageGeometry.Skin;
        if (TraceSlowPairs)
        {
            var ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(ms>5)Console.WriteLine($"SLOW_PAIR t={pa.World.Timestep} {pa.Parameters.Name} {i}:{a.GetOrgan(i)} / {pb.Parameters.Name} {j}:{b.GetOrgan(j)} common={stop} swept={swept} d={hit.Distance} reach={reach} motion={oldA.MotionBound(sa)+oldB.MotionBound(sb)} crossing={crossing} ms={ms:F2}");
        }
        if (!touching && !crossing) return false;
        var normal = ContactNormal(hit, oldA.Pose.Base - oldB.Pose.Base,
            work.Contacts.TryGetValue(key, out var memory) ? memory.Value.Normal : null);
        if (touching)
        {
            work.Contacts[key] = new(new(normal, hit.FeatureA, hit.FeatureB), ContactEpoch);
        }
        var penetration = hit.Distance < reach - FoliageGeometry.Skin;
        if (!penetration && !crossing) return false;
        if (TraceContacts && InterPlantCandidates < 2000)
            Console.WriteLine($"CONTACT t={pa.World.Timestep} {i}:{a.GetOrgan(i)} / {j}:{b.GetOrgan(j)} d={hit.Distance} reach={reach} swept={crossing}");
        if (!correct)
        {
            var rootA = RejectionRoot(a, i, stop); var rootB = RejectionRoot(b, j, stop);
            Reject(pa, rootA); Reject(pb, rootB);
            Work[pa].Normals.TryAdd(rootA, normal); Work[pb].Normals.TryAdd(rootB, -normal);
            return true;
        }
        var depth = MathF.Max(FoliageGeometry.Skin, reach - hit.Distance + FoliageGeometry.Skin);
        if (hit.Distance < 1e-7f)
            depth = MathF.Max(depth, reach - Vector3.Dot(sa.Support(normal) - hit.B, normal));
        var ma = a.ContactMobility(i, hit.A, normal, stop);
        var mb = b.ContactMobility(j, hit.B, -normal, stop);
        var sum = ma + mb;
        if (sum > 1e-20 && double.IsFinite(sum))
        {
            if (ma > 0) a.QueueContactCorrection(i, hit.A, normal, (float)(depth * ma / sum), stop);
            if (mb > 0) b.QueueContactCorrection(j, hit.B, -normal, (float)(depth * mb / sum), stop);
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
        ContactShape oldA, ContactShape a, ContactShape oldB, ContactShape b, bool parentMoved = true)
    {
        // Endpoint overlap is checked separately. Identical geometry has no swept crossing.
        if (oldA == a && oldB == b) return true;
        if (common >= 0 && ag.PreviousGeometry[common].Exists)
        {
            var after = ag.GetContactPose(common);
            var before = parentMoved ? ag.PreviousGeometry[common].Pose : after;
            var radius = MathF.Max(.002f, 2f*MathF.Min(before.Radius,after.Radius)
                + MathF.Min(oldA.Pose.Radius+oldB.Pose.Radius,a.Pose.Radius+b.Pose.Radius));
            var junction = NearAttachment(oldA,oldB,before.Tip,radius) || NearAttachment(a,b,after.Tip,radius);
            // Elongation of a shared ancestor transports both branches and their attachment
            // together. Remove that common translation as well as its rotation. Measuring
            // from the base treated crown elongation as relative leaf motion, exhausting the
            // continuous-check budget and repeatedly suspending otherwise separated seedlings.
            oldA = InFrame(oldA, before with { Base=before.Tip, Length=0 });
            oldB = InFrame(oldB, before with { Base=before.Tip, Length=0 });
            a = InFrame(a, after with { Base=after.Tip, Length=0 });
            b = InFrame(b, after with { Base=after.Tip, Length=0 });
            if (junction) return FoliageGeometry.AttachmentSweepClear(oldA,a,oldB,b,
                Vector3.Zero,Vector3.Zero,radius);
        }
        return FoliageGeometry.SweepClear(oldA, a, oldB, b);
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

    internal bool GrowthTrialClear(PlantFormation2 plant, int root)
    {
        var ag = plant.AG; var affected = new List<int> { root };
        // An earlier development can add organs after this tick's hard pass sized its
        // clearance arrays. Subsequent support trials must include those new organs.
        ag.EnsureClearanceCapacity(ag.Count);
        RefreshPlantBounds(plant);
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
                foreach (var j in TrialOrgans)
                {
                    if (!other.IsCollisionEligiblePublic(j) || other == ag && (i == j || Connected(ag, i, j))) continue;
                    var b = Shape(other, j); var oldB = Previous(other, j, b);
                    var common = ag == other ? CommonAncestor(ag, i, j) : -1;
                    var reach = a.Pose.Radius + b.Pose.Radius;
                    var hit = PairDistance(ag,common,a,b,reach + FoliageGeometry.Skin);
                    if (hit.Distance < reach - FoliageGeometry.Skin) return false;
                    var oldReach = oldA.Pose.Radius + oldB.Pose.Radius;
                    var previous = PairDistance(ag,common,oldA,oldB,oldReach + FoliageGeometry.Skin,previous:true);
                    if (previous.Distance < oldReach - FoliageGeometry.Skin) continue;
                    if (!RelativeSweepClear(ag, common, oldA, a, oldB, b)) return false;
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
        ++ContactEpoch;
        LastCorrectionRounds = LastValidationRounds = LastFallbackRounds = 0;
        for (int round = 0; round < CollisionSettings.HardRounds; ++round)
        {
            ++LastCorrectionRounds;
            foreach (var p in plants) p.AG.BeginContactSweep();
            var contact = VisitPairs(plants, correct: true, swept: false);
            var moved = false;
            foreach (var p in plants) moved |= p.AG.EndContactSweep(CollisionSettings.MaxCorrectionRadians);
            if (!contact || !moved) break;
            ResolveHardPlants(plants);
        }
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
                foreach (var i in rejected.ToArray())
                {
                    if (pass == 0)
                    {
                        Work[plant].Normals.TryGetValue(i, out var normal);
                        bool Accept() => GrowthTrialClear(plant, i);
                        if (plant.AG.TryRedirectGrowth(i, normal, Accept)) { rejected.Remove(i); continue; }
                        if (plant.AG.TryShortenGrowth(i, Accept)) rejected.Remove(i);
                    }
                    plant.AG.SetObstruction(i, true); ++RejectedMotions;
                }
                plant.AG.RestorePreviousGeometry(rejected);
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
    }

    internal bool AcceptDevelopmentPose(PlantSubFormation<AboveGroundAgent> ag, int owner, OrganPose pose,
        LaminaTemplate? template, ulong[]? retired = null, bool replacingOwner = false,
        HashSet<ulong>? moving = null, OrganPose? previousPose = null, bool attachedToOwner = true)
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
                    parentMoved:previousPose.HasValue)) return false;
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

    internal void WriteState(BinaryWriter writer, IReadOnlyList<IFormation> formations)
    {
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
    internal void ReadState(BinaryReader reader, IReadOnlyList<IFormation> formations)
    {
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
