using GodotFrontend.Core.Geometry;

namespace GodotFrontend.Core.Scene;

/// <summary>
/// Decodes scene wire versions 7, 8 and 9 (helpers/BinaryReader.ts readAgroSceneV7 / readRecordedGeometry) into a
/// <see cref="DecodedScene"/>. One sequential pass; the error conditions of the web reader are kept (an unknown organ
/// code, a bad deformation record, a missing asset, a length mismatch all throw rather than return garbage).
///
/// Recorded assets are registered into the shared <see cref="GeometryLibrary"/> and skipped when already known, so a
/// playback decodes each mesh once. The decoder keeps growable scratch arrays between frames (high-water mark) and
/// hands out exact-size copies.
/// </summary>
public sealed class SceneDecoder
{
    private readonly GeometryLibrary library;

    // scratch, reused across frames
    private float[] transform = new float[12 * 1024];
    private PrimitiveType[] kind = new PrimitiveType[1024];
    private OrganType[] organ = new OrganType[1024];
    private float[] length = new float[1024];
    private float[] radius = new float[1024];
    private byte[] rgb = new byte[3 * 1024];
    private byte[] flags = new byte[1024];
    private float[] stats = new float[Stat.Count * 1024];
    private ushort[] laminaSlice = new ushort[1024];
    private int[] plantOf = new int[1024];
    private int[] deformIndex = new int[1024];
    private byte[] leafOrgan = new byte[256];
    private int[] chainPosition = new int[256];

    public SceneDecoder(GeometryLibrary library)
    {
        this.library = library;
    }

