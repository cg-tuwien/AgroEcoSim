using System.Numerics;
using System.Runtime.CompilerServices;

namespace Agro;

public partial class PlantSubFormation<T> where T : struct, IPlantAgent
{
    // Restore-time validation only. World.RestoreTimestep follows formation ReadState,
    // so causal tick relationships are checked without reading the world's old clock.
    private void ValidateTreeModuleState()
    {
        static void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException("Invalid tree module state: " + message);
        }
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        static bool Unit(Quaternion q) => float.IsFinite(q.X) && float.IsFinite(q.Y)
            && float.IsFinite(q.Z) && float.IsFinite(q.W) && MathF.Abs(q.LengthSquared() - 1f) <= .002f;
        static bool UnitOrZero(Quaternion q) => q == default || Unit(q);
        bool Id(ulong id) => id != 0 && id < NextOrganId;
        static bool Kind(TreeModuleKind kind) => kind is TreeModuleKind.Trained or TreeModuleKind.DorsiventralMainline;

        Require(!TreeModuleWithdrawalIncomplete && ModuleWithdrawalNotices is not { Count: > 0 },
            "an incomplete withdrawal transaction is not a resumable boundary");
        Require(TreeModuleProvisionalWithdrawals >= 0 && TreeModuleBarrierExecutions >= 0
            && TreeModuleBarrierExecutions <= TreeModuleProvisionalWithdrawals
            && (TreeModuleBarrierExecutions == 0) == (TreeModuleProvisionalWithdrawals == 0),
            "completed withdrawal/batch counters differ");
        var aboveGround = IsAboveGround && typeof(T) == typeof(AboveGroundAgent);
        var enabled = aboveGround && TreeModulesEnabled;
        if (!enabled)
        {
            Require(!TrainingReleased && TrainingReleaseTick == 0 && TrainingWitnessStemStableId == 0
                && TrainingWitnessHeightM == 0f && TreeModuleProvisionalWithdrawals == 0 && TreeModuleBarrierExecutions == 0
                && (TreeModules?.Count ?? 0) == 0 && (TreeClosureFates?.Count ?? 0) == 0,
                "disabled request carries module/training history");
            if (aboveGround)
            {
                foreach (ref var value in Src())
                    Require(Unsafe.As<T, AboveGroundAgent>(ref value).TreeModuleRootStableId == 0,
                        "disabled live organ carries a module identity");
                foreach (var value in Births)
                {
                    var copy = value;
                    Require(Unsafe.As<T, AboveGroundAgent>(ref copy).TreeModuleRootStableId == 0,
                        "disabled queued organ carries a module identity");
                }
            }
            foreach (var p in Developments.Values)
                Require(!p.FixedTreeAxil && p.NewModuleRootStableIds.Length == 0
                    && p.Before.TreeModuleRootStableId == 0 && p.After.TreeModuleRootStableId == 0
                    && p.Children.All(c => c.TreeModuleRootStableId == 0),
                    "disabled pending development carries module context");
            return;
        }

        Require(NextOrganId != 0, "organ allocator wrapped");
        Require(float.IsFinite(TrainingWitnessHeightM), "nonfinite training witness");
        if (TrainingReleased)
            Require(Id(TrainingWitnessStemStableId) && TrainingWitnessHeightM >= Plant.Parameters.EffectiveTreeMainlineTrainingHeightM,
                "released training lacks its historical height/identity witness");
        else
            Require(TrainingReleaseTick == 0 && TrainingWitnessStemStableId == 0 && TrainingWitnessHeightM == 0f,
                "unreleased training carries a release context");

