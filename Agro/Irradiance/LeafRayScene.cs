using System.Numerics;
using System.Threading.Tasks;
using AgentsSystem;
using Agro.Collisions;

namespace Agro;

/// <summary>
/// Leaf-only measurement hierarchy. Each organ has one outer entry, while ordinary leaves share
/// their species/slice triangle hierarchy. Keeping this separate leaves the lighting BVH, ray
/// intersections and reflected-light feedback unchanged.
/// </summary>
internal sealed class LeafRayScene
{
    const int ParallelThreshold = 16384;
    const int StackCapacity = 192;
    [ThreadStatic] static int[]? TraversalStack;

    struct Leaf
    {
        public LeafRayMesh Mesh;
        public OrganPose Pose;
        public Vector3 InverseScale;
    }

    Leaf[] Leaves = [];
    BvhItem[] Items = [];
    BvhNode[] Nodes = [];
    int[] PlantFirst = [];
    int[] PlantCounts = [];
    int Count;

    internal int LeafCount => Count;

    internal void Build(IList<IFormation> formations)
    {
        var slots = formations.Count;
        if (PlantCounts.Length < slots)
        {
            PlantCounts = new int[slots];
            PlantFirst = new int[slots];
        }

        // Each plant's lazy geometry cache is accessed only by its own job. Prefix sums retain
        // formation/organ order independently of thread scheduling, births and reindexing.
        Parallel.For(0, slots, slot =>
        {
            var count = 0;
            if (formations[slot] is PlantFormation2 plant && plant.AG.Alive)
                for (int organ = 0; organ < plant.AG.Count; ++organ)
                    if (TryLeafPose(plant.AG, organ, out _)) ++count;
            PlantCounts[slot] = count;
        });

        var previousCount = Count;
        Count = 0;
        for (int slot = 0; slot < slots; ++slot)
        {
            PlantFirst[slot] = Count;
            Count += PlantCounts[slot];
        }
        if (previousCount > Count) Array.Clear(Leaves, Count, previousCount - Count);
        if (Count == 0) return;

        if (Leaves.Length < Count)
        {
            var capacity = Math.Max(Count, Math.Max(64, Leaves.Length + Leaves.Length / 2));
            Leaves = new Leaf[capacity];
            Items = new BvhItem[capacity];
        }

        Parallel.For(0, slots, slot =>
        {
            if (PlantCounts[slot] == 0) return;
            var ag = ((PlantFormation2)formations[slot]).AG;
            var next = PlantFirst[slot];
            for (int organ = 0; organ < ag.Count; ++organ)
            {
                if (!TryLeafPose(ag, organ, out var pose)) continue;
                var template = ag.GetLaminaTemplate(organ)!;
                var mesh = LeafRayMesh.For(template);
                Leaves[next] = new Leaf { Mesh = mesh, Pose = pose, InverseScale = mesh.PrepareInverseScale(in pose) };
                var bounds = template.Bounds(pose);
                Items[next] = new BvhItem { Min = bounds.Min, Max = bounds.Max, Payload = next };
                ++next;
            }
        });

        var budget = BvhBuilder.NodeBudgetFor(Count, ParallelThreshold);
        if (Nodes.Length < budget)
            Nodes = new BvhNode[Math.Max(budget, Nodes.Length + Nodes.Length / 2)];
        BvhBuilder.BuildParallel(Items, 0, Count, Nodes, 0, budget, 4, ParallelThreshold);
    }

    static bool TryLeafPose(PlantSubFormation<AboveGroundAgent> ag, int organ, out OrganPose pose)
    {
        pose = default;
        if (ag.GetOrgan(organ) != OrganTypes.Leaf) return false;
        pose = ag.GetContactPose(organ);
        // Use actual blade dimensions, including microscopic seedlings. The lighting proxy's
        // 0.1 mm clamp would create tissue that is absent from the exported mesh.
        return pose.Length > 0f && float.IsFinite(pose.Length)
            && pose.Width > 0f && float.IsFinite(pose.Width)
            && Finite(pose.Base) && float.IsFinite(pose.Radius)
            && float.IsFinite(pose.Rotation.X) && float.IsFinite(pose.Rotation.Y)
            && float.IsFinite(pose.Rotation.Z) && float.IsFinite(pose.Rotation.W)
            && float.IsFinite(pose.Deformation.X) && float.IsFinite(pose.Deformation.Y)
            && float.IsFinite(pose.Deformation.Z) && float.IsFinite(pose.Deformation.W);
    }

    static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal int CountFoliageCrossings(Vector3 origin, Vector3 direction, float tMin, float tMax,
        out float totalAreaSum)
    {
        totalAreaSum = 0f;
        if (Count == 0 || tMax < tMin || !Finite(origin) || !Finite(direction)) return 0;
        var inverse = new Vector3(direction.X == 0f ? 0f : 1f / direction.X,
            direction.Y == 0f ? 0f : 1f / direction.Y,
            direction.Z == 0f ? 0f : 1f / direction.Z);
        var stack = TraversalStack ??= new int[StackCapacity];
        int sp = 0, crossings = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref readonly var node = ref Nodes[stack[--sp]];
            if (!Hits(node.Min, node.Max, origin, direction, inverse, tMin, tMax)) continue;
            if (node.Count == 0)
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Left + 1;
                continue;
            }
            for (int i = node.Left; i < node.Left + node.Count; ++i)
            {
                ref readonly var item = ref Items[i];
                if (!Hits(item.Min, item.Max, origin, direction, inverse, tMin, tMax)) continue;
                ref readonly var leaf = ref Leaves[item.Payload];
                crossings += leaf.Mesh.CountCrossings(in leaf.Pose, leaf.InverseScale, origin, direction, tMin, tMax, out var area);
                totalAreaSum += area;
            }
        }
        return crossings;
    }

    static bool Hits(Vector3 min, Vector3 max, Vector3 origin, Vector3 direction, Vector3 inverse, float tMin, float tMax)
    {
        var a = (min - origin) * inverse;
        var b = (max - origin) * inverse;
        var low = Vector3.Min(a, b);
        var high = Vector3.Max(a, b);
        // A ray parallel to and on a slab is inside it for the full clipping interval.
        if (direction.X == 0f)
        { if (origin.X < min.X || origin.X > max.X) return false; low.X = float.NegativeInfinity; high.X = float.PositiveInfinity; }
        if (direction.Y == 0f)
        { if (origin.Y < min.Y || origin.Y > max.Y) return false; low.Y = float.NegativeInfinity; high.Y = float.PositiveInfinity; }
        if (direction.Z == 0f)
        { if (origin.Z < min.Z || origin.Z > max.Z) return false; low.Z = float.NegativeInfinity; high.Z = float.PositiveInfinity; }
        return MathF.Max(tMin, MathF.Max(low.X, MathF.Max(low.Y, low.Z)))
            <= MathF.Min(tMax, MathF.Min(high.X, MathF.Min(high.Y, high.Z)));
    }
}
