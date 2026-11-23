namespace Ignis.Api.Domain;

/// <summary>Data cuaca di sekitar sebuah hotspot (tabel weather_data, milik satu hotspot).</summary>
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
