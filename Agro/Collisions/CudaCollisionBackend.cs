using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro.Collisions;

/// <summary>Execution preference only; it must not change admissible plant geometry.</summary>
public enum CollisionComputeBackend : byte { Auto, Cpu, Cuda }

/// <summary>A batch describes immutable geometry at one solver barrier. An attachment exemption
/// cannot be represented by a whole-body separating box and remains a CPU query.</summary>
internal readonly record struct CudaCollisionPair(ContactShape A0, ContactShape A1,
    ContactShape B0, ContactShape B1, bool Swept, bool RequiresCpu);

/// <summary>Optional CUDA separating-axis proposer. The CPU verifies every returned certificate;
/// no device result supplies a contact, normal, support force or permission to ignore an uncertain sweep.</summary>
internal sealed unsafe class CudaCollisionBackend : IDisposable
{
    const int MaximumBatch = 16384;
    readonly CudaCollisionNative.Context Context;
    readonly Dictionary<LaminaTemplate, int> TemplateIndices = [];
    readonly List<CudaCollisionNative.Template> Templates = [];
    CudaCollisionNative.Pose[] Poses = [], UploadedPoses = [];
    CudaCollisionNative.Query[] Queries = [];
    int[] Axes = [];
    int UploadedPoseCount;
    bool Available = true;
    internal bool IsAvailable => Available && !Context.IsClosed;
    internal string Diagnostic { get; private set; } = "CUDA separation prototype active; CPU verifies certificates.";
    internal long SubmittedBatches { get; private set; }
    internal long SubmittedPairs { get; private set; }
    internal long CertifiedPairs { get; private set; }
    internal long TotalTicks { get; private set; }

    CudaCollisionBackend(CudaCollisionNative.Context context) => Context = context;

    internal static CollisionComputeBackend RequestedBackend()
    {
        var value = Environment.GetEnvironmentVariable("AGRO_COLLISION_BACKEND");
        if (string.IsNullOrWhiteSpace(value)) return CollisionComputeBackend.Auto;
        if (Enum.TryParse<CollisionComputeBackend>(value, true, out var backend) && Enum.IsDefined(backend)) return backend;
        throw new ArgumentException("AGRO_COLLISION_BACKEND must be Auto, Cpu or Cuda.");
    }

    internal static bool TryCreate(CollisionComputeBackend requested, out CudaCollisionBackend? backend, out string diagnostic)
    {
        backend = null;
        if (requested != CollisionComputeBackend.Cuda)
        {
            diagnostic = requested == CollisionComputeBackend.Auto
                ? "Auto uses CPU: CUDA awaits a measured 20% end-to-end improvement on deployment hardware."
                : "CPU collision backend selected.";
            return false;
        }
        try
        {
            var error = CudaCollisionNative.Create(1, sizeof(CudaCollisionNative.Template), sizeof(CudaCollisionNative.Pose),
                sizeof(CudaCollisionNative.Query), out var pointer);
            if (error != 0 || pointer == 0)
            {
                if (pointer != 0) CudaCollisionNative.Destroy(pointer);
                diagnostic = $"CUDA unavailable (initialization status {error}); using CPU.";
                return false;
            }
            backend = new(new(pointer)); diagnostic = backend.Diagnostic;
            return true;
        }
        catch (Exception error) when (InteropUnavailable(error))
        {
            diagnostic = $"CUDA unavailable ({error.GetType().Name}); using CPU.";
            return false;
        }
    }

    static bool InteropUnavailable(Exception error) => error is DllNotFoundException or EntryPointNotFoundException
        or BadImageFormatException or PlatformNotSupportedException;

