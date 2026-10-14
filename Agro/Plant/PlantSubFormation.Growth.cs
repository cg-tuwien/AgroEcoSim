using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agro.Collisions;

namespace Agro;

public enum OrganDeathReason : byte { Senescence, Resources,
    /// <summary>Born inside a solid with no admissible pose at its attachment (mixed engine); the
    /// birth is withdrawn and the parent retries later, as the certified engine's staged birth would.</summary>
    Obstruction }

public partial class PlantSubFormation<T>
{
    internal sealed class RetryState
    {
        public float NextHour;
        public int Failures;
        public OrganPose LastPose;
        public bool Attempted;
    }

    internal sealed class Development
    {
        public ulong Owner;
        public AboveGroundAgent Before, After;
        public AboveGroundAgent[] Children = [];
        public ulong[] Parents = [];
        public int[] LocalParents = [];
        public ulong[] Retire = [];
        public uint Created;
        public float Energy, Water, EnergyFraction, WaterFraction;
        // Admission retries are independent of the owner's elongation retries.
        public float NextAttemptHour;
        public int AdmissionFailures;
    }

    readonly Dictionary<ulong, RetryState> GrowthRetries = [];
    readonly Dictionary<ulong, Development> Developments = [];
    readonly Dictionary<ulong, int> OrganIndices = [];
    readonly List<(int Index, OrganDeathReason Reason)> DevelopmentDeaths = [];
    readonly List<ulong> RemovedGrowthIds = [], CompletedGrowthIds = [];
    readonly List<Development> DevelopmentOrder = [];
    readonly List<int> DevelopmentDescendants = [];
    readonly HashSet<ulong> DevelopmentMoving = [];
    readonly HashSet<int> DevelopmentSupportRoots = [];
    OrganPose[] DevelopmentPoses = [];
    LaminaTemplate?[] DevelopmentTemplates = [];
    bool RecordingDevelopment;
    AboveGroundAgent TickPhysiology;
    internal void MarkDevelopmentPhysiology(in AboveGroundAgent agent) => TickPhysiology = agent;
    public long RejectedDevelopments { get; private set; }
    public long AcceptedDevelopments { get; private set; }
    public long NaturalDeaths { get; private set; }
    public long ResourceDeaths { get; private set; }
    public int PausedGrowthCount => GrowthRetries.Count;
    public int PendingDevelopmentCount => Developments.Count;
    internal ulong GetOrganId(int i) => Src()[i].StableId;
    internal int FindOrgan(ulong id) => OrganIndices.TryGetValue(id, out var i) ? i : -1;

    void RefreshOrganIndices()
    {
        OrganIndices.Clear();
        var src = Src();
        for (int i = 0; i < src.Length; ++i) OrganIndices.Add(src[i].StableId, i);
        RemovedGrowthIds.Clear();
        foreach (var id in GrowthRetries.Keys)
            if (!OrganIndices.ContainsKey(id)) RemovedGrowthIds.Add(id);
        foreach (var id in RemovedGrowthIds) GrowthRetries.Remove(id);
        RemovedGrowthIds.Clear();
        foreach (var id in Developments.Keys)
            if (!OrganIndices.ContainsKey(id)) RemovedGrowthIds.Add(id);
        foreach (var id in RemovedGrowthIds) Developments.Remove(id);
        PruneDeformationState();
    }

    internal bool HasPendingDevelopment(int i) => i >= 0 && Developments.ContainsKey(Src()[i].StableId);

