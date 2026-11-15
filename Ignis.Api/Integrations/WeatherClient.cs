using System.Globalization;
using System.Text.Json;
using Ignis.Api.Domain;
using Ignis.Shared;

namespace Ignis.Api.Integrations;

public sealed record ProviderCheck(ConnectionState State, string Message);

/// <summary>
/// Sumber data cuaca. Memakai OpenWeather bila API key tersedia; bila tidak ada key atau permintaan gagal,
/// jatuh ke data simulasi (deterministik per koordinat) supaya aplikasi tetap berjalan.
/// Catatan: belum diuji terhadap OpenWeather sungguhan (butuh API key).
/// </summary>
public sealed class WeatherClient(HttpClient http, IntegrationOptions options)
{
    private string ApiKey =>
        !string.IsNullOrWhiteSpace(options.WeatherKey)
            ? options.WeatherKey
            : Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY") ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        if (IsConfigured)
        {
            try { return await FetchAsync(latitude, longitude, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* jatuh ke simulasi */ }
        }
        return Simulate(latitude, longitude);
    }

    public async Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ProviderCheck(ConnectionState.MissingKey, "Weather dilewati: API key belum diisi.");
        try
        {
            using var r = await http.GetAsync($"{options.WeatherEndpoint}?q=Yogyakarta,ID&appid={ApiKey}&units=metric&lang=id", ct);
            return r.IsSuccessStatusCode
                ? new ProviderCheck(ConnectionState.Connected, "Weather API: terhubung.")
                : new ProviderCheck(ConnectionState.Failed, $"Weather API: HTTP {(int)r.StatusCode}.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new ProviderCheck(ConnectionState.Failed, "Weather gagal: " + ex.Message);
        }
    }

    private async Task<WeatherData> FetchAsync(double latitude, double longitude, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = $"{options.WeatherEndpoint}?lat={latitude.ToString(inv)}&lon={longitude.ToString(inv)}&appid={ApiKey}&units=metric&lang=id";
        using var resp = await http.GetAsync(url, ct);
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

    /// <summary>Data cuaca simulasi yang selalu sama untuk koordinat yang sama.</summary>
    public static WeatherData Simulate(double latitude, double longitude)
    {
        var r = new Random((int)(Math.Abs(latitude * 1000) + Math.Abs(longitude * 1000)));
        return new WeatherData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = DateTime.Now,
            Temperature = Math.Round(28.0 + r.NextDouble() * 7.0, 1),
            Humidity = Math.Round(50.0 + r.NextDouble() * 35.0, 0),
            WindSpeed = Math.Round(1.5 + r.NextDouble() * 7.0, 1),
            WindDirection = Math.Round(r.NextDouble() * 360, 0),
            Pressure = Math.Round(1008.0 + r.NextDouble() * 6.0, 0),
            Precipitation = r.NextDouble() < 0.25 ? Math.Round(r.NextDouble() * 4.0, 1) : 0,
            Condition = "Simulasi"
        };
    }
}
