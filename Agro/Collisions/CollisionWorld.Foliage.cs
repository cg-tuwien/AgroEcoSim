using System.Numerics;
using System.Collections.Concurrent;
using AgentsSystem;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    readonly record struct ContactKey(PlantFormation2 A, ulong OrganA, PlantFormation2 B, ulong OrganB);
    readonly record struct RememberedContact(Vector3 Normal, int FeatureA, int FeatureB);
    readonly ConcurrentDictionary<ContactKey, RememberedContact> PersistentContacts = new();
    readonly ConcurrentDictionary<ContactKey, byte> SeenContacts = new();
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
    [ThreadStatic] static List<(int, int)>? OwnPairsBuffer;
    [ThreadStatic] static List<int>? CrossPairHits;
    [ThreadStatic] static List<int>? CrossReachHits;
    readonly Dictionary<PlantFormation2,HashSet<int>> CrossStages = [];
    readonly List<List<(PlantFormation2 A,PlantFormation2 B)>> CrossBatches = [];
    readonly List<int> PairHits = [];
    readonly Bvh PlantBounds = new();
    readonly List<(int, Bvh.BoundingBox)> PlantLeaves = [];
    readonly List<int> TrialPlants = [];
    readonly List<int> TrialOrgans = [];
    readonly Dictionary<PlantFormation2, HashSet<int>> UnresolvedPlants = [];
    readonly ConcurrentDictionary<(PlantFormation2, int), Vector3> RejectionNormals = new();
    public long RejectedMotions { get; private set; }
    public int ActiveFoliageContacts => PersistentContacts.Count;
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
        int ownFound = 0;
        RefreshNeighbours(plants);
        foreach(var plant in plants) if(!PairQueries.ContainsKey(plant)) PairQueries.Add(plant,new());
        void VisitOwn(PlantFormation2 plant)
        {
            var ag = plant.AG;
            var pairs = OwnPairsBuffer ??= [];
            pairs.Clear();
            ag.CollisionBvh.GetOverlappingPairs(pairs, (a, b) => !Connected(ag, a, b), collisionsOnly: false);
            PairQueries[plant].Reserve(pairs.Count);
            var contact = false;
            foreach (var (a, b) in pairs) contact |= VisitPair(plant, a, plant, b, correct, swept);
            if (contact) Interlocked.Exchange(ref ownFound, 1);
        }
        // Within-plant constraints write only that plant. Finish them all before the ordered
        // cross-plant pass; the resulting poses are independent of task scheduling.
        if (plants.Count < 2) foreach (var plant in plants) VisitOwn(plant);
        else Parallel.ForEach(plants, VisitOwn);
        var found = ownFound != 0;
        if (!CollisionSettings.InterPlantPass) return found;
        foreach(var plant in plants)
        {
            if(!CrossStages.TryGetValue(plant,out var stages)) CrossStages.Add(plant,stages=[]);
            stages.Clear();
        }
        foreach(var batch in CrossBatches) batch.Clear();
        foreach(var pair in Neighbours)
        {
            var a=CrossStages[pair.A];var b=CrossStages[pair.B];var stage=0;
            while(a.Contains(stage)||b.Contains(stage)) ++stage;
            while(CrossBatches.Count<=stage) CrossBatches.Add([]);
            CrossBatches[stage].Add(pair);a.Add(stage);b.Add(stage);
        }
        int crossFound=0;
        void VisitCross((PlantFormation2 A,PlantFormation2 B) pair)
        {
            var (pa,pb)=pair;
            var a = pa.AG; var b = pb.AG;
            var reachHits=CrossReachHits??=[];var pairHits=CrossPairHits??=[];
            a.TryGetWorldBounds(out var al, out var ah); b.TryGetWorldBounds(out var bl, out var bh);
            reachHits.Clear(); a.QueryWorldBounds(Vector3.Max(al, bl), Vector3.Min(ah, bh), reachHits);
            var contact=false;
            foreach (var i in reachHits)
            {
                var current = Shape(a, i); var bounds = Previous(a, i, current).SweptBounds(current);
                pairHits.Clear(); b.QueryWorldBounds(bounds.Min, bounds.Max, pairHits);
                foreach (var j in pairHits) contact |= VisitPair(pa, i, pb, j, correct, swept);
            }
            if(contact) Interlocked.Exchange(ref crossFound,1);
        }
        // Greedy edge colouring of the ordered neighbour graph: no two jobs in a batch
        // write the same plant. Every plant receives corrections in a fixed batch order,
        // independent of worker scheduling. Geometry is applied only after all batches.
        foreach(var batch in CrossBatches)
            if(batch.Count<2) foreach(var pair in batch) VisitCross(pair);
            else Parallel.ForEach(batch,VisitCross);
        return found || crossFound!=0;
    }

    bool VisitPair(PlantFormation2 pa, int i, PlantFormation2 pb, int j, bool correct, bool swept)
    {
        var a = pa.AG; var b = pb.AG;
        if (!a.IsCollisionEligiblePublic(i) || !b.IsCollisionEligiblePublic(j)) return false;
        Interlocked.Increment(ref InterPlantCandidates);
        var sa = Shape(a, i); var sb = Shape(b, j);
        var oldA = Previous(a, i, sa); var oldB = Previous(b, j, sb);
        var reach = sa.Pose.Radius + sb.Pose.Radius;
        var stop = pa == pb ? CommonAncestor(a, i, j) : -1;
        var started = TraceSlowPairs ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var hit = CachedPairDistance(pa,i,pb,j,stop,sa,sb,reach+FoliageGeometry.Skin,false);
        var key = new ContactKey(pa, a.GetOrganId(i), pb, b.GetOrganId(j));
        var touching = hit.Distance < reach + FoliageGeometry.Skin;
        var oldReach = oldA.Pose.Radius + oldB.Pose.Radius;
        var oldContact = swept ? oldA==sa && oldB==sb ? hit
            : CachedPairDistance(pa,i,pb,j,stop,oldA,oldB,oldReach+FoliageGeometry.Skin,true) : default;
        var initialOverlap = swept && oldContact.Distance < oldReach - FoliageGeometry.Skin;
        var movement = oldA.MotionBound(sa)+oldB.MotionBound(sb);
        var sweptReach = MathF.Max(oldA.Pose.Radius,sa.Pose.Radius)+MathF.Max(oldB.Pose.Radius,sb.Pose.Radius);
        var separatedPath = stop<0 && oldContact.Distance >= sweptReach+movement+FoliageGeometry.Skin;
        var crossing = swept && !initialOverlap && !separatedPath && !RelativeSweepClear(a, stop, oldA, sa, oldB, sb);
        if (TraceSlowPairs)
        {
            var ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(ms>5)Console.WriteLine($"SLOW_PAIR t={pa.World.Timestep} {pa.Parameters.Name} {i}:{a.GetOrgan(i)} / {pb.Parameters.Name} {j}:{b.GetOrgan(j)} common={stop} swept={swept} d={hit.Distance} reach={reach} motion={oldA.MotionBound(sa)+oldB.MotionBound(sb)} crossing={crossing} ms={ms:F2}");
        }
        if (!touching && !crossing) return false;
        var normal = ContactNormal(hit, oldA.Pose.Base - oldB.Pose.Base,
            PersistentContacts.TryGetValue(key, out var memory) ? memory.Normal : null);
        if (touching)
        {
            SeenContacts.TryAdd(key, 0);
            PersistentContacts[key] = new(normal, hit.FeatureA, hit.FeatureB);
        }
        var penetration = hit.Distance < reach - FoliageGeometry.Skin;
        if (!penetration && !crossing) return false;
        if (TraceContacts && InterPlantCandidates < 2000)
            Console.WriteLine($"CONTACT t={pa.World.Timestep} {i}:{a.GetOrgan(i)} p={a.GetParent(i)} L={sa.Pose.Length} / {j}:{b.GetOrgan(j)} p={b.GetParent(j)} L={sb.Pose.Length} d={hit.Distance} oldD={oldContact.Distance} reach={reach} swept={crossing} motion={movement} oldA={oldA.Pose} A={sa.Pose} oldB={oldB.Pose} B={sb.Pose}");
        if (!correct)
        {
            var rootA = RejectionRoot(a, i, stop); var rootB = RejectionRoot(b, j, stop);
            Reject(pa, rootA); Reject(pb, rootB);
            RejectionNormals.TryAdd((pa, rootA), normal); RejectionNormals.TryAdd((pb, rootB), -normal);
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
        Interlocked.Increment(ref InterPlantContacts);
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
        var query=entry.Query;
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
        var hit=whole ? FoliageGeometry.Distance(a,b,search+(common<0?.005f:0f))
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
            foreach (var pi in TrialPlants)
            {
                var other = ActivePlants[pi].AG;
                TrialOrgans.Clear(); other.QueryWorldBounds(bounds.Min, bounds.Max, TrialOrgans);
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
        if (plant.AG.TryGetWorldBounds(out var lo,out var hi))
            PlantBounds.RefitLeaf(ActivePlants.IndexOf(plant),new(lo,hi));
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
            foreach(var plant in ActivePlants)
            {
                var other=plant.AG;
                PairHits.Clear();other.QueryWorldBounds(shape.Bounds.Min,shape.Bounds.Max,PairHits);
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
        HashSet<int> rejected;
        lock (UnresolvedPlants)
            if (!UnresolvedPlants.TryGetValue(plant, out rejected!)) UnresolvedPlants.Add(plant, rejected = []);
        rejected.Add(organ);
    }

    void ResolveVegetation(List<PlantFormation2> plants)
    {
        SeenContacts.Clear();
        for (int round = 0; round < CollisionSettings.HardRounds; ++round)
        {
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
            ExpandRejection = pass > 0;
            UnresolvedPlants.Clear();
            RejectionNormals.Clear();
            if (!VisitPairs(plants, correct: false, swept: true)) break;
            foreach (var plant in plants)
            {
                if (!UnresolvedPlants.TryGetValue(plant, out var rejected)) continue;
                foreach (var i in rejected.ToArray())
                {
                    if (pass == 0)
                    {
                        RejectionNormals.TryGetValue((plant, i), out var normal);
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
            UnresolvedPlants.Clear(); RejectionNormals.Clear();
            VisitPairs(plants,correct:false,swept:true);
            foreach (var plant in plants)
                for (int i=0; i<plant.AG.Count; ++i)
                    if (!StaticAdmissible(plant,i)) Reject(plant,i);
            if (UnresolvedPlants.Count==0) break;
            var changed = false;
            foreach (var plant in plants)
            {
                if (!UnresolvedPlants.TryGetValue(plant,out var rejected)) continue;
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
                UnresolvedPlants.Clear(); RejectionNormals.Clear();
                VisitPairs(plants, correct:false, swept:true);
                UnresolvedFoliageContacts=UnresolvedPlants.Sum(p=>p.Value.Count);
                break;
            }
        }
        if (restoredComponents.Count>0)
            foreach (var plant in plants) RefreshContactLoads(plant);
        foreach (var key in PersistentContacts.Keys.Where(k => !SeenContacts.ContainsKey(k)).ToArray())
        {
            PersistentContacts.TryRemove(key, out _);
            var a = key.A.AG.FindOrgan(key.OrganA); var b = key.B.AG.FindOrgan(key.OrganB);
            if (a >= 0) key.A.AG.WakeGrowth(a);
            if (b >= 0) key.B.AG.WakeGrowth(b);
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
        foreach (var plant in ActivePlants)
        {
            var other = plant.AG;
            if (!other.TryGetWorldBounds(out var lo, out var hi) || !bounds.Intersects(new(lo, hi))) continue;
            PairHits.Clear(); other.QueryWorldBounds(bounds.Min, bounds.Max, PairHits);
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
        var contacts = PersistentContacts.OrderBy(x => indices[x.Key.A]).ThenBy(x => x.Key.OrganA)
            .ThenBy(x => indices[x.Key.B]).ThenBy(x => x.Key.OrganB).ToArray();
        writer.Write(contacts.Length);
        foreach (var (key, value) in contacts)
        {
            writer.Write(indices[key.A]); writer.Write(key.OrganA); writer.Write(indices[key.B]); writer.Write(key.OrganB);
            PlantSubFormation<AboveGroundAgent>.WriteValue(writer, value);
        }
        writer.Write(RejectedMotions);
    }
    internal void ReadState(BinaryReader reader, IReadOnlyList<IFormation> formations)
    {
        PersistentContacts.Clear(); var count = reader.ReadInt32();
        for (int i = 0; i < count; ++i)
        {
            var key = new ContactKey((PlantFormation2)formations[reader.ReadInt32()], reader.ReadUInt64(),
                (PlantFormation2)formations[reader.ReadInt32()], reader.ReadUInt64());
            PersistentContacts.TryAdd(key, PlantSubFormation<AboveGroundAgent>.ReadValue<RememberedContact>(reader));
        }
        RejectedMotions = reader.ReadInt64();
    }
}