    // A rejected first scape is a sampled spring commitment. Keep its exact
    // proposal while emergence could still lead to bloom, then release the
    // vegetative owner rather than pinning that crown until obstacle removal.
    bool FloralDevelopmentExpired(Development proposal)
    {
        if (proposal.Children.Length != 1 || proposal.Children[0].Organ != OrganTypes.FlowerMeristem
            || !proposal.Children[0].FlowerAgent.flowerBase || proposal.Children[0].FlowerAgent.debth != 0)
            return false;

        var world = Plant.World;
        var species = Plant.Parameters;
        // Created is immutable while the proposal is pending; FlowerStartTime can
        // be rebased after admission and is not a reliable pending-commitment date.
        var created = world.GetTime(proposal.Created);
        var now = world.GetTime(world.Timestep);
        // A pending spring commitment must not become eligible again after the
        // next March-cycle wrap, even if its within-year season is small again.
        if (SeasonCycle(now) != SeasonCycle(created)) return true;

        const float hoursPerSeason = 365f * 24f;
        var createdDay = created.DayOfYear - 1;
        var createdSeason = (createdDay >= 59 ? createdDay - 59 : 306 + createdDay) / 365f;
        // The flowering phase can start late when the plant first reaches maturity.
        // Bound this sampled commitment from its actual creation season, not the
        // nominal start of the species window.
        var lastCommitmentSeason = MathF.Min(species.FloweringSeasonEnd,
            createdSeason + species.FloweringPhaseDurationHours / hoursPerSeason);
        var latestBloomSeason = species.PostFloweringCommitmentProbabilityPerHour > 0f
            || species.DormantFloweringCommitmentProbabilityPerHour > 0f
            ? species.SeasonalSenescence
            : MathF.Min(species.SeasonalSenescence,
                lastCommitmentSeason + species.FlowerSettings.CommitmentToBloomLeadHours / hoursPerSeason);
        return world.Season > latestBloomSeason;
    }

    internal void WakeGrowth(int index = -1)
    {
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        if (index < 0)
        {
            foreach (var retry in GrowthRetries.Values) { retry.NextHour = hour; retry.Failures = 0; }
            foreach (var proposal in Developments.Values) { proposal.NextAttemptHour = hour; proposal.AdmissionFailures = 0; }
            return;
        }
        for (var i = index; i >= 0; i = GetParent(i))
        {
            if (GrowthRetries.TryGetValue(GetOrganId(i), out var retry))
            { retry.NextHour = hour; retry.Failures = 0; }
        }
    }

    void PrepareGrowthAttempts(Span<T> agents)
    {
        if (!IsAboveGround) return;
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        for (int i = 0; i < agents.Length; ++i)
        {
            ref var a = ref agents[i];
            var suspended = false;
            if (GrowthRetries.TryGetValue(a.StableId, out var retry))
            {
                retry.Attempted = hour >= retry.NextHour || Vector3.DistanceSquared(GetContactPose(i).Tip, retry.LastPose.Tip) >= 0.0004f;
                suspended = !retry.Attempted;
            }
            a.GrowthBlocked = suspended || Developments.ContainsKey(a.StableId);
        }
    }

    internal void CompleteGrowthAttempts()
    {
        CompletedGrowthIds.Clear();
        foreach (var (id, retry) in GrowthRetries)
        {
            if (!retry.Attempted || Developments.ContainsKey(id)) continue;
            CompletedGrowthIds.Add(id);
        }
        foreach (var id in CompletedGrowthIds)
        {
            var i = FindOrgan(id);
            if (i >= 0) SetObstruction(i, false);
        }
    }

