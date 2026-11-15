using Ignis.Core.Abstractions;
using Ignis.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Infrastructure.Persistence;

// Implementasi repository untuk PostgreSQL (EF Core).

public sealed class EfHotspotRepository : IHotspotRepository
{
    private readonly AppDbContext _db;
    public EfHotspotRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Hotspot>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Hotspots.AsNoTracking().OrderByDescending(h => h.DetectedAt).ToListAsync(ct);

    public Task<Hotspot?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Hotspots.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<Hotspot> AddAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(hotspot.Id))
            hotspot.AssignId(await IdGenerator.NextAsync("HS", _db.Hotspots.Select(h => h.Id), ct));
        _db.Hotspots.Add(hotspot);
        await _db.SaveChangesAsync(ct);
        return hotspot;
    }

    public async Task UpdateAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        _db.Hotspots.Update(hotspot);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) =>
        await _db.Hotspots.Where(h => h.Id == id).ExecuteDeleteAsync(ct) > 0;

    public Task<bool> SatelliteExistsAsync(DateTime scanTime, double latitude, double longitude, CancellationToken ct = default) =>
        _db.Hotspots.OfType<SatelliteHotspot>().AnyAsync(h =>
            h.ScanTime == scanTime &&
            Math.Abs(h.Latitude - latitude) < 1e-5 && Math.Abs(h.Longitude - longitude) < 1e-5, ct);
}

public sealed class EfWeatherRepository : IWeatherRepository
{
    private readonly AppDbContext _db;
    public EfWeatherRepository(AppDbContext db) => _db = db;

    public Task<WeatherData?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default) =>
        _db.WeatherData.AsNoTracking().Where(w => w.HotspotId == hotspotId)
            .OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(ct);

    public async Task<WeatherData> AddAsync(WeatherData weather, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(weather.Id))
            weather.Id = await IdGenerator.NextAsync("WX", _db.WeatherData.Select(w => w.Id), ct);
        _db.WeatherData.Add(weather);
        await _db.SaveChangesAsync(ct);
        return weather;
    }
}

public sealed class EfPredictionRepository : IPredictionRepository
{
    private readonly AppDbContext _db;
    public EfPredictionRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Predictions.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public Task<Prediction?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default) =>
        _db.Predictions.AsNoTracking().Where(p => p.HotspotId == hotspotId)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<Prediction> AddAsync(Prediction prediction, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(prediction.Id))
            prediction.Id = await IdGenerator.NextAsync("PR", _db.Predictions.Select(p => p.Id), ct);
        _db.Predictions.Add(prediction);
        await _db.SaveChangesAsync(ct);
        return prediction;
    }
}

public sealed class EfUserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public EfUserRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);

    public Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class EfActivityLog : IActivityLog
{
    private readonly AppDbContext _db;
    public EfActivityLog(AppDbContext db) => _db = db;

    public async Task AddAsync(string message, CancellationToken ct = default)
    {
        _db.ActivityLog.Add(new ActivityLogEntry { Message = message, CreatedAt = DateTime.Now });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> RecentAsync(int count = 30, CancellationToken ct = default)
    {
        var rows = await _db.ActivityLog.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(count).ToListAsync(ct);
        return rows.Select(a => $"{a.Message} · {a.CreatedAt:HH:mm}").ToList();
    }
}

internal static class IdGenerator
{
    /// <summary>
    /// Id berikutnya "PREFIX-0001" (maks + 1).
    /// TODO(backend): rawan balapan bila banyak penulis bersamaan. Ganti dengan sequence PostgreSQL
    /// sebelum dipakai multi-user.
    /// </summary>
    public static async Task<string> NextAsync(string prefix, IQueryable<string> ids, CancellationToken ct)
    {
        int max = 0;
        foreach (var id in await ids.Where(i => i.StartsWith(prefix + "-")).ToListAsync(ct))
            if (id.Length > 3 && int.TryParse(id[3..], out var n) && n > max) max = n;
        return $"{prefix}-{max + 1:0000}";
    }
}
