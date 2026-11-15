using Ignis.Core.Enums;
using Ignis.Core.ValueObjects;

namespace Ignis.Core.Entities;

/// <summary>Data cuaca di sekitar sebuah hotspot (tabel WEATHER_DATA, milik satu hotspot).</summary>
public class WeatherData
{
    public string Id { get; set; } = string.Empty;
    public string HotspotId { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double Temperature { get; set; }
    public double Humidity { get; set; }
    public double WindSpeed { get; set; }
    public double WindDirection { get; set; }
    public double Pressure { get; set; }
    public double Precipitation { get; set; }
    public string Condition { get; set; } = string.Empty;

    public Vector GetWindVector() => new(WindSpeed, WindDirection);
}

/// <summary>Hasil prediksi penyebaran api untuk satu hotspot, berdasarkan satu data cuaca.</summary>
public class Prediction
{
    public string Id { get; set; } = string.Empty;
    public string HotspotId { get; set; } = string.Empty;

    /// <summary>Cuaca yang dipakai (nullable: data demo lama tidak punya baris cuaca).</summary>
    public string? WeatherId { get; set; }

    public double Direction { get; set; }   // spreadDirection
    public double SpeedKmH { get; set; }    // spreadSpeed
    public double AreaHa { get; set; }      // estimatedArea
    public double Hours { get; set; }       // estimatedTime
    public RiskLevel Risk { get; set; }     // riskLevel
    public DateTime CreatedAt { get; set; }
}

/// <summary>Area pantauan milik pengguna terdaftar (kotak lintang/bujur + radius).</summary>
public class MonitoringArea
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double MinLatitude { get; set; }
    public double MaxLatitude { get; set; }
    public double MinLongitude { get; set; }
    public double MaxLongitude { get; set; }
    public double Radius { get; set; }

    public bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude &&
        longitude >= MinLongitude && longitude <= MaxLongitude;
}

public class Notification
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string HotspotId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsRead { get; private set; }

    public void MarkAsRead() => IsRead = true;
}