    void TickWithDevelopment(ref T agent, int index, uint timestep)
    {
        if (!IsAboveGround)
        {
            agent.Tick(this, index, timestep);
            return;
        }
        if (!CollisionSettings.Enabled && Plant.Parameters.ConstructionEnergyPerDryGram == 0f)
        {
            var length = agent.Length;
            agent.Tick(this,index,timestep);
            ApplyGrowthLight(index,ref Unsafe.As<T,AboveGroundAgent>(ref agent),length);
            return;
        }
        var before = Unsafe.As<T, AboveGroundAgent>(ref agent);
        TickPhysiology = before;
        var first = Births.Count;
        // The certified engine admits every birth geometrically, so births are staged as a
        // transaction and deaths are retired with it. The soft engine never rejects geometry:
        // its births go straight to Census and deaths apply as they are requested.
        var staging = Plant.World.StagesDevelopment;
        if (staging)
        {
            DevelopmentDeaths.Clear();
            RecordingDevelopment = true;
            try { agent.Tick(this, index, timestep); }
            finally { RecordingDevelopment = false; }
        }
        else agent.Tick(this, index, timestep);
        ref var after = ref Unsafe.As<T, AboveGroundAgent>(ref agent);
        if (!staging)
            ChargeBirths(ref after, first);
        else if (Births.Count > first)
        {
            var retired=new HashSet<int>();
            var retirementQueue=new Queue<int>(DevelopmentDeaths.Select(d=>d.Index));
            while(retirementQueue.TryDequeue(out var retiring))
                if(retired.Add(retiring)) foreach(var child in GetChildren(retiring)) retirementQueue.Enqueue(child);
            var proposal = new Development { Owner = before.StableId, Before = before, After = after,
                Created = timestep, Children = new AboveGroundAgent[Births.Count - first],
                Parents = new ulong[Births.Count - first], LocalParents = new int[Births.Count - first],
                Retire = retired.Order().Select(GetOrganId).ToArray() };
            for (int j = 0; j < proposal.Children.Length; ++j)
            {
                var child = Births[first + j];
                proposal.Children[j] = Unsafe.As<T, AboveGroundAgent>(ref child);
                proposal.LocalParents[j] = child.Parent >= AgentCount + first ? child.Parent - AgentCount - first : -1;
                proposal.Parents[j] = child.Parent >= 0 && child.Parent < AgentCount ? GetOrganId(child.Parent) : 0;
                proposal.Energy += MathF.Max(0, child.Energy);
                proposal.Water += MathF.Max(0, child.Water_g);
            }
            Births.RemoveRange(first, Births.Count - first);
            proposal.EnergyFraction = Math.Clamp(proposal.Energy / MathF.Max(1e-20f, TickPhysiology.Energy), 0f, 1f);
            proposal.WaterFraction = Math.Clamp(proposal.Water / MathF.Max(1e-20f, TickPhysiology.Water_g), 0f, 1f);
            // Only developmental state is rolled back. Maintenance, photosynthesis and daily
            // measurements already occurred and must not be charged again when the birth retries.
            after = AboveGroundAgent.RetainPhysiology(before, after);
            after.Energy = TickPhysiology.Energy;
            after.Water_g = TickPhysiology.Water_g;
            Developments.Add(proposal.Owner, proposal);
        }
        else
            foreach (var death in DevelopmentDeaths) Death(death.Index, reason: death.Reason);

        if (before.GrowthBlocked)
        {
            after.Length = before.Length;
            after.GrowSuspendedRadius(Plant);
        }
        after.FundExpansion(before, Plant.Parameters);
        var delta = Quaternion.Normalize(after.Orientation * Quaternion.Conjugate(before.Orientation));
        if (new Vector3(delta.X,delta.Y,delta.Z).LengthSquared() > 1e-16f)
        {
            SrcDst(out _,out var growing);
            PropagateToSubtree(growing,index,delta);
        }
        ApplyGrowthLight(index, ref after, before.Length);
    }

    /// <summary>
    /// Construction cost of births committed directly (soft contact engine). The certified
    /// engine charges the same cost when it admits the staged transaction.
    /// </summary>
    void ChargeBirths(ref AboveGroundAgent parent, int first)
    {
        if (Plant.Parameters.ConstructionEnergyPerDryGram <= 0f || Births.Count <= first) return;
        var cost = 0f;
        for (int j = first; j < Births.Count; ++j)
        {
            var child = Births[j];
            ref var born = ref Unsafe.As<T, AboveGroundAgent>(ref child);
            cost += ConstructionBudget.Cost(born.Volume(), Plant.Parameters, born.WoodRatio());
        }
        parent.Energy -= cost;
    }

