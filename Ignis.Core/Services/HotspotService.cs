using Ignis.Core.Abstractions;
using Ignis.Core.Common;
using Ignis.Core.Contracts;
using Ignis.Core.Entities;

namespace Ignis.Core.Services;

/// <summary>
/// Use case hotspot: ambil, tambah manual, ubah, hapus, dan impor dari satelit.
/// Service ini hanya mengorkestrasi; aturan (validasi, boleh-tidaknya diubah) ada di entity Hotspot dan subclass-nya.
/// </summary>
public sealed class HotspotService
{
    private readonly IHotspotRepository _repo;
    private readonly IActivityLog _log;

    public HotspotService(IHotspotRepository repo, IActivityLog log)
    {
        _repo = repo;
        _log = log;
    }

    public Task<IReadOnlyList<Hotspot>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);
    public Task<Hotspot?> GetByIdAsync(string id, CancellationToken ct = default) => _repo.GetByIdAsync(id, ct);

    /// <summary>Tambah hotspot manual. Validasi dilakukan konstruktor ManualHotspot (melempar DomainException).</summary>
    public async Task<ManualHotspot> AddManualAsync(ManualHotspotRequest req, CancellationToken ct = default)
    {
        var hotspot = new ManualHotspot(req.Latitude, req.Longitude, req.Confidence, req.IncidentTime,
                                        req.ReportedBy, req.Note, req.Status);
        var saved = (ManualHotspot)await _repo.AddAsync(hotspot, ct);
        await _log.AddAsync($"Admin menambah hotspot manual {saved.Id}", ct);
        return saved;
    }

    public async Task<ManualHotspot> UpdateManualAsync(string id, ManualHotspotRequest req, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct) ?? throw new DomainException($"Hotspot {id} tidak ditemukan.");
        if (existing is not ManualHotspot manual || !existing.IsEditable)
            throw new DomainException("Hotspot satelit bersumber dari API dan tidak dapat diubah.");

        manual.UpdateReport(req.Latitude, req.Longitude, req.Confidence, req.IncidentTime, req.ReportedBy, req.Note, req.Status);
        await _repo.UpdateAsync(manual, ct);
        await _log.AddAsync($"Admin mengubah hotspot {id}", ct);
        return manual;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing == null) return false;
        if (!existing.IsEditable)   // polimorfik: satelit false, manual true
            throw new DomainException("Hanya hotspot manual yang dapat dihapus.");

        var deleted = await _repo.DeleteAsync(id, ct);
        if (deleted) await _log.AddAsync($"Admin menghapus hotspot {id}", ct);
        return deleted;
    }

    /// <summary>Impor hasil parse FIRMS; yang sudah ada (waktu scan dan koordinat sama) dilewati. Mengembalikan jumlah yang baru.</summary>
    public async Task<int> ImportSatelliteAsync(IEnumerable<SatelliteHotspot> incoming, CancellationToken ct = default)
    {
        int added = 0;
        foreach (var h in incoming)
        {
            if (await _repo.SatelliteExistsAsync(h.ScanTime, h.Latitude, h.Longitude, ct)) continue;
            await _repo.AddAsync(h, ct);
            added++;
        }
        if (added > 0) await _log.AddAsync($"Sync NASA FIRMS: +{added} hotspot baru", ct);
        return added;
    }
}
