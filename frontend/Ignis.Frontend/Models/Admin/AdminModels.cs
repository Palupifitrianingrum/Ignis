using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ignis.Frontend.Models.Admin;

public enum HotspotSource { Satellite, Manual }
public enum HotspotStatus { Active, Monitoring, Resolved }
public enum RiskLevel { Low, Medium, High, Critical }

public static class Fmt
{
    private static readonly CultureInfo Id = new("id-ID");
    public static string Date(DateTime d) => d.ToString("dd MMM yyyy, HH:mm", Id);
    public static string Inv(double v, string f = "0.0000") => v.ToString(f, CultureInfo.InvariantCulture);
}

public class Hotspot
{
    public string Id { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DetectedAt { get; set; }
    public double Confidence { get; set; }
    public double Brightness { get; set; }
    public HotspotSource Source { get; set; }
    public HotspotStatus Status { get; set; }
    public string Note { get; set; } = string.Empty;
    public string ReportedBy { get; set; } = string.Empty;
    public string SatelliteName { get; set; } = string.Empty;
    public string Instrument { get; set; } = string.Empty;

    public bool IsManual => Source == HotspotSource.Manual;
    public string CoordText => $"{Fmt.Inv(Latitude)}, {Fmt.Inv(Longitude)}";
    public string SourceText => IsManual ? "Manual" : "Satelit";
    public string StatusText => Status switch
    {
        HotspotStatus.Active => "Aktif",
        HotspotStatus.Monitoring => "Dipantau",
        _ => "Selesai"
    };
    public string DetectedText => Fmt.Date(DetectedAt);
    public string ConfidenceText => $"{Confidence:0}%";
    public string BrightnessText => Brightness > 0 ? $"{Brightness:0.0} K" : "-";
}

public class WeatherData
{
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double WindSpeed { get; set; }
    public double WindDirection { get; set; }
    public double Pressure { get; set; }
    public double Precipitation { get; set; }
}

public class Prediction
{
    public string Id { get; set; } = string.Empty;
    public string HotspotId { get; set; } = string.Empty;
    public double Direction { get; set; }
    public double SpeedKmH { get; set; }
    public double AreaHa { get; set; }
    public double Hours { get; set; }
    public RiskLevel Risk { get; set; }
    public DateTime CreatedAt { get; set; }

    public string DirectionText => $"{Direction:0}°";
    public string SpeedText => $"{SpeedKmH:0.00} km/jam";
    public string AreaText => $"{AreaHa:0.0} ha";
    public string HoursText => $"{Hours:0} jam";
    public string RiskText => Risk.ToString().ToUpperInvariant();
    public string CreatedText => Fmt.Date(CreatedAt);
}

public class UserAccount
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Terdaftar"; // "Admin" | "Terdaftar"
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Non-aktif";
}

public record Region(string Name, double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    public bool IsAll => Name == "Semua daerah";
    public bool Contains(double lat, double lon) => lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;
}

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

