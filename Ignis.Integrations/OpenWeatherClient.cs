using System.Globalization;
using System.Text.Json;
using Ignis.Core.Abstractions;
using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Integrations;

/// <summary>
/// Klien OpenWeather (cuaca saat ini). Menggantikan console app lama di backend/Program.cs.
/// Catatan: belum diuji terhadap API sungguhan (butuh API key).
/// </summary>
public sealed class OpenWeatherClient : IWeatherProvider
{
    private readonly HttpClient _http;
    private readonly ISettingsProvider _settings;

    public OpenWeatherClient(HttpClient http, ISettingsProvider settings)
    {
        _http = http;
        _settings = settings;
    }

    private string ApiKey =>
        !string.IsNullOrWhiteSpace(_settings.Current.WeatherKey)
            ? _settings.Current.WeatherKey
            : Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY") ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("API key OpenWeather belum diisi.");

        var inv = CultureInfo.InvariantCulture;
        var url = $"{_settings.Current.WeatherEndpoint}?lat={latitude.ToString(inv)}&lon={longitude.ToString(inv)}&appid={ApiKey}&units=metric&lang=id";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var main = root.GetProperty("main");
        var wind = root.TryGetProperty("wind", out var w) ? w : default;

        double precip = 0;
        if (root.TryGetProperty("rain", out var rain) && rain.TryGetProperty("1h", out var r1)) precip = r1.GetDouble();

        return new WeatherData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = DateTime.Now,
            Temperature = main.GetProperty("temp").GetDouble(),
            Humidity = main.GetProperty("humidity").GetDouble(),
            Pressure = main.TryGetProperty("pressure", out var p) ? p.GetDouble() : 0,
            WindSpeed = wind.ValueKind == JsonValueKind.Object && wind.TryGetProperty("speed", out var ws) ? ws.GetDouble() : 0,
            WindDirection = wind.ValueKind == JsonValueKind.Object && wind.TryGetProperty("deg", out var wd) ? wd.GetDouble() : 0,
            Precipitation = precip,
            Condition = root.TryGetProperty("weather", out var arr) && arr.GetArrayLength() > 0
                ? arr[0].GetProperty("description").GetString() ?? string.Empty
                : string.Empty
        };
    }

    public async Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ProviderCheck(ConnectionState.MissingKey, "Weather dilewati: API key belum diisi.");
        try
        {
            using var r = await _http.GetAsync(
                $"{_settings.Current.WeatherEndpoint}?q=Yogyakarta,ID&appid={ApiKey}&units=metric&lang=id", ct);
            return r.IsSuccessStatusCode
                ? new ProviderCheck(ConnectionState.Connected, "Weather API: terhubung.")
                : new ProviderCheck(ConnectionState.Failed, $"Weather API: HTTP {(int)r.StatusCode}.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ProviderCheck(ConnectionState.Failed, "Weather gagal: " + ex.Message);
        }
    }
}
