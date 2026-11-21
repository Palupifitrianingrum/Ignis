using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignis.Frontend.Services.Admin;

/// <summary>
/// Pengaturan lokal aplikasi desktop (disimpan di %AppData%\IgnisAdmin\settings.json): alamat API, interval sinkronisasi, key peta CARTO,
/// dan preferensi UI. API key FIRMS/OpenWeather TIDAK disimpan di sini: keduanya milik server dan diatur admin lewat
/// halaman Pengaturan (disimpan terenkripsi di server). Versi lama menyimpannya di file ini dalam teks biasa;
/// <see cref="Load"/> membuangnya dari disk.
/// </summary>
public class AppSettings
{
    public int IntervalIndex { get; set; } = 0; // 0:1 jam, 1:3 jam, 2:6 jam, 3:12 jam
    public string CartoKey { get; set; } = string.Empty;   // API key basemap CARTO (opsional)
    public bool NotificationsEnabled { get; set; } = true;
    public string DefaultRegionName { get; set; } = "Riau";
    public bool HideResolvedHotspots { get; set; } = false;
    public int AutoRefreshMinutes { get; set; } = 0;       // 0 = Manual, 5, 15, 30

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
            {
                var text = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(text) ?? new();

                // File dari versi lama memuat API key layanan luar dalam teks biasa. Tulis ulang tanpa kunci itu.
                if (text.Contains("FirmsKey", StringComparison.OrdinalIgnoreCase) || text.Contains("WeatherKey", StringComparison.OrdinalIgnoreCase))
                    settings.Save();
                return settings;
            }
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
