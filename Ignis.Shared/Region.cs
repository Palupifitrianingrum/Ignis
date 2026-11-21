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
        new("Aceh", 1.9, 5.9, 95.0, 98.3),
        new("Bali", -8.9, -8.0, 114.4, 115.7),
        new("Banten", -7.0, -5.8, 105.1, 106.8),
        new("Bengkulu", -5.5, -2.2, 101.0, 103.8),
        new("DI Yogyakarta", -8.2, -7.5, 110.0, 110.9),
        new("DKI Jakarta", -6.4, -5.2, 106.4, 107.0),
        new("Gorontalo", 0.3, 1.1, 121.1, 123.6),
        new("Jambi", -2.6, -0.7, 101.0, 104.6),
        new("Jawa Barat", -7.8, -5.9, 106.3, 108.9),
        new("Jawa Tengah", -8.2, -6.4, 108.5, 111.7),
        new("Jawa Timur", -8.8, -6.7, 110.9, 114.6),
        new("Kalimantan Barat", -3.1, 2.1, 108.6, 114.2),
        new("Kalimantan Selatan", -4.2, -1.3, 114.3, 116.6),
        new("Kalimantan Tengah", -3.6, 0.8, 110.7, 115.8),
        new("Kalimantan Timur", -2.5, 2.6, 113.8, 119.0),
        new("Kalimantan Utara", 1.2, 4.4, 114.6, 118.0),
        new("Kepulauan Bangka Belitung", -3.8, -1.5, 105.1, 108.9),
        new("Kepulauan Riau", -1.2, 4.8, 103.2, 109.2),
        new("Lampung", -6.1, -3.7, 103.5, 105.9),
        new("Maluku", -8.3, -2.7, 125.7, 134.9),
        new("Maluku Utara", -2.5, 2.7, 124.3, 129.7),
        new("Nusa Tenggara Barat", -9.1, -8.0, 115.8, 119.3),
        new("Nusa Tenggara Timur", -11.0, -8.1, 118.9, 125.2),
        new("Papua", -3.6, -0.8, 134.8, 141.0),
        new("Papua Barat", -4.3, -0.3, 132.0, 135.2),
        new("Papua Barat Daya", -2.6, 0.2, 129.6, 133.5),
        new("Papua Pegunungan", -5.2, -3.2, 137.8, 141.0),
        new("Papua Selatan", -9.2, -5.0, 137.2, 141.0),
        new("Papua Tengah", -5.0, -2.5, 134.8, 138.2),
        new("Riau", -1.2, 2.6, 100.0, 104.6),
        new("Sulawesi Barat", -3.6, -0.8, 118.7, 119.9),
        new("Sulawesi Selatan", -7.5, -1.9, 118.7, 121.8),
        new("Sulawesi Tengah", -3.6, 1.4, 119.4, 124.2),
        new("Sulawesi Tenggara", -5.9, -2.8, 120.8, 124.6),
        new("Sulawesi Utara", 0.3, 4.8, 123.1, 127.2),
        new("Sumatera Barat", -3.5, 0.9, 98.5, 101.9),
        new("Sumatera Selatan", -4.9, -1.6, 102.0, 106.2),
        new("Sumatera Utara", -0.6, 4.3, 97.0, 100.4),
    };
}
