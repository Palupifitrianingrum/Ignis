namespace Ignis.Api.Domain;

/// <summary>Notifikasi hotspot untuk pengguna terdaftar. Belum dipakai endpoint (lihat README).</summary>
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
