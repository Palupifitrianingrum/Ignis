namespace Ignis.Shared;

/// <summary>Wilayah acuan (kotak lintang/bujur) untuk filter daftar hotspot di aplikasi desktop.</summary>
public record Region(string Name, double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    public const string AllName = "Semua daerah";

    public bool IsAll => Name == AllName;
    public bool Contains(double lat, double lon) => lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;

    public static readonly IReadOnlyList<Region> Defaults = new List<Region>
    {
        new(AllName, -90, 90, -180, 180),
        new("Riau", -1.2, 2.6, 100.0, 104.6),
        new("Jambi", -2.6, -0.7, 101.0, 104.6),
        new("Sumatera Selatan", -4.9, -1.9, 102.0, 106.0),
        new("Kalimantan Barat", -3.1, 2.1, 108.6, 114.2),
        new("Kalimantan Tengah", -3.6, 0.8, 111.0, 115.8),
        new("DI Yogyakarta", -8.2, -7.5, 110.0, 110.9),
    };
}
