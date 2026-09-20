using System.Numerics;
using Agro.Collisions;

namespace Agro;

public partial class PlantSubFormation<T>
{
    internal readonly record struct GeometryState(OrganPose Pose, Quaternion BaseOrientation,
        Quaternion RestOrientation, Quaternion TargetOrientation, float Radius, bool Exists);

    internal GeometryState[] PreviousGeometry = [];
    internal LaminaTemplate? GetLaminaTemplate(int index)
    {
        var slice = 0;
        if (GetOrgan(index) == OrganTypes.Leaf)
            for (var p = GetParent(index); p >= 0 && GetOrgan(p) == OrganTypes.Leaf; p = GetParent(p)) ++slice;
        return LaminaTemplates.For(Plant.Parameters, GetOrgan(index), slice);
    }
    Vector3[] ContactTurns = [];
    Quaternion[] TransportedTurns = [];
    bool BatchingContacts;
    Vector3[] SupportMoments = [];
    readonly HashSet<int> RejectedBranches = [];

    internal OrganPose GetContactPose(int index)
    {
        ref var a = ref Src()[index];
        var blade = a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel;
        var radius = blade ? AboveGroundAgent.LeafThickness * 0.5f
            : a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit
                ? MathF.Max(a.Radius, a.Length) : a.Radius;
        return new(GetBaseCenterWorld(index) + a.BaseOffset, a.Orientation, a.Length,
            blade ? a.Radius : 0f, radius, a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit);
    }

    void CaptureGeometry()
    {
        if (!IsAboveGround) return;
        EnsureContactCapacity(AgentCount);
        var src = Src();
        for (int i = 0; i < AgentCount; ++i)
            PreviousGeometry[i] = new(GetContactPose(i), src[i].baseOrientation,
                src[i].restOrientation, src[i].targetOrientation, src[i].Radius, true);
    }

    void EnsureContactCapacity(int count)
    {
        if (PreviousGeometry.Length >= count) return;
        var capacity = Math.Max(count, Math.Max(16, PreviousGeometry.Length * 2));
        Array.Resize(ref PreviousGeometry, capacity);
        Array.Resize(ref ContactTurns, capacity);
        Array.Resize(ref TransportedTurns, capacity);
        Array.Resize(ref SupportMoments, capacity);
    }

    void ReindexGeometry(int oldCount, int newCount, bool reindexed)
    {
        if (!IsAboveGround) return;
        EnsureContactCapacity(Math.Max(oldCount, newCount));
        var survivors = oldCount;
        if (reindexed)
        {
            survivors = 0;
            for (int i = 0; i < oldCount; ++i)
                if (IndexMap[i] >= 0) { PreviousGeometry[IndexMap[i]] = PreviousGeometry[i]; ++survivors; }
        }
        Array.Clear(PreviousGeometry, survivors, Math.Max(0, newCount - survivors));
    }

    internal void BeginContactSweep()
    {
        EnsureContactCapacity(AgentCount);
        Array.Clear(ContactTurns, 0, AgentCount);
        BatchingContacts = true;
    }

