using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Agro;

// Last-tick diagnostic receipt only. It is neither a resource pool nor replay
// state, and is cleared on ordinary preparation/restoration. IDs remain stable
// across the one normal compaction; no physical-array reference escapes it.
internal readonly record struct TreeModuleWithdrawalReceipt(ulong RootStableId, ulong OwnerStableId,
    ulong PetioleStableId, bool ReturnedToLiveSite, float ChildEnergy, float ChildWater,
    float ParentEnergyBefore, float ParentEnergyAfter, float ParentWaterBefore, float ParentWaterAfter,
    double WaterOverflow, double ReserveBefore, double ReserveAfter,
    float RemovedPhysiologicalVolume, float InitialBodyConstructionCostNotRefunded);

public partial class PlantSubFormation<T>
{
    readonly record struct ModuleWithdrawal(ulong Id, int Index, int ParentIndex, TreeModuleRecord Module,
        TreeClosureFate Fate, AboveGroundAgent Body, bool ReturnToSite);

    bool TreeClosureSiteAlive(int owner, ulong petioleId)
    {
        if (owner < 0 || Deaths.Contains(owner)) return false;
        var petiole = FindOrgan(petioleId);
        return petiole >= 0 && !Deaths.Contains(petiole) && Src()[petiole].Parent == owner
            && Src()[petiole].Organ == OrganTypes.Petiole;
    }

    void ObserveLostTreeClosureSites()
    {
        if (TreeClosureFates == null) return;
        foreach (var fate in TreeClosureFates.Values)
            if (fate.State == TreeClosureState.WithdrawnReady
                && !TreeClosureSiteAlive(FindOrgan(fate.SiteOwnerStableId), fate.OriginPetioleStableId))
                fate.State = TreeClosureState.SiteLost;
    }

    internal bool TryReissueTreeModule(ref AboveGroundAgent owner, int index, bool biologicallyReady)
    {
        if (!TreeModulesEnabled || TreeClosureFates == null
            || !TreeClosureFates.TryGetValue(owner.StableId, out var fate)
            || fate.State != TreeClosureState.WithdrawnReady) return false;
        if (!TreeClosureSiteAlive(index, fate.OriginPetioleStableId))
        {
            fate.State = TreeClosureState.SiteLost;
            return false;
        }
        // PrepareGrowthAttempts already incorporates the real owner NextHour,
        // movement and WakeGrowth rules. Do not add a second timer that defeats
        // obstacle-removal wake or manufactures winter/unfunded failed attempts.
        if (!biologicallyReady || owner.GrowthBlocked || HasPendingDevelopment(index)
            || !RealModuleSiteEligible(index, fate.OriginPetioleStableId)) return false;
        if (Plant.World.ContactEngine != ContactEngineKind.Mixed || TreeModuleWithdrawalIncomplete)
            throw new InvalidOperationException("Only completed Mixed withdrawal fates can be reissued.");
        if (fate.AttemptCount == int.MaxValue || NextOrganId is 0 or ulong.MaxValue)
            throw new InvalidOperationException("The module attempt allocator would overflow.");
        var bud = fate.Template; // canonical, identity-free, unfunded sampled phenotype
        var bodyCost = ConstructionBudget.Cost(bud.Volume(), Plant.Parameters, bud.WoodRatio());
        if (!float.IsFinite(bodyCost)) throw new InvalidDataException("Nonfinite module birth-body cost.");
        // Ordinary ChargeBirths still performs the sole debit. A positive-cost
        // retry must be payable from the donor after its .12 transfer; no new
        // reserve, carbon multiplier or refund of initial construction is added.
        if (owner.Energy * .88f < bodyCost) return false;
        var carry = owner.Orientation == fate.OwnerFrameAtSample ? Quaternion.Identity
            : Quaternion.Normalize(owner.Orientation * Quaternion.Conjugate(fate.OwnerFrameAtSample));
        bud.PrepareTreeModuleRetry(owner, index, Plant.World.Timestep, carry);
        var module = new TreeModuleRecord { Kind = fate.Kind, ClosureFateId = fate.FateId,
            PredecessorRootStableId = fate.PredecessorRootStableId, SiteOwnerStableId = fate.SiteOwnerStableId,
            OriginPetioleStableId = fate.OriginPetioleStableId, CreatedTick = Plant.World.Timestep,
            SiteRollRadians = fate.SiteRollRadians, TargetFrameInOwnerLocal = fate.TargetFrameInOwnerLocal,
            OpeningStartLocal = fate.OpeningStartLocal };
        var born = BirthTreeModule(bud, module); // ordinary fresh ID, never a recycled failed ID
        fate.AttemptRootStableId = Births[born - AgentCount].StableId;
        fate.AttemptTick = Plant.World.Timestep;
        ++fate.AttemptCount;
        fate.State = TreeClosureState.ImmediateAttempt;
        owner.TransferTreeModuleRetryShare();
        return true;
    }

