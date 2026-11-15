using Ignis.Core.Abstractions;
using Ignis.Core.Entities;

namespace Ignis.Core.InMemory;

// Implementasi penyimpanan di memori: untuk mode demo/offline di desktop dan pengujian.
// Murni C# (tanpa I/O). Implementasi PostgreSQL ada di Ignis.Infrastructure.

public sealed class InMemoryHotspotRepository : IHotspotRepository
{
    private readonly List<Hotspot> _items;
    private readonly object _gate = new();

    public InMemoryHotspotRepository(IEnumerable<Hotspot>? seed = null) => _items = seed?.ToList() ?? new();

    public Task<IReadOnlyList<Hotspot>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Hotspot>>(_items.ToList());
    }

    public Task<Hotspot?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.FirstOrDefault(h => h.Id == id));
    }

    public Task<Hotspot> AddAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(hotspot.Id)) hotspot.AssignId(Ids.Next("HS", _items.Select(h => h.Id)));
            _items.Insert(0, hotspot);
            return Task.FromResult(hotspot);
        }
    }

    public Task UpdateAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(h => h.Id == hotspot.Id);
            if (i >= 0) _items[i] = hotspot;
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.RemoveAll(h => h.Id == id) > 0);
    }

    public Task<bool> SatelliteExistsAsync(DateTime scanTime, double latitude, double longitude, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_items.OfType<SatelliteHotspot>().Any(x =>
                x.ScanTime == scanTime && Math.Abs(x.Latitude - latitude) < 1e-5 && Math.Abs(x.Longitude - longitude) < 1e-5));
    }
}

public sealed class InMemoryWeatherRepository : IWeatherRepository
{
    private readonly List<WeatherData> _items = new();
    private readonly object _gate = new();

    public Task<WeatherData?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.Where(w => w.HotspotId == hotspotId).OrderByDescending(w => w.Timestamp).FirstOrDefault());
    }

    public Task<WeatherData> AddAsync(WeatherData weather, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(weather.Id)) weather.Id = Ids.Next("WX", _items.Select(w => w.Id));
            _items.Add(weather);
            return Task.FromResult(weather);
        }
    }
}

public sealed class InMemoryPredictionRepository : IPredictionRepository
{
    private readonly List<Prediction> _items;
    private readonly object _gate = new();

    public InMemoryPredictionRepository(IEnumerable<Prediction>? seed = null) => _items = seed?.ToList() ?? new();

    public Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Prediction>>(_items.ToList());
    }

    public Task<Prediction?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.FirstOrDefault(p => p.HotspotId == hotspotId));
    }

    public Task<Prediction> AddAsync(Prediction prediction, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(prediction.Id)) prediction.Id = Ids.Next("PR", _items.Select(p => p.Id));
            _items.Insert(0, prediction);
            return Task.FromResult(prediction);
        }
    }
}

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _items;
    private readonly object _gate = new();

    public InMemoryUserRepository(IEnumerable<User>? seed = null) => _items = seed?.ToList() ?? new();

    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<User>>(_items.ToList());
    }

    public Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.FirstOrDefault(u => u.Id == id));
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_items.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        lock (_gate) _items.Add(user);
        return Task.FromResult(user);
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var i = _items.FindIndex(u => u.Id == user.Id);
            if (i >= 0) _items[i] = user;
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryActivityLog : IActivityLog
{
    private const int MaxEntries = 30;
    private readonly List<string> _items;
    private readonly object _gate = new();

    public InMemoryActivityLog(IEnumerable<string>? seed = null) => _items = seed?.ToList() ?? new();

    public Task AddAsync(string message, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _items.Insert(0, $"{message} · {DateTime.Now:HH:mm}");
            while (_items.Count > MaxEntries) _items.RemoveAt(_items.Count - 1);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RecentAsync(int count = MaxEntries, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<string>>(_items.Take(count).ToList());
    }
}

internal static class Ids
{
    /// <summary>Id berikutnya berformat "PREFIX-0001" (maks + 1). TODO(backend): di database pakai sequence agar aman multi-user.</summary>
    public static string Next(string prefix, IEnumerable<string> existing)
    {
        int max = 0;
        foreach (var id in existing)
            if (id.Length > 3 && int.TryParse(id[3..], out var n) && n > max) max = n;
        return $"{prefix}-{max + 1:0000}";
    }
}