        var modules = TreeModules;
        if (modules != null)
            foreach (var (key, m) in modules)
            {
                Require(Id(key) && key == m.RootStableId && Kind(m.Kind), "bad module key/kind");
                Require(m.BirthState is TreeModuleBirthState.Queued or TreeModuleBirthState.CertifiedPending
                    or TreeModuleBirthState.Provisional or TreeModuleBirthState.Live, "unsupported module birth state");
                Require(float.IsFinite(m.SiteRollRadians) && Unit(m.TargetFrameInOwnerLocal) && Unit(m.OpeningStartLocal),
                    "module frame/roll is not finite and unit");
                Require(float.IsFinite(m.FrozenAllowanceM) && m.FrozenAllowanceM >= 0f
                    && float.IsFinite(m.OpeningLengthM) && m.OpeningLengthM >= 0f, "invalid finite-unit lengths");
                if (m.FrozenAllowanceM == 0f)
                    Require(m.OpeningLengthM == 0f && m.AllowanceCycle == int.MinValue, "unfrozen module has an opening/cycle");
                else
                    Require(m.AllowanceCycle != int.MinValue && (m.Kind == TreeModuleKind.Trained
                        ? m.OpeningLengthM == 0f : m.OpeningLengthM > 0f && m.OpeningLengthM < m.FrozenAllowanceM),
                        "frozen opening must match its trained/dorsiventral kind");
                if (m.Kind == TreeModuleKind.DorsiventralMainline)
                    Require(TrainingReleased && m.CreatedTick > TrainingReleaseTick, "dorsiventral birth predates training release");
                if (m.ClosureFateId == 0)
                    Require(m.Kind == TreeModuleKind.Trained && m.PredecessorRootStableId == 0
                        && m.SiteOwnerStableId == 0 && m.OriginPetioleStableId == 0, "initial module has closure ancestry");
                else
                    Require(Id(m.ClosureFateId) && m.ClosureFateId == m.SiteOwnerStableId
                        && Id(m.OriginPetioleStableId) && Id(m.PredecessorRootStableId)
                        && modules.ContainsKey(m.PredecessorRootStableId)
                        && m.PredecessorRootStableId <= m.SiteOwnerStableId
                        && m.SiteOwnerStableId < key && m.OriginPetioleStableId < key, "module allocation/ancestry order differs");
                if (m.BirthState is TreeModuleBirthState.Queued or TreeModuleBirthState.CertifiedPending)
                    Require(m.BirthCommitTick == 0 && m.AdmittedTick == 0 && !m.CommittedByNormalCensus,
                        "uncommitted module carries admission history");
                else
                    Require(m.BirthCommitTick >= m.CreatedTick && m.CommittedByNormalCensus == m.ImmediatePath
                        && (m.BirthState == TreeModuleBirthState.Provisional ? m.AdmittedTick == 0
                            : m.AdmittedTick >= m.BirthCommitTick), "module admission chronology differs");
                if (m.BirthState == TreeModuleBirthState.CertifiedPending)
                    Require(!m.ImmediatePath, "immediate module is marked CertifiedPending");
            }

