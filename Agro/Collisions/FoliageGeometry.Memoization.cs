using System.Numerics;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    // A work limit cannot establish intersection. Keep the distinction in cached
    // results as well, so cold and warm workers make the same retry decisions.
    internal enum MotionResult : byte { Clear, Intersecting, Uncertified }
    readonly record struct DistanceQuery(ContactShape A, ContactShape B, float Search, Vector3 Attachment, float Radius);
    readonly record struct MotionQuery(ContactShape A0, ContactShape A1, ContactShape B0, ContactShape B1,
        Vector3 Attachment0, Vector3 Attachment1, float Radius);
    [ThreadStatic] static FixedMemo<DistanceQuery,FoliageContact>? DistanceMemo;
    [ThreadStatic] static FixedMemo<MotionQuery,MotionResult>? MotionMemo;

    // Four-way LRU sets retain the same bounded entry count. A two-way set could
    // repeatedly evict an expensive stationary query when several nearby poses
    // hashed into it, making identical contact populations vary sharply in cost.
    sealed class FixedMemo<TKey,TValue> where TKey : struct, IEquatable<TKey>
    {
        const int Ways = 4;
        struct Entry { public bool Used; public TKey Key; public TValue Value; }
        readonly Entry[] entries;
        readonly byte[] victim;
        public FixedMemo(int capacity)
        {
            entries = new Entry[capacity]; victim = new byte[capacity / Ways];
            // Two bits per way, ordered from most to least recently used.
            Array.Fill(victim, (byte)0xe4); // 0, 1, 2, 3
        }
        int Bucket(in TKey key) => (key.GetHashCode() & (victim.Length - 1)) * Ways;
        void Touch(int index, int slot)
        {
            var order = victim[index / Ways];
            var promoted = (byte)slot; var shift = 2;
            for (var position = 0; position < Ways; ++position)
            {
                var previous = (order >> (2 * position)) & 3;
                if (previous == slot) continue;
                promoted |= (byte)(previous << shift); shift += 2;
            }
            victim[index / Ways] = promoted;
        }
        public bool TryGetValue(in TKey key, out TValue value)
        {
            var index = Bucket(key);
            for (int i = index; i < index + Ways; ++i)
                if (entries[i].Used && entries[i].Key.Equals(key))
                { value = entries[i].Value; Touch(index, i - index); return true; }
            value = default!; return false;
        }
        public void Store(in TKey key, TValue value)
        {
            var index = Bucket(key);
            var slot = index + (victim[index / Ways] >> 6);
            for (var i = index; i < index + Ways; ++i)
                if (!entries[i].Used) { slot = i; break; }
            entries[slot] = new() { Used = true, Key = key, Value = value };
            Touch(index, slot - index);
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
        => Sweep(a0,a1,b0,b1,depth) == MotionResult.Clear;

    internal static MotionResult Sweep(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1, int depth=0)
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
        => AttachmentSweep(a0,a1,b0,b1,point0,point1,radius) == MotionResult.Clear;

    internal static MotionResult AttachmentSweep(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1,
        Vector3 point0, Vector3 point1, float radius)
    {
        var cache = MotionMemo ??= new(2048); var key = new MotionQuery(a0,a1,b0,b1,point0,point1,radius);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = AttachmentSweepUncached(a0,a1,b0,b1,point0,point1,radius);
        cache.Store(key, clear);
        return clear;
    }
}
