using System.Numerics;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    readonly record struct DistanceQuery(ContactShape A, ContactShape B, float Search, Vector3 Attachment, float Radius);
    readonly record struct MotionQuery(ContactShape A0, ContactShape A1, ContactShape B0, ContactShape B1,
        Vector3 Attachment0, Vector3 Attachment1, float Radius);
    [ThreadStatic] static FixedMemo<DistanceQuery,FoliageContact>? DistanceMemo;
    [ThreadStatic] static FixedMemo<MotionQuery,bool>? MotionMemo;

    // Two-way bounded storage evicts one result instead of clearing a whole worker's
    // useful stationary-contact history when a crowded canopy crosses the old limit.
    sealed class FixedMemo<TKey,TValue> where TKey : struct, IEquatable<TKey>
    {
        struct Entry { public bool Used; public TKey Key; public TValue Value; }
        readonly Entry[] entries;
        readonly byte[] victim;
        public FixedMemo(int capacity) { entries = new Entry[capacity]; victim = new byte[capacity / 2]; }
        int Bucket(in TKey key) => (key.GetHashCode() & (victim.Length - 1)) * 2;
        public bool TryGetValue(in TKey key, out TValue value)
        {
            var index = Bucket(key);
            for (int i = index; i < index + 2; ++i)
                if (entries[i].Used && entries[i].Key.Equals(key))
                { value = entries[i].Value; victim[index / 2] = (byte)(1 - (i - index)); return true; }
            value = default!; return false;
        }
        public void Store(in TKey key, TValue value)
        {
            var index = Bucket(key);
            var slot = !entries[index].Used ? index : !entries[index + 1].Used ? index + 1 : index + victim[index / 2];
            entries[slot] = new() { Used = true, Key = key, Value = value };
            victim[index / 2] = (byte)(1 - (slot - index));
        }
    }

    internal static FoliageContact Distance(in ContactShape a, in ContactShape b, float search)
    {
        if (a.Feature >= 0 || b.Feature >= 0) return DistanceUncached(a,b,search);
        var cache = DistanceMemo ??= new(4096); var key = new DistanceQuery(a,b,search,default,0);
        if (cache.TryGetValue(key,out var hit)) return hit;
        hit = DistanceUncached(a,b,search);
        cache.Store(key, hit);
        return hit;
    }

    internal static FoliageContact OutsideAttachment(in ContactShape a, in ContactShape b, float search, Vector3 point, float radius)
    {
        // Subdivision features are almost never queried twice. Caching them evicted the
        // stationary whole-leaf pairs that dominate repeated relaxation sweeps.
        if (a.Feature>=0 || b.Feature>=0) return OutsideAttachmentUncached(a,b,search,point,radius);
        var cache = DistanceMemo ??= new(4096); var key = new DistanceQuery(a,b,search,point,radius);
        if (cache.TryGetValue(key,out var hit)) return hit;
        hit = OutsideAttachmentUncached(a,b,search,point,radius);
        cache.Store(key, hit);
        return hit;
    }

    internal static bool SweepClear(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1, int depth=0)
    {
        if (depth != 0 || a0.Feature >= 0 || b0.Feature >= 0) return SweepUncached(a0,a1,b0,b1,depth);
        var cache = MotionMemo ??= new(2048); var key = new MotionQuery(a0,a1,b0,b1,default,default,0);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = SweepUncached(a0,a1,b0,b1);
        cache.Store(key, clear);
        return clear;
    }

    internal static bool AttachmentSweepClear(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1,
        Vector3 point0, Vector3 point1, float radius)
    {
        var cache = MotionMemo ??= new(2048); var key = new MotionQuery(a0,a1,b0,b1,point0,point1,radius);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = AttachmentSweepUncached(a0,a1,b0,b1,point0,point1,radius);
        cache.Store(key, clear);
        return clear;
    }
}