        var allocated = new HashSet<ulong>();
        var liveIds = new HashSet<ulong>();
        var liveIndices = new Dictionary<ulong,int>();
        var liveRoots = new HashSet<ulong>();
        var queuedRoots = new HashSet<ulong>();
        var pendingRoots = new Dictionary<ulong, Development>();
        void Reference(in AboveGroundAgent agent)
        {
            var root = agent.TreeModuleRootStableId;
            if (root == 0) return;
            Require(Id(agent.StableId) && root <= agent.StableId && modules != null && modules.ContainsKey(root),
                "organ references an absent or later module root");
            Require(!agent.IsRizome && agent.Organ is OrganTypes.Bud or OrganTypes.Meristem or OrganTypes.Stem,
                "non-axis organ carries module identity");
            Require(UnitOrZero(agent.CaptureTreeUnitReference())
                && float.IsFinite(agent.TreeModuleAllowance) && agent.TreeModuleAllowance >= 0f
                && float.IsFinite(agent.TreeModuleExtension) && agent.TreeModuleExtension >= 0f,
                "tracked organ has invalid unit material");
        }
        void Physical(in AboveGroundAgent agent)
        {
            Require(Id(agent.StableId) && allocated.Add(agent.StableId), "physical/proposed organ ID is duplicate or unallocated");
            Reference(agent);
        }
        var liveIndex = 0;
        foreach (ref var value in Src())
        {
            ref var agent = ref Unsafe.As<T, AboveGroundAgent>(ref value);
            Physical(agent); liveIds.Add(agent.StableId);
            liveIndices.Add(agent.StableId, liveIndex++);
            if (agent.TreeModuleRootStableId == agent.StableId) liveRoots.Add(agent.StableId);
        }
        foreach (var value in Births)
        {
            var copy = value;
            ref var agent = ref Unsafe.As<T, AboveGroundAgent>(ref copy);
            Physical(agent);
            if (agent.TreeModuleRootStableId == agent.StableId) queuedRoots.Add(agent.StableId);
        }
        foreach (var (key, p) in Developments)
        {
            Require(Id(key) && key == p.Owner && p.Before.StableId == key && p.After.StableId == key
                && liveIds.Contains(key), "pending module owner identity differs");
            Require(p.Before.TreeModuleRootStableId == p.After.TreeModuleRootStableId, "pending owner changes its module identity");
            Reference(p.Before); Reference(p.After);
            Require(p.Parents.Length == p.Children.Length && p.LocalParents.Length == p.Children.Length,
                "pending child parent arrays differ");
            var expected = new List<ulong>();
            var fixedSite = false;
            for (var j = 0; j < p.Children.Length; ++j)
            {
                var child = p.Children[j]; Physical(child);
                if (child.TreeModuleRootStableId == 0 || child.TreeModuleRootStableId != child.StableId) continue;
                var module = modules![child.StableId];
                Require(module.BirthState == TreeModuleBirthState.CertifiedPending && pendingRoots.TryAdd(child.StableId, p),
                    "pending root is duplicated or not CertifiedPending");
                expected.Add(child.StableId);
                if (module.ClosureFateId == 0) continue;
                fixedSite = true;
                Require(module.SiteOwnerStableId == key && p.LocalParents[j] == -1 && p.Parents[j] == key,
                    "fixed-site root has a different pending owner/parent");
            }
            Require(p.NewModuleRootStableIds.SequenceEqual(expected) && p.FixedTreeAxil == fixedSite,
                "pending root list/fixed-axil flag differs from its immutable children");
        }

        if (modules != null)
            foreach (var (id, m) in modules)
            {
                var live = liveRoots.Contains(id); var queued = queuedRoots.Contains(id); var pending = pendingRoots.ContainsKey(id);
                Require(!allocated.Contains(id) || live || queued || pending, "root organ carries a different module identity");
                Require(m.BirthState switch
                {
                    TreeModuleBirthState.Queued => queued && !live && !pending,
                    TreeModuleBirthState.CertifiedPending => pending && !live && !queued,
                    TreeModuleBirthState.Provisional => live && !queued && !pending,
                    TreeModuleBirthState.Live => !queued && !pending,
                    _ => false
                }, "module state disagrees with live/queued/pending root location");
                // Live is historical: its original root organ need not still exist.
                if (m.ClosureFateId == 0) continue;
                if (m.BirthState == TreeModuleBirthState.Live)
                    Require(TreeClosureFates == null || !TreeClosureFates.ContainsKey(m.ClosureFateId),
                        "admitted root retains an unfinished closure fate");
                else
                    Require(TreeClosureFates != null && TreeClosureFates.TryGetValue(m.ClosureFateId, out var f)
                        && f.AttemptRootStableId == id, "unadmitted closure root lacks its exact fate");
            }

