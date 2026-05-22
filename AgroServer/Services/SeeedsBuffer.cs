using Agro;
using FacadeJsonImport;

namespace AgroServer.Services;

public readonly struct SeedsBufferItem
{
    public readonly DateTime Modified = DateTime.UtcNow;
    public readonly Row[] Data;

    public SeedsBufferItem(CellModel[][] distribution)
    {
        Data = new Row[distribution.Length];
        for(int i = 0; i < distribution.Length; ++i)
            Data[i] = new(distribution[i], i);
    }
}

public interface ISeedsBuffer
{
    string Add(CellModel[][] distribution);
    bool TryGet(string key, out Row[] data);
}

public class SeedsBuffer : ISeedsBuffer
{
    readonly Dictionary<string, SeedsBufferItem> SeedsPerConnection = [];
    static readonly TimeSpan CacheTimeout = TimeSpan.FromMinutes(5);

    public string Add(CellModel[][] distribution)
    {
        lock (SeedsPerConnection)
        {
            var key = Guid.NewGuid().ToString();
            SeedsPerConnection[key] = new(distribution);
            var toRemove = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var item in SeedsPerConnection)
                if (now - item.Value.Modified > CacheTimeout)
                    toRemove.Add(item.Key);

            foreach (var item in toRemove)
                SeedsPerConnection.Remove(item);

            return key;
        }
    }

    public bool TryGet(string key, out Row[] data)
    {
        lock (SeedsPerConnection)
        {
            if (SeedsPerConnection.TryGetValue(key, out var result))
            {
                SeedsPerConnection.Remove(key);
                data = result.Data;
                return true;
            }
            else
            {
                data = default;
                return false;
            }
        }
    }
}