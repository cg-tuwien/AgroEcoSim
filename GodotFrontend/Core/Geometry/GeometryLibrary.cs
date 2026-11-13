using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Geometry;

/// <summary>A V8/V9 immutable mesh asset: the geometry ids its leaf slices, petal and fruit meshes were registered under.</summary>
public sealed record RecordedAsset(string Id, int Profile, int[] LeafSlices, int Petal, int? Fruit, int? FruitWing);

/// <summary>
/// The geometry registry (render/core/GeometryStore.ts): string key to a stable integer id to the mesh. Ids are
/// what instances carry. Registering an existing key replaces the mesh under the same id and bumps the version so a
/// renderer knows to re-upload. Thread-safe: the decoder registers recorded assets on a worker thread while the
/// renderer reads on the main thread.
/// </summary>
public sealed class GeometryLibrary
{
    private readonly object gate = new();
    private readonly List<MeshData?> meshes = new();
    private readonly List<int> versions = new();
    private readonly Dictionary<string, int> idsByKey = new();
    private readonly Dictionary<string, RecordedAsset> assets = new();
    private readonly Dictionary<string, int[]> speciesLeaves = new();
    private readonly Dictionary<string, int> speciesPetal = new();
    private int totalVersion;

    public GeometryLibrary()
    {
        var statics = StaticGeometry.BuildAll();
        for (var i = 0; i < statics.Length; ++i)
        {
            meshes.Add(statics[i]);
            versions.Add(1);
            idsByKey[$"static:{i}"] = i;
        }
    }

    /// <summary>Bumped on every registration; cheap to poll from a render loop.</summary>
    public int Version { get { lock (gate) return totalVersion; } }

    public int Count { get { lock (gate) return meshes.Count; } }

    public int Register(string key, MeshData mesh)
    {
        lock (gate)
        {
            if (idsByKey.TryGetValue(key, out var existing))
            {
                meshes[existing] = mesh;
                versions[existing]++;
                totalVersion++;
                return existing;
            }
            var id = meshes.Count;
            meshes.Add(mesh);
            versions.Add(1);
            idsByKey[key] = id;
            totalVersion++;
            return id;
        }
    }

    public int? IdFor(string key)
    {
        lock (gate) return idsByKey.TryGetValue(key, out var id) ? id : null;
    }

    public MeshData Get(int id)
    {
        lock (gate)
        {
            if (id < 0 || id >= meshes.Count || meshes[id] is null) throw new ArgumentOutOfRangeException(nameof(id), $"no geometry {id}");
            return meshes[id]!;
        }
    }

    public int VersionOf(int id)
    {
        lock (gate) return id >= 0 && id < versions.Count ? versions[id] : 0;
    }

    public bool HasAsset(string id)
    {
        lock (gate) return assets.ContainsKey(id);
    }

    public RecordedAsset? Asset(string id)
    {
        lock (gate) return assets.TryGetValue(id, out var a) ? a : null;
    }

    public void AddAsset(RecordedAsset asset)
    {
        lock (gate) assets[asset.Id] = asset;
    }

    /// <summary>Registers the REST-served leaf slices of a species (GET mesh/leaf/{name}/slices).</summary>
    public void RegisterSpeciesLeaves(string species, MeshData[] slices)
    {
        var ids = new int[slices.Length];
        for (var i = 0; i < slices.Length; ++i) ids[i] = Register(Geom.LeafKey(species, i), slices[i]);
        lock (gate) speciesLeaves[species] = ids;
    }

    public void RegisterSpeciesPetal(string species, MeshData petal)
    {
        var id = Register(Geom.PetalKey(species), petal);
        lock (gate) speciesPetal[species] = id;
    }

    /// <summary>The species' leaf slice ids, or null when the backend served none (the packer then draws the unit plane).</summary>
    public int[]? LeafSliceIds(string species)
    {
        lock (gate) return speciesLeaves.TryGetValue(species, out var ids) ? ids : null;
    }

    public int? PetalId(string species)
    {
        lock (gate) return speciesPetal.TryGetValue(species, out var id) ? id : null;
    }

    /// <summary>All assets, for a renderer that wants to upload them up front.</summary>
    public RecordedAsset[] Assets()
    {
        lock (gate) return assets.Values.ToArray();
    }
}
