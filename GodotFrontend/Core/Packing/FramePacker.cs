using GodotFrontend.Core.Color;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Packing;

/// <summary>
/// DecodedScene to MultiMesh buckets (the BuildScene.ts pass): one instance per drawn primitive in scene order, the
/// geometry resolved per organ (recorded asset meshes win over the per-species REST meshes, which win over the unit
/// plane), the natural colour computed here, the readout stats packed into the custom data so a mapping change is a
/// shader uniform rather than a rebuild. Visibility is a property of the bucket's category, never of the data.
///
/// Two parallel passes over chunks of plants: the first resolves every primitive's bucket and counts per chunk, a
/// prefix sum gives each chunk its write offset in every bucket (which keeps scene order within a bucket), the
/// second writes the 20 floats per instance.
/// </summary>
public sealed class FramePacker
{
    private readonly GeometryLibrary lib;
    private int[] keyOf = Array.Empty<int>();

    public FramePacker(GeometryLibrary lib)
    {
        this.lib = lib;
    }

    private readonly struct PlantGeometry
    {
        public readonly RecordedAsset? Asset;
        public readonly int[]? Leaves;
        public readonly int? Petal;
        public PlantGeometry(RecordedAsset? asset, int[]? leaves, int? petal) { Asset = asset; Leaves = leaves; Petal = petal; }
    }

    public PackedFrame Pack(DecodedScene s, PackedFrame? reuse = null)
    {
        var f = reuse ?? new PackedFrame();
        f.ResetCounts();
        Array.Copy(s.SceneMin, f.SceneMin, 3);
        Array.Copy(s.SceneMax, f.SceneMax, 3);
        var n = s.Count;
        if (f.GeometryOf.Length < n) f.GeometryOf = new int[System.Math.Max(n, f.GeometryOf.Length * 3 / 2)];
        if (keyOf.Length < n) keyOf = new int[System.Math.Max(n, keyOf.Length * 3 / 2)];
        if (n == 0) return f;

        var assets = new RecordedAsset?[s.Assets.Count];
        for (var a = 0; a < assets.Length; ++a) assets[a] = lib.Asset(s.Assets[a]);
        var bySpecies = new PlantGeometry[s.Species.Count];
        for (var sp = 0; sp < s.Species.Count; ++sp)
            bySpecies[sp] = new PlantGeometry(null, lib.LeafSliceIds(s.Species[sp]), lib.PetalId(s.Species[sp]));

        // chunks of whole plants, about n / (4 x cores) primitives each
        var target = System.Math.Max(4096, n / (Environment.ProcessorCount * 4));
        var chunkStart = new List<int> { 0 };
        var acc = 0;
        for (var p = 0; p < s.PlantCount; ++p)
        {
            acc += s.PlantDrawCount[p];
            if (acc >= target && p + 1 < s.PlantCount) { chunkStart.Add(s.PlantFirst[p + 1]); acc = 0; }
        }
        chunkStart.Add(n);
        var chunks = chunkStart.Count - 1;
        var counts = new Dictionary<int, int>[chunks];
        var geometryOf = f.GeometryOf;
        var keys = keyOf;

        // pass A: bucket of every primitive, counted per chunk
        Parallel.For(0, chunks, c =>
        {
            var local = new Dictionary<int, int>();
            for (var i = chunkStart[c]; i < chunkStart[c + 1]; ++i)
            {
                var p = s.Plant[i];
                var asset = s.PlantAsset[p] >= 0 ? assets[s.PlantAsset[p]] : null;
                var key = Resolve(s, i, asset, bySpecies[s.PlantSpecies[p]], out var geom);
                geometryOf[i] = geom;
                keys[i] = key;
                if (key < 0) continue;
                local[key] = local.TryGetValue(key, out var v) ? v + 1 : 1;
            }
            counts[c] = local;
        });

        // offsets: per bucket, chunk by chunk
        var offsets = new Dictionary<int, int>[chunks];
        for (var c = 0; c < chunks; ++c) offsets[c] = new Dictionary<int, int>();
        var totals = new Dictionary<int, int>();
        for (var c = 0; c < chunks; ++c)
            foreach (var (key, count) in counts[c])
            {
                var start = totals.TryGetValue(key, out var t) ? t : 0;
                offsets[c][key] = start;
                totals[key] = start + count;
            }
        foreach (var (key, total) in totals)
        {
            var b = f.GetOrAdd(BucketKey.Unpack(key), total);
            b.EnsureCapacity(total);
            b.Count = total;
        }
        var buckets = new Dictionary<int, Bucket>(f.Buckets);

        // pass B: write the instances
        Parallel.For(0, chunks, c =>
        {
            var cursor = new Dictionary<int, int>(offsets[c]);
            Span<float> custom = stackalloc float[4];
            for (var i = chunkStart[c]; i < chunkStart[c + 1]; ++i)
            {
                var key = keys[i];
                if (key < 0) continue;
                var bucket = buckets[key];
                var slot = cursor[key]++;
                var dst = bucket.Data.AsSpan(slot * Bucket.Stride, Bucket.Stride);
                var kind = s.Kind[i];
                var wire = s.TransformOf(i);
                if (kind is PrimitiveType.Cylinder or PrimitiveType.Box)
                    InstanceEncoding.WriteCylinder(wire, s.Radius[i], s.Length[i], dst);
                else
                    InstanceEncoding.WritePlanar(wire, dst);
                var color = PlantColors.Natural(s, i);
                dst[InstanceEncoding.OffColor] = color.R;
                dst[InstanceEncoding.OffColor + 1] = color.G;
                dst[InstanceEncoding.OffColor + 2] = color.B;
                dst[InstanceEncoding.OffColor + 3] = 1;
                InstanceEncoding.WriteCustom(s.StatsOf(i), InstanceEncoding.OrganClass(kind, s.Organ[i]), s.HasColor(i), custom);
                custom.CopyTo(dst.Slice(InstanceEncoding.OffCustom, 4));
                bucket.PrimitiveOf[slot] = i;
            }
        });

        foreach (var b in f.Buckets.Values)
        {
            f.InstanceCount += b.Count;
            f.TriangleCount += (long)b.Count * lib.Get(b.Key.GeometryId).TriangleCount;
        }
        return f;
    }

