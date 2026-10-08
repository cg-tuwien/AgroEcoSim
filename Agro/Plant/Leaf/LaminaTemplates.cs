using System.Numerics;
using System.Runtime.CompilerServices;
using Agro.Collisions;

namespace Agro;

/// <summary>The same immutable, coarse-outline templates used by playback and contact geometry.</summary>
public static class LaminaTemplates
{
    sealed record CachedMesh(MeshData Mesh, float WidthScale);
    static readonly ConditionalWeakTable<LeafMorphology, CachedMesh> Meshes = new();
    static readonly ConditionalWeakTable<SpeciesSettings, LaminaTemplate[]> Leaves = new();
    static readonly ConditionalWeakTable<SpeciesSettings, LaminaTemplate[]> Petals = new();

    // Serve the authored vertices unchanged: recorded V7 matrices already encode their scale.
    public static MeshData Build(LeafMorphology morphology) => GetMesh(morphology).Mesh;

    static CachedMesh GetMesh(LeafMorphology morphology) => Meshes.GetValue(morphology, static m =>
    {
        if (!m.TryGetDefaultLeafMesh(RenderLeafLod.CoarseOutline, out var mesh))
        {
            var leaf = new LeafInstance { Morphology = m, Physiology = new() };
            leaf.InitializeLeafletsIfNeeded();
            mesh = LeafMeshBuilder.Build(leaf, RenderLeafLod.CoarseOutline);
        }
        // Radius is half the full blade width. Authored silhouettes need not span [-1, 1]:
        // Geranium spans 3.53, making a nominal 7 cm blade 12.4 cm wide without this correction.
        // Measure the WHOLE blade once, so a tapered slice keeps its share of that width.
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        foreach (var vertex in mesh.Positions)
        {
            min = MathF.Min(min, vertex.Y);
            max = MathF.Max(max, vertex.Y);
        }
        var width = max - min;
        var correction = width > 1e-12f && float.IsFinite(width) ? 2f / width : 1f;
        return new(mesh, correction);
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
        var cached = GetMesh(morphology);
        var mesh = !petals && species.ContactDeformationMode == ContactDeformationMode.BroadLeafV1
            ? ContactDeformationBinding.For(morphology) : cached.Mesh;
        var result = new LaminaTemplate[count];
        for (int i = 0; i < count; ++i)
        {
            result[i] = new(LaminaMeshSlicer.Slice(mesh, i, count), morphology.Dimensions.MaxWidthM * .5f,
                !petals && count == 1, morphology.Dimensions.LengthM, petals ? 1f : cached.WidthScale);
            if (!petals && species.ContactDeformationMode == ContactDeformationMode.BroadLeafV1) result[i].BindConnectedSeams();
        }
        return result;
    }
}

public sealed partial class LaminaTemplate
{
    // Collision vertices carry the transverse correction; the served mesh does not.
    public readonly Vector3[] Vertices;
    public readonly int[] Indices;
    /// <summary>Correction applied to the authored mesh's transverse axis in new exports.</summary>
    public readonly float WidthScale;
    readonly Bvh Triangles = new();
    [ThreadStatic] static Stack<int>? QueryStack;
    readonly Vector3 Min, Max, Centre, Extent;
    public readonly float LongReach, WideReach, ReliefReach;
    readonly float ReferenceHalfWidth;
    readonly float[] SupportX, SupportY, SupportZ;
    internal Vector3 TemplateCentre => Centre;
    internal Vector3 TemplateExtent => Extent;
    internal float TemplateReferenceHalfWidth => ReferenceHalfWidth;

