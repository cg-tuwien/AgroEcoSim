using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    // Contact alternation often revisits unchanged geometry. Exact keys preserve every
    // geometric decision; bounded thread-local scratch avoids storing meshes per organ or
    // serialising derived acceleration data. A rebuild invalidates all worker caches lazily.
    const int MemoLimit = 4096;
    static long NextCacheIdentity;
    readonly long CacheIdentity = Interlocked.Increment(ref NextCacheIdentity);
    readonly record struct ContactQuery(ContactShape Shape, Vector3 Trusted, StaticSurface Surface);
    readonly record struct ContactResult(bool Hit, Vector3 Point, Vector3 Normal, float Depth);
    readonly record struct SweepQuery(ContactShape From, ContactShape To, StaticSurface Surface);
    sealed class QueryMemo
    {
        internal long Scene;
        internal int Revision;
        internal readonly Dictionary<ContactQuery, ContactResult> Contacts = [];
        internal readonly Dictionary<SweepQuery, bool> Sweeps = [];
        internal readonly Dictionary<Vector3, bool> Soil = [];
    }
    [ThreadStatic] static QueryMemo? Memo;

    QueryMemo Queries()
    {
        var memo = Memo ??= new();
        if (memo.Scene != CacheIdentity || memo.Revision != GeometryRevision)
        {
            memo.Scene = CacheIdentity; memo.Revision = GeometryRevision;
            memo.Contacts.Clear(); memo.Sweeps.Clear(); memo.Soil.Clear();
        }
        return memo;
    }

    public bool InsideSoil(Vector3 point, float searchRadius)
    {
        if (OpenSoil || SoilTriangles == 0) return InsideSoilUncached(point, searchRadius);
        var cache = Queries().Soil;
        if (cache.TryGetValue(point, out var inside)) return inside;
        inside = InsideSoilUncached(point, searchRadius);
        if (cache.Count >= MemoLimit) cache.Clear();
        cache.Add(point, inside);
        return inside;
    }

    internal bool ShapeContact(in ContactShape shape, Vector3 trusted, StaticSurface surface,
        out Vector3 point, out Vector3 normal, out float depth)
    {
        var cache = Queries().Contacts; var key = new ContactQuery(shape,trusted,surface);
        if (!cache.TryGetValue(key, out var result))
        {
            var hit = ShapeContactUncached(shape,trusted,surface,out point,out normal,out depth);
            result = new(hit,point,normal,depth);
            if (cache.Count >= MemoLimit) cache.Clear();
            cache.Add(key,result);
        }
        point = result.Point; normal = result.Normal; depth = result.Depth;
        return result.Hit;
    }

    internal bool ShapeSweepClear(in ContactShape from, in ContactShape to, StaticSurface surface)
    {
        if (NodeCount == 0 || from.MotionBound(to) < FoliageGeometry.Skin*.25f) return true;
        var cache = Queries().Sweeps; var key = new SweepQuery(from,to,surface);
        if (cache.TryGetValue(key,out var clear)) return clear;
        clear = ShapeSweepClearUncached(from,to,surface);
        if (cache.Count >= MemoLimit) cache.Clear();
        cache.Add(key,clear);
        return clear;
    }
}