    internal void ProcessDevelopments(CollisionWorld collisions)
    {
        if (!IsAboveGround || Developments.Count == 0) return;
        var ag = (PlantSubFormation<AboveGroundAgent>)(object)this;
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        // Dictionary slots are reused after removal and are not a developmental order.
        // Stable ordering also preserves resource allocation and organ indices on resume.
        DevelopmentOrder.Clear();
        DevelopmentOrder.AddRange(Developments.Values);
        DevelopmentOrder.Sort(static (a, b) => a.Owner.CompareTo(b.Owner));
        foreach (var proposal in DevelopmentOrder)
        {
            var index = FindOrgan(proposal.Owner);
            if (index < 0 || Deaths.Contains(index)) { Developments.Remove(proposal.Owner); continue; }
            if (FloralDevelopmentExpired(proposal))
            {
                Developments.Remove(proposal.Owner);
                // SetObstruction(false) alone can leave a sleeping retry blocked.
                GrowthRetries.Remove(proposal.Owner);
                SetObstruction(index, false);
                continue;
            }
            if (hour < proposal.NextAttemptHour) continue;
            GeometryState[]? supportFallback=null;
            var supportAttempts=0;
            var supportRoots=DevelopmentSupportRoots;
            supportRoots.Clear();
        RetrySupportedDevelopment:
            ref var current = ref Unsafe.As<T, AboveGroundAgent>(ref Src()[index]);
            if (current.Energy <= 0f) continue;
            // Proposals remember sampled orientations relative to the owner, not a stale world frame.
            var carry = Quaternion.Normalize(current.Orientation * Quaternion.Conjugate(proposal.Before.Orientation));
            var parent = AboveGroundAgent.RetainPhysiology(proposal.After, current);
            parent.CensusUpdateParent(current.Parent);
            parent.TransportDevelopment(carry);
            parent.Radius = MathF.Max(parent.Radius, current.Radius);
            if (Quaternion.Dot(proposal.Before.Orientation, proposal.After.Orientation) > 0.999999f)
            {
                parent.SetOrientation(current.Orientation);
                parent.baseOrientation = current.baseOrientation;
                parent.restOrientation = current.restOrientation;
                parent.targetOrientation = current.targetOrientation;
            }
            var children = proposal.Children;
            var construction = 0f;
            if (Plant.Parameters.ConstructionEnergyPerDryGram > 0f)
            {
                construction = ConstructionBudget.Cost(parent.Volume() - current.Volume(), Plant.Parameters, parent.WoodRatio());
                foreach (var child in children)
                    construction += ConstructionBudget.Cost(child.Volume(), Plant.Parameters, child.WoodRatio());
            }
            // A financial delay retains the sampled transaction without treating it
            // as a physical obstruction or consuming another probability draw.
            if (construction > current.Energy) continue;
            if (DevelopmentPoses.Length < children.Length)
            {
                var capacity = Math.Max(children.Length, Math.Max(8, DevelopmentPoses.Length * 2));
                Array.Resize(ref DevelopmentPoses, capacity);
                Array.Resize(ref DevelopmentTemplates, capacity);
            }
            var poses = DevelopmentPoses;
            var templates = DevelopmentTemplates;
            var parentPose = PoseFor(parent, GetBaseCenterWorld(index)) with { Deformation = GetDeformation(index) };
            var oldPose = GetContactPose(index);
            var changed = oldPose.MotionBound(parentPose) > 1e-7f;
            var descendants = DevelopmentDescendants;
            descendants.Clear();
            var moving = DevelopmentMoving;
            moving.Clear(); moving.Add(proposal.Owner);
            if (changed)
            {
                descendants.AddRange(GetChildren(index));
                for (int k = 0; k < descendants.Count; ++k)
                { moving.Add(GetOrganId(descendants[k])); descendants.AddRange(GetChildren(descendants[k])); }
            }
            var delta = Quaternion.Normalize(AttachmentRotation(parentPose) * Quaternion.Conjugate(AttachmentRotation(oldPose)));
            var valid = !CollisionSettings.Enabled || collisions.AcceptDevelopmentPose(ag, index, parentPose, ag.GetLaminaTemplate(index),
                replacingOwner: true, moving: moving, previousPose: oldPose);
            foreach (var descendant in descendants)
            {
                if (!valid) break;
                var old = GetContactPose(descendant);
                var moved = old with { Base = parentPose.Tip + Vector3.Transform(old.Base - oldPose.Tip, delta),
                    Rotation = Quaternion.Normalize(delta * old.Rotation) };
                valid = !CollisionSettings.Enabled || collisions.AcceptDevelopmentPose(ag, descendant, moved, ag.GetLaminaTemplate(descendant),
                    moving: moving, previousPose: old);
            }
            var childCarry=carry;
            var blockedPose=parentPose;
            var blockedTemplate=ag.GetLaminaTemplate(index);
            var blockedSupport=index;
            // The first basal scape must have a viable finite emergence path. A tiny
            // accepted meristem can otherwise be trapped by older petioles on its
            // very first growth step, despite passing the point-sized birth test.
            var previewFirstScape = children.Length == 1 && children[0].Organ == OrganTypes.FlowerMeristem
                && children[0].FlowerAgent.flowerBase && children[0].FlowerAgent.debth == 0
                && Plant.Parameters.BasalScapeEmergenceMaxTiltDegrees > 0f;
            // A rejected coarse sweep may have exhausted its conservative CCD
            // budget. Re-run only failed first-scape intervals in two shorter
            // halves. Each half passes the complete endpoint, solid and foliage
            // checks with its own size-dependent attachment neighbourhood.
            // Four levels bound both work and the accepted motion certificate.
            bool CertifyFirstScapeInterval(int owner, int childIndex, bool attachedToOwner,
                OrganPose from, OrganPose to, int depth)
            {
                if (collisions.AcceptDevelopmentPose(ag, owner, to, templates[childIndex],
                    proposal.Retire, previousPose: from, attachedToOwner: attachedToOwner,
                    previousPoseInCurrentFrame: true)) return true;
                if (depth >= 4) return false;
                var mid = from.Interpolate(to, .5f);
                return CertifyFirstScapeInterval(owner, childIndex, attachedToOwner, from, mid, depth + 1)
                    && CertifyFirstScapeInterval(owner, childIndex, attachedToOwner, mid, to, depth + 1);
            }
            bool ChildrenFit(Quaternion orientationCarry)
            {
                for (int j = 0; j < children.Length; ++j)
                {
                    var child = children[j];
                    child.TransportDevelopment(orientationCarry);
                    var p = proposal.LocalParents[j];
                    var existing = p < 0 ? FindOrgan(proposal.Parents[j]) : -1;
                    if (p >= j || (p < 0 && existing < 0 && proposal.Parents[j] != 0)) return false;
                    var basis = p >= 0 ? poses[p].Tip : existing == index ? parentPose.Tip
                        : existing >= 0 ? GetContactPose(existing).Tip : Plant.Position + FieldOrigin;
                    poses[j] = PoseFor(child, basis);
                    var slice = 0;
                    var ancestor = p;
                    var existingAncestor = existing;
                    while (ancestor >= 0 && children[ancestor].Organ == OrganTypes.Leaf)
                    {
                        ++slice;
                        existingAncestor = FindOrgan(proposal.Parents[ancestor]);
                        ancestor = proposal.LocalParents[ancestor];
                    }
                    if (ancestor < 0)
                        for (var n = existingAncestor; n >= 0 && GetOrgan(n) == OrganTypes.Leaf; n = GetParent(n)) ++slice;
                    templates[j] = LaminaTemplates.For(Plant.Parameters, child.Organ, slice);
                    blockedPose=poses[j];blockedTemplate=templates[j];blockedSupport=existing>=0?existing:index;
                    if (CollisionSettings.Enabled && !collisions.AcceptDevelopmentPose(ag, existing >= 0 ? existing : index, poses[j], templates[j], proposal.Retire,
                        attachedToOwner: p < 0)) return false;
                    if (CollisionSettings.Enabled && previewFirstScape)
                    {
                        // Test the first basal segment along its monotone growth path.
                        // A single long CCD query can be uncertified in this crowded
                        // geometry even when short, individually certified sweeps pass.
                        // This is a birth-selection forecast; live neighbours are still
                        // rechecked at every subsequent growth step.
                        var growthDurationHours = Plant.World.HoursPerTick / child.GetGrowthTimeVar();
                        var steps = Math.Clamp((int)MathF.Ceiling(growthDurationHours), 1, 32);
                        var prior = poses[j];
                        for (var step = 1; step <= steps; ++step)
                        {
                            var fraction = step / (float)steps;
                            var preview = poses[j] with
                            {
                                Length = child.Length + (child.GetLengthVar() - child.Length) * fraction,
                                Radius = child.Radius + (child.GetRadiusVar() - child.Radius) * fraction
                            };
                            if (!CertifyFirstScapeInterval(existing >= 0 ? existing : index,
                                j, p < 0, prior, preview, 0))
                            { blockedPose = preview; return false; }
                            prior = preview;
                        }
                    }
                    for (int k = 0; k < j; ++k)
                    {
                        if (proposal.LocalParents[j] == k) continue;
                        if (CollisionSettings.Enabled && !CollisionWorld.AdmissibleBirthPair(poses[j], templates[j], poses[k], templates[k], basis)) return false;
                    }
                }
                return true;
            }
            var parentFits=valid;
            valid = valid && ChildrenFit(carry);
            if (!valid && parentFits && previewFirstScape && CollisionSettings.Enabled)
            {
                // This is a rest direction selected once at birth, not an elastic
                // 75-degree bend. Subsequent scape segments still follow their
                // species' gravitropic target. Test least-tilted directions first.
                var authored = Quaternion.Normalize(carry * children[0].Orientation);
                var up = Vector3.Transform(Vector3.UnitX, authored);
                var side = Vector3.Cross(up, MathF.Abs(up.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ);
                side = Vector3.Normalize(side);
                var across = Vector3.Normalize(Vector3.Cross(up, side));
                for (var tiltDegrees = 15; tiltDegrees <= Plant.Parameters.BasalScapeEmergenceMaxTiltDegrees && !valid; tiltDegrees += 15)
                {
                    var tilt = tiltDegrees * (MathF.PI / 180f);
                    for (var azimuth = 0; azimuth < 16; ++azimuth)
                    {
                        var radial = side * MathF.Cos(azimuth * MathF.Tau / 16f)
                            + across * MathF.Sin(azimuth * MathF.Tau / 16f);
                        var axis = Vector3.Normalize(Vector3.Cross(up, radial));
                        var candidate = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, tilt) * carry);
                        if (!ChildrenFit(candidate)) continue;
                        childCarry = candidate;
                        valid = true;
                        break;
                    }
                }
            }
            if (!valid && supportAttempts<4)
            {
                supportFallback ??= SaveContactTrial();
                ++supportAttempts;
                var root=blockedSupport;
                for(var p=GetParent(root);p>=0 && !IsMechanicallyAnchored(p);p=GetParent(p)) root=p;
                supportRoots.Add(root);
                if(collisions.DeflectDevelopment(ag,blockedSupport,blockedPose,blockedTemplate,proposal.Retire))
                    goto RetrySupportedDevelopment;
                RestoreContactTrial(supportFallback);supportFallback=null;supportAttempts=4;
                goto RetrySupportedDevelopment;
            }
            if (parentFits && !valid)
            {
                valid=false;
                // Reuse the sampled organs and resource shares while testing finite alternative
                // emergence directions. All siblings turn together, preserving authored angles.
                var direction=Vector3.Transform(Vector3.UnitX,parent.Orientation);
                var side=Vector3.Cross(direction,MathF.Abs(direction.Y)<.9f?Vector3.UnitY:Vector3.UnitZ);
                side=Vector3.Normalize(side);
                var across=Vector3.Normalize(Vector3.Cross(direction,side));
                var cue=!IsFloralAxis(parent.Organ) && !children.Any(c=>IsFloralAxis(c.Organ))
                    && TryGetGrowthLight(index,out var light,out _) ? light : direction;
                var candidates=new List<(Quaternion Rotation,float Score)>();
                foreach(var axis in new[]{side,across})
                    foreach(var sign in new[]{-1f,1f})
                        foreach(var fraction in new[]{.25f,.6f,1f})
                        {
                            var angle=CollisionSettings.MaxElasticBendRadians*fraction;
                            var turn=Quaternion.CreateFromAxisAngle(axis,sign*angle);
                            candidates.Add((Quaternion.Normalize(turn*carry),angle-.05f*Vector3.Dot(Vector3.Transform(direction,turn),cue)));
                        }
                foreach(var candidate in candidates.OrderBy(c=>c.Score))
                    if (ChildrenFit(candidate.Rotation)) { childCarry=candidate.Rotation;valid=true;break; }
            }
            if(valid && supportFallback!=null && supportRoots.Any(root=>!collisions.GrowthTrialClear(Plant,root)))
            {
                RestoreContactTrial(supportFallback);supportFallback=null;supportAttempts=4;
                goto RetrySupportedDevelopment;
            }
            if (!valid)
            {
                if(supportFallback!=null) RestoreContactTrial(supportFallback);
                ++RejectedDevelopments;
                ++proposal.AdmissionFailures;
                proposal.NextAttemptHour = hour + (proposal.AdmissionFailures >= 3 ? 72f : 24f);
                SetObstruction(index, true);
                continue;
            }
            // Resource distribution runs between the sampled decision and this commit. Preserve
            // the sampled shares, not an obsolete absolute balance from before redistribution.
            parent.Energy -= construction;
            var energy = MathF.Min(MathF.Max(0, parent.Energy) * proposal.EnergyFraction, proposal.Energy);
            var water = MathF.Min(MathF.Max(0, parent.Water_g) * proposal.WaterFraction, proposal.Water);
            parent.Energy -= energy;
            parent.Water_g -= water;
            current = parent;
            if (changed) PropagateToSubtree(Src(), index, delta);
            var first = AgentCount + Births.Count;
            for (int j = 0; j < children.Length; ++j)
            {
                var child = children[j];
                child.Energy *= proposal.Energy > 0 ? energy / proposal.Energy : 0;
                child.Water_g *= proposal.Water > 0 ? water / proposal.Water : 0;
                child.TransportDevelopment(childCarry);
                child.RebaseBirthTime(Plant.World.Timestep - proposal.Created);
                child.CensusUpdateParent(proposal.LocalParents[j] >= 0 ? first + proposal.LocalParents[j] : FindOrgan(proposal.Parents[j]));
                var born = Unsafe.As<AboveGroundAgent, T>(ref child);
                Birth(born);
            }
            foreach (var retire in proposal.Retire)
            {
                var old = FindOrgan(retire);
                if (old >= 0) Death(old);
            }
            Developments.Remove(proposal.Owner);
            GrowthRetries.Remove(proposal.Owner);
            ++AcceptedDevelopments;
            Census();
            if (CollisionSettings.Enabled) collisions.RefreshPlantBounds(Plant);
        }
    }

    internal void SettleConstruction()
    {
        if (!IsAboveGround || Plant.Parameters.ConstructionEnergyPerDryGram == 0f) return;
        foreach (ref var agent in Src()) Unsafe.As<T, AboveGroundAgent>(ref agent).SettleConstruction();
    }

    internal static OrganPose PoseFor(in AboveGroundAgent a, Vector3 basis) => new(basis + a.BaseOffset,
        a.Orientation, a.Length, a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel ? a.Radius : 0,
        a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel ? AboveGroundAgent.LeafThickness * 0.5f : a.Radius,
        a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit);

    void WriteGrowthState(BinaryWriter w)
    {
        w.Write(RejectedDevelopments); w.Write(AcceptedDevelopments); w.Write(NaturalDeaths); w.Write(ResourceDeaths);
        w.Write(GrowthRetries.Count);
        foreach (var (id, r) in GrowthRetries.OrderBy(x => x.Key))
        { w.Write(id); w.Write(r.NextHour); w.Write(r.Failures); WriteValue(w, r.LastPose); }
        w.Write(Developments.Count);
        foreach (var p in Developments.Values.OrderBy(x => x.Owner))
        {
            w.Write(p.Owner); WriteValue(w, p.Before); WriteValue(w, p.After); w.Write(p.Created); w.Write(p.Energy); w.Write(p.Water);
            w.Write(p.EnergyFraction); w.Write(p.WaterFraction);
            w.Write(p.NextAttemptHour); w.Write(p.AdmissionFailures);
            w.Write(p.Children.Length);
            for (int i = 0; i < p.Children.Length; ++i) { WriteValue(w, p.Children[i]); w.Write(p.Parents[i]); w.Write(p.LocalParents[i]); }
            w.Write(p.Retire.Length); foreach (var id in p.Retire) w.Write(id);
        }
        WriteGuidanceState(w);
    }

    void ReadGrowthState(BinaryReader r)
    {
        RejectedDevelopments = r.ReadInt64(); AcceptedDevelopments = r.ReadInt64(); NaturalDeaths = r.ReadInt64(); ResourceDeaths = r.ReadInt64();
        GrowthRetries.Clear(); Developments.Clear();
        var count = r.ReadInt32();
        for (int i = 0; i < count; ++i)
            GrowthRetries.Add(r.ReadUInt64(), new RetryState { NextHour = r.ReadSingle(), Failures = r.ReadInt32(), LastPose = ReadValue<OrganPose>(r) });
        count = r.ReadInt32();
        for (int i = 0; i < count; ++i)
        {
            var p = new Development { Owner = r.ReadUInt64(), Before = ReadValue<AboveGroundAgent>(r), After = ReadValue<AboveGroundAgent>(r),
                Created = r.ReadUInt32(), Energy = r.ReadSingle(), Water = r.ReadSingle(), EnergyFraction = r.ReadSingle(), WaterFraction = r.ReadSingle(),
                NextAttemptHour = r.ReadSingle(), AdmissionFailures = r.ReadInt32() };
            var n = r.ReadInt32(); p.Children = new AboveGroundAgent[n]; p.Parents = new ulong[n]; p.LocalParents = new int[n];
            for (int j = 0; j < n; ++j) { p.Children[j] = ReadValue<AboveGroundAgent>(r); p.Parents[j] = r.ReadUInt64(); p.LocalParents[j] = r.ReadInt32(); }
            p.Retire = new ulong[r.ReadInt32()]; for (int j = 0; j < p.Retire.Length; ++j) p.Retire[j] = r.ReadUInt64();
            Developments.Add(p.Owner, p);
        }
        ReadGuidanceState(r);
        RefreshOrganIndices();
    }

    internal static void WriteValue<U>(BinaryWriter writer, U value) where U : struct
    {
        if (typeof(U) == typeof(OrganPose))
        {
            // Keep the existing 60-byte layout, but do not serialize the three
            // alignment bytes after Rounded. NativeAOT can leave them uninitialized
            // in a stack-created retry pose, making identical states differ on disk.
            // Reading old v20 poses remains valid: their padding is never state.
            if (Unsafe.SizeOf<OrganPose>() != 60)
                throw new InvalidDataException("OrganPose checkpoint layout changed; update its explicit writer.");
            var pose = Unsafe.As<U, OrganPose>(ref value);
            WriteValue(writer, pose.Base);
            WriteValue(writer, pose.Rotation);
            writer.Write(pose.Length);
            writer.Write(pose.Width);
            writer.Write(pose.Radius);
            writer.Write(pose.Rounded);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            WriteValue(writer, pose.Deformation);
            return;
        }
        writer.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
    }
    internal static U ReadValue<U>(BinaryReader reader) where U : struct
    {
        var bytes = reader.ReadBytes(Unsafe.SizeOf<U>());
        if (bytes.Length != Unsafe.SizeOf<U>()) throw new EndOfStreamException("Incomplete growth state.");
        return MemoryMarshal.Read<U>(bytes);
    }
}
