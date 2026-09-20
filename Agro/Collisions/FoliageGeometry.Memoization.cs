using System.Numerics;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    readonly record struct DistanceQuery(ContactShape A, ContactShape B, float Search, Vector3 Attachment, float Radius);
    readonly record struct MotionQuery(ContactShape A0, ContactShape A1, ContactShape B0, ContactShape B1,
        Vector3 Attachment0, Vector3 Attachment1, float Radius);
    [ThreadStatic] static Dictionary<DistanceQuery,FoliageContact>? DistanceMemo;
    [ThreadStatic] static Dictionary<MotionQuery,bool>? MotionMemo;

    internal static FoliageContact Distance(in ContactShape a, in ContactShape b, float search)
    {
        if (a.Feature >= 0 || b.Feature >= 0) return DistanceUncached(a,b,search);
        var cache = DistanceMemo ??= []; var key = new DistanceQuery(a,b,search,default,0);
        if (cache.TryGetValue(key,out var hit)) return hit;
        hit = DistanceUncached(a,b,search);
        if (cache.Count >= 4096) cache.Clear();
        cache[key] = hit;
        return hit;
    }

    internal static FoliageContact OutsideAttachment(in ContactShape a, in ContactShape b, float search, Vector3 point, float radius)
    {
        // Subdivision features are almost never queried twice. Caching them evicted the
        // stationary whole-leaf pairs that dominate repeated relaxation sweeps.
        if (a.Feature>=0 || b.Feature>=0) return OutsideAttachmentUncached(a,b,search,point,radius);
        var cache = DistanceMemo ??= []; var key = new DistanceQuery(a,b,search,point,radius);
        if (cache.TryGetValue(key,out var hit)) return hit;
        hit = OutsideAttachmentUncached(a,b,search,point,radius);
        if (cache.Count >= 4096) cache.Clear();
        cache[key] = hit;
        return hit;
    }

    internal static bool SweepClear(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1, int depth=0)
    {
        if (depth != 0 || a0.Feature >= 0 || b0.Feature >= 0) return SweepUncached(a0,a1,b0,b1,depth);
        var cache = MotionMemo ??= []; var key = new MotionQuery(a0,a1,b0,b1,default,default,0);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = SweepUncached(a0,a1,b0,b1);
        if (cache.Count >= 2048) cache.Clear();
        cache[key] = clear;
        return clear;
    }

    internal static bool AttachmentSweepClear(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1,
        Vector3 point0, Vector3 point1, float radius)
    {
        var cache = MotionMemo ??= []; var key = new MotionQuery(a0,a1,b0,b1,point0,point1,radius);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = AttachmentSweepUncached(a0,a1,b0,b1,point0,point1,radius);
        if (cache.Count >= 2048) cache.Clear();
        cache[key] = clear;
        return clear;
    }
}