    internal bool EndContactSweep(float maxAngle)
    {
        BatchingContacts = false;
        var src = Src();
        var queue = CollisionChildren;
        queue.Clear();
        foreach (var root in GetRoots()) queue.Enqueue(root);
        var moved = false;
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var parent = src[i].Parent;
            var carry = parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity;
            var turn = ContactTurns[i];
            var angle = turn.Length();
            var own = angle > 1e-8f ? Quaternion.CreateFromAxisAngle(turn / angle, MathF.Min(angle, maxAngle)) : Quaternion.Identity;
            var transportedBase = Quaternion.Normalize(carry * src[i].baseOrientation);
            var proposed = Quaternion.Normalize(own * carry * src[i].Orientation);
            var constrained = LimitJointBend(transportedBase, proposed);
            var delta = Quaternion.Normalize(constrained * Quaternion.Conjugate(src[i].Orientation));
            TransportedTurns[i] = delta;
            if (delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z > 1e-16f)
            {
                src[i].SetOrientation(Quaternion.Normalize(delta * src[i].Orientation));
                // The ancestor transform transports this joint's rest frame. Its own contact
                // deformation is elastic and must relax after contact is released.
                src[i].baseOrientation = Quaternion.Normalize(carry * src[i].baseOrientation);
                src[i].restOrientation = Quaternion.Normalize(carry * src[i].restOrientation);
                src[i].targetOrientation = Quaternion.Normalize(carry * src[i].targetOrientation);
                moved = true;
            }
            foreach (var child in GetChildren(i)) queue.Enqueue(child);
        }
        if (moved) RebaseAndRefit();
        return moved;
    }

    internal bool IsMechanicallyAnchored(int index) => Src()[index].SoilAnchored;
    internal bool IsLoadLimited(int index) => index >= 0 && Src()[index].LoadLimited;

    internal void RefreshAnchors(StaticCollider scene)
    {
        var src = Src();
        for (int i = 0; i < src.Length; ++i)
        {
            if (!src[i].IsRizome) continue;
            var pose = GetContactPose(i);
            // Only embedded spans are fixed. A tip outside the hull is a cantilever even when
            // its base is still buried; an isolated surface contact is a unilateral constraint.
            src[i].SoilAnchored = scene.InsideSoil(pose.Base, 1f)
                && scene.InsideSoil(pose.Tip, 1f);
        }
    }

    internal void ClearSupport()
    {
        var src = Src();
        for (int i = 0; i < src.Length; ++i) src[i].GroundSupported = false;
        EnsureContactCapacity(src.Length);
        Array.Clear(SupportMoments, 0, src.Length);
    }

    internal void SetObstruction(int index, bool blocked)
    {
        ref var a = ref Src()[index];
        if (!blocked)
        {
            if (GrowthRetries.TryGetValue(a.StableId, out var waiting) && !waiting.Attempted) return;
            GrowthRetries.Remove(a.StableId);
            a.GrowthBlocked = false; a.ObstructionHours = 0;
            return;
        }
        a.GrowthBlocked = true;
        var hour = Plant.World.Timestep * (float)Plant.World.HoursPerTick;
        if (!GrowthRetries.TryGetValue(a.StableId, out var retry))
            GrowthRetries.Add(a.StableId, retry = new() { Attempted = true });
        if (retry.Attempted || hour >= retry.NextHour)
        {
            a.ObstructionHours += Plant.World.HoursPerTick;
            ++retry.Failures;
            retry.NextHour = hour + (retry.Failures >= 3 ? 72f : 24f);
            retry.LastPose = GetContactPose(index);
            retry.Attempted = false;
        }
    }

    internal float GetObstructionHours(int index) => Src()[index].ObstructionHours;

    /// <summary>Rejects a geometry proposal without undoing age, resources, or senescence.</summary>
    internal void RestorePreviousGeometry(IEnumerable<int> rejected)
    {
        var src = Src();
        RejectedBranches.Clear();
        foreach (var index in rejected)
        {
            RejectedBranches.Add(index);
        }
        var queue = CollisionChildren;
        queue.Clear();
        foreach (var root in GetRoots()) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            foreach (var child in GetChildren(i)) queue.Enqueue(child);
            var original = src[i].Orientation;
            var parent = src[i].Parent;
            var carry = parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity;
            var previous = PreviousGeometry[i];
            if (RejectedBranches.Contains(i) && previous.Exists)
            {
                // Restore only the rejected proposal. Descendants keep their independent
                // elongation, widening and joint bend, transported by this support's change.
                var previousCarry = parent >= 0 && PreviousGeometry[parent].Exists
                    && src[parent].Orientation != PreviousGeometry[parent].Pose.Rotation
                    ? Quaternion.Normalize(src[parent].Orientation * Quaternion.Conjugate(PreviousGeometry[parent].Pose.Rotation))
                    : Quaternion.Identity;
                src[i].SetGeometry(previous.Pose.Length, previous.Radius);
                // The accepted fallback must be exact. Re-normalizing an unchanged frame
                // can move a tip across the attachment exclusion boundary by a nanometre,
                // exposing an otherwise excluded contact and defeating repeated rollback.
                src[i].SetOrientation(previousCarry == Quaternion.Identity ? previous.Pose.Rotation
                    : Quaternion.Normalize(previousCarry * previous.Pose.Rotation));
                src[i].baseOrientation = previousCarry == Quaternion.Identity ? previous.BaseOrientation
                    : Quaternion.Normalize(previousCarry * previous.BaseOrientation);
                src[i].restOrientation = previousCarry == Quaternion.Identity ? previous.RestOrientation
                    : Quaternion.Normalize(previousCarry * previous.RestOrientation);
                src[i].targetOrientation = previousCarry == Quaternion.Identity ? previous.TargetOrientation
                    : Quaternion.Normalize(previousCarry * previous.TargetOrientation);
            }
            else if (new Vector3(carry.X, carry.Y, carry.Z).LengthSquared() > 1e-16f)
            {
                src[i].SetOrientation(Quaternion.Normalize(carry * original));
                src[i].baseOrientation = Quaternion.Normalize(carry * src[i].baseOrientation);
                src[i].restOrientation = Quaternion.Normalize(carry * src[i].restOrientation);
                src[i].targetOrientation = Quaternion.Normalize(carry * src[i].targetOrientation);
            }
            TransportedTurns[i] = src[i].Orientation == original ? Quaternion.Identity
                : Quaternion.Normalize(src[i].Orientation * Quaternion.Conjugate(original));
        }
        RebaseAndRefit();
    }

    internal bool HasPendingDeaths => Deaths.Count > 0;

    // Find only the changed support component responsible for this organ's motion.
    // A change inherited rigidly from a parent is not a second independent joint bend.
    internal int ChangedContactRoot(int index, int stop = -1)
    {
        var root = index;
        for (var i = GetParent(index); i >= 0 && i != stop; i = GetParent(i))
            if (ContactSupportMoved(i)) root = i;
        return root;
    }

    internal IEnumerable<int> ChangedContactComponent(int index)
    {
        yield return index;
        // Restoring only the farthest support leaves intervening joints' independent
        // motion in place. Include every moving support on this path, not its siblings.
        // Even an embedded rhizome can transport a crown when it elongates.
        for (var i = GetParent(index); i >= 0; i = GetParent(i))
            // Final fallback also removes sub-tolerance frame drift. Unlike deformation
            // ranking, restoring an accepted configuration cannot round away changes:
            // attachment clipping has a finite boundary even for an infinitesimal sliver.
            if (PreviousGeometry[i].Exists && (GetLength(i) != PreviousGeometry[i].Pose.Length
                || GetDirection(i) != PreviousGeometry[i].Pose.Rotation)) yield return i;
    }

    bool ContactSupportMoved(int index)
    {
        if (!PreviousGeometry[index].Exists) return false;
        var previous = PreviousGeometry[index].Pose;
        var current = GetContactPose(index);
        var parent = GetParent(index);
        var carry = parent >= 0 && PreviousGeometry[parent].Exists
            ? Quaternion.Normalize(GetDirection(parent) * Quaternion.Conjugate(PreviousGeometry[parent].Pose.Rotation))
            : Quaternion.Identity;
        var relative = Quaternion.Normalize(current.Rotation * Quaternion.Conjugate(carry * previous.Rotation));
        // Radial growth does not transport the attachment; its own contact is checked separately.
        return MathF.Abs(current.Length - previous.Length) > 1e-8f
            || new Vector3(relative.X, relative.Y, relative.Z).LengthSquared() > 1e-14f;
    }

    internal void RestoreAllPreviousGeometry()
    {
        var src = Src();
        for (int i = 0; i < Count; ++i) RestoreOne(i, src);
        RebaseAndRefit();
    }

    internal GeometryState[] SaveContactTrial()
    {
        var live=Src(); var saved=new GeometryState[Count];
        for(int i=0;i<Count;++i)
            saved[i]=new(GetContactPose(i),live[i].baseOrientation,live[i].restOrientation,
                live[i].targetOrientation,live[i].Radius,true);
        return saved;
    }

    internal void RestoreContactTrial(GeometryState[] saved)
    {
        var live=Src();
        for(int i=0;i<saved.Length;++i)
        {
            var state=saved[i];
            live[i].SetGeometry(state.Pose.Length,state.Radius);
            live[i].SetOrientation(state.Pose.Rotation);
            live[i].baseOrientation=state.BaseOrientation;
            live[i].restOrientation=state.RestOrientation;
            live[i].targetOrientation=state.TargetOrientation;
        }
        RebaseAndRefit();
    }

    void RestoreOne(int i, Span<T> src)
    {
        var previous = PreviousGeometry[i];
        if (!previous.Exists) return;
        src[i].SetGeometry(previous.Pose.Length, previous.Radius);
        src[i].SetOrientation(previous.Pose.Rotation);
        src[i].baseOrientation = previous.BaseOrientation;
        src[i].restOrientation = previous.RestOrientation;
        src[i].targetOrientation = previous.TargetOrientation;
    }

    internal void SealNewGeometry()
    {
        EnsureContactCapacity(Count);
        var src = Src();
        for (int i = 0; i < Count; ++i)
            if (!PreviousGeometry[i].Exists)
                PreviousGeometry[i] = new(GetContactPose(i), src[i].baseOrientation, src[i].restOrientation, src[i].targetOrientation, src[i].Radius, true);
    }

    internal bool TryShortenGrowth(int index, Func<bool> admissible)
    {
        var old = PreviousGeometry[index];
        if (!old.Exists) return false;
        var src = Src(); var length = src[index].Length; var radius = src[index].Radius;
        if (length <= old.Pose.Length && radius <= old.Radius) return false;
        src[index].SetGeometry(MathF.Min(length, old.Pose.Length), MathF.Min(radius, old.Radius));
        RebaseAndRefit();
        if (!admissible()) { src[index].SetGeometry(length, radius); RebaseAndRefit(); return false; }
        float lo = 0, hi = 1;
        for (int step = 0; step < 8; ++step)
        {
            var mid = (lo + hi) * 0.5f;
            src[index].SetGeometry(old.Pose.Length + (length - old.Pose.Length) * mid, old.Radius + (radius - old.Radius) * mid);
            RebaseAndRefit();
            if (admissible()) lo = mid; else hi = mid;
        }
        src[index].SetGeometry(old.Pose.Length + (length - old.Pose.Length) * lo, old.Radius + (radius - old.Radius) * lo);
        RebaseAndRefit();
        // Extension and radial strengthening are independent proposals. Contact at the tip
        // must not discard admissible thickening (nor a width limit discard safe extension).
        var acceptedLength=src[index].Length;
        var acceptedRadius=src[index].Radius;
        GrowOneDimension(true,acceptedLength,length,acceptedRadius);
        GrowOneDimension(false,src[index].Radius,radius,src[index].Length);
        return true;

        void GrowOneDimension(bool longitudinal,float from,float to,float fixedSize)
        {
            if(to<=from) return;
            var live=Src();float lower=from,upper=to;
            live[index].SetGeometry(longitudinal?to:fixedSize,longitudinal?fixedSize:to);
            RebaseAndRefit();
            if(admissible()) return;
            for(int n=0;n<8;++n)
            {
                var mid=(lower+upper)*.5f;
                live[index].SetGeometry(longitudinal?mid:fixedSize,longitudinal?fixedSize:mid);
                RebaseAndRefit();
                if(admissible()) lower=mid; else upper=mid;
            }
            live[index].SetGeometry(longitudinal?lower:fixedSize,longitudinal?fixedSize:lower);
            RebaseAndRefit();
        }
    }

    internal bool TryRedirectGrowth(int index, Vector3 normal, Func<bool> admissible)
    {
        var src = Src(); var old = PreviousGeometry[index];
        if (!old.Exists || src[index].Length <= old.Pose.Length + 1e-7f || normal.LengthSquared() < 1e-12f) return false;
        normal = Vector3.Normalize(normal);
        var direction = Vector3.Transform(Vector3.UnitX, src[index].Orientation);
        var tangent = direction - normal * Vector3.Dot(direction, normal);
        var light = !IsFloralAxis(src[index].Organ) && TryGetGrowthLight(index, out var cue, out _) ? cue : direction;
        if (tangent.LengthSquared() < 1e-8f) tangent = light - normal * Vector3.Dot(light, normal);
        if (tangent.LengthSquared() < 1e-8f) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        var candidates = new List<Vector3>(6);
        foreach (var sign in new[] { 1f, -1f })
            foreach (var angle in new[] { 0f, -MathF.PI / 6f, MathF.PI / 6f })
                candidates.Add(Vector3.Normalize(Vector3.Transform(tangent * sign, Quaternion.CreateFromAxisAngle(normal, angle)) + normal * 0.05f));
        candidates.Sort((a, b) => (Vector3.Dot(b, direction) + 0.25f * Vector3.Dot(b, light))
            .CompareTo(Vector3.Dot(a, direction) + 0.25f * Vector3.Dot(a, light)));
        var affected = new List<int> { index };
        for (int j = 0; j < affected.Count; ++j) affected.AddRange(GetChildren(affected[j]));
        var saved = new GeometryState[affected.Count];
        for (int j = 0; j < affected.Count; ++j)
        {
            var i = affected[j];
            saved[j] = new(GetContactPose(i), src[i].baseOrientation, src[i].restOrientation, src[i].targetOrientation, src[i].Radius, true);
        }
        foreach (var candidate in candidates)
        {
            RestoreTrial();
            var axis = Vector3.Cross(direction, candidate);
            if (axis.LengthSquared() < 1e-10f) continue;
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(direction, candidate), -1f, 1f));
            var orientation = LimitJointBend(src[index].baseOrientation,
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) * src[index].Orientation);
            var delta = Quaternion.Normalize(orientation * Quaternion.Conjugate(src[index].Orientation));
            src[index].SetOrientation(orientation);
            PropagateToSubtree(src, index, delta);
            RebaseAndRefit();
            if (admissible()) return true;
        }
        RestoreTrial(); RebaseAndRefit(); return false;

        void RestoreTrial()
        {
            var live = Src();
            for (int j = 0; j < affected.Count; ++j)
            {
                var i = affected[j]; var state = saved[j];
                live[i].SetGeometry(state.Pose.Length, state.Radius); live[i].SetOrientation(state.Pose.Rotation);
                live[i].baseOrientation = state.BaseOrientation; live[i].restOrientation = state.RestOrientation; live[i].targetOrientation = state.TargetOrientation;
            }
        }
    }

    /// <summary>Elastic angular compliance, L/(EI), for the actual organ tissue.</summary>
    internal float ContactCompliance(int index, Vector3 turn)
        => Vector3.Dot(turn, ComplianceVector(index,turn));

    Vector3 ComplianceVector(int index, Vector3 torque)
    {
        ref var a = ref Src()[index];
        var e = a.Organ == OrganTypes.Petiole ? Plant.Parameters.PetioleElasticModulus
            : a.Organ == OrganTypes.FlowerPedicel ? Plant.Parameters.FlowerPedicelElasticModulus
            : a.Organ is OrganTypes.FlowerStem or OrganTypes.FlowerMeristem ? (Plant.Parameters.FlowerScapeElasticModulus > 0f
                ? Plant.Parameters.FlowerScapeElasticModulus : Plant.Parameters.FlowerOrganElasticModulus)
            : IsFloralAxis(a.Organ) ? Plant.Parameters.FlowerOrganElasticModulus
            : MathF.Max(Plant.Parameters.GreenElasticModulus, Plant.Parameters.BasalGreenElasticModulus
                * MathF.Exp(-GetAbsDepth(index) * Plant.Parameters.BasalStiffnessAttenuationPerSegment))
                * (1f - a.WoodRatio()) + Plant.Parameters.WoodElasticModulus * a.WoodRatio();
        if (IsFloralAxis(a.Organ))
            e = e * (1f - a.WoodRatio()) + Plant.Parameters.WoodElasticModulus * a.WoodRatio();
        var inertia = MathF.PI * MathF.Pow(a.Radius, 4) / 4f;
        if (a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel)
        {
            e = Plant.Parameters.LeafElasticModulus > 0f ? Plant.Parameters.LeafElasticModulus : Plant.Parameters.GreenElasticModulus;
            var z = Vector3.Transform(Vector3.UnitZ, a.Orientation);
            var y = Vector3.Transform(Vector3.UnitY, a.Orientation);
            var keel = BladeKeelAt(index);
            var inverse = z * (Vector3.Dot(torque,z) / MathF.Max(1e-25f,BladeInertia(a.Radius,keel)))
                + y * (Vector3.Dot(torque,y) / MathF.Max(1e-25f,BladeInertiaMajor(a.Radius,keel)));
            return inverse * (a.Length / MathF.Max(1f,e));
        }
        var axis = Vector3.Transform(Vector3.UnitX,a.Orientation);
        return (torque-axis*Vector3.Dot(torque,axis)) * (a.Length / MathF.Max(1e-20f,e*inertia));
    }

    internal static Quaternion LimitJointBend(Quaternion basis, Quaternion candidate)
    {
        var delta = Quaternion.Normalize(candidate * Quaternion.Conjugate(basis));
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return angle > CollisionSettings.MaxElasticBendRadians
            ? Quaternion.Normalize(Quaternion.Slerp(basis, candidate, CollisionSettings.MaxElasticBendRadians / angle)) : candidate;
    }

    float AvailableCompliance(int index, Vector3 axis)
    {
        ref var a = ref Src()[index];
        var delta = Quaternion.Normalize(a.Orientation * Quaternion.Conjugate(a.baseOrientation));
        if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var bend = new Vector3(delta.X, delta.Y, delta.Z);
        if (2f * MathF.Atan2(bend.Length(), delta.W) >= CollisionSettings.MaxElasticBendRadians - 1e-5f
            && Vector3.Dot(bend, axis) > 0) return 0;
        return ContactCompliance(index, axis);
    }

    internal double ContactMobility(int index, Vector3 contact, Vector3 normal, int stop = -1)
    {
        double total = 0; var src = Src();
        for (var n = index; n >= 0 && n != stop && !src[n].SoilAnchored; n = src[n].Parent)
        {
            var cross = Vector3.Cross(contact - GetBaseCenterWorld(n) - src[n].BaseOffset, normal);
            var gain = cross.Length();
            if (gain > 1e-8f) total += AvailableCompliance(n, cross / gain) * (double)gain * gain;
        }
        return total;
    }

    internal bool QueueContactCorrection(int index, Vector3 contact, Vector3 normal, float depth, int stop = -1)
    {
        var src = Src();
        var total = ContactMobility(index, contact, normal, stop);
        // Existing graph segments supply the degrees of freedom. Walk to a rooted anchor;
        // a broad, unsplit blade can therefore deflect through its petiole.
        if (total > 1e-20 && double.IsFinite(total))
        {
            var lambda = depth / total;
            for (var n = index; n >= 0 && n != stop && !src[n].SoilAnchored; n = src[n].Parent)
            {
                var cross = Vector3.Cross(contact - GetBaseCenterWorld(n) - src[n].BaseOffset, normal);
                var gain = cross.Length();
                if (gain <= 1e-8f) continue;
                var axis = cross / gain;
                if (AvailableCompliance(n,axis) <= 0) continue;
                var response = ComplianceVector(n,cross) * (float)lambda;
                var angle = response.Length();
                if (angle > CollisionSettings.MaxCorrectionRadians) response *= CollisionSettings.MaxCorrectionRadians/angle;
                ContactTurns[n] += response;
            }
            return true;
        }
        // Axial impact has zero first-order gain. Solve the finite rotation rather than
        // dividing by a fabricated lever. Its side is deterministic, independent of mesh winding.
        var best = -1; float bestMobility = 0; Vector3 bestAxis = default; float bestAngle = 0;
        for (var n = index; n >= 0 && n != stop && !src[n].SoilAnchored; n = src[n].Parent)
        {
            var lever = contact - GetBaseCenterWorld(n) - src[n].BaseOffset;
            var length = lever.Length();
            if (length < 1e-4f || Vector3.Dot(lever, normal) >= 0f) continue;
            var axis = Vector3.Cross(lever, MathF.Abs(lever.Y) < length * 0.9f ? Vector3.UnitY : Vector3.UnitX);
            axis = Vector3.Normalize(axis);
            var angle = MathF.Acos(Math.Clamp(1f - depth / length, -1f, 1f));
            var mobility = AvailableCompliance(n, axis) * length * length;
            if (mobility > bestMobility) { best = n; bestMobility = mobility; bestAxis = axis; bestAngle = angle; }
        }
        if (best < 0) return false;
        ContactTurns[best] += bestAxis * MathF.Min(bestAngle, CollisionSettings.MaxCorrectionRadians);
        return true;
    }

    internal void RecordSupportReaction(int index, Vector3 point, Vector3 normal)
    {
        if (normal.Y <= 0f || Weights.Count < Count) return;
        double mobility = 0, closing = 0;
        var src = Src();
        for (var n = index; n >= 0 && !src[n].SoilAnchored; n = src[n].Parent)
        {
            var cross = Vector3.Cross(point - GetBaseCenterWorld(n), normal);
            var gain = cross.Length(); if (gain < 1e-8f || Weights[n] <= 0) continue;
            var axis = cross / gain;
            var torque = Vector3.Cross(WeightedCentroids[n] - GetBaseCenter(n) * Weights[n], -Vector3.UnitY) + SupportMoments[n];
            var compliance = ContactCompliance(n, axis);
            mobility += compliance * (double)gain * gain;
            closing += Vector3.Dot(cross,ComplianceVector(n,torque));
        }
        if (mobility < 1e-20 || closing >= 0) return;
        var force = normal * (float)(-closing / mobility);
        src[index].GroundSupported = true;
        for (var n = index; n >= 0 && !src[n].SoilAnchored; n = src[n].Parent)
            SupportMoments[n] += Vector3.Cross(point - GetBaseCenterWorld(n), force);
    }

    internal void UpdateSupportedLoads()
    {
        if (Weights.Count < Count) return;
        var src = Src();
        for (int i = 0; i < Count; ++i)
        {
            if (!src[i].IsRizome || src[i].SoilAnchored) continue;
            var torque = Vector3.Cross(WeightedCentroids[i] - GetBaseCenter(i) * Weights[i], -Vector3.UnitY) + SupportMoments[i];
            var axis = Vector3.Transform(Vector3.UnitX, src[i].Orientation);
            torque -= axis * Vector3.Dot(torque, axis);
            var size = torque.Length();
            var angle = size > 1e-12f ? size * ContactCompliance(i, torque / size) : 0;
            src[i].LoadLimited = !float.IsFinite(angle) || angle > CollisionSettings.MaxElasticBendRadians;
        }
    }
}
