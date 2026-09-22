using System.Numerics;
using Agro.Collisions;

namespace Agro;

public partial class PlantSubFormation<T>
{
    internal struct GrowthLight
    {
        public Vector3 Origin, Moment;
        public float OpenWeight, LastHour;
    }
    readonly Dictionary<ulong, GrowthLight> GrowthLights = [];
    readonly List<ulong> RemovedGuidanceIds = [];
    static readonly Vector3[] GrowthRayDirections = MakeGrowthDirections();
    public long GrowthGuidanceRays { get; private set; }

    static Vector3[] MakeGrowthDirections()
    {
        // Eight azimuths in each of eight equal-solid-angle bands, with opposite pairs.
        var result = new Vector3[64];
        for (int band = 0; band < 8; ++band)
            for (int az = 0; az < 8; ++az)
            {
                var y = (band + 0.5f) / 8f;
                var angle = MathF.Tau * (az + (band % 2) * 0.5f) / 8f;
                var r = MathF.Sqrt(1f - y * y);
                result[band * 8 + az] = new(r * MathF.Cos(angle), y, r * MathF.Sin(angle));
            }
        return result;
    }

    bool IsGuidanceApex(int i)
    {
        if (GetIsRizome(i)) return false;
        var organ = GetOrgan(i);
        if (organ == OrganTypes.Meristem) return true;
        if (organ != OrganTypes.Stem) return false;
        foreach (var child in GetChildren(i))
            if (GetOrgan(child) == OrganTypes.Petiole) return true;
        return false;
    }

    bool GuidanceDue(int i, float hour) => !GrowthLights.TryGetValue(GetOrganId(i), out var light)
        || hour - light.LastHour >= 24f || Vector3.DistanceSquared(GetContactPose(i).Tip, light.Origin) >= 0.0004f;

    internal bool NeedsGrowthGuidance()
    {
        if (!IsAboveGround || !Plant.World.GrowthLightGuidanceEnabled) return false;
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        for (int i = 0; i < Count; ++i) if (IsGuidanceApex(i) && GuidanceDue(i, hour)) return true;
        return false;
    }

    internal void RefreshGrowthGuidance(RayScene scene)
    {
        if (!Plant.World.GrowthLightGuidanceEnabled) return;
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        RemovedGuidanceIds.Clear();
        foreach (var id in GrowthLights.Keys) if (FindOrgan(id) < 0) RemovedGuidanceIds.Add(id);
        foreach (var id in RemovedGuidanceIds) GrowthLights.Remove(id);
        for (int i = 0; i < Count; ++i)
        {
            if (!IsGuidanceApex(i) || !GuidanceDue(i, hour)) continue;
            var pose = GetContactPose(i);
            var origin = pose.Tip + Vector3.Transform(Vector3.UnitX, pose.Rotation) * (pose.Radius + 0.0002f);
            var moment = Vector3.Zero; float open = 0, total = 0;
            foreach (var dir in GrowthRayDirections)
            {
                // CIE overcast relative luminance: fixed diffuse openings, no moving sun disc.
                var weight = (1f + 2f * dir.Y) / 3f;
                total += weight;
                ++GrowthGuidanceRays;
                if (scene.AnyHit(origin, dir, 0.0001f, 10000f, RayScene.MaskAll)) continue;
                moment += dir * weight; open += weight;
            }
            moment /= total; open /= total;
            var id = GetOrganId(i);
            if (GrowthLights.TryGetValue(id, out var old))
            {
                var response = 1f - MathF.Exp(-MathF.Max(0, hour - old.LastHour) / 48f);
                moment = Vector3.Lerp(old.Moment, moment, response);
                open = old.OpenWeight + (open - old.OpenWeight) * response;
            }
            GrowthLights[id] = new() { Origin = pose.Tip, Moment = moment, OpenWeight = open, LastHour = hour };
        }
    }

    internal bool TryGetGrowthLight(int index, out Vector3 direction, out float confidence)
    {
        direction = default; confidence = 0;
        for (var n = index; n >= 0; n = GetParent(n))
            if (GrowthLights.TryGetValue(GetOrganId(n), out var light))
            {
                confidence = MathF.Min(light.OpenWeight, light.Moment.Length());
                if (confidence < 0.1f) return false;
                direction = Vector3.Normalize(light.Moment);
                return true;
            }
        return false;
    }

    void ApplyGrowthLight(int index, ref AboveGroundAgent agent, float oldLength)
    {
        if (!Plant.World.GrowthLightGuidanceEnabled || agent.IsRizome || agent.GrowthBlocked
            || agent.Organ is not (OrganTypes.Meristem or OrganTypes.Petiole) || agent.Length <= oldLength
            || !TryGetGrowthLight(index, out var light, out _)) return;
        var current = Vector3.Transform(Vector3.UnitX, agent.Orientation);
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(current, light), -1, 1));
        if (angle < MathF.PI / 90f) return;
        var axis = Vector3.Cross(current, light);
        if (axis.LengthSquared() < 1e-12f) return;
        var fresh = Math.Clamp((agent.Length - oldLength) / agent.Length, 0f, 1f);
        var turn = MathF.Min(angle * fresh, MathF.PI / 60f * Plant.World.HoursPerTick / 24f);
        var transport = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), turn);
        agent.TransportDevelopment(transport);
        SrcDst(out _, out var growing);
        PropagateToSubtree(growing, index, transport);
    }

    void WriteGuidanceState(BinaryWriter w)
    {
        w.Write(GrowthGuidanceRays); w.Write(GrowthLights.Count);
        foreach (var (id, value) in GrowthLights.OrderBy(x => x.Key)) { w.Write(id); WriteValue(w, value); }
    }
    void ReadGuidanceState(BinaryReader r)
    {
        GrowthGuidanceRays = r.ReadInt64(); GrowthLights.Clear();
        var count = r.ReadInt32();
        for (int i = 0; i < count; ++i) GrowthLights.Add(r.ReadUInt64(), ReadValue<GrowthLight>(r));
    }
}