    /// <summary>The bucket a primitive is drawn in (BuildScene.ts), or -1 for one that is not drawn.</summary>
    private static int Resolve(DecodedScene s, int i, RecordedAsset? asset, PlantGeometry species, out int geom)
    {
        BucketVariant variant;
        Category category;
        var organ = s.Organ[i];
        switch (s.Kind[i])
        {
            case PrimitiveType.Rectangle:
                if (organ == OrganType.FlowerPadel)
                {
                    geom = asset?.Petal ?? species.Petal ?? Geom.Plane;
                    variant = BucketVariant.Foliage; category = Category.Leaves;
                }
                else if (organ == OrganType.FruitWing)
                {
                    geom = asset?.FruitWing ?? Geom.Plane;
                    variant = BucketVariant.LaminaOpaque; category = Category.FruitWings;
                }
                else
                {
                    var slice = s.LaminaSlice[i];
                    if (asset != null) geom = asset.LeafSlices[System.Math.Min(slice, asset.LeafSlices.Length - 1)];
                    else geom = species.Leaves is { Length: > 0 } ids ? ids[System.Math.Min(slice, ids.Length - 1)] : Geom.Plane;
                    variant = BucketVariant.Foliage; category = Category.Leaves;
                }
                break;
            case PrimitiveType.Ellipsoid:
                geom = asset?.Fruit ?? Geom.Sphere;
                variant = BucketVariant.Fruit; category = Category.Fruits;
                break;
            case PrimitiveType.Cylinder:
                geom = Geom.Cylinder;
                variant = BucketVariant.Opaque; category = Category.Stems;
                break;
            case PrimitiveType.Box:
                geom = Geom.BoxCentered;
                variant = BucketVariant.Opaque; category = Category.Roots;
                break;
            case PrimitiveType.Disk:
                geom = Geom.CircleHalf;
                variant = BucketVariant.LaminaOpaque; category = Category.Stems;
                break;
            default:
                geom = -1;
                return -1;
        }
        return new BucketKey(geom, variant, category).Packed;
    }
}