    public DecodedScene Decode(ReadOnlySpan<byte> bytes)
    {
        var r = new SpanReader(bytes);
        var version = r.U8();
        if (version < 7) throw new InvalidDataException($"deprecated scene format version {version}");
        if (version > 9) throw new InvalidDataException($"unsupported scene format version {version}");
        var v8 = version >= 8;
        var sourceIdentity = version >= 9;

        var scene = new DecodedScene { Version = version };
        // A drawn primitive costs at least ~70 bytes on the wire; size the scratch once instead of doubling up to it.
        Ensure(bytes.Length / 70 + 1024);
        var flagsByte = (ExportFlags)r.U8();
        if (v8) ReadRecordedGeometry(ref r);
        var doRoots = (flagsByte & ExportFlags.Roots) != 0;
        var doAnalytics = (flagsByte & ExportFlags.Analytics) != 0;
        scene.RootsDownloaded = doRoots;
        scene.Analytics = doAnalytics;

        var plantCount = checked((int)r.U32());
        scene.PlantCount = plantCount;
        scene.PlantSpecies = new int[plantCount];
        scene.PlantAsset = new int[plantCount];
        scene.PlantSourceIndex = new uint[plantCount];
        scene.PlantFirst = new int[plantCount];
        scene.PlantDrawCount = new int[plantCount];
        scene.PlantBoundsMin = new float[plantCount * 3];
        scene.PlantBoundsMax = new float[plantCount * 3];
        var speciesIndex = new Dictionary<string, int>();
        var assetIndex = new Dictionary<string, int>();

        var n = 0; // drawn primitives so far
        Span<float> m = stackalloc float[12];
        Span<float> st = stackalloc float[Stat.Count];
        Span<float> deform = stackalloc float[4];

        for (var i = 0; i < plantCount; ++i)
        {
            var species = r.String();
            if (!speciesIndex.TryGetValue(species, out var sIdx))
            {
                sIdx = scene.Species.Count;
                scene.Species.Add(species);
                speciesIndex[species] = sIdx;
            }
            scene.PlantSpecies[i] = sIdx;

            RecordedAsset? asset = null;
            var aIdx = -1;
            if (v8)
            {
                var assetId = r.String();
                asset = library.Asset(assetId) ?? throw new InvalidDataException("V8 scene references a missing immutable mesh asset.");
                if (!assetIndex.TryGetValue(assetId, out aIdx))
                {
                    aIdx = scene.Assets.Count;
                    scene.Assets.Add(assetId);
                    assetIndex[assetId] = aIdx;
                }
            }
            scene.PlantAsset[i] = aIdx;
            scene.PlantSourceIndex[i] = sourceIdentity ? r.U32() : uint.MaxValue;

            var organCount = checked((int)r.U32());
            scene.PlantFirst[i] = n;
            float maxResShoots = 0, maxProdShoots = 0, maxResRoots = 0, maxProdRoots = 0;
            if (leafOrgan.Length < organCount)
            {
                leafOrgan = new byte[System.Math.Max(organCount, leafOrgan.Length * 2)];
                chainPosition = new int[leafOrgan.Length];
            }
            Array.Clear(leafOrgan, 0, organCount);
            Array.Clear(chainPosition, 0, organCount);

            var box = new Bounds3();

            for (var j = 0; j < organCount; ++j)
            {
                var parentIndex = r.I32();
                var o = (OrganType)r.U8();
                var dk = v8 ? (DeformKind)r.U8() : DeformKind.None;
                if ((byte)dk > 2 || (dk == DeformKind.LeafFold && o != OrganType.Leaf) || (dk == DeformKind.PetioleBend && o != OrganType.Petiole))
                    throw new InvalidDataException("Invalid V8 deformation record.");
                var hasDeform = dk != DeformKind.None;
                if (hasDeform)
                {
                    r.F32Into(deform);
                    ValidateDeformation(dk, deform, asset);
                }
                if (o == OrganType.Leaf)
                {
                    leafOrgan[j] = 1;
                    if (parentIndex >= 0 && parentIndex < j && leafOrgan[parentIndex] != 0)
                        chainPosition[j] = chainPosition[parentIndex] + 1;
                }

                if (WireFormat.IsMeshRecord(o))
                {
                    if (v8 && ((o == OrganType.Fruit && asset!.Fruit is null) || (o == OrganType.FruitWing && asset!.FruitWing is null)))
                        throw new InvalidDataException("Fruit record is missing its immutable mesh asset.");
                    var slice = o == OrganType.Leaf ? chainPosition[j] : 0;
                    r.F32Into(m);
                    st.Clear();
                    if (doAnalytics)
                    {
                        st[Stat.Water] = r.F32();
                        st[Stat.Energy] = r.F32();
                        st[Stat.IrradianceOrWood] = r.F32();
                        st[Stat.DailyResource] = r.F32();
                        st[Stat.DailyProduction] = r.F32();
                    }
                    byte cr = r.U8(), cg = r.U8(), cb = r.U8();
                    if (doAnalytics)
                    {
                        st[Stat.Auxins] = r.F32();
                        r.F32(); // dailyEfficiency, read and discarded
                        if (st[Stat.DailyProduction] > maxProdShoots) maxProdShoots = st[Stat.DailyProduction];
                        if (st[Stat.DailyResource] > maxResShoots) maxResShoots = st[Stat.DailyResource];
                    }
                    Ensure(n + 1);
                    kind[n] = o == OrganType.Fruit ? PrimitiveType.Ellipsoid : PrimitiveType.Rectangle;
                    organ[n] = o;
                    length[n] = 0; radius[n] = 0;
                    m.CopyTo(transform.AsSpan(n * 12, 12));
                    rgb[n * 3] = cr; rgb[n * 3 + 1] = cg; rgb[n * 3 + 2] = cb;
                    flags[n] = (byte)(PrimFlags.HasColor | (hasDeform ? PrimFlags.Deformed : 0));
                    st.CopyTo(stats.AsSpan(n * Stat.Count, Stat.Count));
                    laminaSlice[n] = (ushort)System.Math.Min(slice, ushort.MaxValue);
                    plantOf[n] = i;
                    deformIndex[n] = hasDeform ? AddDeform(scene, dk, deform) : -1;
                    // lamina corners: t +- ax +- ay (columns 0 and 1; az is relief and small)
                    LaminaBounds(m, ref box);
                    n++;
                }
                else if (WireFormat.IsCylinderRecord(o))
                {
                    var len = r.F32();
                    var rad = r.F32();
                    r.F32Into(m);
                    st.Clear();
                    if (doAnalytics)
                    {
                        st[Stat.Water] = r.F32();
                        st[Stat.Energy] = r.F32();
                        st[Stat.IrradianceOrWood] = r.F32();
                        st[Stat.DailyResource] = r.F32();
                        st[Stat.DailyProduction] = r.F32();
                    }
                    byte cr = r.U8(), cg = r.U8(), cb = r.U8();
                    if (doAnalytics)
                    {
                        st[Stat.Auxins] = r.F32();
                        r.F32(); // dailyEfficiency
                        if (st[Stat.DailyProduction] > maxProdShoots) maxProdShoots = st[Stat.DailyProduction];
                        if (st[Stat.DailyResource] > maxResShoots) maxResShoots = st[Stat.DailyResource];
                    }
                    Ensure(n + 1);
                    kind[n] = PrimitiveType.Cylinder;
                    organ[n] = o;
                    length[n] = len; radius[n] = rad;
                    m.CopyTo(transform.AsSpan(n * 12, 12));
                    rgb[n * 3] = cr; rgb[n * 3 + 1] = cg; rgb[n * 3 + 2] = cb;
                    flags[n] = (byte)(PrimFlags.HasColor | (hasDeform ? PrimFlags.Deformed : 0));
                    st.CopyTo(stats.AsSpan(n * Stat.Count, Stat.Count));
                    laminaSlice[n] = 0;
                    plantOf[n] = i;
                    deformIndex[n] = hasDeform ? AddDeform(scene, dk, deform) : -1;
                    CylinderBounds(m, len, rad, ref box);
                    n++;
                }
                else if (o == OrganType.Bud)
                {
                    r.Skip(3 * 4 + 4); // centre, radius
                    if (doAnalytics) r.Skip(4 * 4); // water, energy, auxins, dailyEfficiency
                    // buds are not drawn (BuildScene: Primitives.Sphere is never emitted)
                }
                else
                {
                    throw new InvalidDataException($"Unknown organ code {(byte)o} in the scene stream (plant {i}, organ {j}).");
                }
            }

            if (doRoots)
            {
                var virtualRoots = r.U8() != 0;
                scene.VirtualRoots |= virtualRoots;
                if (virtualRoots) r.Skip(3 * 4); // bounding size of the virtual root system
                var rootsCount = virtualRoots ? 0 : checked((int)r.U32());
                for (var j = 0; j < rootsCount; ++j)
                {
                    r.I32(); // parent
                    var code = r.U8();
                    if (code != 1) throw new InvalidDataException($"Unknown root record code {code} (plant {i}, root {j}).");
                    var len = r.F32();
                    var rad = r.F32();
                    r.F32Into(m);
                    st.Clear();
                    if (doAnalytics)
                    {
                        st[Stat.Water] = r.F32();
                        st[Stat.Energy] = r.F32();
                        st[Stat.IrradianceOrWood] = r.F32();
                        st[Stat.DailyResource] = r.F32();
                        st[Stat.DailyProduction] = r.F32();
                        if (st[Stat.DailyProduction] > maxProdRoots) maxProdRoots = st[Stat.DailyProduction];
                        if (st[Stat.DailyResource] > maxResRoots) maxResRoots = st[Stat.DailyResource];
                    }
                    Ensure(n + 1);
                    kind[n] = PrimitiveType.Box;
                    organ[n] = OrganType.Root;
                    length[n] = len; radius[n] = rad;
                    m.CopyTo(transform.AsSpan(n * 12, 12));
                    rgb[n * 3] = 0xFF; rgb[n * 3 + 1] = 0xFF; rgb[n * 3 + 2] = 0xFF;
                    flags[n] = 0;
                    st.CopyTo(stats.AsSpan(n * Stat.Count, Stat.Count));
                    laminaSlice[n] = 0;
                    plantOf[n] = i;
                    deformIndex[n] = -1;
                    scene.HasRootGeometry = true;
                    CylinderBounds(m, len, rad, ref box);
                    n++;
                }
            }

            // relative resource/production per shoot and root class (the web divides and gets NaN on a zero max)
            if (doAnalytics)
            {
                for (var p = scene.PlantFirst[i]; p < n; ++p)
                {
                    var isRoot = kind[p] == PrimitiveType.Box;
                    var maxRes = isRoot ? maxResRoots : maxResShoots;
                    var maxProd = isRoot ? maxProdRoots : maxProdShoots;
                    var b = p * Stat.Count;
                    stats[b + Stat.RelResource] = maxRes > 0 ? stats[b + Stat.DailyResource] / maxRes : 0;
                    stats[b + Stat.RelProduction] = maxProd > 0 ? stats[b + Stat.DailyProduction] / maxProd : 0;
                }
            }

            scene.PlantDrawCount[i] = n - scene.PlantFirst[i];
            if (scene.PlantDrawCount[i] == 0) box = new Bounds3(0);
            float bminx = box.MinX, bminy = box.MinY, bminz = box.MinZ, bmaxx = box.MaxX, bmaxy = box.MaxY, bmaxz = box.MaxZ;
            scene.PlantBoundsMin[i * 3] = bminx; scene.PlantBoundsMin[i * 3 + 1] = bminy; scene.PlantBoundsMin[i * 3 + 2] = bminz;
            scene.PlantBoundsMax[i * 3] = bmaxx; scene.PlantBoundsMax[i * 3 + 1] = bmaxy; scene.PlantBoundsMax[i * 3 + 2] = bmaxz;
            if (scene.PlantDrawCount[i] > 0)
            {
                scene.SceneMin[0] = System.Math.Min(scene.SceneMin[0], bminx); scene.SceneMax[0] = System.Math.Max(scene.SceneMax[0], bmaxx);
                scene.SceneMin[1] = System.Math.Min(scene.SceneMin[1], bminy); scene.SceneMax[1] = System.Math.Max(scene.SceneMax[1], bmaxy);
                scene.SceneMin[2] = System.Math.Min(scene.SceneMin[2], bminz); scene.SceneMax[2] = System.Math.Max(scene.SceneMax[2], bmaxz);
            }
        }

        if (!r.IsEnd) throw new InvalidDataException($"scene stream has {r.Remaining} trailing bytes after {plantCount} plants");

        scene.Count = n;
        scene.Transform = Exact(transform, n * 12);
        scene.Kind = Exact(kind, n);
        scene.Organ = Exact(organ, n);
        scene.Length = Exact(length, n);
        scene.Radius = Exact(radius, n);
        scene.Rgb = Exact(rgb, n * 3);
        scene.Flags = Exact(flags, n);
        scene.Stats = doAnalytics ? Exact(stats, n * Stat.Count) : null;
        scene.LaminaSlice = Exact(laminaSlice, n);
        scene.Plant = Exact(plantOf, n);
        scene.DeformIndex = Exact(deformIndex, n);
        if (n == 0)
        {
            scene.SceneMin = new float[3];
            scene.SceneMax = new float[3];
        }
        return scene;
    }

