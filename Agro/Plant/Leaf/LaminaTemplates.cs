using System.Numerics;
using System.Runtime.CompilerServices;
using Agro.Collisions;

namespace Agro;

/// <summary>The same immutable, coarse-outline templates used by playback and contact geometry.</summary>
public static class LaminaTemplates
{
    static readonly ConditionalWeakTable<LeafMorphology, MeshData> Meshes = new();
    static readonly ConditionalWeakTable<SpeciesSettings, LaminaTemplate[]> Leaves = new();
    static readonly ConditionalWeakTable<SpeciesSettings, LaminaTemplate[]> Petals = new();

    public static MeshData Build(LeafMorphology morphology) => Meshes.GetValue(morphology, m =>
    {
        if (m.TryGetDefaultLeafMesh(RenderLeafLod.CoarseOutline, out var authored)) return authored;
        var leaf = new LeafInstance { Morphology = m, Physiology = new() };
        leaf.InitializeLeafletsIfNeeded();
        return LeafMeshBuilder.Build(leaf, RenderLeafLod.CoarseOutline);
    });

    public static LaminaTemplate? For(SpeciesSettings species, OrganTypes organ, int slice = 0)
    {
        if (organ is not (OrganTypes.Leaf or OrganTypes.FlowerPadel)) return null;
        var petals = organ == OrganTypes.FlowerPadel;
        // This lookup runs for every candidate pair. A factory capturing `petals` allocated
        // a closure and delegate even on a cache hit, generating gigabytes of temporary data.
        var templates = petals
            ? Petals.GetValue(species, static s => BuildTemplates(s, true))
            : Leaves.GetValue(species, static s => BuildTemplates(s, false));
        return templates[Math.Clamp(slice, 0, templates.Length - 1)];
    }

    static LaminaTemplate[] BuildTemplates(SpeciesSettings species, bool petals)
    {
        var count = petals ? 1 : Math.Max(1, species.LeafSegmentCount);
        var morphology = petals ? species.FlowerSettings.PetalMorphology : species.LeafMorphology;
        var mesh = Build(morphology);
        var result = new LaminaTemplate[count];
        for (int i = 0; i < count; ++i)
            result[i] = new(LaminaMeshSlicer.Slice(mesh, i, count), morphology.Dimensions.MaxWidthM * .5f);
        return result;
    }
}

public sealed class LaminaTemplate
{
    public readonly Vector3[] Vertices;
    public readonly int[] Indices;
    readonly Bvh Triangles = new();
    [ThreadStatic] static Stack<int>? QueryStack;
    readonly Vector3 Min, Max, Centre, Extent;
    public readonly float LongReach, WideReach, ReliefReach;
    readonly float ReferenceHalfWidth;

