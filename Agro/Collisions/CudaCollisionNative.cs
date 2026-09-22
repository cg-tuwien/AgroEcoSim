using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Agro.Collisions;

/// <summary>Blittable ABI only. The optional native library is resolved lazily, including in NativeAOT.</summary>
internal static unsafe partial class CudaCollisionNative
{
    const string Library = "agro_collision_cuda";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Template { internal Vector3 Centre, Extent; internal float ReferenceHalfWidth; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Pose : IEquatable<Pose>
    {
        internal Vector3 Base;
        internal Quaternion Rotation;
        internal float Length, Width;
        internal int Template;
        public readonly bool Equals(Pose other) => Base == other.Base && Rotation == other.Rotation
            && Length == other.Length && Width == other.Width && Template == other.Template;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Query { internal int A, B; internal float Clearance; internal int Enabled; }

    internal sealed class Context : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal Context(nint pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle()
        {
            try { Destroy(handle); return true; }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            { return false; }
        }
    }

    [LibraryImport(Library, EntryPoint = "agro_cuda_create")]
    internal static partial int Create(int abi, int templateSize, int poseSize, int querySize, out nint context);
    [LibraryImport(Library, EntryPoint = "agro_cuda_destroy")]
    internal static partial void Destroy(nint context);
    [LibraryImport(Library, EntryPoint = "agro_cuda_set_templates")]
    internal static partial int SetTemplates(Context context, Template* templates, int count);
    [LibraryImport(Library, EntryPoint = "agro_cuda_set_poses")]
    internal static partial int SetPoses(Context context, Pose* poses, int first, int count, int capacity);
    [LibraryImport(Library, EntryPoint = "agro_cuda_evaluate")]
    internal static partial int Evaluate(Context context, Query* queries, int count, int* axes);
}