    /// <summary>True in <paramref name="separated"/> means a CPU-verified whole-body clearance,
    /// including the entire motion when requested. False means the ordinary CPU solver must run.
    /// Returns false after a device failure and clears every output, including earlier chunks.</summary>
    internal bool CertifyPairs(ReadOnlySpan<CudaCollisionPair> pairs, Span<bool> separated)
    {
        if (separated.Length < pairs.Length) throw new ArgumentException("A result slot is required for every pair.", nameof(separated));
        separated[..pairs.Length].Clear();
        if (!Available || Context.IsClosed || pairs.IsEmpty) return false;
        var started = Stopwatch.GetTimestamp();
        try
        {
            for (int first = 0; first < pairs.Length; first += MaximumBatch)
            {
                var count = Math.Min(MaximumBatch, pairs.Length - first);
                if (!CertifyChunk(pairs.Slice(first, count), separated.Slice(first, count)))
                {
                    separated[..pairs.Length].Clear(); return false;
                }
            }
            return true;
        }
        catch (Exception error) when (InteropUnavailable(error))
        {
            Disable($"CUDA interop failed ({error.GetType().Name}); using CPU.");
            separated[..pairs.Length].Clear(); return false;
        }
        finally { TotalTicks += Stopwatch.GetTimestamp() - started; }
    }

    bool CertifyChunk(ReadOnlySpan<CudaCollisionPair> pairs, Span<bool> separated)
    {
        var count = pairs.Length;
        if (Queries.Length < count)
        {
            Array.Resize(ref Queries, count); Array.Resize(ref Axes, count);
            Array.Resize(ref Poses, count * 2); Array.Resize(ref UploadedPoses, count * 2);
        }
        var templateCount = Templates.Count;
        for (int i = 0; i < count; ++i)
        {
            ref readonly var pair = ref pairs[i];
            var supported = !pair.RequiresCpu && pair.A0.Feature < 0 && pair.A1.Feature < 0 && pair.B0.Feature < 0 && pair.B1.Feature < 0
                && pair.A0.Lamina == pair.A1.Lamina && pair.B0.Lamina == pair.B1.Lamina;
            var movement = pair.Swept ? pair.A0.MotionBound(pair.A1) + pair.B0.MotionBound(pair.B1) : 0f;
            var reach = pair.Swept
                ? MathF.Max(pair.A0.Pose.Radius, pair.A1.Pose.Radius) + MathF.Max(pair.B0.Pose.Radius, pair.B1.Pose.Radius)
                : pair.A1.Pose.Radius + pair.B1.Pose.Radius;
            Queries[i] = new() { A = i * 2, B = i * 2 + 1, Clearance = reach + movement + FoliageGeometry.Skin,
                Enabled = supported && float.IsFinite(movement) && movement >= 0 && float.IsFinite(reach) ? 1 : 0 };
            Poses[i * 2] = ToNative(pair.A1); Poses[i * 2 + 1] = ToNative(pair.B1);
        }
        if (Templates.Count != templateCount)
        {
            fixed (CudaCollisionNative.Template* templates = CollectionsMarshal.AsSpan(Templates))
                if (!Check(CudaCollisionNative.SetTemplates(Context, templates, Templates.Count))) return false;
        }
        // The device pose storage survives solver phases. Transfer only changed contiguous runs;
        // native staging owns these bytes until the following Evaluate has synchronized the stream.
        for (int i = 0; i < count * 2;)
        {
            if (i < UploadedPoseCount && Poses[i].Equals(UploadedPoses[i])) { ++i; continue; }
            var first = i++;
            while (i < count * 2 && (i >= UploadedPoseCount || !Poses[i].Equals(UploadedPoses[i]))) ++i;
            fixed (CudaCollisionNative.Pose* poses = Poses)
                if (!Check(CudaCollisionNative.SetPoses(Context, poses + first, first, i - first, Poses.Length))) return false;
            Poses.AsSpan(first, i - first).CopyTo(UploadedPoses.AsSpan(first));
        }
        UploadedPoseCount = count * 2;
        fixed (CudaCollisionNative.Query* queries = Queries)
        fixed (int* axes = Axes)
            if (!Check(CudaCollisionNative.Evaluate(Context, queries, count, axes))) return false;
        ++SubmittedBatches; SubmittedPairs += count;
        for (int i = 0; i < count; ++i)
            if (Queries[i].Enabled != 0 && Axes[i] > 0 && VerifyCertificate(pairs[i], Axes[i] - 1))
            { separated[i] = true; ++CertifiedPairs; }
        return true;
    }

