using System.Numerics;
using Agro.Collisions;

namespace Agro;

public sealed partial class LaminaTemplate
{
    // Scratch belongs to a worker, never to the shared species template. Node bounds
    // are transformed lazily and survive consecutive queries at the same exact pose.
    sealed class WorldTree
    {
        LaminaTemplate? template;
        OrganPose pose;
        Bvh.BoundingBox[] boxes = [];
        bool[] valid = [];
        public void Set(LaminaTemplate value, in OrganPose frame)
        {
            if (template == value && pose == frame) return;
            template = value; pose = frame;
            if (boxes.Length < value.Triangles.Nodes.Length)
            { boxes = new Bvh.BoundingBox[value.Triangles.Nodes.Length]; valid = new bool[boxes.Length]; }
            else Array.Clear(valid);
        }
        public Bvh.BoundingBox Get(int index)
        {
            if (valid[index]) return boxes[index];
            var local = template!.Triangles.Nodes[index].Bounds;
            var centre = template.Transform(pose, local.Center);
            var e = (local.Max - local.Min) * .5f;
            var extent = RotatedExtent(new(e.X * pose.Length * .5f,
                e.Z * template.ReliefScale(pose.Width), e.Y * pose.Width), pose.Rotation);
            extent += Vector3.Abs(centre) * 4e-7f + new Vector3(1e-8f);
            valid[index] = true;
            return boxes[index] = new(centre - extent, centre + extent);
        }
    }
    [ThreadStatic] static WorldTree? PairTreeA;
    [ThreadStatic] static WorldTree? PairTreeB;
    [ThreadStatic] static Stack<(int A, int B)>? TemplatePairStack;

    internal void QueryPairs(in OrganPose pose, LaminaTemplate other, in OrganPose otherPose,
        float search, List<(int A, int B)> pairs)
    {
        pairs.Clear();
        if (Triangles.IsEmpty || other.Triangles.IsEmpty) return;
        var a = PairTreeA ??= new(); var b = PairTreeB ??= new();
        a.Set(this, pose); b.Set(other, otherPose);
        var stack = TemplatePairStack ??= new(); stack.Clear();
        stack.Push((Triangles.Root, other.Triangles.Root));
        var padding = new Vector3(search);
        while (stack.Count != 0)
        {
            var (ia, ib) = stack.Pop();
            var ba = a.Get(ia); var bb = b.Get(ib);
            if (!new Bvh.BoundingBox(ba.Min - padding, ba.Max + padding).Intersects(bb)) continue;
            ref readonly var na = ref Triangles.Nodes[ia]; ref readonly var nb = ref other.Triangles.Nodes[ib];
            if (na.IsLeaf && nb.IsLeaf) { pairs.Add((na.AgentIndex, nb.AgentIndex)); continue; }
            if (nb.IsLeaf || !na.IsLeaf && (ba.Max - ba.Min).LengthSquared() >= (bb.Max - bb.Min).LengthSquared())
            { stack.Push((na.Left, ib)); stack.Push((na.Right, ib)); }
            else { stack.Push((ia, nb.Left)); stack.Push((ia, nb.Right)); }
        }
        pairs.Sort(static (a, b) => a.A != b.A ? a.A.CompareTo(b.A) : a.B.CompareTo(b.B));
    }
}
