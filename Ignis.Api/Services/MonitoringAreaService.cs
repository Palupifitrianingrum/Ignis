using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>
/// Lokasi tersimpan pengguna (mis. rumah). Setiap operasi dibatasi pada pemiliknya: lokasi milik orang lain diperlakukan
/// seolah tidak ada (null/false), sehingga pengguna tidak bisa mengetahui atau mengubah lokasi pengguna lain.
/// </summary>
public sealed class MonitoringAreaService(AppDbContext db, NotificationOptions options)
{
    public async Task<IReadOnlyList<MonitoringArea>> GetForUserAsync(string userId, CancellationToken ct = default) =>
        await db.MonitoringAreas.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.Name).ThenBy(a => a.Id).ToListAsync(ct);

    public async Task<MonitoringArea> CreateAsync(string userId, MonitoringAreaRequest req, CancellationToken ct = default)
    {
        if (await db.MonitoringAreas.CountAsync(a => a.UserId == userId, ct) >= options.MaxAreasPerUser)
            throw new DomainException($"Maksimal {options.MaxAreasPerUser} lokasi tersimpan. Hapus salah satu lokasi dulu.");

        var area = new MonitoringArea(userId, req.Name, req.Latitude, req.Longitude, req.RadiusKm);
        area.AssignId(await db.NextIdAsync(IdKind.Area, ct));
        db.MonitoringAreas.Add(area);
        await db.SaveChangesAsync(ct);
        return area;
    }

    /// <summary>Ubah lokasi milik <paramref name="userId"/>. Null bila tidak ada atau bukan miliknya.</summary>
    public async Task<MonitoringArea?> UpdateAsync(string userId, string id, MonitoringAreaRequest req, CancellationToken ct = default)
    {
        var area = await db.MonitoringAreas.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (area == null) return null;

        area.Update(req.Name, req.Latitude, req.Longitude, req.RadiusKm);
        await db.SaveChangesAsync(ct);
        return area;
    }

    /// <summary>Hapus lokasi beserta notifikasinya. False bila tidak ada atau bukan milik <paramref name="userId"/>.</summary>
    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken ct = default)
    {
        var area = await db.MonitoringAreas.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (area == null) return false;

        // Dilakukan eksplisit supaya sama di semua penyimpanan (PostgreSQL menghapusnya lewat FK cascade, EF InMemory tidak).
        db.Notifications.RemoveRange(db.Notifications.Where(n => n.AreaId == id));
        db.MonitoringAreas.Remove(area);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