    public LaminaTemplate(MeshData mesh, float referenceHalfWidth = 0)
    {
        ReferenceHalfWidth = referenceHalfWidth;
        Vertices = mesh.Positions.ToArray(); Indices = mesh.Indices.ToArray();
        Min = Vertices.Length == 0 ? Vector3.Zero : Vertices.Aggregate(Vector3.Min);
        Max = Vertices.Length == 0 ? Vector3.Zero : Vertices.Aggregate(Vector3.Max);
        Centre=(Min+Max)*.5f; Extent=(Max-Min)*.5f;
        LongReach = MathF.Max(MathF.Abs(Min.X + 1), MathF.Abs(Max.X + 1)) * 0.5f;
        WideReach = MathF.Max(MathF.Abs(Min.Y), MathF.Abs(Max.Y));
        ReliefReach = MathF.Max(MathF.Abs(Min.Z), MathF.Abs(Max.Z));
        var bounds = new (int, Bvh.BoundingBox)[Indices.Length / 3];
        for (int i = 0; i < bounds.Length; ++i)
        {
            var a = Vertices[Indices[3 * i]]; var b = Vertices[Indices[3 * i + 1]]; var c = Vertices[Indices[3 * i + 2]];
            bounds[i] = (i, new(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c))));
        }
        Triangles.Build(bounds);
    }

    // Relief is authored in metres at the morphology's reference width. It must grow
    // with the blade: unit relief gave a micrometre seedling a mature leaf's curvature.
    public float ReliefScale(float halfWidth) => ReferenceHalfWidth > 0 ? halfWidth / ReferenceHalfWidth : 1f;
    internal float ReliefAt(in OrganPose pose) => ReliefReach * ReliefScale(pose.Width);
    internal float ReliefChange(in OrganPose a, in OrganPose b)
        => ReliefReach * MathF.Abs(ReliefScale(a.Width) - ReliefScale(b.Width));
    public Vector3 Transform(in OrganPose pose, Vector3 v) => pose.Base + Vector3.Transform(
        new Vector3((v.X + 1f) * pose.Length * 0.5f, v.Z * ReliefScale(pose.Width), -v.Y * pose.Width), pose.Rotation);

    public void Triangle(in OrganPose pose, int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
    { a = Transform(pose, Vertices[Indices[triangle * 3]]); b = Transform(pose, Vertices[Indices[triangle * 3 + 1]]); c = Transform(pose, Vertices[Indices[triangle * 3 + 2]]); }

    public Bvh.BoundingBox Bounds(in OrganPose pose)
    {
        var centre=Transform(pose,Centre);
        var extent=RotatedExtent(new(Extent.X*pose.Length*.5f,Extent.Z*ReliefScale(pose.Width),Extent.Y*pose.Width),pose.Rotation);
        // Round outward as well as adding physical thickness: at facade coordinates the
        // centre/extent form can round differently from transforming individual vertices.
        extent+=new Vector3(pose.Radius)+Vector3.Abs(centre)*2e-7f+new Vector3(1e-8f);
        return new(centre-extent,centre+extent);
    }

    static Vector3 RotatedExtent(Vector3 extent,Quaternion rotation)
    {
        var m=Matrix4x4.CreateFromQuaternion(rotation);
        return new(MathF.Abs(m.M11)*extent.X+MathF.Abs(m.M21)*extent.Y+MathF.Abs(m.M31)*extent.Z,
            MathF.Abs(m.M12)*extent.X+MathF.Abs(m.M22)*extent.Y+MathF.Abs(m.M32)*extent.Z,
            MathF.Abs(m.M13)*extent.X+MathF.Abs(m.M23)*extent.Y+MathF.Abs(m.M33)*extent.Z);
    }

    internal void ProjectBounds(in OrganPose pose, Vector3 direction, out float low, out float high)
    {
        var centre=Centre;
        var extent=Extent;
        var local=Vector3.Transform(direction,Quaternion.Conjugate(pose.Rotation));
        var mid=Vector3.Dot(pose.Base,direction)+local.X*(centre.X+1)*pose.Length*.5f
            +local.Y*centre.Z*ReliefScale(pose.Width)-local.Z*centre.Y*pose.Width;
        var radius=MathF.Abs(local.X)*extent.X*pose.Length*.5f+MathF.Abs(local.Y)*extent.Z*ReliefScale(pose.Width)
            +MathF.Abs(local.Z)*extent.Y*pose.Width;
        low=mid-radius;high=mid+radius;
    }

    public void Query(in OrganPose pose, Bvh.BoundingBox world, List<int> hits)
    {
        var low = new Vector3(float.PositiveInfinity); var high = new Vector3(float.NegativeInfinity);
        var inverse = Quaternion.Conjugate(pose.Rotation);
        for (int i = 0; i < 8; ++i)
        {
            var v = Vector3.Transform(new Vector3((i & 1) == 0 ? world.Min.X : world.Max.X,
                (i & 2) == 0 ? world.Min.Y : world.Max.Y, (i & 4) == 0 ? world.Min.Z : world.Max.Z) - pose.Base, inverse);
            v = new(v.X * 2f / MathF.Max(1e-12f, pose.Length) - 1f, -v.Z / MathF.Max(1e-12f, pose.Width),
                v.Y / MathF.Max(1e-12f, ReliefScale(pose.Width)));
            low = Vector3.Min(low, v); high = Vector3.Max(high, v);
        }
        hits.Clear();
        // Immutable templates are shared; scratch belongs to the worker so thousands of
        // plants can query the same species without serializing every triangle lookup.
        Triangles.QueryOverlaps(new(low, high), hits, QueryStack ??= new());
    }

    public float MotionBound(in OrganPose a, in OrganPose b)
    {
        var delta = Quaternion.Normalize(b.Rotation * Quaternion.Conjugate(a.Rotation));
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return Vector3.Distance(a.Base, b.Base) + LongReach * MathF.Abs(a.Length - b.Length)
            + WideReach * MathF.Abs(a.Width - b.Width) + ReliefChange(a,b) + MathF.Abs(a.Radius - b.Radius)
            + angle * (LongReach * MathF.Max(a.Length, b.Length) + WideReach * MathF.Max(a.Width, b.Width)
                + MathF.Max(ReliefAt(a),ReliefAt(b)));
    }
}
