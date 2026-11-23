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

    /// <summary>
    /// Impor hasil parse FIRMS; yang sudah ada (waktu scan dan koordinat sama) dilewati. Mengembalikan jumlah yang baru.
    /// Satu query untuk memeriksa yang sudah ada, Id diambil sekaligus, dan simpan per kelompok, jadi ribuan baris tidak
    /// berarti ribuan perjalanan ke database.
    /// </summary>
    public async Task<int> ImportSatelliteAsync(IEnumerable<SatelliteHotspot> incoming, CancellationToken ct = default)
    {
        var list = incoming.ToList();
        if (list.Count == 0) return 0;

        // Kunci kesamaan: waktu scan + koordinat dibulatkan 5 desimal (±1 m). Juga membuang duplikat di dalam CSV itu sendiri,
        // yang akan melanggar indeks unik (detected_at, latitude, longitude) di database.
        static (long Ticks, long Lat, long Lon) Key(DateTime t, double lat, double lon) =>
            (t.Ticks, (long)Math.Round(lat * 1e5), (long)Math.Round(lon * 1e5));

        var from = list.Min(h => h.ScanTime);
        var to = list.Max(h => h.ScanTime);
        var existing = await db.Hotspots.OfType<SatelliteHotspot>().AsNoTracking()
            .Where(x => x.ScanTime >= from && x.ScanTime <= to)
            .Select(x => new { x.ScanTime, x.Latitude, x.Longitude })
            .ToListAsync(ct);
        var seen = existing.Select(x => Key(x.ScanTime, x.Latitude, x.Longitude)).ToHashSet();

        var fresh = new List<SatelliteHotspot>();
        foreach (var h in list)
            if (seen.Add(Key(h.ScanTime, h.Latitude, h.Longitude))) fresh.Add(h);
        if (fresh.Count == 0) return 0;

        var ids = await db.NextIdsAsync(IdKind.Hotspot, fresh.Count, ct);
        for (int i = 0; i < fresh.Count; i++) fresh[i].AssignId(ids[i]);

        const int batch = 500;   // transaksi dibatasi agar tidak membengkak
        for (int i = 0; i < fresh.Count; i += batch)
        {
            db.Hotspots.AddRange(fresh.Skip(i).Take(batch));
            await db.SaveChangesAsync(ct);
        }

        await log.AddAsync($"Sync NASA FIRMS: +{fresh.Count} hotspot baru", ct);
        return fresh.Count;
    }
}