    /// <summary>An exact-size copy without zeroing the destination first.</summary>
    private static T[] Exact<T>(T[] src, int count) where T : unmanaged
    {
        var a = GC.AllocateUninitializedArray<T>(count);
        Array.Copy(src, a, count);
        return a;
    }

    private static int AddDeform(DecodedScene scene, DeformKind dk, ReadOnlySpan<float> q)
    {
        var idx = scene.DeformKinds.Length;
        Array.Resize(ref scene.DeformKinds, idx + 1);
        scene.DeformKinds[idx] = (byte)dk;
        scene.Deform.Add(q[0]); scene.Deform.Add(q[1]); scene.Deform.Add(q[2]); scene.Deform.Add(q[3]);
        return idx;
    }

    private static void ValidateDeformation(DeformKind dk, ReadOnlySpan<float> q, RecordedAsset? asset)
    {
        var bad = asset is null || asset.Profile != 1;
        for (var k = 0; k < 4 && !bad; ++k) bad = !float.IsFinite(q[k]);
        if (!bad && dk == DeformKind.LeafFold)
            for (var k = 0; k < 4; ++k)
                if (System.Math.Abs(q[k]) > (k < 2 ? 25 : 75) * System.Math.PI / 180 + 1e-6) bad = true;
        if (!bad && dk == DeformKind.PetioleBend)
            if (System.Math.Sqrt(q[0] * q[0] + q[1] * q[1]) + System.Math.Sqrt(q[2] * q[2] + q[3] * q[3]) > System.Math.PI / 2 + 1e-6) bad = true;
        if (bad) throw new InvalidDataException("Invalid V8 deformation coordinates.");
    }

