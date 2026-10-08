using System.Numerics;
using AgentsSystem;

namespace Agro.Collisions;

public sealed partial class SoftContactEngine
{
    // Cumulative since the run began.
    public long FoliageCandidates { get; private set; }
    public long FoliageContacts { get; private set; }
    public long NewSides { get; private set; }
    public long Crossings { get; private set; }
    public long SuspectedTunnels { get; private set; }
    public long DeepFoliagePenetrations { get; private set; }
    public long SolidClips { get; private set; }
    /// <summary>Contacts answered by a tangential slide because every joint was at its limit against the normal.</summary>
    public long Slides { get; private set; }
    public double ClippedElongationMetres { get; private set; }
    public long ObstacleContacts { get; private set; }
    public long SoilContacts { get; private set; }
    public long StaticQueries { get; private set; }
    public long ClearanceSkips { get; private set; }
    public long ApproximationQueries { get; private set; }
    public long ApproximationSeparations { get; private set; }
    public long FoliageTicks { get; private set; }
    public long HardTicks { get; private set; }
    public long Ticks { get; private set; }
    public long TickCount { get; private set; }
    // Last tick.
    public long LastCandidates { get; private set; }
    public long LastContacts { get; private set; }
    public long LastNewSides { get; private set; }
    public long LastCrossings { get; private set; }
    public long LastSuspectedTunnels { get; private set; }
    public long LastDeepPenetrations { get; private set; }
    public long LastClips { get; private set; }
    public double LastClippedMetres { get; private set; }
    /// <summary>Largest residual overlap beyond the physical radii among touching foliage pairs, last tick.</summary>
    public float MaxFoliagePenetrationMetres { get; private set; }
    /// <summary>Mean residual overlap over the overlapping foliage pairs, last tick.</summary>
    public float MeanFoliagePenetrationMetres { get; private set; }
    /// <summary>Largest obstacle or soil penetration seen before this tick's pushes.</summary>
    public float MaxSolidPenetrationMetres { get; private set; }
    public int ActiveFoliageContacts
    {
        get { var n = 0; foreach (var work in Work.Values) n += work.Contacts.Count; return n; }
    }

    void FlushCounters()
    {
        long candidates = 0, contacts = 0, sides = 0, crossings = 0, tunnels = 0, deep = 0, clips = 0, overlapping = 0;
        double clipped = 0, penetration = 0;
        float maxPenetration = 0, maxSolid = 0;
        // Plant order, not dictionary order, so the double sums do not depend on history.
        foreach (var plant in Plants)
        {
            var w = Work[plant];
            candidates += w.Candidates; contacts += w.ContactsCount; sides += w.NewSides; crossings += w.Crossings;
            tunnels += w.SuspectedTunnels; deep += w.DeepPenetrations; clips += w.Clips; clipped += w.ClippedMetres; Slides += w.Slides;
            overlapping += w.PenetrationCount; penetration += w.PenetrationSum;
            maxPenetration = MathF.Max(maxPenetration, w.MaxPenetration); maxSolid = MathF.Max(maxSolid, w.MaxSolidPenetration);
            ObstacleContacts += w.Obstacle; SoilContacts += w.Soil; StaticQueries += w.Queries; ClearanceSkips += w.Skips;
            ApproximationQueries += w.ApproximationQueries; ApproximationSeparations += w.ApproximationSeparations;
            w.ResetCounters();
        }
        FoliageCandidates += candidates; FoliageContacts += contacts; NewSides += sides; Crossings += crossings;
        SuspectedTunnels += tunnels; DeepFoliagePenetrations += deep; SolidClips += clips; ClippedElongationMetres += clipped;
        LastCandidates = candidates; LastContacts = contacts; LastNewSides = sides; LastCrossings = crossings;
        LastSuspectedTunnels = tunnels; LastDeepPenetrations = deep; LastClips = clips; LastClippedMetres = clipped;
        MaxFoliagePenetrationMetres = maxPenetration;
        MeanFoliagePenetrationMetres = overlapping > 0 ? (float)(penetration / overlapping) : 0f;
        MaxSolidPenetrationMetres = maxSolid;
    }

    /// <summary>
    /// The sparse contact memories, sorted, each with its age in ticks so that a resumed run makes
    /// the same side decisions on its first tick as the uninterrupted one. Everything else here is
    /// rebuilt.
    /// </summary>
    internal void WriteState(BinaryWriter writer, IReadOnlyList<IFormation> formations)
    {
        var indices = new Dictionary<IFormation, int>(formations.Count);
        for (int i = 0; i < formations.Count; ++i) indices[formations[i]] = i;
        var memories = new List<(int PlantA, ulong OrganA, int PlantB, ulong OrganB, SoftContactMemory Memory)>();
        foreach (var work in Work.Values)
            foreach (var (key, memory) in work.Contacts)
                if (indices.TryGetValue(key.A, out var pa) && indices.TryGetValue(key.B, out var pb))
                    memories.Add((pa, key.OrganA, pb, key.OrganB, memory));
        memories.Sort(static (x, y) =>
        {
            var c = x.PlantA.CompareTo(y.PlantA); if (c != 0) return c;
            c = x.OrganA.CompareTo(y.OrganA); if (c != 0) return c;
            c = x.PlantB.CompareTo(y.PlantB); if (c != 0) return c;
            return x.OrganB.CompareTo(y.OrganB);
        });
        writer.Write(memories.Count);
        foreach (var m in memories)
        {
            writer.Write(m.PlantA); writer.Write(m.OrganA); writer.Write(m.PlantB); writer.Write(m.OrganB);
            writer.Write(m.Memory.Normal.X); writer.Write(m.Memory.Normal.Y); writer.Write(m.Memory.Normal.Z);
            writer.Write(m.Memory.FeatureA); writer.Write(m.Memory.FeatureB);
            writer.Write((byte)Math.Clamp(ContactEpoch - m.Memory.Seen, 0, 255));
        }
    }

    internal void ReadState(BinaryReader reader, IReadOnlyList<IFormation> formations)
    {
        Work.Clear(); CrossJobs.Clear();
        foreach (var batch in Batches) batch.Clear();
        PreviousNeighbours.Clear(); PreviousPlants.Clear();
        ScheduleInvalidated = true;
        ContactEpoch = 0;
        var count = reader.ReadInt32();
        if (count < 0) throw new InvalidDataException("Invalid soft contact state.");
        for (int k = 0; k < count; ++k)
        {
            var a = (PlantFormation2)formations[reader.ReadInt32()]; var organA = reader.ReadUInt64();
            var b = (PlantFormation2)formations[reader.ReadInt32()]; var organB = reader.ReadUInt64();
            var normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var featureA = reader.ReadInt32(); var featureB = reader.ReadInt32();
            var age = reader.ReadByte();
            if (!Work.TryGetValue(a, out var work)) Work.Add(a, work = new(a));
            // Seen = -age: the first tick after a resume runs at epoch 1 and treats these exactly
            // as the uninterrupted run would have at its own epoch.
            work.Contacts.TryAdd(new(a, organA, b, organB), new(normal, featureA, featureB, -age));
        }
    }
}
