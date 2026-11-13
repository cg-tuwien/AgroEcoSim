using Godot;
using GodotFrontend.Core.Packing;

namespace GodotFrontend.App.Render;

/// <summary>
/// One MultiMeshInstance3D per bucket (geometry x material x toggle category). Applying a packed frame is one
/// buffer upload per bucket: the bucket's float array already has Godot's layout (12 transform floats as rows, 4
/// colour, 4 custom), so no per-instance engine calls happen. Capacity changes reallocate the MultiMesh (which
/// zero-fills), so capacity only grows and the upload length is always capacity x stride. Visibility is a flag on
/// the node per category, so the toggles never touch the data.
/// </summary>
public sealed class PlantBatches
{
    private sealed class BucketNode
    {
        public MultiMeshInstance3D Instance = null!;
        public MultiMesh MultiMesh = null!;
        public Rid Rid;
        public int Capacity;
        public int GeometryId;
        public int GeometryVersion;
    }

    private readonly Node3D root;
    private readonly GeometryUploader geometry;
    private readonly MaterialSet materials;
    private readonly Dictionary<int, BucketNode> nodes = new();
    private readonly bool[] categoryVisible = { true, true, true, true, true };

    public int InstanceCount { get; private set; }
    public long TriangleCount { get; private set; }

    public PlantBatches(Node3D root, GeometryUploader geometry, MaterialSet materials)
    {
        this.root = root;
        this.geometry = geometry;
        this.materials = materials;
    }

    public void SetVisible(Category category, bool visible)
    {
        categoryVisible[(int)category] = visible;
        foreach (var (packed, node) in nodes)
            if (BucketKey.Unpack(packed).Category == category) node.Instance.Visible = visible;
    }

    public bool IsVisible(Category category) => categoryVisible[(int)category];

    public void Apply(PackedFrame frame)
    {
        foreach (var (packed, bucket) in frame.Buckets)
        {
            if (!nodes.TryGetValue(packed, out var node))
            {
                node = Create(bucket.Key);
                nodes[packed] = node;
            }
            // geometry re-registered under the same id (a species mesh arrived from REST): re-point the multimesh
            var version = geometry.VersionOf(bucket.Key.GeometryId);
            if (node.GeometryVersion != version)
            {
                node.MultiMesh.Mesh = geometry.Get(bucket.Key.GeometryId);
                node.GeometryVersion = version;
            }
            if (bucket.Count == 0)
            {
                node.MultiMesh.VisibleInstanceCount = 0;
                continue;
            }
            if (node.Capacity != bucket.Capacity)
            {
                node.MultiMesh.InstanceCount = bucket.Capacity; // reallocates and zero-fills
                node.Capacity = bucket.Capacity;
            }
            // the custom AABB first: without it the server recomputes the bounds over every instance on upload
            var min = new Vector3(frame.SceneMin[0], frame.SceneMin[1], frame.SceneMin[2]);
            var max = new Vector3(frame.SceneMax[0], frame.SceneMax[1], frame.SceneMax[2]);
            var pad = new Vector3(0.5f, 0.5f, 0.5f);
            node.MultiMesh.CustomAabb = new Aabb(min - pad, max - min + pad * 2);
            RenderingServer.MultimeshSetBuffer(node.Rid, bucket.Data);
            node.MultiMesh.VisibleInstanceCount = bucket.Count;
        }
        foreach (var (packed, node) in nodes)
            if (!frame.Buckets.ContainsKey(packed))
                node.MultiMesh.VisibleInstanceCount = 0;
        InstanceCount = frame.InstanceCount;
        TriangleCount = frame.TriangleCount;
    }

    public void Clear()
    {
        foreach (var node in nodes.Values) node.MultiMesh.VisibleInstanceCount = 0;
        InstanceCount = 0;
        TriangleCount = 0;
    }

    private BucketNode Create(BucketKey key)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = geometry.Get(key.GeometryId),
        };
        var inst = new MultiMeshInstance3D
        {
            Name = $"bucket_{key.GeometryId}_{key.Variant}_{key.Category}",
            Multimesh = mm,
            MaterialOverride = materials.For(key.Variant),
            Visible = categoryVisible[(int)key.Category],
            CastShadow = key.Variant is BucketVariant.Foliage or BucketVariant.FoliageFolded or BucketVariant.LaminaOpaque or BucketVariant.Fruit
                ? GeometryInstance3D.ShadowCastingSetting.DoubleSided
                : GeometryInstance3D.ShadowCastingSetting.On,
        };
        root.AddChild(inst);
        return new BucketNode
        {
            Instance = inst,
            MultiMesh = mm,
            Rid = mm.GetRid(),
            Capacity = 0,
            GeometryId = key.GeometryId,
            GeometryVersion = geometry.VersionOf(key.GeometryId),
        };
    }
}
