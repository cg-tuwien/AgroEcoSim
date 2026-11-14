using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Agro;

public partial class PlantSubFormation<T>
{
    Dictionary<ulong, TreeModuleRecord>? TreeModules;
    Dictionary<ulong, TreeClosureFate>? TreeClosureFates;
    HashSet<ulong>? ModuleWithdrawalNotices;
    bool TrainingReleased;
    uint TrainingReleaseTick;
    ulong TrainingWitnessStemStableId;
    float TrainingWitnessHeightM;
    bool TreeModuleWithdrawalIncomplete;
    internal List<TreeModuleWithdrawalReceipt>? ModuleWithdrawalReceipts;
    // Counts only completed removals/batches. The separate latch rejects every
    // interrupted notice, preflight or partially mutated transaction on restore.
    public long TreeModuleProvisionalWithdrawals { get; private set; }
    public long TreeModuleBarrierExecutions { get; private set; }
    internal bool TreeModulesEnabled => IsAboveGround && Plant.Parameters.TreeMainlineModulesEnabled;

    // A value enumerator keeps the disabled path allocation-free and visits
    // only sparse module metadata, never the full organ tree. The identity map
    // gives O(1) lookup for each actual normal-Census replacement root.
    internal FinalProvisionalTreeModuleRoots FinalProvisionalTreeModules() => new(this);

    internal struct FinalProvisionalTreeModuleRoots
    {
        readonly PlantSubFormation<T> Formation;
        Dictionary<ulong, TreeModuleRecord>.ValueCollection.Enumerator Modules;
        readonly bool Active;

        internal FinalProvisionalTreeModuleRoots(PlantSubFormation<T> formation)
        {
            Formation = formation;
            Active = formation.TreeModulesEnabled && formation.Plant.World.ContactEngine == ContactEngineKind.Mixed
                && formation.TreeModules != null;
            Modules = Active ? formation.TreeModules!.Values.GetEnumerator() : default;
        }

        internal bool MoveNext(out int index)
        {
            while (Active && Modules.MoveNext())
            {
                var module = Modules.Current;
                if (module.BirthState != TreeModuleBirthState.Provisional || module.ClosureFateId == 0
                    || !module.ImmediatePath || !module.CommittedByNormalCensus
                    || module.CreatedTick != Formation.Plant.World.Timestep
                    || module.BirthCommitTick != Formation.Plant.World.Timestep) continue;
                var i = Formation.FindOrgan(module.RootStableId);
                if (i < 0 || Formation.Deaths.Contains(i) || i >= Formation.PreviousGeometry.Length
                    || Formation.PreviousGeometry[i].Exists || Formation.GetChildren(i).Count != 0) continue;
                ref var root = ref Unsafe.As<T, AboveGroundAgent>(ref Formation.Src()[i]);
                if (root.TreeModuleRootStableId != module.RootStableId || root.BirthTime != Formation.Plant.World.Timestep
                    || root.Organ != OrganTypes.Bud || root.Parent < 0
                    || Formation.GetOrganId(root.Parent) != module.SiteOwnerStableId) continue;
                index = i;
                return true;
            }
            index = -1;
            return false;
        }
    }

    internal TreeModuleStep TreeModuleStepFor(in AboveGroundAgent agent)
    {
        if (!TreeModulesEnabled || agent.TreeModuleRootStableId == 0) return default;
        if (TreeModules == null || !TreeModules.TryGetValue(agent.TreeModuleRootStableId, out var module))
            throw new InvalidDataException("A tracked organ has no persistent module record.");
        return new(module.RootStableId, module.Kind, module.OpeningStartLocal,
            module.OpeningLengthM, module.FrozenAllowanceM);
    }

    internal bool IsDorsiventralModule(in AboveGroundAgent agent)
        => TreeModuleStepFor(agent).IsDorsiventral;

