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
        OrganPose pose, previous;
        bool swept;
        Bvh.BoundingBox[] boxes = [];
        bool[] valid = [];
        public void Set(LaminaTemplate value, in OrganPose frame)
        {
            if (!swept && template == value && pose == frame) return;
            swept=false;
            template = value; pose = frame;
            if (boxes.Length < value.Triangles.Nodes.Length)
            { boxes = new Bvh.BoundingBox[value.Triangles.Nodes.Length]; valid = new bool[boxes.Length]; }
            else Array.Clear(valid);
        }
        public void SetSweep(LaminaTemplate value,in OrganPose from,in OrganPose to)
        {
            if (swept && template==value && previous==from && pose==to) return;
            Set(value,to);previous=from;swept=true;Array.Clear(valid);
        }
        public Bvh.BoundingBox Get(int index)
        {
            if (valid[index]) return boxes[index];
            ref readonly var node = ref template!.Triangles.Nodes[index];
            if (swept)
            {
                Bvh.BoundingBox moving;
                if (node.IsLeaf)
                {
                    var from=new ContactShape(previous,template,node.AgentIndex);
                    moving=from.SweptBounds(new(pose,template,node.AgentIndex));
                    var rounding=Vector3.Max(Vector3.Abs(moving.Min),Vector3.Abs(moving.Max))*4e-7f+new Vector3(1e-8f);
                    moving=new(moving.Min-rounding,moving.Max+rounding);
                }
                else
                {var left=Get(node.Left);var right=Get(node.Right);moving=new(Vector3.Min(left.Min,right.Min),Vector3.Max(left.Max,right.Max));}
                valid[index]=true;return boxes[index]=moving;
            }
            if (pose.Deformation != Vector4.Zero)
            {
                // A whole-blade angular envelope on EVERY facet destroys BVH
                // selectivity in a folded crown. Refit this small worker-local tree
                // from the actual deformed triangles once per queried pose instead.
                Bvh.BoundingBox folded;
                if (node.IsLeaf)
                {
                    template.Triangle(pose,node.AgentIndex,out var a,out var b,out var c);
                    var low=Vector3.Min(a,Vector3.Min(b,c));var high=Vector3.Max(a,Vector3.Max(b,c));
                    var roundoff=Vector3.Max(Vector3.Abs(low),Vector3.Abs(high))*4e-7f+new Vector3(1e-8f);
                    folded=new(low-roundoff,high+roundoff);
                }
                else
                {
                    var left=Get(node.Left);var right=Get(node.Right);
                    folded=new(Vector3.Min(left.Min,right.Min),Vector3.Max(left.Max,right.Max));
                }
                valid[index]=true;
                return boxes[index]=folded;
            }
            var local = node.Bounds;
            var centre = template.Transform(pose with { Deformation = default }, local.Center);
            var e = (local.Max - local.Min) * .5f;
            var extent = RotatedExtent(new(e.X * pose.Length * .5f,
                e.Z * template.ReliefScale(pose.Width), e.Y * pose.Width), pose.Rotation);
            extent += Vector3.Abs(centre) * 4e-7f + new Vector3(1e-8f + template.DeformationReach(pose));
            valid[index] = true;
            return boxes[index] = new(centre - extent, centre + extent);
        }
    }
    [ThreadStatic] static WorldTree? PairTreeA;
    [ThreadStatic] static WorldTree? PairTreeB;
    [ThreadStatic] static Stack<(int A, int B)>? TemplatePairStack;

    /// <param name="maxPairs">Give up, returning false with the list incomplete, once more
    /// candidate pairs than this have been gathered. A caller with a cheaper fallback for large
    /// overlaps (the soft engine's sampled sheet distance) stops paying for the gathering it will
    /// not use.</param>
    internal bool QueryPairs(in OrganPose pose, LaminaTemplate other, in OrganPose otherPose,
        float search, List<(int A, int B)> pairs, int maxPairs = int.MaxValue)
    {
        pairs.Clear();
        if (Triangles.IsEmpty || other.Triangles.IsEmpty) return true;
        var a = PairTreeA ??= new(); var b = PairTreeB ??= new();
        a.Set(this, pose); b.Set(other, otherPose);
        return QueryTrees(a,b,Triangles.Root,other.Triangles.Root,search,pairs,other,maxPairs);
    }

    internal void QuerySelfSweepPairs(in OrganPose from,in OrganPose to,List<(int A,int B)> pairs)
    {
        pairs.Clear();if(Triangles.IsEmpty)return;
        var a=PairTreeA ??= new();var b=PairTreeB ??= new();
        a.SetSweep(this,from,to);b.SetSweep(this,from,to);
        QueryTrees(a,b,Triangles.Root,Triangles.Root,0,pairs,this);
    }

    bool QueryTrees(WorldTree a,WorldTree b,int rootA,int rootB,float search,List<(int A,int B)> pairs,LaminaTemplate other,int maxPairs=int.MaxValue)
    {
        var stack = TemplatePairStack ??= new(); stack.Clear();
        stack.Push((rootA,rootB));
        var padding = new Vector3(search);
        while (stack.Count != 0)
        {
            var (ia, ib) = stack.Pop();
            var ba = a.Get(ia); var bb = b.Get(ib);
            if (!new Bvh.BoundingBox(ba.Min - padding, ba.Max + padding).Intersects(bb)) continue;
            ref readonly var na = ref Triangles.Nodes[ia]; ref readonly var nb = ref other.Triangles.Nodes[ib];
            if (na.IsLeaf && nb.IsLeaf) { pairs.Add((na.AgentIndex, nb.AgentIndex)); if (pairs.Count > maxPairs) return false; continue; }
            if (nb.IsLeaf || !na.IsLeaf && (ba.Max - ba.Min).LengthSquared() >= (bb.Max - bb.Min).LengthSquared())
            { stack.Push((na.Left, ib)); stack.Push((na.Right, ib)); }
            else { stack.Push((ia, nb.Left)); stack.Push((ia, nb.Right)); }
        }
        pairs.Sort(static (a, b) => a.A != b.A ? a.A.CompareTo(b.A) : a.B.CompareTo(b.B));
        return true;
    }
}
