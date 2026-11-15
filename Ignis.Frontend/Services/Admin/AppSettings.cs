using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Core.Abstractions;

namespace Ignis.Frontend.Services.Admin;

/// <summary>
/// Pengaturan lokal aplikasi desktop (disimpan di %AppData%\IgnisAdmin\settings.json).
/// TODO(backend): kunci/endpoint FIRMS &amp; weather, interval sync, dan retry harus pindah ke server
/// (mis. GET/PUT /api/settings) supaya API key tidak berada di mesin klien. Yang tetap lokal: CartoKey dan preferensi UI.
/// </summary>
public class AppSettings : ISettingsProvider
{
    public int IntervalIndex { get; set; } = 0; // 0:1 jam, 1:3 jam, 2:6 jam, 3:12 jam
    public string FirmsEndpoint { get; set; } = "https://firms.modaps.eosdis.nasa.gov/api/area/csv";
    public string FirmsKey { get; set; } = string.Empty;
    public string WeatherEndpoint { get; set; } = "https://api.openweathermap.org/data/2.5/weather";
    public string WeatherKey { get; set; } = string.Empty;
    public string CartoKey { get; set; } = string.Empty;   // API key basemap CARTO (opsional)
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 5;
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>"Api" (default): data lewat Ignis.Api + PostgreSQL. "Demo": data contoh di memori (offline). Env IGNIS_BACKEND menimpa.</summary>
    public string BackendMode { get; set; } = "Api";

    /// <summary>Alamat dasar Ignis.Api.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000/";

    [JsonIgnore]
    public bool UseApi => !string.Equals(
        Environment.GetEnvironmentVariable("IGNIS_BACKEND") ?? BackendMode, "Demo", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public int IntervalHours => IntervalIndex switch
    {
        0 => 1,
        1 => 3,
        2 => 6,
        _ => 12
    };

    /// <summary>Konfigurasi integrasi yang dibaca oleh klien FIRMS/OpenWeather di Ignis.Integrations.</summary>
    [JsonIgnore]
    public IntegrationSettings Current => new()
    {
        FirmsEndpoint = FirmsEndpoint,
        FirmsKey = FirmsKey,
        WeatherEndpoint = WeatherEndpoint,
        WeatherKey = WeatherKey,
        RetryCount = RetryCount,
        RetryDelayMinutes = RetryDelayMinutes
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "IgnisAdmin",
        "settings.json"
    );

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
