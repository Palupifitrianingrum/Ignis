using Ignis.Core.Abstractions;
using Ignis.Core.Entities;

namespace Ignis.Infrastructure.Services;

/// <summary>
/// TODO(backend): notifikasi belum diimplementasikan. Rencana:
///  - CheckNewHotspotsAsync: ambil MonitoringArea milik user, cari hotspot dengan DetectedAt &gt; since
///    yang berada di dalam area (MonitoringArea.Contains) — butuh IMonitoringAreaRepository.
///  - SendAsync: simpan Notification, lalu dorong ke klien (SignalR / polling endpoint /api/notifications).
/// </summary>
public sealed class NotificationService : INotifier
{
    public Task<IReadOnlyList<Hotspot>> CheckNewHotspotsAsync(string userId, DateTime since, CancellationToken ct = default)
    {
        // var areas = await _areas.GetByUserAsync(userId, ct);
        // var all   = await _hotspots.GetAllAsync(ct);
        // return all.Where(h => h.DetectedAt > since && areas.Any(a => a.Contains(h.Latitude, h.Longitude))).ToList();
        throw new NotImplementedException("Pengecekan hotspot baru per area pantauan belum diimplementasikan.");
    }

    public Task SendAsync(Notification notification, CancellationToken ct = default)
    {
        // await _notifications.AddAsync(notification, ct);
        // await _hub.Clients.User(notification.UserId).SendAsync("notification", notification, ct);
        throw new NotImplementedException("Pengiriman notifikasi belum diimplementasikan.");
    }
}