    void CommitTreeModuleWithdrawals()
    {
        // NoticeTreeModuleDeath sets this persistent latch before any guard.
        // A failure at any point below stays explicitly non-resumable.
        TreeModuleWithdrawalIncomplete = true;
        if (Plant.World.ContactEngine != ContactEngineKind.Mixed || ModuleWithdrawalNotices is not { Count: > 0 }
            || TreeModules == null || TreeClosureFates == null || Births.Count != 0)
            throw new InvalidOperationException("Module withdrawal lacks the strict Mixed notice/birth precondition.");
        SrcDst(out var source, out var previous);
        foreach (var b in MemoryMarshal.AsBytes(previous))
            if (b != 0) throw new InvalidOperationException("Module withdrawal requires an already-cleared opposite buffer.");
        if (TreeModuleProvisionalWithdrawals > long.MaxValue - ModuleWithdrawalNotices.Count
            || TreeModuleBarrierExecutions == long.MaxValue)
            throw new InvalidOperationException("Module withdrawal counters would overflow.");
        var ids = ModuleWithdrawalNotices.Order().ToArray();
        var batch = new List<ModuleWithdrawal>(ids.Length);
        var originalDeaths = new List<ulong>(Deaths.Count);
        foreach (var death in Deaths) originalDeaths.Add(source[death].StableId);
        var removed = new HashSet<ulong>(ids);
        var owners = new HashSet<ulong>();
        // Preflight ALL roots and liveness against the original, unmasked deaths.
        foreach (var id in ids)
        {
            var index = FindOrgan(id);
            if (index < 0 || !Deaths.Contains(index) || !TreeModules.TryGetValue(id, out var module)
                || module.BirthState != TreeModuleBirthState.Provisional || !module.ImmediatePath
                || !module.CommittedByNormalCensus || module.BirthCommitTick != Plant.World.Timestep
                || module.CreatedTick != Plant.World.Timestep || module.ClosureFateId == 0
                || module.ClosureFateId != module.SiteOwnerStableId
                || !owners.Add(module.SiteOwnerStableId)
                || PreviousGeometry[index].Exists || GetChildren(index).Count != 0
                || !TreeClosureFates.TryGetValue(module.ClosureFateId, out var fate)
                || fate.FateId != module.ClosureFateId || fate.State != TreeClosureState.ImmediateAttempt || fate.AttemptRootStableId != id
                || fate.AttemptTick != Plant.World.Timestep)
                throw new InvalidOperationException("Module withdrawal is not a childless same-tick committed replacement.");
            ref var body = ref Unsafe.As<T, AboveGroundAgent>(ref source[index]);
            var parent = body.Parent;
            if (body.StableId != id || body.TreeModuleRootStableId != id || body.Organ != OrganTypes.Bud
                || body.BirthTime != Plant.World.Timestep || parent < 0 || parent >= source.Length
                || body.TreeModuleExtension != 0f || body.TreeModuleAllowance != 0f
                || source[parent].StableId != module.SiteOwnerStableId || removed.Contains(source[parent].StableId)
                || fate.SiteOwnerStableId != module.SiteOwnerStableId || fate.OriginPetioleStableId != module.OriginPetioleStableId
                || !float.IsFinite(body.Energy) || body.Energy < 0f || !float.IsFinite(body.Water_g) || body.Water_g < 0f
                || !float.IsFinite(body.Volume()) || body.Volume() < 0f
                || body.ConstructionEscrow != 0f || body.ConstructionStartVolume != 0f || body.ConstructionProposedVolume != 0f)
                throw new InvalidOperationException("Module withdrawal body, owner or settled balances differ.");
            var returnToSite = TreeClosureSiteAlive(parent, module.OriginPetioleStableId);
            if (returnToSite)
            {
                if (!GrowthRetries.ContainsKey(module.SiteOwnerStableId))
                    throw new InvalidOperationException("A returned module owner lacks its existing obstruction retry.");
                ref var donor = ref Unsafe.As<T, AboveGroundAgent>(ref source[parent]);
                if (!float.IsFinite(donor.Energy) || !float.IsFinite(donor.Water_g) || donor.Water_g < 0f
                    || !float.IsFinite(donor.Energy + body.Energy) || !float.IsFinite(donor.Water_g + body.Water_g)
                    || !float.IsFinite(donor.WaterStorageCapacity_g()) || !double.IsFinite(Plant.UnallocatedWater_g)
                    || !double.IsFinite(Plant.UnallocatedWater_g + (double)donor.Water_g + body.Water_g))
                    throw new InvalidOperationException("Module withdrawal credit would create a nonfinite pool.");
            }
            batch.Add(new(id, index, parent, module, fate, body, returnToSite));
        }
        var receipts = ModuleWithdrawalReceipts ??= [];
        receipts.Clear();
        foreach (var item in batch)
        {
            ref var donor = ref Unsafe.As<T, AboveGroundAgent>(ref source[item.ParentIndex]);
            var energyBefore = donor.Energy; var waterBefore = donor.Water_g;
            var reserveBefore = Plant.UnallocatedWater_g; double overflow = 0d;
            if (item.ReturnToSite)
            {
                donor.Energy += item.Body.Energy;
                donor.Water_g += item.Body.Water_g;
                overflow = donor.ReleaseWaterAboveCapacity();
                Plant.RetainWaterReserve(overflow);
            }
            receipts.Add(new(item.Id, item.Module.SiteOwnerStableId, item.Module.OriginPetioleStableId,
                item.ReturnToSite, item.Body.Energy, item.Body.Water_g, energyBefore, donor.Energy,
                waterBefore, donor.Water_g, overflow, reserveBefore, Plant.UnallocatedWater_g,
                item.Body.Volume(), ConstructionBudget.Cost(item.Fate.Template.Volume(), Plant.Parameters, item.Fate.Template.WoodRatio())));
            item.Fate.State = item.ReturnToSite ? TreeClosureState.WithdrawnReady : TreeClosureState.SiteLost;
            TreeModules.Remove(item.Id);
        }
        // Exactly one ordinary compaction. The unrelated death requests already
        // own their counters/descendants: remap their IDs, never call Death twice.
        Deaths.Clear();
        foreach (var item in batch) Deaths.Add(item.Index);
        Census();
        foreach (var id in originalDeaths)
            if (!removed.Contains(id))
            {
                var index = FindOrgan(id);
                if (index < 0) throw new InvalidOperationException("Module compaction removed an unrelated queued death.");
                Deaths.Add(index);
            }
        foreach (var item in batch)
            if (item.ReturnToSite && item.Fate.State == TreeClosureState.WithdrawnReady)
            {
                var owner = FindOrgan(item.Module.SiteOwnerStableId);
                if (!TreeClosureSiteAlive(owner, item.Module.OriginPetioleStableId)
                    || !GrowthRetries.TryGetValue(item.Module.SiteOwnerStableId, out var retry))
                    throw new InvalidOperationException("The returned owner/site or existing retry disappeared during module compaction.");
                // Earlier SetObstruction may have recorded a soft-pushed pose.
                // The final support rollback and Census now define the accepted
                // baseline. Preserve schedule, failures, attempted flag and all
                // organ state; only genuine LATER movement may bypass the clock.
                retry.LastPose = GetContactPose(owner);
            }
        foreach (var id in ids)
            if (FindOrgan(id) >= 0 || TreeModules.ContainsKey(id))
                throw new InvalidOperationException("The withdrawn module still occupies a live identity.");
        TreeModuleProvisionalWithdrawals += ids.Length;
        ++TreeModuleBarrierExecutions;
        ModuleWithdrawalNotices.Clear();
        TreeModuleWithdrawalIncomplete = false;
    }
}
