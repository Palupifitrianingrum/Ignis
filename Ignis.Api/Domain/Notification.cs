using Ignis.Shared;

namespace Ignis.Api.Domain;

/// <summary>
/// Notifikasi untuk pengguna terdaftar: api atau asap hasil simulasi sebuah hotspot diperkirakan mendekati salah satu lokasi tersimpannya.
/// Memuat data terstruktur (jenis, tingkat, perkiraan waktu tiba, jarak) selain teks, supaya aplikasi bisa memberi warna dan mengurutkan,
/// dan supaya notifikasi yang sama tidak dikirim berulang (lihat NotificationService).
/// </summary>
public class Notification
{
    public const int MaxTitleLength = 200;

    private Notification() { }   // untuk EF Core

    /// <param name="level">1 = perhatian, 2 = waspada, 3 = bahaya.</param>
    /// <param name="etaHours">Perkiraan jam sampai api/asap mencapai radius (0 = sudah berada di dalam radius).</param>
    /// <param name="distanceKm">Api: jarak terdekat tepi api ke lokasi selama simulasi. Asap: jarak lokasi ke hotspot sumber asap (km).</param>
    public Notification(string userId, string hotspotId, string? areaId, NotificationKind kind, int level, int etaHours,
                        double distanceKm, string title, string message, DateTime? createdAt = null)
    {
        Guard.NotBlank(userId, "Penerima notifikasi wajib diisi.");
        Guard.NotBlank(hotspotId, "Hotspot sumber notifikasi wajib diisi.");
        Guard.NotBlank(title, "Judul notifikasi wajib diisi.");
        Guard.NotBlank(message, "Isi notifikasi wajib diisi.");
        if (title.Length > MaxTitleLength) throw new DomainException($"Judul notifikasi maksimal {MaxTitleLength} karakter.");
        if (level is < 1 or > 3) throw new DomainException("Tingkat notifikasi harus 1 sampai 3.");
        if (etaHours < 0) throw new DomainException("Perkiraan waktu tidak boleh negatif.");
        if (double.IsNaN(distanceKm) || distanceKm < 0) throw new DomainException("Jarak tidak boleh negatif.");

        UserId = userId;
        HotspotId = hotspotId;
        AreaId = areaId;
        Kind = kind;
        Level = level;
        EtaHours = etaHours;
        DistanceKm = Math.Round(distanceKm, 1);
        Title = title.Trim();
        Message = message.Trim();
        CreatedAt = createdAt ?? DateTime.Now;
    }

    public string Id { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string HotspotId { get; private set; } = string.Empty;
    public string? AreaId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int Level { get; private set; }
    public int EtaHours { get; private set; }
    public double DistanceKm { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.Now;
    public bool IsRead { get; private set; }

    public void AssignId(string id)
    {
        if (!string.IsNullOrEmpty(Id)) throw new DomainException("Id notifikasi sudah ditetapkan.");
        Guard.NotBlank(id, "Id notifikasi tidak boleh kosong.");
        Id = id;
    }

    public void MarkAsRead() => IsRead = true;
}