    public LaminaTemplate(MeshData mesh, float referenceHalfWidth = 0,
        bool allowCollisionApproximation = false, float referenceLength = 0, float widthScale = 1f)
    {
        ReferenceHalfWidth = referenceHalfWidth;
        WidthScale = widthScale;
        Vertices = mesh.Positions.ToArray(); Indices = mesh.Indices.ToArray();
        for (int i = 0; i < Vertices.Length; ++i)
            Vertices[i].Y *= widthScale;
        // Do not recenter asymmetric outlines, move the petiole junction, or rescale relief.
        // Bounds, support points, BVHs and simplified collision levels all use these copies.
        SupportX = new float[Vertices.Length]; SupportY = new float[Vertices.Length]; SupportZ = new float[Vertices.Length];
        for (int i = 0; i < Vertices.Length; ++i)
        { SupportX[i] = Vertices[i].X + 1f; SupportY[i] = Vertices[i].Z; SupportZ[i] = -Vertices[i].Y; }
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
        BuildDeformationBinding();
        if (allowCollisionApproximation && referenceHalfWidth > 0 && referenceLength > 0)
            CollisionLevels = new(() => BuildCollisionLevels(referenceLength), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // Relief is authored in metres at the morphology's reference width. It must grow
    // with the blade: unit relief gave a micrometre seedling a mature leaf's curvature.
    public float ReliefScale(float halfWidth) => ReferenceHalfWidth > 0 ? halfWidth / ReferenceHalfWidth : 1f;
    internal float ReliefAt(in OrganPose pose) => ReliefReach * ReliefScale(pose.Width);
    internal float ReliefChange(in OrganPose a, in OrganPose b)
        => ReliefReach * MathF.Abs(ReliefScale(a.Width) - ReliefScale(b.Width));
    public Vector3 LocalPoint(in OrganPose pose, Vector3 v) =>
        new((v.X + 1f) * pose.Length * .5f, v.Z * ReliefScale(pose.Width), -v.Y * pose.Width);

    public Vector3 Transform(in OrganPose pose, Vector3 v) => pose.Base + Vector3.Transform(
        pose.Deformation == Vector4.Zero ? LocalPoint(pose,v) : ContactDeformation.BladePoint(LocalPoint(pose, v), pose.Length,
            pose.Deformation, MidribHeight(v.X) * ReliefScale(pose.Width)), pose.Rotation);

    public Vector3 TransformVertex(in OrganPose pose,int vertex) => pose.Base + Vector3.Transform(
        pose.Deformation == Vector4.Zero ? LocalPoint(pose,Vertices[vertex]) : ContactDeformation.BladePoint(LocalPoint(pose,Vertices[vertex]),
            pose.Length,pose.Deformation,HingeHeights[vertex]*ReliefScale(pose.Width)),pose.Rotation);

    internal float DeformationReach(in OrganPose pose) => ContactDeformation.MotionBound(
        LongReach * pose.Length, WideReach * pose.Width + ReliefAt(pose), Vector4.Zero, pose.Deformation);


    public void Triangle(in OrganPose pose, int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        // Three direct vector transforms are cheaper than hashing a full OrganPose
        // at every subdivided contact feature (including alternating leaf poses).
        if (pose.Deformation != Vector4.Zero)
        {
            var cached = DeformedVertices(pose);
            a = CachedVertex(cached, Indices[triangle * 3]);
            b = CachedVertex(cached, Indices[triangle * 3 + 1]);
            c = CachedVertex(cached, Indices[triangle * 3 + 2]);
            return;
        }
        a = TransformVertex(pose, Indices[triangle * 3]);
        b = TransformVertex(pose, Indices[triangle * 3 + 1]);
        c = TransformVertex(pose, Indices[triangle * 3 + 2]);
    }

    public Bvh.BoundingBox Bounds(in OrganPose pose)
    {
        var rest = pose with { Deformation = default };
        var centre=Transform(rest,Centre);
        var extent=RotatedExtent(new(Extent.X*pose.Length*.5f,Extent.Z*ReliefScale(pose.Width),Extent.Y*pose.Width),pose.Rotation);
        // Round outward as well as adding physical thickness: at facade coordinates the
        // centre/extent form can round differently from transforming individual vertices.
        extent+=new Vector3(pose.Radius + DeformationReach(pose))+Vector3.Abs(centre)*2e-7f+new Vector3(1e-8f);
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
        radius += DeformationReach(pose);
        low=mid-radius;high=mid+radius;
    }

    public void Query(in OrganPose pose, Bvh.BoundingBox world, List<int> hits)
    {
        var deformationPad = new Vector3(DeformationReach(pose));
        world = new(world.Min - deformationPad, world.Max + deformationPad);
        var inverse = Quaternion.Conjugate(pose.Rotation);
        var length = MathF.Max(1e-12f, pose.Length);
        var width = MathF.Max(1e-12f, pose.Width);
        var relief = MathF.Max(1e-12f, ReliefScale(pose.Width));
        // The inverse of a box needs one centre transform and |R| times its extents,
        // rather than eight quaternion transforms per triangle query. Round outward
        // before scaling, particularly for tiny blades at large facade coordinates.
        var centre = Vector3.Transform((world.Min + world.Max) * .5f - pose.Base, inverse);
        var padding = (Vector3.Max(Vector3.Abs(world.Min), Vector3.Abs(world.Max))
            + Vector3.Abs(pose.Base)) * 4e-7f + new Vector3(1e-8f);
        var extent = RotatedExtent((world.Max - world.Min) * .5f + padding, inverse);
        var lo = centre - extent; var hi = centre + extent;
        var low = new Vector3(lo.X * 2f / length - 1f, -hi.Z / width, lo.Y / relief);
        var high = new Vector3(hi.X * 2f / length - 1f, -lo.Z / width, hi.Y / relief);
        hits.Clear();
        // Immutable templates are shared; scratch belongs to the worker so thousands of
        // plants can query the same species without serializing every triangle lookup.
        Triangles.QueryOverlaps(new(low, high), hits, QueryStack ??= new());
        // A hierarchy rebuild/refit must not change the first penetrating feature used by
        // the response or the finite continuous-query budget.
        hits.Sort();
    }

    public float MotionBound(in OrganPose a, in OrganPose b)
    {
        var delta = Quaternion.Normalize(b.Rotation * Quaternion.Conjugate(a.Rotation));
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return Vector3.Distance(a.Base, b.Base) + LongReach * MathF.Abs(a.Length - b.Length)
            + WideReach * MathF.Abs(a.Width - b.Width) + ReliefChange(a,b) + MathF.Abs(a.Radius - b.Radius)
            + angle * (LongReach * MathF.Max(a.Length, b.Length) + WideReach * MathF.Max(a.Width, b.Width)
                + MathF.Max(ReliefAt(a),ReliefAt(b)))
            + ContactDeformation.MotionBound(LongReach * MathF.Max(a.Length, b.Length),
                WideReach * MathF.Max(a.Width, b.Width) + MathF.Max(ReliefAt(a), ReliefAt(b)), a.Deformation, b.Deformation);
    }
}