    internal void FreezeTreeModule(in AboveGroundAgent agent, float allowance, int cycle)
    {
        if (!TreeModulesEnabled || agent.TreeModuleRootStableId == 0) return;
        var module = TreeModules![agent.TreeModuleRootStableId];
        if (module.AllowanceCycle == cycle) return;
        if (!(allowance > 0f) || !float.IsFinite(allowance))
            throw new InvalidDataException("A module allowance must be finite and positive.");
        module.AllowanceCycle = cycle;
        module.FrozenAllowanceM = allowance;
        module.OpeningLengthM = module.Kind == TreeModuleKind.DorsiventralMainline
            ? allowance * Plant.Parameters.TreeMainlineOpeningFraction : 0f;
    }

    internal int BirthInitialTreeModule(AboveGroundAgent agent)
        => BirthTreeModule(agent, new() { Kind = TreeModuleKind.Trained,
            CreatedTick = Plant.World.Timestep, OpeningStartLocal = Quaternion.Identity });

    int BirthTreeModule(AboveGroundAgent agent, TreeModuleRecord module)
    {
        if (!TreeModulesEnabled || typeof(T) != typeof(AboveGroundAgent))
            throw new InvalidOperationException("Module births require the explicitly enabled above-ground tree formation.");
        if (agent.StableId != 0 || agent.TreeModuleRootStableId != 0)
            throw new InvalidOperationException("A new immediate module must not recycle an organ or module identity.");
        var value = Unsafe.As<AboveGroundAgent, T>(ref agent);
        var index = Birth(value); // assigns the ordinary ID to its queued VALUE copy
        ref var queued = ref CollectionsMarshal.AsSpan(Births)[index - AgentCount];
        ref var born = ref Unsafe.As<T, AboveGroundAgent>(ref queued);
        born.TreeModuleRootStableId = born.StableId;
        module.RootStableId = born.StableId;
        module.BirthState = TreeModuleBirthState.Queued;
        module.ImmediatePath = !Plant.World.StagesDevelopment
            || !Collisions.CollisionSettings.Enabled && Plant.Parameters.ConstructionEnergyPerDryGram == 0f;
        (TreeModules ??= []).Add(module.RootStableId, module);
        return index;
    }

    // Called after the EXISTING normal Census has rebuilt its identity map.
    void ObserveTreeModuleCensus()
    {
        if (!TreeModulesEnabled || TreeModules == null) return;
        foreach (var module in TreeModules.Values)
            if (module.BirthState == TreeModuleBirthState.Queued && FindOrgan(module.RootStableId) >= 0)
            {
                module.BirthState = TreeModuleBirthState.Provisional;
                module.BirthCommitTick = Plant.World.Timestep;
                module.CommittedByNormalCensus = module.ImmediatePath;
            }
    }

    bool RealModuleSiteEligible(int owner, ulong petioleId, ulong allowedAttempt = 0)
    {
        var src = Src();
        if (owner < 0 || Deaths.Contains(owner)) return false;
        var petiole = FindOrgan(petioleId);
        if (petiole < 0 || Deaths.Contains(petiole) || src[petiole].Parent != owner
            || src[petiole].Organ != OrganTypes.Petiole) return false;
        foreach (var child in GetChildren(owner))
        {
            if (src[child].StableId == allowedAttempt || Deaths.Contains(child)) continue;
            var kind = src[child].Organ;
            if (kind is OrganTypes.Bud or OrganTypes.Meristem or OrganTypes.Stem
                or OrganTypes.FlowerStem or OrganTypes.FlowerPedicel or OrganTypes.FlowerPadel
                or OrganTypes.FlowerBud or OrganTypes.FlowerMeristem or OrganTypes.FlowerBaseBud
                or OrganTypes.Fruit or OrganTypes.FruitWing) return false;
        }
        return true;
    }

