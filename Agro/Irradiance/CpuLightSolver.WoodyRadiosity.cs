namespace Agro;

public sealed partial class CpuLightSolver
{
    // A large crown cannot share one reflected-light value: that lets bright
    // outer foliage illuminate fully shaded organs without a geometric path.
    // Stable organ IDs preserve local carried light through removal/reindexing.
    sealed class WoodyLightCache
    {
        public readonly Dictionary<ulong, float> Values = [];
        public readonly List<ulong> Removed = [];
    }

    readonly Dictionary<int, WoodyLightCache> WoodyLight = [];
    float[] WoodyLightValues = [];

    void WriteWoodyRadiosity(BinaryWriter writer)
    {
        writer.Write(WoodyLight.Count);
        foreach (var pair in WoodyLight.OrderBy(p => p.Key))
        {
            writer.Write(pair.Key);
            writer.Write(pair.Value.Values.Count);
            foreach (var leaf in pair.Value.Values.OrderBy(p => p.Key))
            {
                writer.Write(leaf.Key);
                writer.Write(leaf.Value);
            }
        }
    }

    void ReadWoodyRadiosity(BinaryReader reader)
    {
        WoodyLight.Clear();
        var slots = reader.ReadInt32();
        if (slots < 0) throw new InvalidDataException("Negative woody light slot count.");
        for (var i = 0; i < slots; ++i)
        {
            var slot = reader.ReadInt32();
            var count = reader.ReadInt32();
            if (slot < 0 || count < 0) throw new InvalidDataException("Invalid woody light cache.");
            var cache = new WoodyLightCache();
            for (var j = 0; j < count; ++j)
            {
                var id = reader.ReadUInt64();
                var value = reader.ReadSingle();
                if (!float.IsFinite(value) || value < 0f) throw new InvalidDataException("Invalid local foliage radiosity.");
                cache.Values.Add(id, value);
            }
            WoodyLight.Add(slot, cache);
        }
    }

    float[]? PrepareWoodyRadiosity(IList<AgentsSystem.IFormation> formations, int slots, float seeded)
    {
        var any = false;
        for (var slot = 0; slot < slots; ++slot)
        {
            if (formations[slot] is not PlantFormation2 plant || plant.Parameters.Behavior != Behavior.Woody)
                continue;
            if (SensorCounts[slot] == 0 && !WoodyLight.ContainsKey(slot)) continue;
            if (!WoodyLight.TryGetValue(slot, out var cache)) WoodyLight.Add(slot, cache = new());
            var ag = plant.AG;
            // A same-count replacement can leave stale entries for one solve;
            // the next count check removes them. Scratch lists are retained.
            if (cache.Values.Count > SensorCounts[slot])
            {
                cache.Removed.Clear();
                foreach (var id in cache.Values.Keys)
                {
                    var i = ag.FindOrgan(id);
                    if (i < 0 || Classify(ag.GetOrgan(i)) != PrimitiveClass.Lamina) cache.Removed.Add(id);
                }
                foreach (var id in cache.Removed) cache.Values.Remove(id);
            }
            if (SensorCounts[slot] == 0) continue;
            if (WoodyLightValues.Length < SensorTotal)
                Array.Resize(ref WoodyLightValues, Math.Max(SensorTotal, Math.Max(1024, WoodyLightValues.Length * 3 / 2)));
            any = true;
            var offsets = OffsetArrays[slot]!;
            for (var i = 0; i < ag.Count; ++i)
                if (offsets[i] >= 0)
                    WoodyLightValues[offsets[i]] = cache.Values.TryGetValue(ag.GetOrganId(i), out var value) ? value : seeded;
        }
        return any ? WoodyLightValues : null;
    }

    void UpdateWoodyRadiosity(IList<AgentsSystem.IFormation> formations, int slot, float hours)
    {
        if (formations[slot] is not PlantFormation2 plant || plant.Parameters.Behavior != Behavior.Woody)
            return;
        var cache = WoodyLight[slot];
        var ag = plant.AG;
        var offsets = OffsetArrays[slot]!;
        for (var i = 0; i < ag.Count; ++i)
            if (offsets[i] >= 0)
            {
                // The sensor sums incident light over both faces. This lumped
                // lamina model emits equally into two hemispheres, so their
                // combined reflected power is 2*pi*L, not pi*L.
                var value = FoliageAlbedo * SensorValues[offsets[i]] / (2f * hours * MathF.PI);
                cache.Values[ag.GetOrganId(i)] = value;
                // The obstacle gather follows this update, just as it sees the
                // updated legacy per-plant array. Leaf gathering already finished.
                WoodyLightValues[offsets[i]] = value;
            }
    }
}
