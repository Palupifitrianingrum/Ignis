using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.Abstractions;

/// <summary>Konfigurasi integrasi eksternal. Di desktop berasal dari pengaturan lokal; di server dari konfigurasi/env.</summary>
public sealed record IntegrationSettings
{
    public string FirmsEndpoint { get; init; } = "https://firms.modaps.eosdis.nasa.gov/api/area/csv";
    public string FirmsKey { get; init; } = string.Empty;
    public string WeatherEndpoint { get; init; } = "https://api.openweathermap.org/data/2.5/weather";
    public string WeatherKey { get; init; } = string.Empty;
    public int RetryCount { get; init; } = 3;
    public int RetryDelayMinutes { get; init; } = 5;
}

public interface ISettingsProvider
{
    IntegrationSettings Current { get; }
}

public sealed record ProviderCheck(ConnectionState State, string Message);

public interface IFirmsClient
{
    /// <summary>Ambil CSV hotspot area Indonesia 24 jam terakhir (VIIRS SNPP NRT). Melempar exception bila gagal.</summary>
    Task<string> FetchIndonesiaCsvAsync(CancellationToken ct = default);
}

public interface IWeatherProvider
{
    /// <summary>True bila punya sumber data cuaca sungguhan (API key terisi).</summary>
    bool IsConfigured { get; }
    Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default);
    Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default);
}
