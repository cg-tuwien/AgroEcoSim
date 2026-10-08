using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    CudaCollisionBackend? Cuda;
    bool BackendInitialized;
    CudaCollisionPair[] DevicePairs = [];
    bool[] DeviceSeparated = [];
    readonly record struct DeviceGroup(PlantWork A, PlantWork B, List<(int,int)> Pairs, bool[] Results);
    readonly List<DeviceGroup> DeviceGroups = [];
    readonly List<(bool[] Target, int Offset, int Source, int Count)> DeviceCopies = [];
    bool CudaAvailable => Cuda is { IsAvailable: true };
    public string BackendDiagnostic { get; private set; } = "CPU";
    public long CudaSubmittedPairs => Cuda?.SubmittedPairs ?? 0;
    public long CudaCertifiedPairs => Cuda?.CertifiedPairs ?? 0;
    public long CudaTicks => Cuda?.TotalTicks ?? 0;
    public long ApproximationQueries { get; private set; }
    public long ApproximationSeparations { get; private set; }

    void InitializeBackend(AgroWorld world)
    {
        if (BackendInitialized) return;
        BackendInitialized = true;
        CudaCollisionBackend.TryCreate(world.CollisionBackend, out Cuda, out var diagnostic);
        BackendDiagnostic = diagnostic;
        if (world.CollisionBackend == CollisionComputeBackend.Cuda) Console.WriteLine(diagnostic);
    }

    void CertifyOwnBatch(List<PlantFormation2> plants, bool swept)
    {
        DeviceGroups.Clear();
        foreach (var plant in plants)
        {
            var work = Work[plant];
            if (work.Certified.Length < work.OwnPairs.Count) Array.Resize(ref work.Certified, work.OwnPairs.Count);
            DeviceGroups.Add(new(work, work, work.OwnPairs, work.Certified));
        }
        CertifyDeviceGroups(swept);
    }

    void CertifyCrossBatch(List<CrossWork> batch, bool swept)
    {
        if (!CudaAvailable || swept) return;
        DeviceGroups.Clear();
        foreach (var job in batch)
        {
            if (job.Certified.Length < job.Pairs.Count) Array.Resize(ref job.Certified, job.Pairs.Count);
            DeviceGroups.Add(new(job.A, job.B, job.Pairs, job.Certified));
        }
        CertifyDeviceGroups(swept);
    }

    void CertifyDeviceGroups(bool swept)
    {
        // A single context is used at a phase barrier, outside worker jobs. There is one
        // bounded readback per chunk, and no solver writes while certificates are in flight.
        const int capacity = 16384;
        if (DevicePairs.Length == 0) { DevicePairs = new CudaCollisionPair[capacity]; DeviceSeparated = new bool[capacity]; }
        // Clear the entire batch before dispatch: a failure in an early job must not
        // leave certificates from the preceding phase on jobs we have not visited.
        long total = 0;
        foreach (var group in DeviceGroups)
        { Array.Clear(group.Results, 0, group.Pairs.Count); total += group.Pairs.Count; }
        if (!CudaAvailable || total < MinimumParallelWork) return;
        // Aggregate small crowns too. Applying the minimum to each plant pair left a
        // 124k-organ P003 scene with no GPU work even though the phase had millions of pairs.
        var used = 0;
        DeviceCopies.Clear();
        foreach (var group in DeviceGroups)
        {
            for (int offset = 0; offset < group.Pairs.Count;)
            {
                var count = Math.Min(capacity - used, group.Pairs.Count - offset);
                for (int k = 0; k < count; ++k)
                {
                    var (i, j) = group.Pairs[offset + k];
                    ref readonly var a = ref group.A.Geometry[i]; ref readonly var b = ref group.B.Geometry[j];
                    // A full-body separation is sufficient even at an attachment; no
                    // attachment-clipped geometry or feature identifiers go to the GPU.
                    DevicePairs[used+k] = new(a.Previous, a.Current, b.Previous, b.Current, swept, !a.Eligible || !b.Eligible || a.Previous.Pose.Deformation != Vector4.Zero || a.Current.Pose.Deformation != Vector4.Zero
                        || b.Previous.Pose.Deformation != Vector4.Zero || b.Current.Pose.Deformation != Vector4.Zero);
                }
                DeviceCopies.Add((group.Results, offset, used, count));
                used += count; offset += count;
                if (used == capacity && !Flush()) return;
            }
        }
        if (used != 0) Flush();

        bool Flush()
        {
            if (!Cuda!.CertifyPairs(DevicePairs.AsSpan(0, used), DeviceSeparated.AsSpan(0, used)))
            {
                foreach (var pending in DeviceGroups) Array.Clear(pending.Results);
                BackendDiagnostic = Cuda.Diagnostic;
                DeviceCopies.Clear(); return false;
            }
            foreach (var copy in DeviceCopies)
                DeviceSeparated.AsSpan(copy.Source, copy.Count).CopyTo(copy.Target.AsSpan(copy.Offset));
            DeviceCopies.Clear(); used = 0; return true;
        }
    }

    FoliageContact DetailedDistance(in ContactShape a, in ContactShape b, float search, float tolerance, PlantWork work)
    {
        // Exact boundaries and error-inflated clearance make the approximation a safe
        // rejection filter. It never produces persistent feature IDs or blocks a birth.
        float ea = 0, eb = 0;
        var ta = a.Lamina?.GetCollisionApproximation(a.Pose, a.Pose, tolerance, out ea);
        var tb = b.Lamina?.GetCollisionApproximation(b.Pose, b.Pose, tolerance, out eb);
        if (ta != a.Lamina || tb != b.Lamina)
        {
            ++work.ApproximationQueries;
            var inflated = search + ea + eb + FoliageGeometry.Skin;
            var hit = FoliageGeometry.Distance(a with { Lamina = ta }, b with { Lamina = tb }, inflated);
            if (hit.Distance >= inflated)
            {
                ++work.ApproximationSeparations;
                return new(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
            }
        }
        return FoliageGeometry.Distance(a, b, search);
    }
}
