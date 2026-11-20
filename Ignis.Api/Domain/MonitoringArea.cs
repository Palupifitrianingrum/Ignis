namespace Ignis.Api.Domain;

/// <summary>
/// Lokasi tersimpan milik pengguna terdaftar (mis. rumah): titik pusat + radius peringatan dalam km.
/// Notifikasi dikirim bila api atau asap hasil simulasi mendekati titik ini dalam radiusnya.
///
/// Kotak lintang/bujur (Min/Max) tetap disimpan sesuai ERD dan dihitung otomatis dari titik pusat dan radius,
/// jadi <see cref="Contains"/> tetap berlaku sebagai pemeriksaan kasar. Setter privat: nilai hanya berubah lewat konstruktor
/// dan <see cref="Update"/> yang memvalidasi semuanya terlebih dulu.
/// </summary>
public class MonitoringArea
{
    public const double MinRadiusKm = 1;
    public const double MaxRadiusKm = 50;
    public const int MaxNameLength = 120;
    private const double KmPerDegree = 111.32;

    private MonitoringArea() { }   // untuk EF Core

    public MonitoringArea(string userId, string name, double latitude, double longitude, double radiusKm)
    {
        Guard.NotBlank(userId, "Pemilik lokasi wajib diisi.");
        Apply(name, latitude, longitude, radiusKm);
        UserId = userId;
    }

    public string Id { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Titik pusat lokasi (mis. rumah).</summary>
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public double MinLatitude { get; private set; }
    public double MaxLatitude { get; private set; }
    public double MinLongitude { get; private set; }
    public double MaxLongitude { get; private set; }

    /// <summary>Radius peringatan dalam km.</summary>
    public double Radius { get; private set; }

    public void AssignId(string id)
    {
        if (!string.IsNullOrEmpty(Id)) throw new DomainException("Id lokasi sudah ditetapkan.");
        Guard.NotBlank(id, "Id lokasi tidak boleh kosong.");
        Id = id;
    }

    /// <summary>Ubah nama, titik, dan radius sekaligus. Semua divalidasi dulu; bila ada yang tidak valid, tidak ada yang berubah.</summary>
    public void Update(string name, double latitude, double longitude, double radiusKm) =>
        Apply(name, latitude, longitude, radiusKm);

    public bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude &&
        longitude >= MinLongitude && longitude <= MaxLongitude;

    private void Apply(string name, double latitude, double longitude, double radiusKm)
    {
        var trimmed = (name ?? string.Empty).Trim();
        Guard.NotBlank(trimmed, "Nama lokasi wajib diisi.");
        if (trimmed.Length > MaxNameLength) throw new DomainException($"Nama lokasi maksimal {MaxNameLength} karakter.");
        Guard.Location(latitude, longitude);
        if (double.IsNaN(radiusKm) || radiusKm < MinRadiusKm || radiusKm > MaxRadiusKm)
            throw new DomainException($"Radius harus antara {MinRadiusKm:0} dan {MaxRadiusKm:0} km.");

        // Semua sudah valid: baru ubah state.
        double halfLat = radiusKm / KmPerDegree;
        double halfLon = radiusKm / (KmPerDegree * Math.Max(0.01, Math.Cos(latitude * Math.PI / 180.0)));

        Name = trimmed;
        Latitude = latitude;
        Longitude = longitude;
        Radius = radiusKm;
        MinLatitude = Math.Max(-90, latitude - halfLat);
        MaxLatitude = Math.Min(90, latitude + halfLat);
        MinLongitude = Math.Max(-180, longitude - halfLon);
        MaxLongitude = Math.Min(180, longitude + halfLon);
    }
}
