using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>
/// Use case hotspot: ambil, tambah manual, ubah, hapus, dan impor dari satelit.
/// Service ini hanya mengorkestrasi; aturan (validasi, boleh-tidaknya diubah) ada di entity Hotspot dan subclass-nya.
/// </summary>
public sealed class HotspotService(AppDbContext db, ActivityLogService log)
{
    /// <summary>Semua hotspot (terbaru dulu), opsional difilter sumber/status.</summary>
    public async Task<IReadOnlyList<Hotspot>> GetAllAsync(HotspotSource? source = null, HotspotStatus? status = null, CancellationToken ct = default) =>
        await db.Hotspots.AsNoTracking()
            .Where(h => (source == null || h.Source == source) && (status == null || h.Status == status))
            .OrderByDescending(h => h.DetectedAt)
            .ToListAsync(ct);

    public Task<Hotspot?> GetByIdAsync(string id, CancellationToken ct = default) =>
        db.Hotspots.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);

    /// <summary>Tambah hotspot manual. Validasi dilakukan konstruktor ManualHotspot (melempar DomainException).</summary>
    public async Task<ManualHotspot> AddManualAsync(ManualHotspotRequest req, CancellationToken ct = default)
    {
        var hotspot = new ManualHotspot(req.Latitude, req.Longitude, req.Confidence, req.IncidentTime,
                                        req.ReportedBy, req.Note, req.Status);
        hotspot.AssignId(await db.NextIdAsync(IdKind.Hotspot, ct));
        db.Hotspots.Add(hotspot);
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"Admin menambah hotspot manual {hotspot.Id}", ct);
        return hotspot;
    }

    public async Task<ManualHotspot> UpdateManualAsync(string id, ManualHotspotRequest req, CancellationToken ct = default)
    {
        var existing = await db.Hotspots.FirstOrDefaultAsync(h => h.Id == id, ct) ?? throw new DomainException($"Hotspot {id} tidak ditemukan.");
        if (existing is not ManualHotspot manual || !existing.IsEditable)
            throw new DomainException("Hotspot satelit bersumber dari API dan tidak dapat diubah.");

        manual.UpdateReport(req.Latitude, req.Longitude, req.Confidence, req.IncidentTime, req.ReportedBy, req.Note, req.Status);
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"Admin mengubah hotspot {id}", ct);
        return manual;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await db.Hotspots.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (existing == null) return false;
        if (!existing.IsEditable)   // polimorfik: satelit false, manual true
            throw new DomainException("Hanya hotspot manual yang dapat dihapus.");

        db.Predictions.RemoveRange(db.Predictions.Where(p => p.HotspotId == id));
        db.WeatherData.RemoveRange(db.WeatherData.Where(w => w.HotspotId == id));
        db.Hotspots.Remove(existing);
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"Admin menghapus hotspot {id}", ct);
        return true;
    }

    /// <summary>Impor hasil parse FIRMS; yang sudah ada (waktu scan dan koordinat sama) dilewati. Mengembalikan jumlah yang baru.</summary>
    public async Task<int> ImportSatelliteAsync(IEnumerable<SatelliteHotspot> incoming, CancellationToken ct = default)
    {
        int added = 0;
        foreach (var h in incoming)
        {
            var exists = await db.Hotspots.OfType<SatelliteHotspot>().AnyAsync(x =>
                x.ScanTime == h.ScanTime && Math.Abs(x.Latitude - h.Latitude) < 1e-5 && Math.Abs(x.Longitude - h.Longitude) < 1e-5, ct);
            if (exists) continue;

            h.AssignId(await db.NextIdAsync(IdKind.Hotspot, ct));
            db.Hotspots.Add(h);
            await db.SaveChangesAsync(ct);
            added++;
        }
        if (added > 0) await log.AddAsync($"Sync NASA FIRMS: +{added} hotspot baru", ct);
        return added;
    }
}
