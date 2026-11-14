using System.Numerics;
using System.Runtime.CompilerServices;

namespace Agro;

public partial class PlantSubFormation<T>
{
    sealed record SecondaryModule(ulong Root, ulong Successor, uint SuccessorCommitTick, int MaterialClosureCycle,
        float FormedLengthM, float ProximalEndM);
    sealed record SecondaryCompletion(ulong Root, ulong Successor, uint Tick, int Cycle);
    sealed class SecondaryWood
    {
        internal ulong Organ, Module;
        internal int EligibleCycle;
        internal float MaterialStartM, ReactingLengthM;
        internal double AreaM2, CurvaturePathPerM;
    }
    sealed class SecondaryStep
    {
        internal required SecondaryWood Wood;
        internal float Radius0, ProposedRadius, CurrentRadius, StrainAmplitude;
        internal Vector3 FrozenDrive;
        internal double AreaBefore, PathBefore;
        internal AboveGroundAgent.TreeModuleMaterialFrame Before, Proposed;
        internal bool HasFramePair;
    }
    readonly record struct SecondarySnapshot(bool Known, double Area, double Path, float Radius);
    readonly record struct SecondaryKey(ulong Organ, GeometryState Geometry);
    sealed class SecondaryFrames
    {
        internal AboveGroundAgent.TreeModuleMaterialFrame Before, Current;
        internal Quaternion Carry;
    }
    Dictionary<ulong, SecondaryModule>? SecondaryModules;
    Dictionary<ulong, SecondaryCompletion>? SecondaryCompletions;
    Dictionary<ulong, SecondaryWood>? SecondaryWoods;
    Dictionary<ulong, SecondaryStep>? SecondarySteps;
    Dictionary<SecondaryKey, SecondarySnapshot>? SecondarySnapshots;
    Dictionary<ulong, SecondaryFrames>? SecondaryFramePairs;
    Quaternion[] SecondaryCarries = [];
    Queue<int>? SecondaryWalk;
    bool SecondaryTransactionIncomplete;
    bool SecondaryEnabled => IsAboveGround && Plant.Parameters.TreeSecondaryErectionEnabled;

    void BeginSecondaryTick()
    {
        if (!SecondaryEnabled) return;
        if (SecondaryTransactionIncomplete) throw new InvalidOperationException("An unfinished secondary material transaction cannot tick again.");
        SecondarySteps?.Clear(); SecondarySnapshots?.Clear(); SecondaryFramePairs?.Clear();
    }

    // A real accepted successor establishes completion of the previous mainline
    // module. Following-cycle activation is an explicit trial rule, not botany.
    void RecordSecondaryCompletion(TreeModuleRecord successor)
    {
        if (!SecondaryEnabled || successor.ClosureFateId == 0 || TreeModules == null
            || !TreeModules.TryGetValue(successor.PredecessorRootStableId, out var previous)
            || previous.Kind != TreeModuleKind.DorsiventralMainline) return;
        if (SecondaryModules?.ContainsKey(previous.RootStableId) == true) return;
        if (!(previous.FrozenAllowanceM > 0f) || previous.AllowanceCycle == int.MinValue
            || TreeClosureFates == null || !TreeClosureFates.TryGetValue(successor.ClosureFateId, out var fate))
            throw new InvalidDataException("Secondary eligibility requires an actual completed finite module.");
        (SecondaryCompletions ??= []).TryAdd(previous.RootStableId, new(previous.RootStableId, successor.RootStableId,
            Plant.World.Timestep, fate.SampledShootCycle));
        SecondaryTransactionIncomplete = true;
    }

