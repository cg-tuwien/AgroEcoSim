using Agro;

namespace AgroServer.Services;

public interface ISimulationUploadService
{
    string Add(SimulationRequest input);
    bool TryFetch(string id, out SimulationRequest result);
}

internal class UploadEntry
{
    public Guid Id { get; set; }
    public DateTime CreatedUTC { get; set; }
    public SimulationRequest Data { get; set; }
}

public class SimulationUploadService : ISimulationUploadService
{
    readonly List<UploadEntry> Cache = [];

    static readonly TimeSpan MaxStorageTime = TimeSpan.FromMinutes(5);

    public string Add(SimulationRequest input)
    {
        lock (Cache)
        {
            Cache.RemoveAll(x => DateTime.UtcNow - x.CreatedUTC > MaxStorageTime);
            var entry = new UploadEntry()
            {
                Id = Guid.NewGuid(),
                CreatedUTC = DateTime.UtcNow,
                Data = input
            };
            Cache.Add(entry);
            return entry.Id.ToString();
        }
    }

    public bool TryFetch(string id, out SimulationRequest result)
    {
        if (Guid.TryParse(id, out var guid))
            lock (Cache)
            {
                var entry = Cache.FirstOrDefault(x => x.Id == guid);
                if (entry != null)
                    Cache.Remove(entry);

                Cache.RemoveAll(x => DateTime.UtcNow - x.CreatedUTC > MaxStorageTime);

                if (entry?.Data != null)
                {
                    result = entry.Data;
                    return true;
                }
            }

        result = null;
        return false;
    }
}