    /// <summary>A running axis-aligned box; a struct so the per-primitive bound costs no allocation or call.</summary>
    private struct Bounds3
    {
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        public Bounds3()
        {
            MinX = MinY = MinZ = float.PositiveInfinity;
            MaxX = MaxY = MaxZ = float.NegativeInfinity;
        }

        public Bounds3(float zero) { MinX = MinY = MinZ = MaxX = MaxY = MaxZ = zero; }

        public void Add(float x, float y, float z)
        {
            if (x < MinX) MinX = x; if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y; if (y > MaxY) MaxY = y;
            if (z < MinZ) MinZ = z; if (z > MaxZ) MaxZ = z;
        }
    }

    private static void LaminaBounds(ReadOnlySpan<float> m, ref Bounds3 b)
    {
        // columns: ax = (m0, m4, m8), ay = (m1, m5, m9), t = (m3, m7, m11); the plane mesh spans [-1, 1] in x and y
        float tx = m[3], ty = m[7], tz = m[11];
        for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
                b.Add(tx + sx * m[0] + sy * m[1], ty + sx * m[4] + sy * m[5], tz + sx * m[8] + sy * m[9]);
    }

    private static void CylinderBounds(ReadOnlySpan<float> m, float len, float rad, ref Bounds3 b)
    {
        float tx = m[3], ty = m[7], tz = m[11];
        b.Add(tx - rad, ty - rad, tz - rad);
        b.Add(tx + rad, ty + rad, tz + rad);
        float ex = tx + m[1] * len, ey = ty + m[5] * len, ez = tz + m[9] * len;
        b.Add(ex - rad, ey - rad, ez - rad);
        b.Add(ex + rad, ey + rad, ez + rad);
    }

