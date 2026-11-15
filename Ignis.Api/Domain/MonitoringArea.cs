namespace Ignis.Api.Domain;

/// <summary>Area pantauan milik pengguna terdaftar (kotak lintang/bujur + radius). Belum dipakai endpoint (lihat README).</summary>
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
