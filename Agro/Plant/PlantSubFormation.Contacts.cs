using System.Numerics;
using Agro.Collisions;

namespace Agro;

internal readonly record struct ContactAngularCompliance(Vector3 First, Vector3 Second, double AlongFirst, double AlongSecond)
{
    static double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
    internal double Bilinear(Vector3 a, Vector3 b)
        => AlongFirst * Dot(a, First) * Dot(b, First) + AlongSecond * Dot(a, Second) * Dot(b, Second);
    internal Vector3 Response(Vector3 torque)
        => First * (float)(AlongFirst * Dot(torque, First)) + Second * (float)(AlongSecond * Dot(torque, Second));
}

public partial class PlantSubFormation<T>
{
    internal readonly record struct GeometryState(OrganPose Pose, Quaternion BaseOrientation,
        Quaternion RestOrientation, Quaternion TargetOrientation, float Radius, bool Exists);

    internal GeometryState[] PreviousGeometry = [];
    LaminaTemplate?[] LaminaCache = [];
    OrganTypes[] LaminaKinds = [];
    bool[] LaminaKnown = [];

    void InvalidateLaminaCache() => Array.Clear(LaminaKnown);

    internal LaminaTemplate? GetLaminaTemplate(int index)
    {
        var organ = GetOrgan(index);
        if (organ is not (OrganTypes.Leaf or OrganTypes.FlowerPadel)) return null;
        if (index >= LaminaCache.Length)
        {
            var capacity = Math.Max(AgentCount, Math.Max(16, LaminaCache.Length * 2));
            Array.Resize(ref LaminaCache, capacity);
            Array.Resize(ref LaminaKinds, capacity);
            Array.Resize(ref LaminaKnown, capacity);
        }
        if (LaminaKnown[index] && LaminaKinds[index] == organ) return LaminaCache[index];
        var slice = 0;
        if (organ == OrganTypes.Leaf)
            for (var p = GetParent(index); p >= 0 && GetOrgan(p) == OrganTypes.Leaf; p = GetParent(p)) ++slice;
        LaminaKinds[index] = organ;
        LaminaKnown[index] = true;
        return LaminaCache[index] = LaminaTemplates.For(Plant.Parameters, organ, slice);
    }
    Vector3[] ContactTurns = [];
    Quaternion[] TransportedTurns = [];
    bool BatchingContacts;
    Vector3[] SupportMoments = [];
    readonly HashSet<int> RejectedBranches = [];
    GeometryState[] ContactTrialStates = [], RedirectTrialStates = [], ContinuationStates = [];
    readonly List<int> RedirectTrialOrgans = [];
    readonly List<int> ContactMovedRoots = [];
    readonly List<Vector3> RedirectCandidates = new(6);
    readonly Comparison<Vector3> RedirectComparison;
    Vector3 RedirectDirection, RedirectLight;

    int CompareRedirectCandidates(Vector3 a, Vector3 b)
        => (Vector3.Dot(b, RedirectDirection) + 0.25f * Vector3.Dot(b, RedirectLight))
            .CompareTo(Vector3.Dot(a, RedirectDirection) + 0.25f * Vector3.Dot(a, RedirectLight));