        var attempts = new HashSet<ulong>();
        long activeFateWithdrawals = 0;
        if (TreeClosureFates != null)
            foreach (var (key, f) in TreeClosureFates)
            {
                Require(Id(key) && key == f.FateId && key == f.SiteOwnerStableId && Kind(f.Kind)
                    && Id(f.PredecessorRootStableId) && modules != null && modules.ContainsKey(f.PredecessorRootStableId)
                    && Id(f.OriginPetioleStableId) && Id(f.AttemptRootStableId) && attempts.Add(f.AttemptRootStableId)
                    && f.PredecessorRootStableId <= key && key < f.AttemptRootStableId
                    && f.OriginPetioleStableId < f.AttemptRootStableId, "bad closure identity/allocation ancestry");
                Require(f.State is TreeClosureState.Sampled or TreeClosureState.CertifiedPending
                    or TreeClosureState.ImmediateAttempt or TreeClosureState.SiteLost or TreeClosureState.WithdrawnReady,
                    "closure state is unsupported");
                Require(f.AttemptCount >= 1 && f.AttemptTick >= f.SampledTick
                    && (f.AttemptCount == 1 ? f.AttemptTick == f.SampledTick : f.AttemptTick > f.SampledTick)
                    && float.IsFinite(f.SiteRollRadians) && Finite(f.SiteOffsetOwnerLocal)
                    && Unit(f.OwnerFrameAtSample) && Unit(f.TargetFrameInOwnerLocal) && Unit(f.OpeningStartLocal),
                    "closure sample/attempt/frame differs");
                var template = f.Template;
                Require(template.StableId == 0 && template.TreeModuleRootStableId == 0
                    && template.Energy == 0f && template.Water_g == 0f && !template.IsRizome && template.Organ == OrganTypes.Bud
                    && template.BirthTime == f.SampledTick && template.TreeModuleCycle == f.SampledShootCycle
                    && float.IsFinite(template.Length) && template.Length > 0f && float.IsFinite(template.Radius) && template.Radius > 0f
                    && Finite(template.BaseOffset) && Unit(template.Orientation) && Unit(template.baseOrientation)
                    && Unit(template.restOrientation) && Unit(template.targetOrientation) && Unit(template.CaptureTreeUnitReference()),
                    "closure template is not a canonical unfunded identity-free bud");
                if (f.State == TreeClosureState.SiteLost)
                {
                    Require(!modules!.ContainsKey(f.AttemptRootStableId) && !allocated.Contains(f.AttemptRootStableId),
                        "cancelled fixed-site attempt still has a module/body");
                    activeFateWithdrawals += f.AttemptCount - 1L;
                    continue; // The cancelled reserved ID and old site are intentionally historical.
                }
                if (f.State == TreeClosureState.WithdrawnReady)
                {
                    Require(Plant.World.ContactEngine == ContactEngineKind.Mixed
                        && !modules!.ContainsKey(f.AttemptRootStableId) && !allocated.Contains(f.AttemptRootStableId)
                        && liveIndices.TryGetValue(f.SiteOwnerStableId, out var owner)
                        && !Deaths.Contains(owner) && liveIndices.TryGetValue(f.OriginPetioleStableId, out var petiole)
                        && !Deaths.Contains(petiole) && Src()[petiole].Parent == owner && Src()[petiole].Organ == OrganTypes.Petiole,
                        "completed withdrawn fate lacks its consumed absent root and live original site");
                    activeFateWithdrawals += f.AttemptCount;
                    continue;
                }
                activeFateWithdrawals += f.AttemptCount - 1L;
                Require(modules!.TryGetValue(f.AttemptRootStableId, out var m)
                    && m.ClosureFateId == key && m.PredecessorRootStableId == f.PredecessorRootStableId
                    && m.SiteOwnerStableId == key && m.OriginPetioleStableId == f.OriginPetioleStableId && m.Kind == f.Kind
                    && m.CreatedTick == f.AttemptTick && m.SiteRollRadians == f.SiteRollRadians
                    && m.TargetFrameInOwnerLocal == f.TargetFrameInOwnerLocal && m.OpeningStartLocal == f.OpeningStartLocal,
                    "closure and sparse module record differ");
                Require(f.State switch
                {
                    TreeClosureState.Sampled => m!.BirthState == TreeModuleBirthState.Queued && !m.ImmediatePath,
                    // Admission switches the module back to Queued, then Census makes
                    // it Provisional; its fixed fate is removed only by finalization.
                    TreeClosureState.CertifiedPending => !m!.ImmediatePath && (m.BirthState == TreeModuleBirthState.CertifiedPending
                        ? pendingRoots.ContainsKey(m.RootStableId)
                        : m.BirthState is TreeModuleBirthState.Queued or TreeModuleBirthState.Provisional),
                    TreeClosureState.ImmediateAttempt => m!.ImmediatePath
                        && m.BirthState is TreeModuleBirthState.Queued or TreeModuleBirthState.Provisional,
                    _ => false
                }, "closure state disagrees with admission path");
            }
        Require(activeFateWithdrawals <= TreeModuleProvisionalWithdrawals,
            "unfinished retry fates exceed completed withdrawal history");
    }
}
