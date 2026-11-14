using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        Quaternion RestOrientation, Quaternion TargetOrientation, float Radius, bool Exists,
        Vector3 BaseOffset = default, Quaternion UnitReferenceLocal = default,
        AboveGroundAgent.TreeLightMaterial LightMaterial = default);

    internal GeometryState[] PreviousGeometry = [];

    // Contact frames belong to above-ground organs; underground agents have no offsets.
    static void SetContactOffset(ref T agent, Vector3 offset)
        => Unsafe.As<T, AboveGroundAgent>(ref agent).BaseOffset = offset;

    GeometryState CaptureContactGeometry(int index, ref T agent)
    {
        var state = new GeometryState(GetContactPose(index), agent.baseOrientation, agent.restOrientation,
            agent.targetOrientation, agent.Radius, true, agent.BaseOffset,
            Plant.Parameters.Behavior == Behavior.Woody && Plant.Parameters.TreeGrowthUnitsEnabled
                ? Unsafe.As<T, AboveGroundAgent>(ref agent).CaptureTreeUnitReference() : default,
            Plant.Parameters.Behavior == Behavior.Woody && Plant.Parameters.TreeGrowthUnitsEnabled
                ? Unsafe.As<T, AboveGroundAgent>(ref agent).CaptureTreeLightMaterial() : default);
        RememberSecondarySnapshot(agent.StableId, state);
        return state;
    }

    void RestoreContactUnitReference(ref T agent, in GeometryState state)
    {
        RestoreSecondarySnapshot(agent.StableId, state);
        if (Plant.Parameters.Behavior == Behavior.Woody && Plant.Parameters.TreeGrowthUnitsEnabled)
        {
            Unsafe.As<T, AboveGroundAgent>(ref agent).RestoreTreeUnitReference(state.UnitReferenceLocal);
            Unsafe.As<T, AboveGroundAgent>(ref agent).RestoreTreeLightMaterial(state.LightMaterial);
        }
    }

    readonly record struct GrowthTrialBookmark(int Offset, int Count);
    readonly List<int> GrowthTrialOrgans = [];
    GeometryState[] GrowthTrialFrames = [];

    GrowthTrialBookmark CaptureGrowthTrial(int index)
    {
        var live = Src();
        if (!HasSecondaryStep(live[index].StableId) && (Plant.Parameters.Behavior != Behavior.Woody || !Plant.Parameters.TreeGrowthUnitsEnabled
            || !Unsafe.As<T, AboveGroundAgent>(ref live[index]).IsTreeUnitAxis
            || !WoodyExtensions.ContainsKey(live[index].StableId))) return default;
        // Append ranges rather than overwriting shared scratch: a nested trial
        // cannot invalidate its caller's immutable local subtree bookmark.
        var offset = GrowthTrialOrgans.Count;
        GrowthTrialOrgans.Add(index);
        for (int j = offset; j < GrowthTrialOrgans.Count; ++j)
            foreach (var child in GetChildren(GrowthTrialOrgans[j])) GrowthTrialOrgans.Add(child);
        var count = GrowthTrialOrgans.Count - offset;
        if (GrowthTrialFrames.Length < GrowthTrialOrgans.Count)
            Array.Resize(ref GrowthTrialFrames, Math.Max(GrowthTrialOrgans.Count, Math.Max(16, GrowthTrialFrames.Length * 2)));
        for (int j = offset; j < offset + count; ++j)
        {
            var organ = GrowthTrialOrgans[j];
            GrowthTrialFrames[j] = CaptureContactGeometry(organ, ref live[organ]);
        }
        return new(offset, count);
    }

    void RestoreGrowthTrial(GrowthTrialBookmark bookmark)
    {
        var live = Src();
        for (int j = bookmark.Offset; j < bookmark.Offset + bookmark.Count; ++j)
        {
            var i = GrowthTrialOrgans[j];
            var state = GrowthTrialFrames[j];
            SetDeformation(i, state.Pose.Deformation);
            live[i].SetGeometry(state.Pose.Length, state.Radius);
            live[i].SetOrientation(state.Pose.Rotation);
            live[i].baseOrientation = state.BaseOrientation;
            live[i].restOrientation = state.RestOrientation;
            live[i].targetOrientation = state.TargetOrientation;
            SetContactOffset(ref live[i], state.BaseOffset);
            RestoreContactUnitReference(ref live[i], state);
        }
    }

    void ReleaseGrowthTrial(GrowthTrialBookmark bookmark)
    {
        if (bookmark.Count > 0)
            GrowthTrialOrgans.RemoveRange(bookmark.Offset, bookmark.Count);
    }

    void SetGrowthTrialGeometry(int index, float length, float radius, GrowthTrialBookmark bookmark = default)
    {
        if (bookmark.Count > 0) RestoreGrowthTrial(bookmark);
        var live = Src();
        ref var agent = ref live[index];
        if (Plant.Parameters.Behavior == Behavior.Woody && Plant.Parameters.TreeGrowthUnitsEnabled
            && length != agent.Length
            && Unsafe.As<T, AboveGroundAgent>(ref agent).IsTreeUnitAxis
            && WoodyExtensions.TryGetValue(agent.StableId, out var proposal) && proposal.HasLightStep)
        {
            ref var woody = ref Unsafe.As<T, AboveGroundAgent>(ref agent);
            if (proposal.Module.IsDorsiventral && woody.TreeModuleRootStableId != proposal.Module.RootStableId)
                throw new InvalidDataException("A growth trial cannot change developmental module identity.");
            var oldExtension = woody.TreeTrialExtension(proposal.LightStartLength, proposal.ProposedLength,
                agent.Length, proposal.LightStep);
            var newExtension = woody.TreeTrialExtension(proposal.LightStartLength, proposal.ProposedLength,
                length, proposal.LightStep);
            var zeroModule = (proposal.Module.IsDorsiventral || woody.CorrectsOrdinaryLateralUnitTrial(Plant.Parameters))
                && newExtension == proposal.LightStep.StartExtension
                && length == proposal.StartLength;
            var moduleBefore = default(AboveGroundAgent.TreeModuleMaterialFrame);
            var moduleLocalBefore = default(AboveGroundAgent.TreeModuleLocalMaterial);
            var exactUnchangedPostOwn = false;
            if (zeroModule)
            {
                if (ModuleMaterialFrames == null || !ModuleMaterialFrames.TryGetValue(woody.StableId, out var materialPair))
                    throw new InvalidDataException("A zero unit trial lacks its paired pre-own/post-own material snapshot.");
                if (materialPair.Tick != Plant.World.Timestep || materialPair.ModuleRootStableId != woody.TreeModuleRootStableId)
                    throw new InvalidDataException("A module trial material pair belongs to a different tick/root identity.");
                moduleBefore = materialPair.Before;
                moduleLocalBefore = materialPair.LocalBefore;
                exactUnchangedPostOwn = woody.CaptureTreeModuleMaterial().SameBits(materialPair.Proposed);
            }
            var continuation = FindOrgan(proposal.Continuation);
            var relay = FindOrgan(proposal.Relay);
            var continuationFrame = continuation >= 0
                ? Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).CaptureTreeUnitFrame() : Quaternion.Identity;
            var relayFrame = relay >= 0
                ? Unsafe.As<T, AboveGroundAgent>(ref live[relay]).CaptureTreeUnitFrame() : Quaternion.Identity;
            var corrected = proposal.ReferenceAtProposal.LengthSquared() > .5f;
            var endpointTurn = continuation < 0 ? Quaternion.Identity : corrected
                ? Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).LateralTreeUnitTrialTurnWithLight(
                    Plant.Parameters, oldExtension, newExtension, proposal.LightStep,
                    proposal.ReferenceAtProposal, proposal.Opening, atTip: true)
                : Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).TreeUnitTrialTurnWithLight(
                    Plant.Parameters, oldExtension, newExtension, proposal.LightStep, atTip: true, module: proposal.Module);
            var carry = corrected
                ? woody.TransportLateralTreeUnitTrialWithLight(Plant.Parameters, oldExtension, newExtension,
                    proposal.LightStep, proposal.ReferenceAtProposal, proposal.Opening)
                : woody.TransportTreeUnitTrialOpeningWithLight(Plant.Parameters, oldExtension, newExtension,
                    proposal.LightStep, proposal.Opening, proposal.Module);
            if (carry != Quaternion.Identity)
                PropagateToSubtree(live, index, carry);
            if (continuation >= 0)
            {
                // The same-tick continuation starts at the accepted endpoint,
                // rather than inheriting the bearer's mean-segment phase turn.
                var turn = Quaternion.Normalize(endpointTurn * Quaternion.Conjugate(carry));
                if (turn != Quaternion.Identity)
                {
                    Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).TransportTreeUnitFrame(turn, continuationFrame);
                    PropagateToSubtree(live, continuation, turn);
                }
                else if (carry != Quaternion.Identity)
                    Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).RestoreTreeUnitFrame(continuationFrame);
                Unsafe.As<T, AboveGroundAgent>(ref live[continuation]).InheritTreeUnitLight(woody);
            }
            if (relay >= 0 && live[relay].Parent == index && carry != Quaternion.Identity)
                Unsafe.As<T, AboveGroundAgent>(ref live[relay]).RestoreTreeUnitFrame(relayFrame);
            if (relay >= 0)
                Unsafe.As<T, AboveGroundAgent>(ref live[relay]).InheritTreeUnitLight(woody, newUnit: true);
            if (zeroModule)
                woody.RestoreTreeModuleMaterial(moduleBefore, moduleLocalBefore, absoluteFrames: exactUnchangedPostOwn);
        }
        ApplySecondaryTrial(index, radius);
        agent.SetGeometry(length, radius);
    }
    LaminaTemplate?[] LaminaCache = [];
    OrganTypes[] LaminaKinds = [];
    bool[] LaminaKnown = [];

    void InvalidateLaminaCache() => Array.Clear(LaminaKnown);

    internal LaminaTemplate? GetLaminaTemplate(int index)
    {
        var organ = GetOrgan(index);
        if (organ is not (OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.Fruit or OrganTypes.FruitWing)) return null;
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
    /// <summary>
    /// Resolves every lamina template once, on the plant's own job. The cache fills and resizes
    /// lazily, which is only safe while one thread at a time touches the plant; a job that reads
    /// this plant concurrently later in the tick must find the cache complete.
    /// </summary>
    internal void WarmLaminaCache()
    {
        var count = AgentCount;
        // Several passes of a tick ask for this, and after the first there is nothing to fill:
        // a large plant is checked on all cores before it is walked on one.
        if (count >= SweepBlock * 2 && LaminaCache.Length >= count && LaminaCacheComplete(count)) return;
        for (int i = 0; i < count; ++i) GetLaminaTemplate(i);
    }

    bool LaminaCacheComplete(int count)
    {
        var missing = 0;
        Parallel.For(0, (count + SweepBlock - 1) / SweepBlock, block =>
        {
            var last = Math.Min(count, (block + 1) * SweepBlock);
            for (int i = block * SweepBlock; i < last; ++i)
            {
                var organ = GetOrgan(i);
                if ((organ is OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.Fruit or OrganTypes.FruitWing)
                    && !(LaminaKnown[i] && LaminaKinds[i] == organ)) { Volatile.Write(ref missing, 1); return; }
            }
        });
        return missing == 0;
    }

    Vector3[] ContactTurns = [];
    /// <summary>Soft engine: the largest single-pair turn queued on a joint this sweep, and the
    /// longest lever any of them acted through. <see cref="EndContactSweep"/> caps the summed turn
    /// by them under the joint-sum rule.</summary>
    float[] ContactTurnPeak = [], ContactLeverMax = [];
    /// <summary>Diagnostic: the contact turns applied this tick, summed per organ type (radians).
    /// Says which tissue is yielding - a scape pressed by leaves, or the leaves.</summary>
    public readonly float[] ContactTurnByOrgan = new float[32];
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
        var blade = a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.FruitWing;
        var fruit = a.Organ == OrganTypes.Fruit;
        //A bud is a rounded capsule of its own radius and length, as it is drawn. The old
        //max(radius, length) made an opened Carex receptacle, 30 mm long and 2 mm wide, a 30 mm
        //ball that every leaf brushing the culm pushed against.
        var radius = fruit ? 0f : blade ? AboveGroundAgent.LeafThickness * 0.5f : a.Radius;
        return new(GetBaseCenterWorld(index) + a.BaseOffset, a.Orientation, a.Length,
            blade || fruit ? a.Radius : 0f, radius, a.Organ is OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud,
            GetDeformation(index));
    }

    void CaptureGeometry()
    {
        if (!IsAboveGround) return;
        EnsureContactCapacity(AgentCount);
        var src = Src();
        for (int i = 0; i < AgentCount; ++i)
            PreviousGeometry[i] = CaptureContactGeometry(i, ref src[i]);
    }

    void EnsureContactCapacity(int count)
    {
        if (PreviousGeometry.Length >= count) return;
        var capacity = Math.Max(count, Math.Max(16, PreviousGeometry.Length * 2));
        Array.Resize(ref PreviousGeometry, capacity);
        Array.Resize(ref ContactTurns, capacity);
        Array.Resize(ref ContactTurnPeak, capacity);
        Array.Resize(ref ContactLeverMax, capacity);
        Array.Resize(ref TransportedTurns, capacity);
        Array.Resize(ref SweepVisit, capacity);
        Array.Resize(ref SweepApplied, capacity);
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

    /// <param name="resetDiagnostics">False for the solid pass's extra rounds, so
    /// <see cref="ContactTurnByOrgan"/> accumulates over the whole tick.</param>
    internal void BeginContactSweep(bool resetDiagnostics = true)
    {
        EnsureContactCapacity(AgentCount);
        ContactSweepPrepared = false;
        Array.Clear(ContactTurns, 0, AgentCount);
        Array.Clear(ContactTurnPeak, 0, AgentCount);
        Array.Clear(ContactLeverMax, 0, AgentCount);
        if (resetDiagnostics) Array.Clear(ContactTurnByOrgan);
        BeginDeformationSweep();
        BatchingContacts = true;
    }

    /// <param name="maxAngle">Per-joint cap on this sweep's turn.</param>
    /// <param name="jointSumRule">Soft engine: cap each joint's summed turn at the largest single
    /// contribution it received and at the displacement cap over its longest lever. Several contacts
    /// that ask for the same motion then move the joint once, not several times; contributions on
    /// opposite sides still cancel in the sum.</param>
    internal bool EndContactSweep(float maxAngle, bool jointSumRule = false)
    {
        var sweepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        BatchingContacts = false;
        ContactSweepPrepared = false; // the sweep turns the joints the descriptions were taken from
        var moved = false;
        ContactMovedRoots.Clear();
        // The pose before the sweep is only read back for an organ whose shape deformed, and only
        // a plant with contact deformation has such organs.
        var shapesDeform = Deformations != null || HasContactDeformation;
        if (shapesDeform || AgentCount < TreeCacheData.ParallelWalkMinimum)
        {
            // Parents before children, breadth first.
            var src = Src();
            var queue = CollisionChildren;
            queue.Clear();
            foreach (var root in GetRoots()) queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var parent = src[i].Parent;
                var applied = TurnJoint(i, parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity, maxAngle, jointSumRule, shapesDeform, ref moved, ContactMovedRoots);
                if (!(applied < 0f)) ContactTurnByOrgan[(int)src[i].Organ & 31] += applied;
                foreach (var child in GetChildren(i)) queue.Enqueue(child);
            }
        }
        else if (!TurnQueuedJointsOnly(maxAngle, jointSumRule, ref moved))
            TurnJointsOnAllCores(maxAngle, jointSumRule, ref moved);
        var walked = System.Diagnostics.Stopwatch.GetTimestamp();
        if (moved)
            foreach (var root in ContactMovedRoots) RebaseAndRefit(root);
        ContactWalkTicks += walked - sweepStarted;
        ContactRefitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - walked;
        return moved;
    }

    /// <summary>Time spent in <see cref="EndContactSweep"/>: the walk that turns the joints, and
    /// rebasing and refitting what moved. Diagnostic; never feeds the simulation.</summary>
    public long ContactWalkTicks, ContactRefitTicks;

    /// <summary>
    /// Whether a sweep passes over a joint that has no turn queued and whose parent did not move:
    /// one safely inside its bend limit is an exact no-op. Re-normalizing such frames every solver
    /// pass created tiny artificial motions and invalidated clearance caches.
    /// </summary>
    bool RestsWithinBendLimit(int i)
    {
        ref var a = ref Src()[i];
        var basis = a.baseOrientation; var orientation = a.Orientation;
        var norm = MathF.Sqrt(basis.LengthSquared() * orientation.LengthSquared());
        var cosineLimit = MathF.Cos(ContactBendLimit(i) * .5f);
        return norm > 0 && MathF.Abs(Quaternion.Dot(basis, orientation)) > (cosineLimit + 1e-6f) * norm;
    }

    /// <summary>
    /// One joint of a contact sweep: applies the turn queued on it and the rotation its parent
    /// carries down (<paramref name="carry"/>), within the joint's bend limit, and leaves in
    /// <see cref="TransportedTurns"/> what its own children are carried by. A joint that moved
    /// while its parent carried nothing is the top of a subtree to rebase and is added to
    /// <paramref name="movedRoots"/>.
    /// </summary>
    /// <returns>The turn applied from the joint's own queue, radians, for the per-tissue
    /// diagnostic; negative when it had none.</returns>
    float TurnJoint(int i, Quaternion carry, float maxAngle, bool jointSumRule, bool shapesDeform, ref bool moved, List<int> movedRoots)
    {
        var src = Src();
        var oldPose = shapesDeform ? GetContactPose(i) : default;
        var deformed = ApplyDeformationIncrement(i);
        var turn = ContactTurns[i];
        if (!deformed && turn == Vector3.Zero && carry == Quaternion.Identity && RestsWithinBendLimit(i))
        {
            TransportedTurns[i] = Quaternion.Identity;
            return -1f;
        }
        var angle = turn.Length();
        var limit = maxAngle;
        if (jointSumRule && angle > 1e-8f)
        {
            limit = MathF.Min(limit, ContactTurnPeak[i]);
            if (ContactLeverMax[i] > 0f && CollisionSettings.MaxCorrectionDisplacement > 0f)
                limit = MathF.Min(limit, CollisionSettings.MaxCorrectionDisplacement / ContactLeverMax[i]);
        }
        var own = angle > 1e-8f ? Quaternion.CreateFromAxisAngle(turn / angle, MathF.Min(angle, limit)) : Quaternion.Identity;
        var applied = angle > 1e-8f ? MathF.Min(angle, limit) : -1f;
        var transportedBase = Quaternion.Normalize(carry * src[i].baseOrientation);
        var proposed = Quaternion.Normalize(own * carry * src[i].Orientation);
        var constrained = LimitJointBend(transportedBase, proposed, ContactBendLimit(i));
        var delta = Quaternion.Normalize(constrained * Quaternion.Conjugate(src[i].Orientation));
        TransportedTurns[i] = delta;
        if (Plant.Parameters.Behavior == Behavior.Woody && carry != Quaternion.Identity)
        {
            // The attachment belongs to the parent's frame, independent of this
            // joint's own elastic bend (which may even cancel the parent's rotation).
            var offset = Vector3.Transform(src[i].BaseOffset, carry);
            moved |= offset != src[i].BaseOffset;
            SetContactOffset(ref src[i], offset);
            src[i].baseOrientation = Quaternion.Normalize(carry * src[i].baseOrientation);
            src[i].restOrientation = Quaternion.Normalize(carry * src[i].restOrientation);
            src[i].targetOrientation = Quaternion.Normalize(carry * src[i].targetOrientation);
        }
        if (delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z > 1e-16f)
        {
            src[i].SetOrientation(Quaternion.Normalize(delta * src[i].Orientation));
            // The ancestor transform transports this joint's rest frame. Its own contact
            // deformation is elastic and must relax after contact is released.
            if (Plant.Parameters.Behavior != Behavior.Woody)
            {
                src[i].baseOrientation = Quaternion.Normalize(carry * src[i].baseOrientation);
                src[i].restOrientation = Quaternion.Normalize(carry * src[i].restOrientation);
                src[i].targetOrientation = Quaternion.Normalize(carry * src[i].targetOrientation);
            }
            if (carry == Quaternion.Identity) movedRoots.Add(i);
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
            if (carry == Quaternion.Identity && !movedRoots.Contains(i)) movedRoots.Add(i);
            moved = true;
        }
        return applied;
    }

    /// <summary>
    /// The organs the last contact sweep can have moved: every subtree it rebased, an organ more
    /// than once when one moved subtree holds another. False, with the list incomplete, when
    /// there are more than <paramref name="limit"/>.
    /// </summary>
    internal bool CollectSweptOrgans(List<int> into, int limit)
    {
        foreach (var root in ContactMovedRoots)
        {
            var first = into.Count;
            into.Add(root);
            for (int k = first; k < into.Count; ++k)
            {
                foreach (var child in GetChildren(into[k])) into.Add(child);
                if (into.Count > limit) return false;
            }
        }
        return true;
    }

    /// <summary>Per organ, the last <see cref="SweepPass"/> that visited it.</summary>
    int[] SweepVisit = [];
    int SweepPass;
    /// <summary>Per organ, what <see cref="TurnJoint"/> returned in a sweep on several cores.</summary>
    float[] SweepApplied = [];
    List<int>[] SweepActive = [];
    readonly PriorityQueue<int, int> SweepQueue = new();
    readonly List<int> SweepBranches = [];
    /// <summary>Organs per block of the scan for joints a sweep has work on.</summary>
    const int SweepBlock = 8192;

    /// <summary>
    /// The contact sweep over the joints that have something to do, instead of over the plant.
    /// Returns false, having changed nothing, when the whole walk is to run instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tree's solid pass ends a sweep four times a tick and each walked a quarter of a million
    /// organs to turn a few dozen: the walk passes over a joint that has no turn queued, rests
    /// within its bend limit and whose parent did not move, and all it writes for such a joint is
    /// the identity its children are carried by.
    /// </para>
    /// <para>
    /// So the joints are found first - a read-only scan, on all cores - and only they and what
    /// they carry are visited. A joint's result depends on its own state and its parent's
    /// transported turn alone, and the walk visits parents before children breadth first; the
    /// queue here hands out the joints by their rank in that same order
    /// (<see cref="TreeCacheData.BreadthRanks"/>), so every joint sees the same parent turn, and
    /// the two things that do depend on the order of visits - the per-tissue turn sum and the
    /// list of moved subtrees - are built in the order the whole walk builds them. A parent this
    /// sweep did not visit carries nothing, which is what the whole walk would have written for it.
    /// </para>
    /// </remarks>
    bool TurnQueuedJointsOnly(float maxAngle, bool jointSumRule, ref bool moved)
    {
        var count = AgentCount;
        var blocks = (count + SweepBlock - 1) / SweepBlock;
        if (SweepActive.Length < blocks)
        {
            var old = SweepActive.Length;
            Array.Resize(ref SweepActive, blocks);
            for (int k = old; k < blocks; ++k) SweepActive[k] = [];
        }
        var active = SweepActive; var turns = ContactTurns;
        Parallel.For(0, blocks, block =>
        {
            var found = active[block]; found.Clear();
            var last = Math.Min(count, (block + 1) * SweepBlock);
            for (int i = block * SweepBlock; i < last; ++i)
                if (turns[i] != Vector3.Zero || !RestsWithinBendLimit(i)) found.Add(i);
        });
        var total = 0;
        for (int k = 0; k < blocks; ++k) total += active[k].Count;
        // A sweep that turns much of the plant is cheaper as a walk over all of it.
        if (total > count / 8) return false;
        var src = Src(); var rank = TreeCache.BreadthRanks(); var visit = SweepVisit; var pass = ++SweepPass;
        var queue = SweepQueue; queue.Clear();
        for (int k = 0; k < blocks; ++k)
            foreach (var i in active[k]) { visit[i] = pass; queue.Enqueue(i, rank[i]); }
        while (queue.TryDequeue(out var i, out _))
        {
            var parent = src[i].Parent;
            var carry = parent >= 0 && visit[parent] == pass ? TransportedTurns[parent] : Quaternion.Identity;
            var applied = TurnJoint(i, carry, maxAngle, jointSumRule, shapesDeform: false, ref moved, ContactMovedRoots);
            if (!(applied < 0f)) ContactTurnByOrgan[(int)src[i].Organ & 31] += applied;
            if (TransportedTurns[i] == Quaternion.Identity) continue;
            foreach (var child in GetChildren(i))
                if (visit[child] != pass) { visit[child] = pass; queue.Enqueue(child, rank[child]); }
        }
        return true;
    }

    /// <summary>Turns the joints of one subtree, its root first, for a sweep on several cores.</summary>
    void TurnBranch(int root, SweepWorker worker, float maxAngle, bool jointSumRule)
    {
        var src = Src(); var applied = SweepApplied; var pending = worker.Pending;
        pending.Push(root);
        while (pending.TryPop(out var i))
        {
            var parent = src[i].Parent;
            applied[i] = TurnJoint(i, parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity, maxAngle, jointSumRule, shapesDeform: false, ref worker.Moved, worker.MovedRoots);
            foreach (var child in GetChildren(i)) pending.Push(child);
        }
    }

    /// <summary>What one worker of a sweep on several cores collects.</summary>
    sealed class SweepWorker
    {
        public readonly Stack<int> Pending = new();
        public readonly List<int> MovedRoots = [];
        public bool Moved;
    }

    /// <summary>
    /// The contact sweep over every joint, on several cores.
    /// </summary>
    /// <remarks>
    /// A contact between two limbs turns the joints all the way down to their fork, however
    /// little, and everything above a turned joint is carried: on a mature tree nearly every
    /// organ takes the full step. A joint needs its parent's transported turn and nothing else,
    /// so the joints that carry most of the tree are turned one after another and the subtrees
    /// left over side by side (<see cref="TreeCacheData.SplitForWalk"/>). The two results that
    /// depend on the order of visits are put back into the order of the plain walk afterwards:
    /// the moved subtrees are sorted by breadth-first rank, and the per-tissue turn sum is added
    /// up in breadth-first order from the turns the workers noted per joint.
    /// </remarks>
    void TurnJointsOnAllCores(float maxAngle, bool jointSumRule, ref bool moved)
    {
        var src = Src(); var applied = SweepApplied; var branches = SweepBranches; var roots = ContactMovedRoots;
        foreach (var i in TreeCache.SplitForWalk(CollectionsMarshal.AsSpan(GetRoots()), branches))
        {
            var parent = src[i].Parent;
            applied[i] = TurnJoint(i, parent >= 0 ? TransportedTurns[parent] : Quaternion.Identity, maxAngle, jointSumRule, shapesDeform: false, ref moved, roots);
        }
        var anyMoved = moved;
        Parallel.For(0, branches.Count, () => new SweepWorker(), (branch, _, worker) =>
        {
            TurnBranch(branches[branch], worker, maxAngle, jointSumRule);
            return worker;
        }, worker =>
        {
            lock (roots)
            {
                roots.AddRange(worker.MovedRoots);
                anyMoved |= worker.Moved;
            }
        });
        moved = anyMoved;
        var rank = TreeCache.BreadthRanks();
        roots.Sort((first, second) => rank[first].CompareTo(rank[second]));
        foreach (var i in TreeCache.BreadthFirst())
            if (!(applied[i] < 0f)) ContactTurnByOrgan[(int)src[i].Organ & 31] += applied[i];
    }

    internal bool IsMechanicallyAnchored(int index) => Src()[index].SoilAnchored;
    internal bool IsLoadLimited(int index) => index >= 0 && Src()[index].LoadLimited;

    internal void RefreshAnchors(StaticCollider scene)
    {
        var src = Src();
        foreach (var i in RhizomeOrgans)
        {
            var pose = GetContactPose(i);
            // Only embedded spans are fixed. A tip outside the hull is a cantilever even when
            // its base is still buried; an isolated surface contact is a unilateral constraint.
            src[i].SoilAnchored = scene.InsideSoil(pose.Base, 1f)
                && scene.InsideSoil(pose.Tip, 1f);
        }
    }

    internal void ClearSupport()
    {
        // Whatever moved the organs or their water since the loads were last summed, this is
        // where a new support solve starts: it must not reuse them.
        LoadsFresh = false;
        var length = Src().Length;
        if (length < SweepBlock * 2) ClearSupported(0, length);
        else
        {
            var organs = length; // captured here, so that a small plant pays for no closure
            Parallel.For(0, (organs + SweepBlock - 1) / SweepBlock, block => ClearSupported(block * SweepBlock, Math.Min(organs, (block + 1) * SweepBlock)));
        }
        EnsureContactCapacity(length);
        Array.Clear(SupportMoments, 0, length);
    }

    /// <summary>Only the organs that were supported are written to: on a tree that is a handful,
    /// and reading a flag does not take a quarter of a million cache lines away from the other cores.</summary>
    void ClearSupported(int from, int to)
    {
        var src = Src();
        for (int i = from; i < to; ++i)
            if (src[i].GroundSupported) src[i].GroundSupported = false;
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

    /// <summary>Queued for removal at the next Census.</summary>
    internal bool IsDying(int index) => Deaths.Contains(index);

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
                RestoreContactUnitReference(ref src[i], previous);
                if (Plant.Parameters.Behavior == Behavior.Woody)
                    SetContactOffset(ref src[i], previousCarry == Quaternion.Identity ? previous.BaseOffset
                        : Vector3.Transform(previous.BaseOffset, previousCarry));
            }
            else if (new Vector3(carry.X, carry.Y, carry.Z).LengthSquared() > 1e-16f)
            {
                src[i].SetOrientation(Quaternion.Normalize(carry * original));
                src[i].baseOrientation = Quaternion.Normalize(carry * src[i].baseOrientation);
                src[i].restOrientation = Quaternion.Normalize(carry * src[i].restOrientation);
                src[i].targetOrientation = Quaternion.Normalize(carry * src[i].targetOrientation);
                if (Plant.Parameters.Behavior == Behavior.Woody)
                    SetContactOffset(ref src[i], Vector3.Transform(src[i].BaseOffset, carry));
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
            saved[i] = CaptureContactGeometry(i, ref live[i]);
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
            ContinuationStates[i] = CaptureContactGeometry(i, ref live[i]);
        return ContinuationStates;
    }

    internal bool HasContinuationWaypoint => ReferenceEquals(PreviousGeometry, ContinuationStates);

    internal void SetContinuationDimensions(int index, float length, float radius)
    {
        SetGrowthTrialGeometry(index, length, radius);
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
            RestoreContactUnitReference(ref live[i], state);
            if (Plant.Parameters.Behavior == Behavior.Woody) SetContactOffset(ref live[i], state.BaseOffset);
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
        RestoreContactUnitReference(ref src[i], previous);
        if (Plant.Parameters.Behavior == Behavior.Woody) SetContactOffset(ref src[i], previous.BaseOffset);
    }

    internal void SealNewGeometry()
    {
        FinalizeTreeModuleBookkeeping();
        CompleteSecondaryMaterials();
        SynchronizeSamaraUnitPhenology();
        EnsureContactCapacity(Count);
        var src = Src();
        for (int i = 0; i < Count; ++i)
            if (!PreviousGeometry[i].Exists)
                PreviousGeometry[i] = CaptureContactGeometry(i, ref src[i]);
    }

    /// <summary>
    /// An organ born this tick that already stands inside a solid has no previous geometry to
    /// fall back to, so it is shortened toward its base instead: the longest admissible fraction of
    /// its length, down to a tenth of a millimetre, radius unchanged. The mixed engine's births are
    /// not staged, which is the only way an organ reaches the solid pass without a history; its
    /// base is the parent's tip, admitted last tick, so a fraction is always found unless the
    /// parent itself moved into the solid, which the escalating rollback then undoes.
    /// </summary>
    internal bool TryShrinkNewborn(int index, Func<bool> admissible)
    {
        if (PreviousGeometry[index].Exists) return false;
        var src = Src(); var length = src[index].Length; var radius = src[index].Radius;
        const float floor = 1e-4f;
        if (length <= floor) return false;
        src[index].SetGeometry(floor, radius);
        RebaseAndRefit(index);
        if (!admissible()) { src[index].SetGeometry(length, radius); RebaseAndRefit(index); return false; }
        float lo = floor, hi = length;
        for (int step = 0; step < 8; ++step)
        {
            var mid = (lo + hi) * 0.5f;
            src[index].SetGeometry(mid, radius);
            RebaseAndRefit(index);
            if (admissible()) lo = mid; else hi = mid;
        }
        src[index].SetGeometry(lo, radius);
        RebaseAndRefit(index);
        return true;
    }

    internal bool TryShortenGrowth(int index, Func<bool> admissible)
    {
        var old = PreviousGeometry[index];
        if (!old.Exists) return false;
        var src = Src(); var length = src[index].Length; var radius = src[index].Radius;
        if (length <= old.Pose.Length && radius <= old.Radius) return false;
        var bookmark = CaptureGrowthTrial(index);
        try
        {
            SetGrowthTrialGeometry(index, MathF.Min(length, old.Pose.Length), MathF.Min(radius, old.Radius), bookmark);
            RebaseAndRefit(index);
            if (!admissible()) { SetGrowthTrialGeometry(index, length, radius, bookmark); RebaseAndRefit(index); return false; }
            float lo = 0, hi = 1;
            for (int step = 0; step < 8; ++step)
            {
                var mid = (lo + hi) * 0.5f;
                SetGrowthTrialGeometry(index, old.Pose.Length + (length - old.Pose.Length) * mid, old.Radius + (radius - old.Radius) * mid, bookmark);
                RebaseAndRefit(index);
                if (admissible()) lo = mid; else hi = mid;
            }
            SetGrowthTrialGeometry(index, old.Pose.Length + (length - old.Pose.Length) * lo, old.Radius + (radius - old.Radius) * lo, bookmark);
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
                float lower=from,upper=to;
                SetGrowthTrialGeometry(index, longitudinal?to:fixedSize,longitudinal?fixedSize:to, bookmark);
                RebaseAndRefit(index);
                if(admissible()) return;
                for(int n=0;n<8;++n)
                {
                    var mid=(lower+upper)*.5f;
                    SetGrowthTrialGeometry(index, longitudinal?mid:fixedSize,longitudinal?fixedSize:mid, bookmark);
                    RebaseAndRefit(index);
                    if(admissible()) lower=mid; else upper=mid;
                }
                SetGrowthTrialGeometry(index, longitudinal?lower:fixedSize,longitudinal?fixedSize:lower, bookmark);
                RebaseAndRefit(index);
            }
        }
        finally { ReleaseGrowthTrial(bookmark); }
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
        var bookmark = CaptureGrowthTrial(index);
        try
        {
            SetGrowthTrialGeometry(index, length, radius, bookmark);
            RebaseAndRefit(index);
            if (admissible())
            {
                GrowthRetries.Remove(live[index].StableId);
                live[index].GrowthBlocked = false;
                live[index].ObstructionHours = 0f;
                return true;
            }
            SetGrowthTrialGeometry(index, previousLength, previousRadius, bookmark);
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
                SetGrowthTrialGeometry(index, previousLength, previousRadius, bookmark);
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
                    SetGrowthTrialGeometry(index, longitudinal ? value : fixedSize,
                        longitudinal ? fixedSize : value, bookmark);
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
        finally { ReleaseGrowthTrial(bookmark); }
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
            saved[j] = CaptureContactGeometry(i, ref src[i]);
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
                RestoreContactUnitReference(ref live[i], state);
                if (Plant.Parameters.Behavior == Behavior.Woody) SetContactOffset(ref live[i], state.BaseOffset);
            }
        }
    }

    /// <summary>Elastic angular compliance, L/(EI), for the actual organ tissue.</summary>
    internal float ContactCompliance(int index, Vector3 turn)
        => Vector3.Dot(turn, ComplianceVector(index,turn));

    /// <summary>
    /// Everything a joint's elastic response to a torque depends on except the torque: the
    /// tissue's stiffness, the section and the organ's axes.
    /// </summary>
    /// <remarks>
    /// A contact asks every joint between the organ and the common ancestor for its response
    /// three times, and a crown has a few hundred thousand contacts over paths tens of joints
    /// long. The stiffness is an exponential and a fourth power of nothing but the joint's own
    /// state, so it is described once per joint and sweep (<see cref="PrepareContactSweep"/>)
    /// and applied per torque. There is one description and one application, used with and
    /// without the cache, so the cached answer is the uncached one bit for bit.
    /// </remarks>
    readonly struct JointCompliance
    {
        /// <summary>A rod's axis; a blade's across axis.</summary>
        public readonly Vector3 First;
        /// <summary>A blade's second in-plane axis; unused for a rod.</summary>
        public readonly Vector3 Second;
        /// <summary>A blade's two second moments of area, about <see cref="First"/> and <see cref="Second"/>.</summary>
        public readonly float FirstInertia, SecondInertia;
        /// <summary>Length over stiffness: L/(E I) for a rod, L/E for a blade.</summary>
        public readonly float Scale;
        /// <summary>Three for a deformable organ, whose shape takes part of the load; otherwise one.</summary>
        public readonly float Divisor;
        public readonly bool Blade;

        public JointCompliance(Vector3 axis, float scale, float divisor)
        { First = axis; Second = default; FirstInertia = SecondInertia = 0f; Scale = scale; Divisor = divisor; Blade = false; }

        public JointCompliance(Vector3 first, Vector3 second, float firstInertia, float secondInertia, float scale, float divisor)
        { First = first; Second = second; FirstInertia = firstInertia; SecondInertia = secondInertia; Scale = scale; Divisor = divisor; Blade = true; }

        public Vector3 Respond(Vector3 torque)
        {
            if (Blade)
            {
                var inverse = First * (Vector3.Dot(torque, First) / FirstInertia)
                    + Second * (Vector3.Dot(torque, Second) / SecondInertia);
                return inverse * Scale / Divisor;
            }
            return (torque - First * Vector3.Dot(torque, First)) * Scale / Divisor;
        }
    }

    JointCompliance DescribeCompliance(int index)
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
            e = e * (1f - a.WoodRatio()) + MathF.Max(e, Plant.Parameters.WoodElasticModulus) * a.WoodRatio(); // never softer, see Gravity
        var divisor = Deformable(index) ? 3f : 1f;
        if (a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.FruitWing)
        {
            e = Plant.Parameters.LeafElasticModulus > 0f ? Plant.Parameters.LeafElasticModulus : Plant.Parameters.GreenElasticModulus;
            var z = Vector3.Transform(Vector3.UnitZ, a.Orientation);
            var y = Vector3.Transform(Vector3.UnitY, a.Orientation);
            var keel = BladeKeelAt(index);
            return new(z, y, MathF.Max(1e-25f,BladeInertia(a.Radius,keel)), MathF.Max(1e-25f,BladeInertiaMajor(a.Radius,keel)),
                a.Length / MathF.Max(1f,e), divisor);
        }
        var inertia = MathF.PI * MathF.Pow(a.Radius, 4) / 4f;
        var axis = Vector3.Transform(Vector3.UnitX,a.Orientation);
        return new(axis, a.Length / MathF.Max(1e-20f,e*inertia), divisor);
    }

    Vector3 ComplianceVector(int index, Vector3 torque)
        => ContactSweepPrepared ? ContactJoints[index].Compliance.Respond(torque) : DescribeCompliance(index).Respond(torque);

    /// <summary>How far a joint is already bent from its base frame, and whether that is its
    /// limit: a joint at its limit only yields to a torque that unbends it.</summary>
    readonly struct JointBend
    {
        public readonly Vector3 Bend;
        public readonly bool Saturated;
        public JointBend(Vector3 bend, bool saturated) { Bend = bend; Saturated = saturated; }
        public bool Yields(Vector3 axis) => !(Saturated && Vector3.Dot(Bend, axis) > 0);
    }

    JointBend DescribeBend(int index)
    {
        ref var a = ref Src()[index];
        var delta = Quaternion.Normalize(a.Orientation * Quaternion.Conjugate(a.baseOrientation));
        if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var bend = new Vector3(delta.X, delta.Y, delta.Z);
        return new(bend, 2f * MathF.Atan2(bend.Length(), delta.W) >= ContactBendLimit(index) - 1e-5f);
    }

    /// <summary>
    /// One joint as a contact walks over it: where it sits, how stiff it is, how far it is bent,
    /// and which joint comes next on the way to the root.
    /// </summary>
    /// <remarks>
    /// A walk from an organ to the common ancestor reads every joint on the way, and a crown
    /// runs half a million such walks a tick. Read from the organs themselves, a joint is spread
    /// over the agent, the tree cache and the soil that owns the field origin; described once
    /// per sweep it is two cache lines side by side.
    /// </remarks>
    readonly struct ContactJoint
    {
        public readonly JointCompliance Compliance;
        public readonly JointBend Bend;
        /// <summary>The organ's base in the world, and its offset from it, kept apart because
        /// a lever subtracts them one after the other.</summary>
        public readonly Vector3 Base, Offset;
        public readonly int Parent;
        /// <summary>Fixed in the soil: a walk ends before it.</summary>
        public readonly bool Anchored;

        public ContactJoint(JointCompliance compliance, JointBend bend, Vector3 basePoint, Vector3 offset, int parent, bool anchored)
        { Compliance = compliance; Bend = bend; Base = basePoint; Offset = offset; Parent = parent; Anchored = anchored; }

        /// <summary>Elastic angular compliance about a unit axis, zero when the joint is at its
        /// limit against it.</summary>
        public float Available(Vector3 axis) => Bend.Yields(axis) ? Vector3.Dot(axis, Compliance.Respond(axis)) : 0;
    }

    ContactJoint DescribeJoint(int index)
    {
        ref var a = ref Src()[index];
        return new(DescribeCompliance(index), DescribeBend(index), GetBaseCenterWorld(index), a.BaseOffset, a.Parent, a.SoilAnchored);
    }

    /// <summary>
    /// One organ as the evaluation of a candidate pair reads it: its shape, its stable id,
    /// whether it takes part in contacts and whether it stands where the last tick left it.
    /// </summary>
    /// <remarks>
    /// An organ is in ten candidate pairs on average, and each asked the agent, the tree cache,
    /// the lamina cache and the previous geometry for the same answers.
    /// </remarks>
    internal readonly struct ContactOrgan
    {
        public readonly ContactShape Shape;
        public readonly ulong Id;
        public readonly bool Eligible, Unmoved;
        public ContactOrgan(ContactShape shape, ulong id, bool eligible, bool unmoved)
        { Shape = shape; Id = id; Eligible = eligible; Unmoved = unmoved; }
    }

    ContactOrgan DescribeOrgan(int index)
    {
        var shape = new ContactShape(GetContactPose(index), GetLaminaTemplate(index));
        return new(shape, GetOrganId(index), IsCollisionEligible(index),
            index < PreviousGeometry.Length && PreviousGeometry[index].Exists && PreviousGeometry[index].Pose == shape.Pose);
    }

    internal ContactOrgan ContactOrganAt(int index) => ContactSweepPrepared ? ContactOrgans[index] : DescribeOrgan(index);

    ContactJoint[] ContactJoints = [];
    ContactOrgan[] ContactOrgans = [];
    /// <summary>The two arrays above describe every joint and organ as it is now. True from
    /// <see cref="PrepareContactSweep"/> until the sweep that will turn the joints begins.</summary>
    bool ContactSweepPrepared;

    /// <summary>
    /// Describes every joint and organ once for the contacts about to be evaluated and queued
    /// on this plant. Only valid while nothing turns, grows, stiffens or re-anchors an organ: the
    /// caller evaluates pairs, queues corrections and then ends the sweep, which drops the
    /// descriptions before it moves anything.
    /// </summary>
    internal void PrepareContactSweep()
    {
        var count = AgentCount;
        if (ContactJoints.Length < count)
        {
            var capacity = Math.Max(count, Math.Max(16, ContactJoints.Length * 2));
            ContactJoints = new ContactJoint[capacity];
            ContactOrgans = new ContactOrgan[capacity];
        }
        WarmLaminaCache(); // filled lazily, which only one thread at a time may do
        void Describe(int from, int to)
        {
            for (int i = from; i < to; ++i) { ContactJoints[i] = DescribeJoint(i); ContactOrgans[i] = DescribeOrgan(i); }
        }
        if (count >= 4096 && Environment.ProcessorCount > 1)
            Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, count, 2048), range => Describe(range.Item1, range.Item2));
        else Describe(0, count);
        ContactSweepPrepared = true;
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
        => ContactSweepPrepared ? ContactJoints[index].Bend.Yields(axis) : DescribeBend(index).Yields(axis);

    internal double ContactMobility(int index, Vector3 contact, Vector3 normal, int stop = -1)
    {
        double total = 0;
        var ready = ContactSweepPrepared; var joints = ContactJoints; var described = default(ContactJoint);
        for (var n = index; n >= 0 && n != stop; )
        {
            if (!ready) described = DescribeJoint(n);
            ref readonly var joint = ref (ready ? ref joints[n] : ref described);
            if (joint.Anchored) break;
            var cross = Vector3.Cross(contact - joint.Base - joint.Offset, normal);
            var gain = cross.Length();
            if (gain > 1e-8f) total += joint.Available(cross / gain) * (double)gain * gain;
            total += ShapeMobility(n, index, contact, normal);
            n = joint.Parent;
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
        var alongSecond = a.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.FruitWing
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

    /// <param name="maxDisplacement">Soft contact engine only: largest motion of the witness that
    /// any one joint's turn may cause this tick, metres. The certified engine leaves it infinite.</param>
    /// <param name="knownMobility">The value <see cref="ContactMobility"/> returns for the same arguments,
    /// when the caller has just computed it to split a correction between two bodies; NaN to
    /// compute it here. Nothing a caller queues between the two changes it.</param>
    internal bool QueueContactCorrection(int index, Vector3 contact, Vector3 normal, float depth, int stop = -1,
        float maxDisplacement = float.PositiveInfinity, double knownMobility = double.NaN)
    {
        var plan = PlanContactCorrection(index, contact, normal, depth, stop, maxDisplacement, knownMobility);
        ApplyContactCorrection(plan, null, -1);
        return plan.Kind != CorrectionKind.None;
    }

    internal enum CorrectionKind : byte { None, Walk, Axial }

    /// <summary>The partitions of <see cref="TreeCacheData.WalkPartition"/>, for a contact
    /// response applied on several cores.</summary>
    internal int[] WalkPartition(out int parts) => TreeCache.WalkPartition(this, out parts);

    /// <summary>Whether the joint descriptions of this sweep are in place, which a correction
    /// planned or applied on several cores relies on.</summary>
    internal bool ContactJointsDescribed => ContactSweepPrepared;

    /// <summary>
    /// Appends to <paramref name="routes"/>, for every partition a planned correction turns joints
    /// in and in the order its walk reaches them, three numbers: <paramref name="planIndex"/>, the
    /// partition, and the joint at which the walk enters it. Needs the joint descriptions of this
    /// sweep.
    /// </summary>
    internal void RouteContactCorrection(in ContactCorrection plan, int planIndex, int[] owner, List<int> routes)
    {
        if (plan.Kind == CorrectionKind.Axial) { routes.Add(planIndex); routes.Add(owner[plan.Best]); routes.Add(plan.Best); return; }
        if (plan.Kind != CorrectionKind.Walk) return;
        var joints = ContactJoints; var last = -1;
        for (var n = plan.Index; n >= 0 && n != plan.Stop && !joints[n].Anchored; n = joints[n].Parent)
            if (owner[n] != last) { routes.Add(planIndex); routes.Add(last = owner[n]); routes.Add(n); }
    }

    /// <summary>
    /// One contact correction as <see cref="QueueContactCorrection"/> decides it before it turns
    /// anything: either the compliance-weighted walk from the organ towards the common ancestor,
    /// with the multiplier every joint on it scales its response by, or - when the contact has
    /// no lever on any joint - a single finite turn of the most mobile one.
    /// </summary>
    internal readonly record struct ContactCorrection(CorrectionKind Kind, int Index, int Stop, Vector3 Contact, Vector3 Normal,
        double Lambda, float MaxDisplacement, int Best, Vector3 AxialTurn, float AxialPeak, float AxialLever);

    /// <summary>
    /// Decides a contact correction without queueing it. Reads the joints and nothing a
    /// correction writes, so the corrections of many pairs can be planned side by side.
    /// </summary>
    internal ContactCorrection PlanContactCorrection(int index, Vector3 contact, Vector3 normal, float depth, int stop,
        float maxDisplacement, double knownMobility)
    {
        if (depth < 0) { normal = -normal; depth = -depth; }
        var src = Src();
        var total = double.IsNaN(knownMobility) ? ContactMobility(index, contact, normal, stop) : knownMobility;
        // Existing graph segments supply the degrees of freedom. Walk to a rooted anchor;
        // a broad, unsplit blade can therefore deflect through its petiole.
        if (total > 1e-20 && double.IsFinite(total))
            return new(CorrectionKind.Walk, index, stop, contact, normal, depth / total, maxDisplacement, -1, default, 0, 0);
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
        if (best < 0) return new(CorrectionKind.None, index, stop, contact, normal, 0, maxDisplacement, -1, default, 0, 0);
        var axialCap = CollisionSettings.MaxCorrectionRadians;
        var bestLever = float.NaN;
        if (maxDisplacement < float.PositiveInfinity)
        {
            bestLever = (contact - GetBaseCenterWorld(best) - src[best].BaseOffset).Length();
            axialCap = MathF.Min(axialCap, maxDisplacement / MathF.Max(bestLever, 1e-6f));
        }
        return new(CorrectionKind.Axial, index, stop, contact, normal, 0, maxDisplacement, best,
            bestAxis * MathF.Min(bestAngle, axialCap), MathF.Min(bestAngle, axialCap), bestLever);
    }

    /// <summary>
    /// Queues a planned correction on its joints - all of them when <paramref name="owner"/> is
    /// null, or only those whose entry in it is <paramref name="part"/>. Each joint's turn is the
    /// same arithmetic whichever joints are applied; the order in which a joint receives the
    /// turns of several corrections is the caller's.
    /// </summary>
    /// <remarks>
    /// The partitions are those of <see cref="TreeCacheData.WalkPartition"/>, which a walk towards
    /// the roots enters at most once each: a partition's part of it runs from the first joint in
    /// it - <paramref name="entry"/>, when the caller knows it - to the first joint after that
    /// outside it. Only a plant whose organs do not change shape
    /// under contact may be applied in parts: the shape correction of a deformable joint is
    /// queued with every joint visited.
    /// </remarks>
    internal void ApplyContactCorrection(in ContactCorrection plan, int[]? owner, int part, int entry = -1)
    {
        if (plan.Kind == CorrectionKind.Axial)
        {
            var best = plan.Best;
            if (owner != null && owner[best] != part) return;
            ContactTurns[best] += plan.AxialTurn;
            if (plan.MaxDisplacement < float.PositiveInfinity)
            {
                ContactTurnPeak[best] = MathF.Max(ContactTurnPeak[best], plan.AxialPeak);
                ContactLeverMax[best] = MathF.Max(ContactLeverMax[best], plan.AxialLever);
            }
            return;
        }
        if (plan.Kind != CorrectionKind.Walk) return;
        var index = plan.Index; var contact = plan.Contact; var normal = plan.Normal; var lambda = plan.Lambda;
        var maxDisplacement = plan.MaxDisplacement;
        var ready = ContactSweepPrepared; var joints = ContactJoints; var described = default(ContactJoint);
        var entered = false;
        // The walk from the organ to the entry passes no anchor and not the common ancestor:
        // that is how the entry was found.
        for (int n = entry >= 0 ? entry : index, next; n >= 0 && n != plan.Stop; n = next)
        {
            if (!ready) described = DescribeJoint(n);
            ref readonly var joint = ref (ready ? ref joints[n] : ref described);
            if (joint.Anchored) break;
            next = joint.Parent;
            if (owner != null)
            {
                if (owner[n] != part) { if (entered) break; continue; }
                entered = true;
            }
            var lever = contact - joint.Base - joint.Offset;
            var cross = Vector3.Cross(lever, normal);
            var gain = cross.Length();
            QueueShapeCorrection(n, index, contact, normal, (float)lambda);
            if (gain <= 1e-8f) continue;
            var axis = cross / gain;
            if (joint.Available(axis) <= 0) continue;
            var response = joint.Compliance.Respond(cross) * (float)lambda;
            var angle = response.Length();
            // A turn of θ about this joint moves the witness by at most θ·|lever|.
            var cap = CollisionSettings.MaxCorrectionRadians;
            if (maxDisplacement < float.PositiveInfinity) cap = MathF.Min(cap, maxDisplacement / MathF.Max(lever.Length(), 1e-6f));
            if (angle > cap) response *= cap/angle;
            ContactTurns[n] += response;
            if (maxDisplacement < float.PositiveInfinity)
            {
                ContactTurnPeak[n] = MathF.Max(ContactTurnPeak[n], MathF.Min(angle, cap));
                ContactLeverMax[n] = MathF.Max(ContactLeverMax[n], lever.Length());
            }
        }
    }

    /// <summary>
    /// Soft contact engine: shortens this tick's elongation to the free gap in front of a solid.
    /// Only the length changes; descendants are transported and the hierarchy refitted. It is the
    /// one place where that engine takes growth away, and only ever down to last tick's length.
    /// </summary>
    internal void ClipLength(int index, float length)
    {
        var src = Src();
        if (length >= src[index].Length) return;
        src[index].SetGeometry(length, src[index].Radius);
        RebaseAndRefit(index);
    }

    internal void RecordSupportReaction(int index, Vector3 point, Vector3 normal)
    {
        if (normal.Y <= 0f) return;
        EnsureMechanicalLoads();
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
        var src = Src();
        foreach (var i in RhizomeOrgans)
        {
            if (src[i].SoilAnchored) continue;
            EnsureMechanicalLoads();
            var torque = Vector3.Cross(WeightedCentroids[i] - GetBaseCenter(i) * Weights[i], -Vector3.UnitY) + SupportMoments[i];
            var axis = Vector3.Transform(Vector3.UnitX, src[i].Orientation);
            torque -= axis * Vector3.Dot(torque, axis);
            var size = torque.Length();
            var angle = size > 1e-12f ? size * ContactCompliance(i, torque / size) : 0;
            src[i].LoadLimited = !float.IsFinite(angle) || angle > CollisionSettings.MaxElasticBendRadians;
        }
    }
}
