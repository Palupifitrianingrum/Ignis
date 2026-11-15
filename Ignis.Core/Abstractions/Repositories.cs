using Ignis.Core.Entities;

namespace Ignis.Core.Abstractions;

public interface IHotspotRepository
{
    Task<IReadOnlyList<Hotspot>> GetAllAsync(CancellationToken ct = default);
    Task<Hotspot?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Simpan hotspot baru. Id diberikan repository (AssignId) bila masih kosong. Mengembalikan entity tersimpan.</summary>
    Task<Hotspot> AddAsync(Hotspot hotspot, CancellationToken ct = default);
    Task UpdateAsync(Hotspot hotspot, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>True bila hotspot satelit dengan waktu scan dan koordinat (toleransi 1e-5) sudah ada. Dasar deduplikasi impor FIRMS.</summary>
    Task<bool> SatelliteExistsAsync(DateTime scanTime, double latitude, double longitude, CancellationToken ct = default);
}

public interface IWeatherRepository
{
    Task<WeatherData?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default);
    Task<WeatherData> AddAsync(WeatherData weather, CancellationToken ct = default);
}

public interface IPredictionRepository
{
    /// <summary>Urut terbaru di atas.</summary>
    Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default);
    Task<Prediction?> GetLatestForHotspotAsync(string hotspotId, CancellationToken ct = default);
    Task<Prediction> AddAsync(Prediction prediction, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User> AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

public interface IActivityLog
{
    Task AddAsync(string message, CancellationToken ct = default);

    /// <summary>Entri terbaru di atas.</summary>
    Task<IReadOnlyList<string>> RecentAsync(int count = 30, CancellationToken ct = default);
}
