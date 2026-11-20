using System.Globalization;
using System.Net;
using System.Text.Json;
using Ignis.Api.Domain;
using Ignis.Shared;

namespace Ignis.Api.Integrations;

/// <summary>
/// Sumber data cuaca. Memakai OpenWeather Current Weather (<c>data/2.5/weather</c>, units=metric) bila API key tersedia.
/// Bila tidak ada key atau permintaan gagal, <see cref="GetWeatherAsync"/> jatuh ke data simulasi (deterministik per koordinat) supaya
/// aplikasi tetap berjalan; alasan kegagalan dicatat di log dan <see cref="IntegrationStatus"/>.
///
/// Satuan menurut dokumentasi OpenWeather (units=metric): suhu °C, tekanan hPa, kelembapan %, kecepatan angin m/s,
/// hujan <c>rain.1h</c> mm/jam. <c>wind.deg</c> adalah arah ASAL angin (konvensi meteorologi), disimpan apa adanya di
/// <see cref="WeatherData.WindDirection"/>; simulasi dan prediksi sudah memperhitungkannya.
/// </summary>
public sealed class WeatherClient(HttpClient http, IntegrationOptions options,
                                  IntegrationStatus? status = null, ILogger<WeatherClient>? logger = null)
{
    private const string Provider = "OpenWeather";

    private string ApiKey => options.EffectiveWeatherKey;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Cuaca nyata dari OpenWeather, atau null bila key kosong atau pengambilan gagal (alasannya dicatat). Tidak melempar.</summary>
    public async Task<WeatherData?> TryFetchAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        try
        {
            var data = await FetchAsync(latitude, longitude, ct);
            status?.WeatherSucceeded();
            return data;
        }
        catch (IntegrationException ex) when (!ct.IsCancellationRequested)
        {
            status?.WeatherFailed(ex.Message);
            logger?.LogWarning("Cuaca OpenWeather gagal ({Failure}), memakai data lain: {Message}", ex.Failure, ex.Message);
            return null;
        }
    }

    /// <summary>Cuaca nyata bila bisa; bila tidak, data simulasi (Condition = "Simulasi").</summary>
    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default) =>
        await TryFetchAsync(latitude, longitude, ct) ?? Simulate(latitude, longitude);

    /// <summary>Periksa key dan koneksi dengan satu panggilan cuaca untuk satu koordinat tetap.</summary>
    public async Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new ProviderCheck(ConnectionState.MissingKey,
                "Weather dilewati: API key belum diisi (Integrations__WeatherKey). Cuaca memakai data simulasi.");
        try
        {
            await FetchAsync(0.32, 101.85, ct);   // Riau
            status?.WeatherSucceeded();
            return new ProviderCheck(ConnectionState.Connected, "Weather API: terhubung, key valid (OpenWeather).");
        }
        catch (IntegrationException ex)
        {
            status?.WeatherFailed(ex.Message);
            return ProviderCheck.From(ex);
        }
    }

    private async Task<WeatherData> FetchAsync(double latitude, double longitude, CancellationToken ct)
    {
        var key = ApiKey;
        var inv = CultureInfo.InvariantCulture;
        var url = $"{options.WeatherEndpoint}?lat={latitude.ToString(inv)}&lon={longitude.ToString(inv)}" +
                  $"&appid={Uri.EscapeDataString(key)}&units=metric&lang=id";
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) throw Classify(resp.StatusCode);
            return Parse(await resp.Content.ReadAsStringAsync(ct), latitude, longitude);
        }
        catch (HttpRequestException ex)
        {
            throw new IntegrationException(Provider, IntegrationFailure.Unavailable,
                "Tidak dapat menghubungi OpenWeather: " + IntegrationText.Redact(ex.Message, key), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IntegrationException(Provider, IntegrationFailure.Unavailable, "OpenWeather tidak menjawab (timeout).", ex);
        }
    }

    private static IntegrationException Classify(HttpStatusCode status)
    {
        var code = (int)status;
        return status switch
        {
            HttpStatusCode.Unauthorized =>
                new(Provider, IntegrationFailure.InvalidKey,
                    "API key OpenWeather ditolak (HTTP 401): key salah, belum aktif (key baru bisa butuh sampai 2 jam setelah mendaftar), " +
                    "atau paket Anda tidak mencakup endpoint ini."),
            HttpStatusCode.TooManyRequests =>
                new(Provider, IntegrationFailure.RateLimited, "Batas panggilan OpenWeather terlampaui (HTTP 429; paket gratis 60 panggilan per menit)."),
            >= HttpStatusCode.InternalServerError =>
                new(Provider, IntegrationFailure.Unavailable, $"OpenWeather sedang bermasalah (HTTP {code})."),
            _ =>
                new(Provider, IntegrationFailure.BadResponse, $"OpenWeather menjawab HTTP {code}.")
        };
    }

    /// <summary>Urai jawaban Current Weather. Field wajib: main.temp dan main.humidity; sisanya opsional dengan nilai 0.</summary>
    public static WeatherData Parse(string json, double latitude, double longitude)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("main", out var main) ||
                !main.TryGetProperty("temp", out var temp) || !main.TryGetProperty("humidity", out var humidity))
                throw new IntegrationException(Provider, IntegrationFailure.BadResponse,
                    "Jawaban OpenWeather tidak memuat main.temp dan main.humidity.");

            var wind = root.TryGetProperty("wind", out var w) && w.ValueKind == JsonValueKind.Object ? w : default;
            double precip = root.TryGetProperty("rain", out var rain) && rain.ValueKind == JsonValueKind.Object
                            && rain.TryGetProperty("1h", out var r1) ? r1.GetDouble() : 0;

            return new WeatherData
            {
                Latitude = latitude,
                Longitude = longitude,
                Timestamp = DateTime.Now,
                Temperature = temp.GetDouble(),
                Humidity = humidity.GetDouble(),
                Pressure = main.TryGetProperty("pressure", out var p) ? p.GetDouble() : 0,
                WindSpeed = Number(wind, "speed"),
                WindDirection = Number(wind, "deg"),
                Precipitation = precip,
                Condition = root.TryGetProperty("weather", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                            && arr[0].TryGetProperty("description", out var d)
                    ? d.GetString() ?? string.Empty
                    : string.Empty
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new IntegrationException(Provider, IntegrationFailure.BadResponse, "Jawaban OpenWeather tidak dapat dibaca.", ex);
        }
    }

    private static double Number(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) ? v.GetDouble() : 0;

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