    private void Ensure(int count)
    {
        if (count <= kind.Length) return;
        var cap = System.Math.Max(count, kind.Length * 2);
        Array.Resize(ref transform, cap * 12);
        Array.Resize(ref kind, cap);
        Array.Resize(ref organ, cap);
        Array.Resize(ref length, cap);
        Array.Resize(ref radius, cap);
        Array.Resize(ref rgb, cap * 3);
        Array.Resize(ref flags, cap);
        Array.Resize(ref stats, cap * Stat.Count);
        Array.Resize(ref laminaSlice, cap);
        Array.Resize(ref plantOf, cap);
        Array.Resize(ref deformIndex, cap);
    }

    /// <summary>readRecordedGeometry: the V8 asset table, registered once per asset id.</summary>
    private void ReadRecordedGeometry(ref SpanReader r)
    {
        var count = r.U32();
        if (count > 4096) throw new InvalidDataException("Invalid V8 asset count.");
        for (var i = 0; i < count; ++i)
        {
            var id = r.String();
            var bytes = checked((int)r.U32());
            var end = r.Pos + bytes;
            if (id.Length != 64 || !id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) || end > r.Length)
                throw new InvalidDataException("Invalid V8 asset.");
            if (library.HasAsset(id)) { r.Pos = end; continue; }
            var profile = r.U8();
            var version = r.U8();
            if (profile > 1 || (version != 1 && version != 2)) throw new InvalidDataException($"Unsupported geometry profile {profile}/{version}.");
            var slices = r.U32();
            if (slices == 0 || slices > 256) throw new InvalidDataException("Invalid V8 blade slices.");

            var leaves = new int[slices];
            for (var s = 0; s < slices; ++s) leaves[s] = ReadMesh(ref r, Geom.RecordedLeafKey(id, s), end);
            var petal = ReadMesh(ref r, Geom.RecordedPetalKey(id), end);
            int? fruit = version >= 2 ? ReadMesh(ref r, Geom.RecordedFruitKey(id), end) : null;
            var wings = version >= 2 ? r.U8() : 0;
            if (wings > 1) throw new InvalidDataException("Invalid fruit-wing mesh flag.");
            int? fruitWing = wings != 0 ? ReadMesh(ref r, Geom.RecordedFruitWingKey(id), end) : null;
            if (r.Pos != end) throw new InvalidDataException("Invalid V8 asset length.");
            library.AddAsset(new RecordedAsset(id, profile, leaves, petal, fruit, fruitWing));
        }
    }

    private int ReadMesh(ref SpanReader r, string key, int end)
    {
        var vertices = r.U32();
        if (vertices > 1_000_000 || r.Pos + vertices * 12 > end) throw new InvalidDataException("Invalid V8 vertices.");
        var positions = r.F32Array((int)vertices * 3);
        var size = r.U32();
        if (size % 3 != 0 || size > 6_000_000 || r.Pos + size * 4 + vertices * 4 > end) throw new InvalidDataException("Invalid V8 triangles.");
        var indices = r.I32Array((int)size);
        foreach (var x in indices) if ((uint)x >= vertices) throw new InvalidDataException("Invalid V8 vertex index.");
        var uvs = new float[vertices * 2];
        for (var v = 0; v < vertices; ++v) uvs[v * 2] = r.F32(); // hinge height in u
        var mesh = new MeshData(positions, MeshData.ComputeVertexNormals(positions, indices), indices, key, uvs);
        return library.Register(key, mesh);
    }
}
