using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    readonly record struct ContinuationPlant(PlantFormation2 Plant,
        PlantSubFormation<AboveGroundAgent>.GeometryState[] Original,
        PlantSubFormation<AboveGroundAgent>.GeometryState[] RetryStart);
    readonly List<ContinuationPlant> ContinuationPlants = [];
    bool ContinuingGrowth;
    bool CollectingContinuationContacts;
    const int MaximumContinuationContacts = 16, MaximumContinuationJoints = 256;
    // Preserve the existing separation target. The smaller-gap experiment failed
    // the seasonal gate and substantially increased V1 certification cost.
    const float ContinuationReactionGap = FoliageGeometry.Skin;
    readonly record struct ContinuationWitness(ContactKey Key, int I, int J, bool Reaction);
    readonly record struct ContinuationJoint(PlantFormation2 Plant, int Index);
    readonly record struct ContinuationGradient(int Row, int Joint, Vector3 Torque, Vector3 Turn, Vector4 Shape, Vector4 Fold);
    readonly List<ContinuationWitness> ContinuationWitnesses = new(MaximumContinuationContacts);
    readonly List<ContinuationJoint> ContinuationJoints = [];
    readonly List<ContactAngularCompliance> ContinuationCompliance = [];
    readonly Dictionary<ContinuationJoint, int> ContinuationJointIndices = [];
    readonly List<ContinuationGradient> ContinuationGradients = [];
    ushort[] ContinuationSlots = [];
    double[] ContinuationMatrix = [], ContinuationDepth = [], ContinuationForces = [];
    Vector3[] ContinuationTurns = [];
    Vector4[] ContinuationFolds = [];
    bool[] ContinuationEnabled = [];
    /// <summary>Rows assembled for the last continuation tangent, including optional guards.</summary>
    public int LastContinuationContactRows { get; private set; }
    /// <summary>Last tangent complementarity residual, metres; NaN when no valid tangent was solved.</summary>
    public double LastContinuationDualResidualMetres { get; private set; } = double.NaN;

    /// <summary>Certified mechanical recovery work in the last tick, separate from biological time.</summary>
    public int LastContinuationTrials { get; private set; }
    public int LastContinuationSteps { get; private set; }
    public int LastContinuationCorrections { get; private set; }
    public int LastContinuationCoupledSets { get; private set; }
    /// <summary>Certified evasion legs attempted this tick, including subsequently rolled-back trials.</summary>
    public int LastContinuationEvasionStages { get; private set; }

    // Only sampled, already-existing floral axes enter this bounded recovery path.
    // It neither samples biology again nor commits births. The previous geometry
    // becomes the last certified waypoint, so CCD checks the actual piecewise path.
    bool TryCoupledGrowth(PlantFormation2 plant, int index, float length, float radius)
    {
        var ag = plant.AG;
        if (UnresolvedFoliageContacts != 0 || ag.HasPendingDevelopment(index)
            || !ag.PreviousGeometry[index].Exists) return false;
        var initialLength = ag.GetLength(index);
        var initialRadius = ag.GetBaseRadius(index);
        // The richer path remains with the explicitly experimental V1 profile.
        // Its seasonal and end-to-end performance gates have not passed.
        var guardedPaths = plant.Parameters.ContactDeformationMode == ContactDeformationMode.BroadLeafV1;
        if (length <= initialLength + 1e-7f && radius <= initialRadius + 1e-7f) return false;

        ContinuationPlants.Clear();
        var accepted = false;
        AddPlant(plant);
        try
        {
            ContinuingGrowth = true;
            // Extension and thickening have independent admissible paths. A tight
            // radial contact must not discard feasible longitudinal development.
            Advance(longitudinal: true, length);
            Advance(longitudinal: false, radius);
            if (accepted) ag.CompleteCertifiedGrowth(index);
            return accepted;
        }
        finally
        {
            ContinuingGrowth = false;
            CollectingContinuationContacts = false;
            RestoreWaypoint();
            foreach (var participant in ContinuationPlants)
            {
                var other = participant.Plant.AG;
                if (accepted)
                    Array.Copy(other.PreviousGeometry, participant.Original, other.Count);
                other.PreviousGeometry = participant.Original;
                // Previous poses participate in BVH bounds and query identities.
                other.RebaseAndRefit();
                RefreshPlantBounds(participant.Plant);
                if (accepted) RefreshContactLoads(participant.Plant);
            }
            ContinuationPlants.Clear();
            ContinuationWitnesses.Clear();
            LastTrialFailure = null;
        }

        void AddPlant(PlantFormation2 participant)
        {
            foreach (var existing in ContinuationPlants)
                if (existing.Plant == participant) return;
            var other = participant.AG;
            var backup = guardedPaths ? other.SaveContactTrial() : other.SaveContinuationGeometry();
            ContinuationPlants.Add(new(participant, other.PreviousGeometry, backup));
            other.PreviousGeometry = other.SaveContinuationGeometry();
            other.RebaseAndRefit();
            RefreshPlantBounds(participant);
        }

        void RestoreWaypoint()
        {
            foreach (var participant in ContinuationPlants)
            {
                participant.Plant.AG.RestoreContactTrial(participant.Plant.AG.PreviousGeometry);
                RefreshPlantBounds(participant.Plant);
            }
        }

        void Advance(bool longitudinal, float target)
        {
            var start = longitudinal ? ag.GetLength(index) : ag.GetBaseRadius(index);
            if (target <= start + 1e-7f) return;
            var fixedSize = longitudinal ? ag.GetBaseRadius(index) : ag.GetLength(index);
            var fraction = 0f;
            var step = 1f;
            // Exhaustion means uncertified remainder, never proof of confinement.
            for (var trial = 0; trial < 16 && fraction < 1f; ++trial)
            {
                ++LastContinuationTrials;
                RestoreWaypoint();
                if (guardedPaths)
                    foreach (var participant in ContinuationPlants) participant.Plant.AG.SaveContactTrial();
                ContinuationWitnesses.Clear();
                var next = MathF.Min(1f, fraction + step);
                var size = start + (target - start) * next;
                ag.SetContinuationDimensions(index, longitudinal ? size : fixedSize,
                    longitudinal ? fixedSize : size);
                RefreshPlantBounds(plant);
                var clear = CertifyStep();
                for (var round = 0; !clear && round < (guardedPaths && ContinuationWitnesses.Count > 1 ? 16 : 8); ++round)
                {
                    if (!YieldAtFailure())
                    {
                        // A clear endpoint may still cross foliage on the direct
                        // growth/bend path. Certify evasion before development;
                        // endpoint clearance alone never authorizes either leg.
                        if (!guardedPaths || !StageEvasion()) break;
                        clear = CertifyStep();
                        continue;
                    }
                    ++LastContinuationCorrections;
                    clear = CertifyStep();
                }
                if (TraceContacts)
                    Console.WriteLine($"CONTINUATION t={plant.World.Timestep} id={ag.GetOrganId(index)} lengthAxis={longitudinal} trial={trial} from={fraction:R} to={next:R} clear={clear} failure={LastTrialFailure?.IdA}/{LastTrialFailure?.IdB}");
                if (clear)
                {
                    accepted = true;
                    ++LastContinuationSteps;
                    fraction = next;
                    foreach (var participant in ContinuationPlants)
                    {
                        var other = participant.Plant.AG;
                        for (var i = 0; i < other.Count; ++i)
                            if (other.GetContactPose(i) != participant.RetryStart[i].Pose)
                                other.WakeGrowth(i);
                        other.SaveContinuationGeometry();
                        other.RebaseAndRefit();
                        RefreshPlantBounds(participant.Plant);
                    }
                    step = MathF.Min(1f - fraction, step * 2f);
                }
                else
                {
                    // Staged evasion belongs to this development transaction.
                    // Failed growth cannot leave an artificial permanent bend.
                    if (guardedPaths)
                        foreach (var participant in ContinuationPlants)
                            Array.Copy(participant.RetryStart, participant.Plant.AG.PreviousGeometry, participant.Plant.AG.Count);
                    step *= .5f;
                    if ((target - start) * step <= 1e-7f) break;
                }
            }
            RestoreWaypoint();
        }

        bool StageEvasion()
        {
            var proposedLength = ag.GetLength(index); var proposedRadius = ag.GetBaseRadius(index);
            var waypoint = ag.PreviousGeometry[index];
            ag.SetContinuationDimensions(index, waypoint.Pose.Length, waypoint.Radius);
            RefreshPlantBounds(plant);
            var moved = false;
            foreach (var participant in ContinuationPlants)
                for (var i = 0; i < participant.Plant.AG.Count; ++i)
                    moved |= participant.Plant.AG.GetContactPose(i) != participant.Plant.AG.PreviousGeometry[i].Pose;
            var clear = moved && CertifyStep();
            if (clear)
            {
                foreach (var participant in ContinuationPlants)
                {
                    participant.Plant.AG.SaveContinuationGeometry();
                    participant.Plant.AG.RebaseAndRefit();
                    RefreshPlantBounds(participant.Plant);
                }
                ++LastContinuationEvasionStages;
            }
            ag.SetContinuationDimensions(index, proposedLength, proposedRadius);
            RefreshPlantBounds(plant);
            return clear;
        }

        bool CertifyStep()
        {
            var hint = LastTrialFailure;
            LastTrialFailure = null;
            foreach (var participant in ContinuationPlants)
            {
                var other = participant.Plant.AG;
                for (var i = 0; i < other.Count; ++i)
                {
                    // This is a whole-component scan, but only changed bodies
                    // issue queries. Descendant transport is included by exact
                    // pose comparison; ancestor thickening is not transport.
                    if (!other.IsCollisionEligiblePublic(i)
                        || other.GetContactPose(i) == other.PreviousGeometry[i].Pose) continue;
                    LastTrialFailure = hint;
                    if (!GrowthTrialClear(participant.Plant, i, radiusOnly: true)) return false;
                }
            }
            LastTrialFailure = null;
            return true;
        }

        bool YieldAtFailure()
        {
            // Static obstruction or an uncertified sweep has no endpoint reaction.
            // Shrink the development increment instead of inventing a force.
            if (LastTrialFailure is not { } failure) return false;
            var pa = failure.A; var pb = failure.B;
            var a = pa.AG; var b = pb.AG; var i = failure.I; var j = failure.J;
            var sa = Shape(a, i); var sb = Shape(b, j);
            var common = a == b ? CommonAncestor(a, i, j) : -1;
            var reach = sa.Pose.Radius + sb.Pose.Radius;
            var hit = PairDistance(a, common, sa, sb, reach + FoliageGeometry.Skin);
            if (TraceContacts)
                Console.WriteLine($"CONTINUATION_CONTACT t={pa.World.Timestep} ids={failure.IdA}/{failure.IdB} gapUm={(hit.Distance-reach)*1e6f:R}");
            if (hit.Distance >= reach - FoliageGeometry.Skin)
            {
                if (TraceContacts) Console.WriteLine($"CONTINUATION_FAILURE motionCertificate ids={failure.IdA}/{failure.IdB} witnesses={ContinuationWitnesses.Count}");
                return false;
            }
            var first = a == b ? failure.IdA < failure.IdB : PlantIndices[pa] < PlantIndices[pb];
            var key = first ? new ContactKey(pa, failure.IdA, pb, failure.IdB)
                : new ContactKey(pb, failure.IdB, pa, failure.IdA);
            var discoverGuards = true;
            foreach (var old in ContinuationWitnesses)
                if (old.Key == key && old.Reaction) discoverGuards = false;
            if (!RememberContinuationContact(pa, i, pb, j, reaction: true)) return false;
            AddPlant(pa); AddPlant(pb);
            if (guardedPaths && discoverGuards)
            {
                CollectingContinuationContacts = true;
                try
                {
                    // Discover touching neighbours before a correction closes their
                    // gaps. These are inequalities, not extra applied loads. Queries
                    // use current geometry, never a cached clearance or contact list.
                    foreach (var participant in ContinuationPlants)
                        for (var body = 0; body < participant.Plant.AG.Count; ++body)
                            if (participant.Plant.AG.IsCollisionEligiblePublic(body)
                                && participant.Plant.AG.GetContactPose(body) != participant.Plant.AG.PreviousGeometry[body].Pose)
                                GrowthTrialClear(participant.Plant, body, radiusOnly: true);
                }
                finally { CollectingContinuationContacts = false; }
            }
            if (ContinuationWitnesses.Count > 1 && YieldTogether(key, hit, first)) return true;
            // Main-pass memories use stable-ID/plant order. A trial can discover
            // the reverse direction; retain the remembered side and reverse its
            // normal rather than silently falling back to the organ bases.
            var ownFirst = a == b ? failure.IdA < failure.IdB : PlantIndices[pa] < PlantIndices[pb];
            var memoryWork = Work[ownFirst ? pa : pb];
            Vector3? remembered = memoryWork.Contacts.TryGetValue(key, out var memory)
                ? ownFirst ? memory.Value.Normal : -memory.Value.Normal : null;
            var normal = ContactNormal(hit, a.PreviousGeometry[i].Pose.Base - b.PreviousGeometry[j].Pose.Base,
                remembered);
            var depth = reach - hit.Distance + ContinuationReactionGap;
            var ma = a.ContactMobility(i, hit.A, normal, common);
            var mb = b.ContactMobility(j, hit.B, -normal, common);
            var shared = a.SharedShapeMobility(common, i, hit.A, j, hit.B, normal);
            var total = ma + mb + shared;
            if (total <= 1e-20 || !double.IsFinite(total))
            {
                if (TraceContacts) Console.WriteLine($"CONTINUATION_FAILURE mobility ids={failure.IdA}/{failure.IdB} a={ma:R} b={mb:R} shared={shared:R} witnesses={ContinuationWitnesses.Count}");
                return false;
            }
            a.BeginContactSweep();
            if (a != b) b.BeginContactSweep();
            if (ma > 0) a.QueueContactCorrection(i, hit.A, normal, (float)(depth * ma / total), common);
            if (mb > 0) b.QueueContactCorrection(j, hit.B, -normal, (float)(depth * mb / total), common);
            if (shared > 0) a.QueueSharedShapeCorrection(common, i, hit.A, j, hit.B, normal, (float)(depth / total));
            // A coarse correction can leave its endpoint clear while sweeping
            // through a tightly packed third body. This local numerical cap is
            // independent of biological tick duration and whole-organ stiffness.
            var limit = MathF.Min(CollisionSettings.MaxCorrectionRadians, MathF.PI / 1800f);
            var moved = a.EndContactSweep(limit);
            if (a != b) moved |= b.EndContactSweep(limit);
            RefreshPlantBounds(pa);
            if (a != b) RefreshPlantBounds(pb);
            return moved;
        }

        bool YieldTogether(ContactKey latest, FoliageContact endpoint, bool canonicalEndpoint)
        {
            // A bounded unilateral tangent solve. Full nonlinear endpoint and swept
            // certificates still decide acceptance; this is not an equilibrium proof.
            if (ContinuationSlots.Length == 0)
            {
                ContinuationSlots = new ushort[MaximumContinuationContacts * MaximumContinuationJoints];
                ContinuationMatrix = new double[MaximumContinuationContacts * MaximumContinuationContacts];
                ContinuationDepth = new double[MaximumContinuationContacts];
                ContinuationForces = new double[MaximumContinuationContacts];
                ContinuationEnabled = new bool[MaximumContinuationContacts];
            }
            Array.Fill(ContinuationSlots, ushort.MaxValue);
            Array.Clear(ContinuationMatrix); Array.Clear(ContinuationForces);
            ContinuationJoints.Clear(); ContinuationCompliance.Clear(); ContinuationJointIndices.Clear(); ContinuationGradients.Clear();
            var rows = ContinuationWitnesses.Count;
            LastContinuationContactRows = rows;
            LastContinuationDualResidualMetres = double.NaN;
            Array.Fill(ContinuationEnabled, true);
            for (var row = 0; row < rows; ++row)
            {
                var w = ContinuationWitnesses[row]; var a = w.Key.A.AG; var b = w.Key.B.AG;
                AddPlant(w.Key.A); AddPlant(w.Key.B);
                var sa = Shape(a, w.I); var sb = Shape(b, w.J);
                var common = a == b ? CommonAncestor(a, w.I, w.J) : -1;
                var reach = sa.Pose.Radius + sb.Pose.Radius;
                // A proximity query caps the returned distance at its search radius.
                // Search beyond touching by the trial's motion bound so a cleared
                // witness retains usable clearance instead of becoming a pinned seam.
                var search = reach + FoliageGeometry.Skin
                    + Previous(a, w.I, sa).MotionBound(sa) + Previous(b, w.J, sb).MotionBound(sb);
                // The failing pair was just evaluated at these exact poses. Reuse
                // its penetrating witness when already in canonical query order;
                // reversing tie order can otherwise choose a different mesh feature.
                var hit = canonicalEndpoint && w.Key == latest ? endpoint
                    : PairDistance(a, common, sa, sb, search);
                Vector3? remembered = Work[w.Key.A].Contacts.TryGetValue(w.Key, out var memory) ? memory.Value.Normal : null;
                var normal = ContactNormal(hit, a.PreviousGeometry[w.I].Pose.Base - b.PreviousGeometry[w.J].Pose.Base, remembered);
                // Negative depths preserve a previously cleared contact as an inequality:
                // another correction must not spend more than its available clearance.
                ContinuationDepth[row] = reach - hit.Distance + (w.Reaction ? ContinuationReactionGap : -FoliageGeometry.Skin);
                if (!AppendChain(a, w.I, hit.A, normal, common, row)
                    || !AppendChain(b, w.J, hit.B, -normal, common, row)) return false;
                if (common >= 0 && !Append(a, common, row, a.CommonContactGradient(common, w.I, hit.A, w.J, hit.B, normal))) return false;
            }
            foreach (var g in ContinuationGradients)
                for (var column = 0; column < rows; ++column)
                {
                    var slot = ContinuationSlots[g.Joint * MaximumContinuationContacts + column];
                    if (slot == ushort.MaxValue) continue;
                    var other = ContinuationGradients[slot];
                    ContinuationMatrix[g.Row * MaximumContinuationContacts + column]
                        += (guardedPaths ? ContinuationCompliance[g.Joint].Bilinear(g.Torque, other.Torque)
                            : (double)g.Torque.X * other.Turn.X + (double)g.Torque.Y * other.Turn.Y + (double)g.Torque.Z * other.Turn.Z)
                            + (double)g.Shape.X * other.Fold.X
                            + (double)g.Shape.Y * other.Fold.Y + (double)g.Shape.Z * other.Fold.Z
                            + (double)g.Shape.W * other.Fold.W;
                }
            // Directional bend/strain limits can make the local tangent nonsymmetric.
            // Preserve the old bounded response in that case instead of silently
            // replacing it with an invented material operator.
            for (var row = 0; row < rows; ++row)
            {
                var diagonal = ContinuationMatrix[row * MaximumContinuationContacts + row];
                if (!double.IsFinite(diagonal) || diagonal < 0 || diagonal <= 1e-20 && ContinuationDepth[row] > 0)
                {
                    if (!ContinuationWitnesses[row].Reaction) { ContinuationEnabled[row] = false; continue; }
                    if (TraceContacts) Console.WriteLine($"CONTINUATION_TANGENT blockedRow={row} diagonal={diagonal:R} depth={ContinuationDepth[row]:R}");
                    return false;
                }
                for (var column = 0; column < row; ++column)
                {
                    var x = ContinuationMatrix[row * MaximumContinuationContacts + column];
                    var y = ContinuationMatrix[column * MaximumContinuationContacts + row];
                    if (!ContinuationEnabled[column]) continue;
                    var scale = Math.Sqrt(diagonal * ContinuationMatrix[column * MaximumContinuationContacts + column]);
                    if (!double.IsFinite(x + y) || Math.Abs(x - y) > 1e-5 * scale
                        || Math.Abs(x) > scale * (1 + 1e-5))
                    {
                        // A saturated joint gives a one-sided response, not a
                        // symmetric material tensor. Do not invent compliance to
                        // fit it. Optional clearance guards can leave this tangent
                        // solve; the nonlinear motion certificate still checks them.
                        if (!ContinuationWitnesses[row].Reaction) { ContinuationEnabled[row] = false; break; }
                        if (!ContinuationWitnesses[column].Reaction) { ContinuationEnabled[column] = false; continue; }
                        if (TraceContacts) Console.WriteLine($"CONTINUATION_TANGENT invalidRows={row}/{column} x={x:R} y={y:R} scale={scale:R}");
                        return false;
                    }
                }
            }
            ContinuationBendLimits.Clear();
            LastContinuationActiveBendLimits = 0;
            var solved = guardedPaths ? SolveContinuationBendLimits(rows, out var residual)
                : SolveContinuationContacts(rows, out residual, allowDirect: false);
            if (!solved) return false;
            LastContinuationDualResidualMetres = residual;
            if (guardedPaths && residual > 1e-8) return false; // exhausted tangent budget, not confinement
            // Bound the complete tangent direction with one scalar. Clipping each
            // joint separately changes the compliance-weighted direction and can
            // drive a previously satisfied contact back into penetration.
            var limit = MathF.Min(CollisionSettings.MaxCorrectionRadians, MathF.PI / 1800f);
            var stepScale = 1f;
            if (guardedPaths)
            {
                if (ContinuationTurns.Length == 0)
                {
                    ContinuationTurns = new Vector3[MaximumContinuationJoints];
                    ContinuationFolds = new Vector4[MaximumContinuationJoints];
                }
                Array.Clear(ContinuationTurns); Array.Clear(ContinuationFolds);
                foreach (var g in ContinuationGradients)
                {
                    var force = (float)ContinuationForces[g.Row];
                    if (force == 0) continue;
                    ContinuationTurns[g.Joint] += g.Turn * force;
                    ContinuationFolds[g.Joint] += g.Fold * force;
                }
                AccumulateContinuationBendReactions(ContinuationTurns);
                for (var joint = 0; joint < ContinuationJoints.Count; ++joint)
                {
                    var turn = ContinuationTurns[joint].Length();
                    var fold = Vector4.Abs(ContinuationFolds[joint]);
                    var maximumFold = MathF.Max(MathF.Max(fold.X, fold.Y), MathF.Max(fold.Z, fold.W));
                    if (turn > limit) stepScale = MathF.Min(stepScale, limit / turn);
                    if (maximumFold > CollisionSettings.MaxCorrectionRadians)
                        stepScale = MathF.Min(stepScale, CollisionSettings.MaxCorrectionRadians / maximumFold);
                }
            }
            if (!(stepScale > 0) || !float.IsFinite(stepScale)) return false;
            foreach (var p in ContinuationPlants) p.Plant.AG.BeginContactSweep();
            foreach (var g in ContinuationGradients)
                if (ContinuationForces[g.Row] > 0)
                    ContinuationJoints[g.Joint].Plant.AG.QueueCoupledContactResponse(ContinuationJoints[g.Joint].Index,
                        g.Turn, g.Fold, (float)ContinuationForces[g.Row] * stepScale);
            QueueContinuationBendReactions(stepScale);
            var moved = false;
            foreach (var p in ContinuationPlants)
            {
                moved |= p.Plant.AG.EndContactSweep(limit);
                RefreshPlantBounds(p.Plant);
            }
            ++LastContinuationCoupledSets;
            return moved;
        }

        bool AppendChain(PlantSubFormation<AboveGroundAgent> other, int body, Vector3 point, Vector3 normal, int stop, int row)
        {
            for (var joint = body; joint >= 0 && joint != stop && !other.IsMechanicallyAnchored(joint); joint = other.GetParent(joint))
                if (!Append(other, joint, row, other.ContactGradient(joint, body, point, normal, coupledLimits: guardedPaths))) return false;
            return true;
        }

        bool Append(PlantSubFormation<AboveGroundAgent> other, int index, int row,
            (Vector3 Torque, Vector3 Turn, Vector4 Shape, Vector4 Fold) gradient)
        {
            if (gradient.Torque == Vector3.Zero && gradient.Shape == Vector4.Zero) return true;
            var key = new ContinuationJoint(other.Plant, index);
            if (!ContinuationJointIndices.TryGetValue(key, out var joint))
            {
                if (ContinuationJoints.Count == MaximumContinuationJoints) return false;
                joint = ContinuationJoints.Count;
                ContinuationJoints.Add(key); ContinuationJointIndices.Add(key, joint);
                if (guardedPaths) ContinuationCompliance.Add(other.CoupledAngularCompliance(index));
            }
            if (guardedPaths) gradient.Turn = ContinuationCompliance[joint].Response(gradient.Torque);
            // At most 16 * 256 gradients; the sentinel is outside this bounded range.
            ContinuationSlots[joint * MaximumContinuationContacts + row] = (ushort)ContinuationGradients.Count;
            ContinuationGradients.Add(new(row, joint, gradient.Torque, gradient.Turn, gradient.Shape, gradient.Fold));
            return true;
        }
    }

    bool RememberContinuationContact(PlantFormation2 pa, int i, PlantFormation2 pb, int j, bool reaction)
    {
        var first = pa == pb ? pa.AG.GetOrganId(i) < pb.AG.GetOrganId(j) : PlantIndices[pa] < PlantIndices[pb];
        var w = first ? new ContinuationWitness(new(pa, pa.AG.GetOrganId(i), pb, pb.AG.GetOrganId(j)), i, j, reaction)
            : new ContinuationWitness(new(pb, pb.AG.GetOrganId(j), pa, pa.AG.GetOrganId(i)), j, i, reaction);
        var at = 0;
        while (at < ContinuationWitnesses.Count)
        {
            var old = ContinuationWitnesses[at];
            if (old.Key == w.Key)
            {
                if (reaction && !old.Reaction) ContinuationWitnesses[at] = old with { Reaction = true };
                return true;
            }
            var c = PlantIndices[old.Key.A].CompareTo(PlantIndices[w.Key.A]);
            if (c == 0) c = old.Key.OrganA.CompareTo(w.Key.OrganA);
            if (c == 0) c = PlantIndices[old.Key.B].CompareTo(PlantIndices[w.Key.B]);
            if (c == 0) c = old.Key.OrganB.CompareTo(w.Key.OrganB);
            if (c > 0) break;
            ++at;
        }
        if (ContinuationWitnesses.Count == MaximumContinuationContacts)
        {
            if (TraceContacts) Console.WriteLine($"CONTINUATION_FAILURE witnessBudget ids={w.Key.OrganA}/{w.Key.OrganB} reaction={reaction}");
            return false;
        }
        ContinuationWitnesses.Insert(at, w);
        return true;
    }
}