    internal OrganPose GetContactPose(int index)
    {
        ref var a = ref Src()[index];
        var blade = a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel;
        var radius = blade ? AboveGroundAgent.LeafThickness * 0.5f
            : a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit
                ? MathF.Max(a.Radius, a.Length) : a.Radius;
        return new(GetBaseCenterWorld(index) + a.BaseOffset, a.Orientation, a.Length,
            blade ? a.Radius : 0f, radius, a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit,
            GetDeformation(index));
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
        BeginDeformationSweep();
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
        ContactMovedRoots.Clear();
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var cosineLimit = MathF.Cos(ContactBendLimit(i) * .5f);
            var parent = src[i].Parent;
            var carry = parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity;
            var oldPose = GetContactPose(i);
            var deformed = ApplyDeformationIncrement(i);
            var turn = ContactTurns[i];
            if (!deformed && turn == Vector3.Zero && carry == Quaternion.Identity)
            {
                // No load correction and no parent transport: a joint safely inside
                // its limit is an exact no-op. Re-normalizing such frames every solver
                // pass created tiny artificial motions and invalidated clearance caches.
                var basis = src[i].baseOrientation; var orientation = src[i].Orientation;
                var norm = MathF.Sqrt(basis.LengthSquared() * orientation.LengthSquared());
                if (norm > 0 && MathF.Abs(Quaternion.Dot(basis, orientation)) > (cosineLimit + 1e-6f) * norm)
                {
                    TransportedTurns[i] = Quaternion.Identity;
                    foreach (var child in GetChildren(i)) queue.Enqueue(child);
                    continue;
                }
            }
            var angle = turn.Length();
            var own = angle > 1e-8f ? Quaternion.CreateFromAxisAngle(turn / angle, MathF.Min(angle, maxAngle)) : Quaternion.Identity;
            var transportedBase = Quaternion.Normalize(carry * src[i].baseOrientation);
            var proposed = Quaternion.Normalize(own * carry * src[i].Orientation);
            var constrained = LimitJointBend(transportedBase, proposed, ContactBendLimit(i));
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
                if (carry == Quaternion.Identity) ContactMovedRoots.Add(i);
                moved = true;
            }
            else TransportedTurns[i] = Quaternion.Identity;
            if(Deformable(i) && GetOrgan(i)==OrganTypes.Petiole)
            {
                var current=GetDeformation(i);var limited=LimitedShape(i,current);
                if(current!=limited){SetDeformation(i,limited);deformed=true;}
            }
            if (deformed)
            {
                TransportedTurns[i] = Quaternion.Normalize(AttachmentRotation(GetContactPose(i))
                    * Quaternion.Conjugate(AttachmentRotation(oldPose)));
                if (carry == Quaternion.Identity && !ContactMovedRoots.Contains(i)) ContactMovedRoots.Add(i);
                moved = true;
            }
            foreach (var child in GetChildren(i)) queue.Enqueue(child);
        }
        if (moved)
            foreach (var root in ContactMovedRoots) RebaseAndRefit(root);
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
            var originalAttachment = AttachmentRotation(GetContactPose(i));
            var parent = src[i].Parent;
            var carry = parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity;
            var previous = PreviousGeometry[i];
            if (RejectedBranches.Contains(i) && previous.Exists)
            {
                // Restore only the rejected proposal. Descendants keep their independent
                // elongation, widening and joint bend, transported by this support's change.
                var previousCarry = parent >= 0 && PreviousGeometry[parent].Exists
                    && AttachmentRotation(GetContactPose(parent)) != AttachmentRotation(PreviousGeometry[parent].Pose)
                    ? Quaternion.Normalize(AttachmentRotation(GetContactPose(parent)) * Quaternion.Conjugate(AttachmentRotation(PreviousGeometry[parent].Pose)))
                    : Quaternion.Identity;
                src[i].SetGeometry(previous.Pose.Length, previous.Radius);
                SetDeformation(i, previous.Pose.Deformation);
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
            var attachment = AttachmentRotation(GetContactPose(i));
            TransportedTurns[i] = attachment == originalAttachment ? Quaternion.Identity
                : Quaternion.Normalize(attachment * Quaternion.Conjugate(originalAttachment));
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
                || GetDirection(i) != PreviousGeometry[i].Pose.Rotation
                || GetDeformation(i) != PreviousGeometry[i].Pose.Deformation)) yield return i;
    }

    bool ContactSupportMoved(int index)
    {
        if (!PreviousGeometry[index].Exists) return false;
        var previous = PreviousGeometry[index].Pose;
        var current = GetContactPose(index);
        var parent = GetParent(index);
        var carry = parent >= 0 && PreviousGeometry[parent].Exists
            ? Quaternion.Normalize(AttachmentRotation(GetContactPose(parent)) * Quaternion.Conjugate(AttachmentRotation(PreviousGeometry[parent].Pose)))
            : Quaternion.Identity;
        var relative = Quaternion.Normalize(current.Rotation * Quaternion.Conjugate(carry * previous.Rotation));
        // Radial growth does not transport the attachment; its own contact is checked separately.
        return current.Deformation != previous.Deformation || MathF.Abs(current.Length - previous.Length) > 1e-8f
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
        // Development retries are sequential within one plant. Their fallback remains
        // live until that proposal commits or is restored; redirection has separate scratch.
        if (ContactTrialStates.Length < Count)
            Array.Resize(ref ContactTrialStates, Math.Max(Count, ContactTrialStates.Length * 3 / 2 + 8));
        var live=Src(); var saved=ContactTrialStates;
        for(int i=0;i<Count;++i)
            saved[i]=new(GetContactPose(i),live[i].baseOrientation,live[i].restOrientation,
                live[i].targetOrientation,live[i].Radius,true);
        return saved;
    }

    // Separate reusable scratch: a coupled growth step may move several plants,
    // while ordinary development/redirection trials use their own saved frames.
    internal GeometryState[] SaveContinuationGeometry()
    {
        if (ContinuationStates.Length < Count)
            Array.Resize(ref ContinuationStates, Math.Max(Count, ContinuationStates.Length * 3 / 2 + 8));
        var live = Src();
        for (int i = 0; i < Count; ++i)
            ContinuationStates[i] = new(GetContactPose(i), live[i].baseOrientation,
                live[i].restOrientation, live[i].targetOrientation, live[i].Radius, true);
        return ContinuationStates;
    }

    internal bool HasContinuationWaypoint => ReferenceEquals(PreviousGeometry, ContinuationStates);

    internal void SetContinuationDimensions(int index, float length, float radius)
    {
        Src()[index].SetGeometry(length, radius);
        RebaseAndRefit(index);
    }

    internal void CompleteCertifiedGrowth(int index)
    {
        ref var organ = ref Src()[index];
        GrowthRetries.Remove(organ.StableId);
        organ.GrowthBlocked = false;
        organ.ObstructionHours = 0f;
    }

    internal void RestoreContactTrial(GeometryState[] saved)
    {
        var live=Src();
        // A trial cannot commit births before restoration. Capacity may exceed the
        // live count after an earlier proposal grew or compacted the plant.
        for(int i=0;i<Count;++i)
        {
            var state=saved[i];
            SetDeformation(i, state.Pose.Deformation);
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
        SetDeformation(i, previous.Pose.Deformation);
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
        RebaseAndRefit(index);
        if (!admissible()) { src[index].SetGeometry(length, radius); RebaseAndRefit(index); return false; }
        float lo = 0, hi = 1;
        for (int step = 0; step < 8; ++step)
        {
            var mid = (lo + hi) * 0.5f;
            src[index].SetGeometry(old.Pose.Length + (length - old.Pose.Length) * mid, old.Radius + (radius - old.Radius) * mid);
            RebaseAndRefit(index);
            if (admissible()) lo = mid; else hi = mid;
        }
        src[index].SetGeometry(old.Pose.Length + (length - old.Pose.Length) * lo, old.Radius + (radius - old.Radius) * lo);
        RebaseAndRefit(index);
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
            RebaseAndRefit(index);
            if(admissible()) return;
            for(int n=0;n<8;++n)
            {
                var mid=(lower+upper)*.5f;
                live[index].SetGeometry(longitudinal?mid:fixedSize,longitudinal?fixedSize:mid);
                RebaseAndRefit(index);
                if(admissible()) lower=mid; else upper=mid;
            }
            live[index].SetGeometry(longitudinal?lower:fixedSize,longitudinal?fixedSize:lower);
            RebaseAndRefit(index);
        }
    }

    // A simultaneous contact correction may fail even when one restored organ can
    // safely complete its biological increment against the accepted neighbours.
    // Retry that exact increment only after rollback, and retain it only with a
    // full swept clearance certificate for the organ and all its descendants.
    internal bool TryGrowthAfterRollback(int index, float length, float radius, Func<bool> admissible,
        bool allowPartial = false)
    {
        if (!PreviousGeometry[index].Exists || HasPendingDevelopment(index)) return false;
        var live = Src();
        var previousLength = live[index].Length;
        var previousRadius = live[index].Radius;
        if (length < previousLength || radius < previousRadius
            || length <= previousLength + 1e-7f && radius <= previousRadius + 1e-7f) return false;
        live[index].SetGeometry(length, radius);
        RebaseAndRefit(index);
        if (admissible())
        {
            GrowthRetries.Remove(live[index].StableId);
            live[index].GrowthBlocked = false;
            live[index].ObstructionHours = 0f;
            return true;
        }
        live[index].SetGeometry(previousLength, previousRadius);
        RebaseAndRefit(index);
        if (allowPartial && admissible())
        {
            // Search from the accepted dimensions, not the pre-tick snapshot: a
            // prior certified retry may already have retained part of the growth.
            // Extension and thickening have separate clearance limits.
            GrowDimension(longitudinal: true, length);
            GrowDimension(longitudinal: false, radius);
            if ((live[index].Length > previousLength + 1e-7f
                || live[index].Radius > previousRadius + 1e-7f) && admissible())
            {
                GrowthRetries.Remove(live[index].StableId);
                live[index].GrowthBlocked = false;
                live[index].ObstructionHours = 0f;
                return true;
            }
            live[index].SetGeometry(previousLength, previousRadius);
            RebaseAndRefit(index);
        }
        return false;

        void GrowDimension(bool longitudinal, float target)
        {
            var current = Src();
            var lower = longitudinal ? current[index].Length : current[index].Radius;
            if (target <= lower + 1e-7f) return;
            var fixedSize = longitudinal ? current[index].Radius : current[index].Length;
            void Set(float value)
            {
                Src()[index].SetGeometry(longitudinal ? value : fixedSize,
                    longitudinal ? fixedSize : value);
                RebaseAndRefit(index);
            }
            Set(target);
            if (admissible()) return;
            var upper = target;
            for (var step = 0; step < 8; ++step)
            {
                var mid = (lower + upper) * .5f;
                Set(mid);
                if (admissible()) lower = mid; else upper = mid;
            }
            Set(lower);
        }
    }

    internal bool TryRedirectGrowth(int index, Vector3 normal, Func<bool> admissible,
        bool stationaryPetioleSupport = false)
    {
        var src = Src(); var old = PreviousGeometry[index];
        if (!old.Exists || normal.LengthSquared() < 1e-12f
            || stationaryPetioleSupport && (src[index].Organ != OrganTypes.Petiole
                || IsMechanicallyAnchored(index))
            || !stationaryPetioleSupport && src[index].Length <= old.Pose.Length + 1e-7f)
            return false;
        normal = Vector3.Normalize(normal);
        var direction = Vector3.Transform(Vector3.UnitX, src[index].Orientation);
        var tangent = direction - normal * Vector3.Dot(direction, normal);
        var light = !IsFloralAxis(src[index].Organ) && TryGetGrowthLight(index, out var cue, out _) ? cue : direction;
        if (tangent.LengthSquared() < 1e-8f) tangent = light - normal * Vector3.Dot(light, normal);
        if (tangent.LengthSquared() < 1e-8f) tangent = Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        var candidates = RedirectCandidates;
        candidates.Clear();
        for (int signIndex = 0; signIndex < 2; ++signIndex)
            for (int angleIndex = 0; angleIndex < 3; ++angleIndex)
            {
                var sign = signIndex == 0 ? 1f : -1f;
                var angle = angleIndex == 0 ? 0f : angleIndex == 1 ? -MathF.PI / 6f : MathF.PI / 6f;
                candidates.Add(Vector3.Normalize(Vector3.Transform(tangent * sign, Quaternion.CreateFromAxisAngle(normal, angle)) + normal * 0.05f));
            }
        RedirectDirection = direction; RedirectLight = light;
        candidates.Sort(RedirectComparison);
        var affected = RedirectTrialOrgans;
        affected.Clear(); affected.Add(index);
        for (int j = 0; j < affected.Count; ++j) affected.AddRange(GetChildren(affected[j]));
        if (RedirectTrialStates.Length < affected.Count)
            Array.Resize(ref RedirectTrialStates, Math.Max(affected.Count, Math.Max(16, RedirectTrialStates.Length * 2)));
        var saved = RedirectTrialStates;
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
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle) * src[index].Orientation, ContactBendLimit(index));
            if (stationaryPetioleSupport
                && MathF.Abs(Quaternion.Dot(orientation, src[index].Orientation)) >= 1f - 1e-7f)
                continue;
            var delta = Quaternion.Normalize(orientation * Quaternion.Conjugate(src[index].Orientation));
            src[index].SetOrientation(orientation);
            PropagateToSubtree(src, index, delta);
            RebaseAndRefit(index);
            if (admissible()) return true;
        }
        RestoreTrial(); RebaseAndRefit(index); return false;

        void RestoreTrial()
        {
            var live = Src();
            for (int j = 0; j < affected.Count; ++j)
            {
                var i = affected[j]; var state = saved[j];
                SetDeformation(i, state.Pose.Deformation);
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
            return inverse * (a.Length / MathF.Max(1f,e)) / (Deformable(index) ? 3f : 1f);
        }
        var axis = Vector3.Transform(Vector3.UnitX,a.Orientation);
        return (torque-axis*Vector3.Dot(torque,axis)) * (a.Length / MathF.Max(1e-20f,e*inertia)) / (Deformable(index) ? 3f : 1f);
    }

    float ContactBendLimit(int index) => GetOrgan(index) == OrganTypes.Petiole && Plant.Parameters.PetioleContactBendDegrees > 0
        ? Plant.Parameters.PetioleContactBendDegrees * (MathF.PI / 180f) : CollisionSettings.MaxElasticBendRadians;

    internal static Quaternion LimitJointBend(Quaternion basis, Quaternion candidate, float limit = 0f)
    {
        if (limit <= 0f) limit = CollisionSettings.MaxElasticBendRadians;
        var delta = Quaternion.Normalize(candidate * Quaternion.Conjugate(basis));
        var angle = 2f * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W));
        return angle > limit
            ? Quaternion.Normalize(Quaternion.Slerp(basis, candidate, limit / angle)) : candidate;
    }

    float AvailableCompliance(int index, Vector3 axis)
        => ContactTurnAvailable(index, axis) ? ContactCompliance(index, axis) : 0;

    bool ContactTurnAvailable(int index, Vector3 axis)
    {
        ref var a = ref Src()[index];
        var delta = Quaternion.Normalize(a.Orientation * Quaternion.Conjugate(a.baseOrientation));
        if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var bend = new Vector3(delta.X, delta.Y, delta.Z);
        if (2f * MathF.Atan2(bend.Length(), delta.W) >= ContactBendLimit(index) - 1e-5f
            && Vector3.Dot(bend, axis) > 0) return false;
        return true;
    }

    internal double ContactMobility(int index, Vector3 contact, Vector3 normal, int stop = -1)
    {
        double total = 0; var src = Src();
        for (var n = index; n >= 0 && n != stop && !src[n].SoilAnchored; n = src[n].Parent)
        {
            var cross = Vector3.Cross(contact - GetBaseCenterWorld(n) - src[n].BaseOffset, normal);
            var gain = cross.Length();
            if (gain > 1e-8f) total += AvailableCompliance(n, cross / gain) * (double)gain * gain;
            total += ShapeMobility(n, index, contact, normal);
        }
        return total;
    }

    // Tangent of the same compliance operator used by QueueContactCorrection.
    // Kept separate from geometry trials so several contact rows can share a joint.
    internal (Vector3 Torque, Vector3 Turn, Vector4 Shape, Vector4 Fold) ContactGradient(
        int joint, int contacted, Vector3 point, Vector3 normal, bool coupledLimits = false)
    {
        var torque = Vector3.Cross(point - GetBaseCenterWorld(joint) - Src()[joint].BaseOffset, normal);
        // Coupled contacts solve the joint's unilateral angular limit as one
        // constraint. Removing compliance separately for each force direction
        // produces a nonsymmetric matrix when contacts share a saturated joint.
        var gain = torque.Length();
        // V1's assembler applies one cached material tensor per shared joint.
        // Keep the inexpensive directional response for the rigid retry path.
        var turn = !coupledLimits && gain > 1e-8f && ContactTurnAvailable(joint, torque / gain)
            ? ComplianceVector(joint, torque) : Vector3.Zero;
        if (coupledLimits && gain <= 1e-8f) torque = Vector3.Zero;
        var shape = Deformable(joint) ? ShapeGradient(joint, contacted, point, normal) : Vector4.Zero;
        return (torque, turn, shape, Deformable(joint) ? ShapeResponse(joint, shape) : Vector4.Zero);
    }

    internal ContactAngularCompliance CoupledAngularCompliance(int joint)
    {
        ref var a = ref Src()[joint];
        var first = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, a.Orientation));
        var second = Vector3.Normalize(Vector3.Cross(first, Vector3.Transform(Vector3.UnitX, a.Orientation)));
        var alongFirst = (double)ContactCompliance(joint, first);
        var alongSecond = a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel
            ? (double)ContactCompliance(joint, second) : alongFirst;
        // The scalar beam coefficients are unchanged. Double spectral products
        // keep reciprocal terms consistent, especially when a limit removes an
        // entire direction and its remaining compliance is nearly zero.
        return new(first, second, alongFirst, alongSecond);
    }

    internal bool ContactBendConstraint(int joint, out Vector3 normal, out double allowance)
    {
        ref var a = ref Src()[joint];
        var delta = Quaternion.Normalize(a.Orientation * Quaternion.Conjugate(a.baseOrientation));
        if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var bend = new Vector3(delta.X, delta.Y, delta.Z);
        var sine = bend.Length();
        var angle = 2f * MathF.Atan2(sine, delta.W);
        allowance = Math.Max(0, ContactBendLimit(joint) - angle);
        normal = Vector3.Zero;
        // Inside the limit the existing finite-step clamp remains authoritative.
        // Only near-saturated joints need an additional tangent constraint.
        if (sine <= 1e-8f || allowance > 1e-5) return false;
        normal = bend / sine;
        return true;
    }

    internal (Vector3 Torque, Vector3 Turn, Vector4 Shape, Vector4 Fold) CommonContactGradient(
        int common, int first, Vector3 a, int second, Vector3 b, Vector3 normal)
    {
        if (common < 0 || !Deformable(common) || IsMechanicallyAnchored(common)) return default;
        var shape = ShapeGradient(common, first, a, normal) + ShapeGradient(common, second, b, -normal);
        return (Vector3.Zero, Vector3.Zero, shape, ShapeResponse(common, shape));
    }

    internal void QueueCoupledContactResponse(int joint, Vector3 turn, Vector4 fold, float force)
    {
        ContactTurns[joint] += turn * force;
        if (!Deformable(joint)) return;
        var state = State(joint);
        state.Pending += fold * force;
        state.ReactionNewtons = MathF.Max(state.ReactionNewtons, force);
    }

    internal float ContactForceStepLimit(int index,Vector3 contact,Vector3 normal,int stop=-1)
    {
        var limit=float.MaxValue;var src=Src();
        for(var n=index;n>=0 && n!=stop && !src[n].SoilAnchored;n=src[n].Parent)
        {
            var cross=Vector3.Cross(contact-GetBaseCenterWorld(n)-src[n].BaseOffset,normal);
            var gain=cross.Length();
            var turn=gain>1e-8f && AvailableCompliance(n,cross/gain)>0 ? ComplianceVector(n,cross).Length() : 0;
            if(turn>0)limit=MathF.Min(limit,CollisionSettings.MaxCorrectionRadians/turn);
            if(Deformable(n))
            {
                var response=Vector4.Abs(ShapeResponse(n,ShapeGradient(n,index,contact,normal)));
                var largest=MathF.Max(MathF.Max(response.X,response.Y),MathF.Max(response.Z,response.W));
                if(largest>0)limit=MathF.Min(limit,CollisionSettings.MaxCorrectionRadians/largest);
            }
        }
        return limit;
    }

    internal bool QueueContactCorrection(int index, Vector3 contact, Vector3 normal, float depth, int stop = -1)
    {
        if(depth<0)return QueueContactCorrection(index,contact,-normal,-depth,stop);
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
                QueueShapeCorrection(n, index, contact, normal, (float)lambda);
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