    // Freeze actual accepted material, not the annual allowance: a real module
    // can close at season end below that allowance. All simultaneous completion
    // events share this one linear topology pass, with no per-module ancestor walk.
    void FreezeSecondaryCompletions()
    {
        if (!SecondaryEnabled || SecondaryCompletions is not { Count: > 0 }) return;
        var live = Src(); var starts = new float[live.Length]; var ends = new float[live.Length];
        var pieces = new Dictionary<ulong, List<(int Index, float Start)>>();
        var lengths = new Dictionary<ulong, float>();
        var walk = SecondaryWalk ??= new(); walk.Clear(); foreach (var root in GetRoots()) walk.Enqueue(root);
        while (walk.TryDequeue(out int i))
        {
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            if (SecondaryCompletions.ContainsKey(a.TreeModuleRootStableId) && !Deaths.Contains(i))
            {
                if (a.IsRizome || a.Organ != OrganTypes.Stem || !(a.Length > 0f))
                    throw new InvalidDataException("A completed secondary module must contain accepted perennial mainline wood.");
                if (a.Parent >= 0 && Unsafe.As<T, AboveGroundAgent>(ref live[a.Parent]).TreeModuleRootStableId == a.TreeModuleRootStableId)
                    starts[i] = ends[a.Parent];
                ends[i] = starts[i] + a.Length;
                if (!float.IsFinite(ends[i])) throw new InvalidDataException("Nonfinite completed secondary material extent.");
                if (!pieces.TryGetValue(a.TreeModuleRootStableId, out var rows)) pieces.Add(a.TreeModuleRootStableId, rows = []);
                rows.Add((i, starts[i])); lengths[a.TreeModuleRootStableId] = MathF.Max(lengths.GetValueOrDefault(a.TreeModuleRootStableId), ends[i]);
                int continuations = 0;
                foreach (var child in GetChildren(i))
                    if (!Deaths.Contains(child) && Unsafe.As<T, AboveGroundAgent>(ref live[child]).TreeModuleRootStableId == a.TreeModuleRootStableId) ++continuations;
                if (continuations > 1) throw new InvalidDataException("Secondary completion requires a single material mainline, not an identity-sharing fork.");
            }
            foreach (var child in GetChildren(i)) walk.Enqueue(child);
        }
        foreach (var completion in SecondaryCompletions.Values.OrderBy(c => c.Root))
        {
            if (!lengths.TryGetValue(completion.Root, out var length) || !(length > 0f))
                throw new InvalidDataException("Accepted successor has no surviving completed predecessor material.");
            float zone = length * Plant.Parameters.TreeSecondaryProximalFraction;
            (SecondaryModules ??= []).Add(completion.Root, new(completion.Root, completion.Successor, completion.Tick,
                completion.Cycle, length, zone));
            foreach (var (index, start) in pieces[completion.Root])
            {
                ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[index]);
                float reacting = MathF.Min(a.Length, zone - start); if (!(reacting > 0f)) continue;
                (SecondaryWoods ??= []).Add(a.StableId, new() { Organ = a.StableId, Module = completion.Root,
                    EligibleCycle = checked(completion.Cycle + 1), MaterialStartM = start, ReactingLengthM = reacting });
            }
        }
        SecondaryCompletions.Clear();
    }

    void ProposeSecondaryMaterials()
    {
        if (!SecondaryEnabled || SecondaryModules is not { Count: > 0 }) return;
        var live = Src(); var cycle = AboveGroundAgent.SecondaryCycle(Plant.World, Plant.World.Timestep);
        for (int i = 0; i < live.Length; ++i)
        {
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            if (a.Organ != OrganTypes.Stem || a.IsRizome || Deaths.Contains(i)
                || !SecondaryModules.TryGetValue(a.TreeModuleRootStableId, out var module)
                || cycle <= module.MaterialClosureCycle || !PreviousGeometry[i].Exists) continue;
            float radius0 = PreviousGeometry[i].Radius;
            if (a.Radius <= radius0) continue;
            if (!(radius0 > 0f)) throw new InvalidDataException("Secondary wood must have a positive pre-existing radius.");
            if (SecondaryWoods == null || !SecondaryWoods.TryGetValue(a.StableId, out var wood)) continue;
            // An axillary insertion between modules is not material curvature.
            var parentFrame = a.Orientation;
            if (a.Parent >= 0)
            {
                ref var parent = ref Unsafe.As<T, AboveGroundAgent>(ref live[a.Parent]);
                if (parent.TreeModuleRootStableId == a.TreeModuleRootStableId) parentFrame = parent.Orientation;
            }
            var drive = SecondaryErectionProgram.FrozenDrive(a.Orientation, parentFrame,
                a.baseOrientation, a.Length, Plant.Parameters.TreeSecondaryProprioceptiveLengthM);
            _ = SecondaryErectionProgram.At(radius0, a.Radius, wood.ReactingLengthM,
                Plant.Parameters.TreeSecondaryMaturationStrainAmplitude, drive); // validate before any frame changes
            (SecondarySteps ??= []).Add(a.StableId, new() { Wood = wood, Radius0 = radius0, ProposedRadius = a.Radius,
                StrainAmplitude = Plant.Parameters.TreeSecondaryMaturationStrainAmplitude,
                CurrentRadius = radius0, FrozenDrive = drive, AreaBefore = wood.AreaM2, PathBefore = wood.CurvaturePathPerM });
        }
        if (SecondarySteps is not { Count: > 0 }) return;
        SecondaryTransactionIncomplete = true;
        if (SecondaryCarries.Length < live.Length) Array.Resize(ref SecondaryCarries, live.Length);
        var walk = SecondaryWalk ??= new();
        walk.Clear(); foreach (var root in GetRoots()) walk.Enqueue(root);
        while (walk.TryDequeue(out int i))
        {
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            var carrier = a.Parent >= 0 ? SecondaryCarries[a.Parent] : Quaternion.Identity;
            var beforeFrames = a.CaptureTreeModuleMaterial();
            a.TransportSecondaryMaterial(carrier, carrier: true);
            var own = Quaternion.Identity;
            if (SecondarySteps.TryGetValue(a.StableId, out var step))
            {
                step.Before = a.CaptureTreeModuleMaterial();
                var end = Endpoint(step, step.ProposedRadius);
                own = SecondaryErectionProgram.Delta(a.baseOrientation, Quaternion.Identity, end.Turn);
                a.TransportSecondaryMaterial(own, carrier: false);
                CommitEndpoint(step, step.ProposedRadius, end);
                step.Proposed = a.CaptureTreeModuleMaterial(); step.HasFramePair = true;
            }
            SecondaryCarries[i] = own == Quaternion.Identity ? carrier
                : carrier == Quaternion.Identity ? own : Quaternion.Normalize(own * carrier);
            if (SecondaryCarries[i] != Quaternion.Identity || SecondarySteps.ContainsKey(a.StableId))
                (SecondaryFramePairs ??= [])[a.StableId] = new() { Before = beforeFrames,
                    Current = a.CaptureTreeModuleMaterial(), Carry = SecondaryCarries[i] };
            foreach (var child in GetChildren(i)) walk.Enqueue(child);
        }
        // Immediate births were sampled before this pass. Pending Certified
        // blueprints remain immutable: admission already derives their carrier
        // from the accepted current owner, and carrying them here would double it.
        var birthCarries = SecondaryBirthCarries ??= [];
        birthCarries.Clear();
        for (int j = 0; j < Births.Count; ++j)
        {
            var born = Births[j]; ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref born);
            var beforeFrames = a.CaptureTreeModuleMaterial();
            int p = a.Parent;
            var carrier = Quaternion.Identity;
            if (p >= 0)
            {
                if (p >= live.Length + j) throw new InvalidDataException("Secondary newborns require parent-before-child admission order.");
                carrier = p < live.Length ? SecondaryCarries[p] : birthCarries[p - live.Length];
                a.TransportSecondaryMaterial(carrier, carrier: true);
            }
            birthCarries.Add(carrier);
            if (carrier != Quaternion.Identity)
                (SecondaryFramePairs ??= [])[a.StableId] = new() { Before = beforeFrames,
                    Current = a.CaptureTreeModuleMaterial(), Carry = carrier };
            Births[j] = born;
        }
        TreeCache.UpdateBases(this);
    }
    List<Quaternion>? SecondaryBirthCarries;

    SecondaryErectionProgram.Endpoint Endpoint(SecondaryStep step, float radius)
        => SecondaryErectionProgram.At(step.Radius0, radius, step.Wood.ReactingLengthM,
            step.StrainAmplitude, step.FrozenDrive);
    static void CommitEndpoint(SecondaryStep step, float radius, SecondaryErectionProgram.Endpoint end)
    {
        step.Wood.AreaM2 = radius == step.Radius0 ? step.AreaBefore : step.AreaBefore + end.AreaM2;
        step.Wood.CurvaturePathPerM = end.CurvaturePathPerM == 0d ? step.PathBefore : step.PathBefore + end.CurvaturePathPerM;
        step.CurrentRadius = radius;
    }

    bool HasSecondaryStep(ulong id) => SecondaryEnabled && SecondarySteps?.ContainsKey(id) == true;

    void ApplySecondaryTrial(int index, float radius)
    {
        var live = Src(); ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[index]);
        if (!SecondaryEnabled || SecondarySteps == null || !SecondarySteps.TryGetValue(a.StableId, out var step)) return;
        if (radius < step.Radius0 || radius > step.ProposedRadius)
            throw new InvalidDataException("A secondary trial must use its admitted radial interval.");
        if (radius == step.CurrentRadius) return;
        // If every affected tuple is still the last programme-owned tuple,
        // rebuild from its exact pre-programme values. At zero total turn this
        // also restores untouched descendants bit-for-bit, without one subtree
        // snapshot per acting ancestor. Any external carry uses inverse transport.
        if (TryRebuildSecondaryTrial(index, radius, step)) return;
        var before = Endpoint(step, step.CurrentRadius); var end = Endpoint(step, radius);
        bool exactZero = radius == step.Radius0 && step.HasFramePair && a.CaptureTreeModuleMaterial().SameBits(step.Proposed);
        var delta = SecondaryErectionProgram.Delta(a.baseOrientation, before.Turn, end.Turn);
        a.TransportSecondaryMaterial(delta, carrier: false);
        if (delta != Quaternion.Identity) PropagateToSubtree(live, index, delta);
        if (exactZero)
            a.RestoreTreeModuleMaterial(step.Before, a.CaptureTreeModuleLocalMaterial(), absoluteFrames: true);
        CommitEndpoint(step, radius, end);
    }

    bool TryRebuildSecondaryTrial(int index, float radius, SecondaryStep changing)
    {
        if (SecondaryFramePairs == null) return false;
        var live = Src(); var walk = SecondaryWalk ??= new();
        int outsideParent = live[index].Parent;
        if (outsideParent >= 0 && SecondaryFramePairs.TryGetValue(live[outsideParent].StableId, out var outside)
            && !Unsafe.As<T, AboveGroundAgent>(ref live[outsideParent]).CaptureTreeModuleMaterial().SameBits(outside.Current)) return false;
        walk.Clear(); walk.Enqueue(index);
        var nodes = SecondaryTrialNodes ??= [];
        nodes.Clear();
        while (walk.TryDequeue(out int i))
        {
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            if (!SecondaryFramePairs.TryGetValue(a.StableId, out var f)
                || !a.CaptureTreeModuleMaterial().SameBits(f.Current)) return false;
            nodes.Add(i); foreach (var child in GetChildren(i)) walk.Enqueue(child);
        }
        var accepted = Endpoint(changing, radius);
        CommitEndpoint(changing, radius, accepted);
        foreach (int i in nodes)
        {
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref live[i]);
            var f = SecondaryFramePairs[a.StableId];
            var carrier = a.Parent >= 0 && SecondaryFramePairs.TryGetValue(live[a.Parent].StableId, out var parent)
                ? parent.Carry : Quaternion.Identity;
            a.RestoreTreeModuleMaterial(f.Before, a.CaptureTreeModuleLocalMaterial(), absoluteFrames: true);
            a.TransportSecondaryMaterial(carrier, carrier: true);
            var own = Quaternion.Identity;
            if (SecondarySteps!.TryGetValue(a.StableId, out var step))
            {
                var end = Endpoint(step, step.CurrentRadius);
                own = SecondaryErectionProgram.Delta(a.baseOrientation, Quaternion.Identity, end.Turn);
                a.TransportSecondaryMaterial(own, carrier: false);
            }
            f.Carry = own == Quaternion.Identity ? carrier
                : carrier == Quaternion.Identity ? own : Quaternion.Normalize(own * carrier);
            f.Current = a.CaptureTreeModuleMaterial();
        }
        return true;
    }
    List<int>? SecondaryTrialNodes;

    void RememberSecondarySnapshot(ulong id, in GeometryState geometry)
    {
        if (!SecondaryEnabled) return;
        SecondaryWood? wood = null; SecondaryWoods?.TryGetValue(id, out wood);
        if (wood == null && SecondarySteps?.ContainsKey(id) != true) return;
        (SecondarySnapshots ??= [])[new(id, geometry)] = new(wood != null, wood?.AreaM2 ?? 0d,
            wood?.CurvaturePathPerM ?? 0d, geometry.Radius);
    }
    void RestoreSecondarySnapshot(ulong id, in GeometryState geometry)
    {
        if (!SecondaryEnabled || SecondaryWoods == null || !SecondaryWoods.TryGetValue(id, out var wood)) return;
        if (SecondarySnapshots != null && SecondarySnapshots.TryGetValue(new(id, geometry), out var snapshot))
        {
            wood.AreaM2 = snapshot.Area; wood.CurvaturePathPerM = snapshot.Path;
            if (SecondarySteps?.TryGetValue(id, out var found) == true) found.CurrentRadius = snapshot.Radius;
        }
        else if (SecondarySteps?.TryGetValue(id, out var step) == true && geometry.Radius == step.Radius0)
        {
            wood.AreaM2 = step.AreaBefore; wood.CurvaturePathPerM = step.PathBefore; step.CurrentRadius = step.Radius0;
        }
        else throw new InvalidDataException("Secondary material snapshot is unavailable for an exact frame restore.");
    }

    // Called by Seal only after support/development trials. No orientation moves
    // occur here; both the ledger and any escrow use the already-tested radius.
    internal void CompleteSecondaryMaterials()
    {
        if (!SecondaryEnabled) return;
        if (SecondaryCompletions is { Count: > 0 }) throw new InvalidOperationException("Secondary eligibility has not finished its material freeze.");
        if (SecondarySteps is not { Count: > 0 }) { SecondaryTransactionIncomplete = false; return; }
        double water = 0d;
        foreach (var step in SecondarySteps.Values)
        {
            int index = FindOrgan(step.Wood.Organ);
            if (index < 0 || Deaths.Contains(index))
            { step.Wood.AreaM2 = step.AreaBefore; step.Wood.CurvaturePathPerM = step.PathBefore; continue; }
            ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref Src()[index]);
            if (a.Radius != step.CurrentRadius) throw new InvalidDataException("A radial setter bypassed secondary endpoint admission.");
            if (Plant.Parameters.ConstructionEnergyPerDryGram > 0f) a.SettleConstruction();
            water += a.ReleaseWaterAboveCapacity();
        }
        Plant.RetainWaterReserve(water);
        SecondaryTransactionIncomplete = false; SecondarySteps.Clear();
    }

    void WriteSecondaryState(BinaryWriter w)
    {
        if (!IsAboveGround) return;
        WriteSecondaryControls(w, SecondaryEnabled, Plant.Parameters.TreeSecondaryMaturationStrainAmplitude,
            Plant.Parameters.TreeSecondaryProximalFraction, Plant.Parameters.TreeSecondaryProprioceptiveLengthM);
        w.Write(SecondaryTransactionIncomplete); w.Write(SecondaryModules?.Count ?? 0);
        if (SecondaryModules != null) foreach (var m in SecondaryModules.Values.OrderBy(m => m.Root))
        { w.Write(m.Root); w.Write(m.Successor); w.Write(m.SuccessorCommitTick); w.Write(m.MaterialClosureCycle); w.Write(m.FormedLengthM); w.Write(m.ProximalEndM); }
        w.Write(SecondaryWoods?.Count ?? 0);
        if (SecondaryWoods != null) foreach (var s in SecondaryWoods.Values.OrderBy(s => s.Organ))
        { w.Write(s.Organ); w.Write(s.Module); w.Write(s.EligibleCycle); w.Write(s.MaterialStartM); w.Write(s.ReactingLengthM); w.Write(s.AreaM2); w.Write(s.CurvaturePathPerM); }
    }
    void ReadSecondaryState(BinaryReader r)
    {
        if (!IsAboveGround) return;
        ReadSecondaryControls(r, SecondaryEnabled, Plant.Parameters.TreeSecondaryMaturationStrainAmplitude,
            Plant.Parameters.TreeSecondaryProximalFraction, Plant.Parameters.TreeSecondaryProprioceptiveLengthM);
        SecondaryModules = null; SecondaryCompletions = null; SecondaryWoods = null; SecondarySteps = null; SecondarySnapshots = null; SecondaryFramePairs = null;
        SecondaryTransactionIncomplete = r.ReadBoolean();
        if (SecondaryTransactionIncomplete) throw new InvalidDataException("An unfinished secondary material transaction is diagnostic only.");
        int count = r.ReadInt32();
        if (count < 0 || count > (TreeModules?.Count ?? 0)) throw new InvalidDataException("Invalid secondary module count.");
        for (int i = 0; i < count; ++i)
        {
            var m = new SecondaryModule(r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt32(), r.ReadInt32(), r.ReadSingle(), r.ReadSingle());
            if (!SecondaryEnabled || m.Root == 0 || TreeModules == null || !TreeModules.TryGetValue(m.Root, out var module)
                || !TreeModules.TryGetValue(m.Successor, out var successor) || successor.PredecessorRootStableId != m.Root
                || successor.BirthState != TreeModuleBirthState.Live || successor.AdmittedTick != m.SuccessorCommitTick
                || module.Kind != TreeModuleKind.DorsiventralMainline || !(m.ProximalEndM > 0f)
                || !float.IsFinite(m.FormedLengthM) || !(m.FormedLengthM > 0f)
                || !float.IsFinite(m.ProximalEndM) || m.ProximalEndM != m.FormedLengthM * Plant.Parameters.TreeSecondaryProximalFraction
                || m.MaterialClosureCycle != module.AllowanceCycle || m.SuccessorCommitTick < module.CreatedTick)
                throw new InvalidDataException("Invalid frozen secondary module eligibility.");
            (SecondaryModules ??= []).Add(m.Root, m);
        }
        count = r.ReadInt32(); if (count < 0 || (ulong)count >= NextOrganId) throw new InvalidDataException("Invalid secondary wood count.");
        for (int i = 0; i < count; ++i)
        {
            var s = new SecondaryWood { Organ = r.ReadUInt64(), Module = r.ReadUInt64(), EligibleCycle = r.ReadInt32(),
                MaterialStartM = r.ReadSingle(), ReactingLengthM = r.ReadSingle(), AreaM2 = r.ReadDouble(), CurvaturePathPerM = r.ReadDouble() };
            if (!SecondaryEnabled || s.Organ == 0 || s.Organ >= NextOrganId || SecondaryModules == null
                || !SecondaryModules.TryGetValue(s.Module, out var module) || s.EligibleCycle != module.MaterialClosureCycle + 1
                || !float.IsFinite(s.MaterialStartM) || s.MaterialStartM < 0f || s.MaterialStartM >= module.ProximalEndM
                || !(s.ReactingLengthM > 0f) || !float.IsFinite(s.ReactingLengthM) || s.ReactingLengthM > module.ProximalEndM - s.MaterialStartM
                || !double.IsFinite(s.AreaM2) || s.AreaM2 < 0d || !double.IsFinite(s.CurvaturePathPerM) || s.CurvaturePathPerM < 0d)
                throw new InvalidDataException("Invalid accepted secondary wood ledger.");
            (SecondaryWoods ??= []).Add(s.Organ, s);
            int current = FindOrgan(s.Organ);
            if (current >= 0)
            {
                ref var a = ref Unsafe.As<T, AboveGroundAgent>(ref Src()[current]);
                if (a.Organ != OrganTypes.Stem || a.IsRizome || a.TreeModuleRootStableId != s.Module
                    || s.ReactingLengthM > a.Length)
                    throw new InvalidDataException("Frozen secondary wood does not match its current material segment.");
            }
        }
    }

    // Bind the effective new material model before interpreting its history.
    // Disabled controls are inactive and do not need to constrain a resume.
    static void WriteSecondaryControls(BinaryWriter w, bool enabled, float strain, float proximal, float proprioceptive)
    {
        w.Write(enabled);
        if (enabled) { w.Write(strain); w.Write(proximal); w.Write(proprioceptive); }
    }
    static void ReadSecondaryControls(BinaryReader r, bool enabled, float strain, float proximal, float proprioceptive)
    {
        if (r.ReadBoolean() != enabled)
            throw new InvalidDataException("Secondary material mode differs from its checkpoint.");
        if (!enabled) return;
        static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        float savedStrain = r.ReadSingle(), savedProximal = r.ReadSingle(), savedProprioceptive = r.ReadSingle();
        if (!Same(savedStrain, strain) || !Same(savedProximal, proximal) || !Same(savedProprioceptive, proprioceptive))
            throw new InvalidDataException("Secondary material coefficients differ from their checkpoint.");
    }
}
