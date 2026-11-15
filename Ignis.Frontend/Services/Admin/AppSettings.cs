using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignis.Frontend.Services.Admin;

/// <summary>
/// Pengaturan lokal aplikasi desktop (disimpan di %AppData%\IgnisAdmin\settings.json).
/// Catatan: kunci/endpoint FIRMS dan weather, serta retry, sekarang dibaca SERVER dari konfigurasinya sendiri
/// (Integrations__* di .env server), bukan dari sini — isian di halaman Pengaturan belum dipakai siapa pun.
/// TODO(backend): sediakan GET/PUT /api/settings (admin) atau hapus isian itu dari halaman Pengaturan.
/// Yang memang lokal: ApiBaseUrl, CartoKey, dan preferensi UI.
/// </summary>
public class AppSettings
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

    /// <summary>Alamat dasar Ignis.Api. Env IGNIS_API_URL menimpa (berguna untuk menunjuk ke server tanpa mengubah file).</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000/";

    [JsonIgnore]
    public int IntervalHours => IntervalIndex switch
    {
        0 => 1,
        1 => 3,
        2 => 6,
        _ => 12
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