    CudaCollisionNative.Pose ToNative(in ContactShape shape)
    {
        var template = -1;
        if (shape.Lamina is { } lamina && !TemplateIndices.TryGetValue(lamina, out template))
        {
            template = Templates.Count; TemplateIndices.Add(lamina, template);
            Templates.Add(new() { Centre = lamina.TemplateCentre, Extent = lamina.TemplateExtent,
                ReferenceHalfWidth = lamina.TemplateReferenceHalfWidth });
        }
        return new() { Base = shape.Pose.Base, Rotation = shape.Pose.Rotation, Length = shape.Pose.Length,
            Width = shape.Pose.Width, Template = template };
    }

    // Only the existing scalar projection authorizes rejection. Device math can suggest a
    // useless axis but cannot certify a touching leaf, even with a faulty/nonmatching driver.
    internal static bool VerifyCertificate(in CudaCollisionPair pair, int axisIndex)
    {
        if (pair.RequiresCpu || pair.A0.Feature >= 0 || pair.A1.Feature >= 0 || pair.B0.Feature >= 0 || pair.B1.Feature >= 0
            || pair.A0.Lamina != pair.A1.Lamina || pair.B0.Lamina != pair.B1.Lamina || axisIndex is < 0 or >= 15) return false;
        var motion = pair.Swept ? pair.A0.MotionBound(pair.A1) + pair.B0.MotionBound(pair.B1) : 0f;
        var reach = pair.Swept
            ? MathF.Max(pair.A0.Pose.Radius, pair.A1.Pose.Radius) + MathF.Max(pair.B0.Pose.Radius, pair.B1.Pose.Radius)
            : pair.A1.Pose.Radius + pair.B1.Pose.Radius;
        var clearance = reach + motion + FoliageGeometry.Skin;
        if (!float.IsFinite(clearance) || motion < 0 || reach < 0) return false;
        Span<Vector3> axes = stackalloc Vector3[6];
        axes[0] = Vector3.Transform(Vector3.UnitX, pair.A1.Pose.Rotation);
        axes[1] = Vector3.Transform(Vector3.UnitY, pair.A1.Pose.Rotation);
        axes[2] = Vector3.Transform(Vector3.UnitZ, pair.A1.Pose.Rotation);
        axes[3] = Vector3.Transform(Vector3.UnitX, pair.B1.Pose.Rotation);
        axes[4] = Vector3.Transform(Vector3.UnitY, pair.B1.Pose.Rotation);
        axes[5] = Vector3.Transform(Vector3.UnitZ, pair.B1.Pose.Rotation);
        var axis = axisIndex < 6 ? axes[axisIndex] : Vector3.Cross(axes[(axisIndex - 6) / 3], axes[3 + (axisIndex - 6) % 3]);
        var length = axis.Length();
        if (!float.IsFinite(length) || length <= 1e-6f) return false;
        // Match the existing SAT's normalization for cross axes. Quaternion axes already have
        // the same small roundoff as the scalar reference; scale motion outward for that error.
        if (axisIndex >= 6) axis /= length;
        Project(pair.A1, axis, out var al, out var ah); Project(pair.B1, axis, out var bl, out var bh);
        var scale = MathF.Max(1f, MathF.Max(MathF.Max(MathF.Abs(al), MathF.Abs(ah)), MathF.Max(MathF.Abs(bl), MathF.Abs(bh))));
        var required = clearance * MathF.Max(1f, axisIndex < 6 ? length : 1f) + scale * 4e-7f;
        return float.IsFinite(al) && float.IsFinite(ah) && float.IsFinite(bl) && float.IsFinite(bh)
            && MathF.Max(al - bh, bl - ah) >= required;
    }

    static void Project(in ContactShape shape, Vector3 axis, out float low, out float high)
    {
        if (shape.Lamina is { } lamina) { lamina.ProjectBounds(shape.Pose, axis, out low, out high); return; }
        var a = Vector3.Dot(shape.Pose.Base, axis); var b = Vector3.Dot(shape.Pose.Tip, axis);
        low = MathF.Min(a, b); high = MathF.Max(a, b);
    }

    bool Check(int error)
    {
        if (error == 0) return true;
        Disable($"CUDA query failed (status {error}); using CPU."); return false;
    }
    void Disable(string diagnostic) { Available = false; Diagnostic = diagnostic; Context.Dispose(); }
    public void Dispose() { Available = false; Context.Dispose(); }
}
