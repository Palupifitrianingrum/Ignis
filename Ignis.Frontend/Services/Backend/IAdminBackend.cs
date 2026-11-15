using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ignis.Core.Contracts;
using Ignis.Core.Services;

namespace Ignis.Frontend.Services.Backend;

/// <summary>
/// Pintu masuk Frontend ke "backend". Dua implementasi:
///  - <see cref="ApiAdminBackend"/>: memanggil Ignis.Api (HTTP) yang menyimpan ke PostgreSQL. Mode default.
///  - <see cref="LocalAdminBackend"/>: data demo di memori (offline), memakai service Ignis.Core langsung.
/// Pelanggaran aturan domain dilaporkan sebagai exception dengan pesan siap tampil.
/// </summary>
public interface IAdminBackend
{
    string Name { get; }

    Task<IReadOnlyList<HotspotDto>> GetHotspotsAsync();
    Task<HotspotDto> AddManualHotspotAsync(ManualHotspotRequest request);
    Task<HotspotDto> UpdateManualHotspotAsync(string id, ManualHotspotRequest request);
    Task DeleteHotspotAsync(string id);

    Task<WeatherDto> GetWeatherAsync(string hotspotId);
    Task<IReadOnlyList<PredictionDto>> GetPredictionsAsync();
    Task<PredictionDto> GetOrPredictAsync(string hotspotId);
    Task<PredictionDto> PredictAsync(string hotspotId);

    Task<IReadOnlyList<UserDto>> GetUsersAsync();

    /// <summary>Aktif/nonaktifkan pengguna. Mengembalikan pesan error, atau null bila berhasil.</summary>
    Task<string?> ToggleUserAsync(string id);

    Task<IReadOnlyList<string>> GetActivitiesAsync();
    Task AddActivityAsync(string message);

    Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress);
}

/// <summary>Backend tidak dapat dihubungi atau menjawab di luar kontrak.</summary>
public sealed class BackendException : Exception
{
    public BackendException(string message, Exception? inner = null) : base(message, inner) { }
}