    internal bool TryCloseTreeModule(ref AboveGroundAgent owner, int index, int cycle)
    {
        if (!TreeModulesEnabled || owner.TreeModuleRootStableId == 0) return false;
        if (owner.GrowthBlocked || HasPendingDevelopment(index)) return true;
        if (TreeClosureFates != null && TreeClosureFates.TryGetValue(owner.StableId, out var previous))
        {
            return true; // a fixed sampled site is never reselected on retry or loss
        }
        var source = Src();
        var best = -1;
        var distal = float.NegativeInfinity;
        var axis = Vector3.Transform(Vector3.UnitX, owner.Orientation);
        foreach (var child in GetChildren(index))
        {
            if (source[child].Organ != OrganTypes.Petiole || Deaths.Contains(child)) continue;
            var offset = Vector3.Dot(source[child].BaseOffset, axis);
            if (best < 0 || offset > distal) { best = child; distal = offset; }
        }
        if (best < 0 || !RealModuleSiteEligible(index, source[best].StableId)) return true;
        var kind = TrainingReleased && TrainingReleaseTick < Plant.World.Timestep
            ? TreeModuleKind.DorsiventralMainline : TreeModuleKind.Trained;
        var parentDorsiventral = IsDorsiventralModule(owner);
        var roll = owner.TreeModuleSiteRoll(Plant.Parameters, parentDorsiventral);
        var ranked = owner.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll);
        var side = -Vector3.Transform(Vector3.UnitY, ranked);
        var rawAxil = ranked * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -Plant.Parameters.LateralPitch);
        var axilDirection = Vector3.Transform(Vector3.UnitX, rawAxil);
        var petioleDirection = Vector3.Transform(Vector3.UnitX, source[best].Orientation);
        var transverse = petioleDirection - axis * Vector3.Dot(petioleDirection, axis);
        if (transverse.LengthSquared() < 1e-10f || Vector3.Dot(transverse, side) <= 0f) return true;
        Quaternion reference;
        if (kind == TreeModuleKind.DorsiventralMainline)
        {
            var h = new Vector3(axilDirection.X, 0f, axilDirection.Z);
            if (h.LengthSquared() < 1e-10f) return true; // no invented world bearing
            var beta = Plant.Parameters.TreeMainlineReferenceInclinationRadians;
            var target = Vector3.Normalize(h) * MathF.Sin(beta) + Vector3.UnitY * MathF.Cos(beta);
            reference = AboveGroundAgent.TurnUpwards(AboveGroundAgent.TreeFrameToward(owner.Orientation, target));
        }
        else reference = owner.CaptureTreeUnitFrame();
        var appressedAxis = Vector3.Normalize(axis + side * .1f);
        var budFrame = AboveGroundAgent.TreeFrameToward(reference, appressedAxis);
        var opening = Quaternion.Normalize(Quaternion.Conjugate(reference) * budFrame);
        var siteOffset = source[best].BaseOffset + side * (owner.Radius + .0004f) - axis * .001f;
        var ag = (PlantSubFormation<AboveGroundAgent>)(object)this;
        var bud = owner.SampleTreeModuleBud(Plant, index, budFrame, reference, siteOffset, cycle);
        var fate = new TreeClosureFate { FateId = owner.StableId,
            PredecessorRootStableId = owner.TreeModuleRootStableId, SiteOwnerStableId = owner.StableId,
            OriginPetioleStableId = source[best].StableId, SampledTick = Plant.World.Timestep,
            AttemptTick = Plant.World.Timestep, SampledShootCycle = cycle, AttemptCount = 1,
            SiteRollRadians = roll, OwnerFrameAtSample = owner.Orientation,
            TargetFrameInOwnerLocal = Quaternion.Normalize(Quaternion.Conjugate(owner.Orientation) * reference),
            OpeningStartLocal = opening, SiteOffsetOwnerLocal = Vector3.Transform(siteOffset, Quaternion.Conjugate(owner.Orientation)),
            Kind = kind, State = TreeClosureState.Sampled, Template = bud };
        fate.Template.Energy = fate.Template.Water_g = 0f;
        var module = new TreeModuleRecord { Kind = kind, ClosureFateId = fate.FateId,
            PredecessorRootStableId = fate.PredecessorRootStableId, SiteOwnerStableId = fate.SiteOwnerStableId,
            OriginPetioleStableId = fate.OriginPetioleStableId, CreatedTick = Plant.World.Timestep,
            SiteRollRadians = roll, TargetFrameInOwnerLocal = fate.TargetFrameInOwnerLocal, OpeningStartLocal = opening };
        var queuedIndex = BirthTreeModule(bud, module);
        fate.AttemptRootStableId = Births[queuedIndex - AgentCount].StableId;
        fate.State = module.ImmediatePath ? TreeClosureState.ImmediateAttempt : TreeClosureState.Sampled;
        (TreeClosureFates ??= []).Add(fate.FateId, fate);
        owner.CommitTreeModuleClosure();
        return true;
    }

    void CaptureModuleDevelopment(Development proposal)
    {
        if (!TreeModulesEnabled) return;
        proposal.NewModuleRootStableIds = proposal.Children.Where(c => c.TreeModuleRootStableId != 0
            && c.TreeModuleRootStableId == c.StableId).Select(c => c.StableId).ToArray();
        foreach (var id in proposal.NewModuleRootStableIds)
        {
            var module = TreeModules![id];
            module.BirthState = TreeModuleBirthState.CertifiedPending;
            if (module.ClosureFateId != 0)
            {
                proposal.FixedTreeAxil = true;
                TreeClosureFates![module.ClosureFateId].State = TreeClosureState.CertifiedPending;
            }
        }
    }

    bool ModuleDevelopmentSiteEligible(Development proposal)
    {
        if (!proposal.FixedTreeAxil) return true;
        foreach (var id in proposal.NewModuleRootStableIds)
        {
            var m = TreeModules![id];
            if (m.ClosureFateId != 0 && !RealModuleSiteEligible(FindOrgan(m.SiteOwnerStableId), m.OriginPetioleStableId)) return false;
        }
        return true;
    }

    void CommitModuleDevelopment(Development proposal)
    {
        foreach (var id in proposal.NewModuleRootStableIds)
            TreeModules![id].BirthState = TreeModuleBirthState.Queued;
    }

    void CancelModuleDevelopment(Development proposal, bool ownerDied)
    {
        foreach (var id in proposal.NewModuleRootStableIds)
        {
            if (TreeModules == null || !TreeModules.Remove(id, out var module)) continue;
            if (module.ClosureFateId == 0 || TreeClosureFates == null) continue;
            if (ownerDied) TreeClosureFates.Remove(module.ClosureFateId);
            else TreeClosureFates[module.ClosureFateId].State = TreeClosureState.SiteLost;
        }
    }

    void NoticeTreeModuleDeath(int index, OrganDeathReason reason)
    {
        if (!TreeModulesEnabled || reason != OrganDeathReason.Obstruction || TreeModules == null) return;
        var id = Src()[index].StableId;
        if (TreeModules.TryGetValue(id, out var module) && module.BirthState == TreeModuleBirthState.Provisional
            && module.ImmediatePath && module.CreatedTick == Plant.World.Timestep)
            if ((ModuleWithdrawalNotices ??= []).Add(id))
                TreeModuleWithdrawalIncomplete = true; // BEFORE any throwable preflight or mutation
    }

    internal void FinalizeTreeModuleBookkeeping()
    {
        if (!TreeModulesEnabled) return;
        if (TreeModuleWithdrawalIncomplete || ModuleWithdrawalNotices is { Count: > 0 })
            CommitTreeModuleWithdrawals();
        ObserveLostTreeClosureSites();
        if (TreeModules != null)
            foreach (var module in TreeModules.Values)
            {
                if (module.BirthState != TreeModuleBirthState.Provisional) continue;
                var index = FindOrgan(module.RootStableId);
                if (index < 0 || Deaths.Contains(index)) continue;
                module.BirthState = TreeModuleBirthState.Live;
                module.AdmittedTick = Plant.World.Timestep;
                RecordSecondaryCompletion(module);
                if (module.ClosureFateId != 0) TreeClosureFates?.Remove(module.ClosureFateId);
            }
        FreezeSecondaryCompletions();
        if (TrainingReleased || TreeModules == null) return;
        var live = Src();
        for (int i = 0; i < live.Length; ++i)
        {
            ref var agent = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            if (agent.TreeModuleRootStableId == 0 || agent.IsRizome || agent.Organ != OrganTypes.Stem
                || Deaths.Contains(i) || TreeModules[agent.TreeModuleRootStableId].Kind != TreeModuleKind.Trained) continue;
            var height = GetTipPosition(i).Y - Plant.WoodyGroundHeight;
            if (height < Plant.Parameters.EffectiveTreeMainlineTrainingHeightM) continue;
            TrainingReleased = true; TrainingReleaseTick = Plant.World.Timestep;
            TrainingWitnessStemStableId = agent.StableId; TrainingWitnessHeightM = height;
            break;
        }
    }

    void WriteTreeModuleState(BinaryWriter w)
    {
        WriteTreeModuleControls(w);
        w.Write(TrainingReleased); w.Write(TrainingReleaseTick); w.Write(TrainingWitnessStemStableId); w.Write(TrainingWitnessHeightM);
        w.Write(TreeModuleProvisionalWithdrawals);
        w.Write(TreeModuleBarrierExecutions); w.Write(TreeModuleWithdrawalIncomplete);
        w.Write(TreeModules?.Count ?? 0);
        if (TreeModules != null) foreach (var m in TreeModules.Values.OrderBy(m => m.RootStableId))
        {
            w.Write(m.RootStableId); w.Write(m.ClosureFateId); w.Write(m.PredecessorRootStableId); w.Write(m.SiteOwnerStableId); w.Write(m.OriginPetioleStableId);
            w.Write((byte)m.Kind); w.Write((byte)m.BirthState); w.Write(m.CreatedTick); w.Write(m.BirthCommitTick); w.Write(m.AdmittedTick);
            w.Write(m.ImmediatePath); w.Write(m.CommittedByNormalCensus); w.Write(m.SiteRollRadians);
            WriteValue(w,m.TargetFrameInOwnerLocal); WriteValue(w,m.OpeningStartLocal);
            w.Write(m.OpeningLengthM); w.Write(m.FrozenAllowanceM); w.Write(m.AllowanceCycle);
        }
        w.Write(TreeClosureFates?.Count ?? 0);
        if (TreeClosureFates != null) foreach (var f in TreeClosureFates.Values.OrderBy(f => f.FateId))
        {
            w.Write(f.FateId); w.Write(f.PredecessorRootStableId); w.Write(f.SiteOwnerStableId); w.Write(f.OriginPetioleStableId); w.Write(f.AttemptRootStableId);
            w.Write(f.SampledTick); w.Write(f.AttemptTick); w.Write(f.SampledShootCycle); w.Write(f.AttemptCount); w.Write(f.SiteRollRadians);
            WriteValue(w,f.OwnerFrameAtSample); WriteValue(w,f.TargetFrameInOwnerLocal); WriteValue(w,f.OpeningStartLocal); WriteValue(w,f.SiteOffsetOwnerLocal);
            w.Write((byte)f.Kind); w.Write((byte)f.State); WriteValue(w,f.Template);
        }
    }

    void ReadTreeModuleState(BinaryReader r)
    {
        ReadTreeModuleControls(r);
        TrainingReleased=r.ReadBoolean(); TrainingReleaseTick=r.ReadUInt32(); TrainingWitnessStemStableId=r.ReadUInt64(); TrainingWitnessHeightM=r.ReadSingle();
        TreeModuleProvisionalWithdrawals=r.ReadInt64(); TreeModuleBarrierExecutions=r.ReadInt64(); TreeModuleWithdrawalIncomplete=r.ReadBoolean();
        ModuleWithdrawalNotices=null; ModuleWithdrawalReceipts=null; ModuleMaterialFrames=null; TreeModules=null; TreeClosureFates=null;
        var n=r.ReadInt32(); if(n<0)throw new InvalidDataException("Negative module count.");
        if(n>0)TreeModules=[];
        for(int i=0;i<n;++i)
        {
            var m=new TreeModuleRecord {RootStableId=r.ReadUInt64(),ClosureFateId=r.ReadUInt64(),PredecessorRootStableId=r.ReadUInt64(),SiteOwnerStableId=r.ReadUInt64(),OriginPetioleStableId=r.ReadUInt64(),
                Kind=(TreeModuleKind)r.ReadByte(),BirthState=(TreeModuleBirthState)r.ReadByte(),CreatedTick=r.ReadUInt32(),BirthCommitTick=r.ReadUInt32(),AdmittedTick=r.ReadUInt32(),
                ImmediatePath=r.ReadBoolean(),CommittedByNormalCensus=r.ReadBoolean(),SiteRollRadians=r.ReadSingle(),TargetFrameInOwnerLocal=ReadValue<Quaternion>(r),OpeningStartLocal=ReadValue<Quaternion>(r),
                OpeningLengthM=r.ReadSingle(),FrozenAllowanceM=r.ReadSingle(),AllowanceCycle=r.ReadInt32()};
            TreeModules!.Add(m.RootStableId,m);
        }
        n=r.ReadInt32();if(n<0)throw new InvalidDataException("Negative closure fate count.");
        if(n>0)TreeClosureFates=[];
        for(int i=0;i<n;++i)
        {
            var f=new TreeClosureFate {FateId=r.ReadUInt64(),PredecessorRootStableId=r.ReadUInt64(),SiteOwnerStableId=r.ReadUInt64(),OriginPetioleStableId=r.ReadUInt64(),AttemptRootStableId=r.ReadUInt64(),
                SampledTick=r.ReadUInt32(),AttemptTick=r.ReadUInt32(),SampledShootCycle=r.ReadInt32(),AttemptCount=r.ReadInt32(),SiteRollRadians=r.ReadSingle(),OwnerFrameAtSample=ReadValue<Quaternion>(r),
                TargetFrameInOwnerLocal=ReadValue<Quaternion>(r),OpeningStartLocal=ReadValue<Quaternion>(r),SiteOffsetOwnerLocal=ReadValue<Vector3>(r),Kind=(TreeModuleKind)r.ReadByte(),State=(TreeClosureState)r.ReadByte(),Template=ReadValue<AboveGroundAgent>(r)};
            TreeClosureFates!.Add(f.FateId,f);
        }
        if(!TreeModulesEnabled&&(TrainingReleased||TreeModules!=null||TreeClosureFates!=null))
            throw new InvalidDataException("A disabled request cannot restore enabled module history.");
        ValidateTreeModuleState();
    }

    void WriteTreeModuleControls(BinaryWriter w)
    {
        w.Write(TreeModulesEnabled);
        if (!TreeModulesEnabled) return;
        w.Write(Plant.Parameters.EffectiveTreeMainlineTrainingHeightM);
        w.Write(Plant.Parameters.TreeMainlineReferenceInclinationDegrees);
        w.Write(Plant.Parameters.TreeMainlineOpeningFraction);
    }

    void ReadTreeModuleControls(BinaryReader r)
    {
        if (r.ReadBoolean() != TreeModulesEnabled)
            throw new InvalidDataException("Mainline model differs from its checkpoint.");
        if (!TreeModulesEnabled) return;
        static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        float training = r.ReadSingle(), inclination = r.ReadSingle(), opening = r.ReadSingle();
        if (!Same(training, Plant.Parameters.EffectiveTreeMainlineTrainingHeightM)
            || !Same(inclination, Plant.Parameters.TreeMainlineReferenceInclinationDegrees)
            || !Same(opening, Plant.Parameters.TreeMainlineOpeningFraction))
            throw new InvalidDataException("Mainline model coefficients differ from their checkpoint.");
    }
}
