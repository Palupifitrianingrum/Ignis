namespace Ignis.Api.Integrations;

/// <summary>Konfigurasi layanan luar (bagian "Integrations" di appsettings, atau env Integrations__FirmsKey dst.). Dibaca di server, bukan di klien.</summary>
public sealed class IntegrationOptions
{
    public string FirmsEndpoint { get; set; } = "https://firms.modaps.eosdis.nasa.gov/api/area/csv";
    public string FirmsKey { get; set; } = string.Empty;
    public string WeatherEndpoint { get; set; } = "https://api.openweathermap.org/data/2.5/weather";
    public string WeatherKey { get; set; } = string.Empty;   // bila kosong dicoba env OPENWEATHER_API_KEY (lihat WeatherClient)
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 5;
}
