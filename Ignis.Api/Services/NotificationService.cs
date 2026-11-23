using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Ringkasan satu putaran pemeriksaan.</summary>
public sealed record EvaluationSummary(int Areas, int HotspotsSimulated, int Created);

/// <summary>
/// Notifikasi dampak kebakaran: memeriksa apakah api atau asap hasil simulasi mendekati lokasi tersimpan pengguna.
///
/// Alur untuk tiap lokasi: (1) pilih hotspot kandidat yang masih relevan dan cukup dekat (terdekat dari tiap gugus, paling banyak
/// <see cref="NotificationOptions.MaxCandidatesPerArea"/>), (2) simulasikan tiap kandidat dengan cuaca terbaru, (3) nilai apakah api/asap
/// mencapai radius lokasi dalam horizon, (4) ambil kasus terburuk per jenis (api, asap), (5) simpan notifikasi bila belum ada yang sejenis
/// dengan tingkat sama atau lebih tinggi dalam masa jeda.
///
/// Jadi satu lokasi menerima paling banyak dua notifikasi per putaran (api dan asap), bukan satu per hotspot. Putaran dijalankan
/// setelah sinkronisasi hotspot, setelah admin menambah/mengubah hotspot manual, dan saat pengguna menambah lokasi atau meminta pemeriksaan.
/// </summary>
public sealed class NotificationService(AppDbContext db, PredictionService predictions, FireSpreadSimulator simulator,
                                        NotificationOptions options, ILogger<NotificationService>? logger = null)
{
    // Satu putaran pada satu waktu: dua putaran bersamaan bisa saling tidak melihat notifikasi yang baru dibuat dan mengirim ganda.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // ================================================================== pemeriksaan

    public Task<EvaluationSummary> EvaluateAllAsync(CancellationToken ct = default) => EvaluateAsync(null, null, null, ct);

    public Task<EvaluationSummary> EvaluateUserAsync(string userId, CancellationToken ct = default) => EvaluateAsync(userId, null, null, ct);

    public Task<EvaluationSummary> EvaluateAreaAsync(string areaId, CancellationToken ct = default) => EvaluateAsync(null, areaId, null, ct);

    /// <summary>Hotspot baru/berubah: periksa semua lokasi yang dekat dengannya.</summary>
    public Task<EvaluationSummary> EvaluateHotspotAsync(string hotspotId, CancellationToken ct = default) => EvaluateAsync(null, null, hotspotId, ct);

    private async Task<EvaluationSummary> EvaluateAsync(string? userId, string? areaId, string? onlyHotspotId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            int horizon = Math.Clamp(options.HorizonHours, 1, FireSpreadSimulator.MaxHours);

            // Hanya pengguna aktif yang diberi notifikasi.
            var activeUsers = await db.Users.OfType<RegisteredUser>().AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct);
            var areas = await db.MonitoringAreas.AsNoTracking()
                .Where(a => activeUsers.Contains(a.UserId)
                            && (userId == null || a.UserId == userId)
                            && (areaId == null || a.Id == areaId))
                .ToListAsync(ct);
            if (areas.Count == 0) return new EvaluationSummary(0, 0, 0);

            var cutoff = DateTime.Now.AddHours(-Math.Max(1, options.SatelliteMaxAgeHours));
            var hotspots = await db.Hotspots.AsNoTracking()
                .Where(h => h.Status != HotspotStatus.Resolved
                            && (h.Source == HotspotSource.Manual || h.DetectedAt >= cutoff)
                            && (onlyHotspotId == null || h.Id == onlyHotspotId))
                .ToListAsync(ct);

            var recent = await LoadRecentAsync(areas, ct);
            var simulations = new Dictionary<string, SimulationResult?>();
            var fresh = new List<Notification>();

            foreach (var area in areas)
            {
                var worstFire = default((Hotspot H, AreaExposure E, SimulationResult S)?);
                var worstSmoke = default((Hotspot H, AreaExposure E, SimulationResult S)?);

                foreach (var h in PickCandidates(area, hotspots))
                {
                    var sim = await SimulateAsync(h, horizon, simulations, ct);
                    if (sim == null) continue;

                    var exposure = ExposureEvaluator.Evaluate(sim, area.Latitude, area.Longitude, area.Radius, horizon);
                    if (exposure.FireReaches && IsWorseFire(exposure, worstFire?.E)) worstFire = (h, exposure, sim);
                    if (exposure.SmokeReaches && IsWorseSmoke(exposure, worstSmoke?.E)) worstSmoke = (h, exposure, sim);
                }

                if (worstFire is { } f)
                {
                    int level = FireLevel(f.E.FireEtaHours!.Value);
                    if (ShouldNotify(recent, area.Id, NotificationKind.Fire, level))
                    {
                        var (title, message) = NotificationText.Fire(area, f.H, f.E, f.S, horizon, level);
                        fresh.Add(new Notification(area.UserId, f.H.Id, area.Id, NotificationKind.Fire, level,
                                                   f.E.FireEtaHours!.Value, f.E.FireMinDistanceKm, title, message));
                    }
                }

                if (worstSmoke is { } s)
                {
                    int level = s.E.SmokeMaxLevel;
                    if (ShouldNotify(recent, area.Id, NotificationKind.Smoke, level))
                    {
                        var (title, message) = NotificationText.Smoke(area, s.H, s.E, s.S, horizon);
                        fresh.Add(new Notification(area.UserId, s.H.Id, area.Id, NotificationKind.Smoke, level,
                                                   s.E.SmokeEtaHours ?? 0, s.E.HotspotDistanceKm, title, message));
                    }
                }
            }

            if (fresh.Count > 0)
            {
                var ids = await db.NextIdsAsync(IdKind.Notification, fresh.Count, ct);
                for (int i = 0; i < fresh.Count; i++) fresh[i].AssignId(ids[i]);
                db.Notifications.AddRange(fresh);
                await db.SaveChangesAsync(ct);
            }

            return new EvaluationSummary(areas.Count, simulations.Count(x => x.Value != null), fresh.Count);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Hotspot terdekat dari tiap gugus di sekitar lokasi, paling banyak <see cref="NotificationOptions.MaxCandidatesPerArea"/>.</summary>
    private List<Hotspot> PickCandidates(MonitoringArea area, IReadOnlyList<Hotspot> hotspots)
    {
        var near = hotspots
            .Select(h => (H: h, D: Geo.DistanceKm(area.Latitude, area.Longitude, h.Latitude, h.Longitude)))
            .Where(x => x.D <= options.CandidateRadiusKm)
            .OrderBy(x => x.D).ThenByDescending(x => x.H.Confidence).ThenBy(x => x.H.Id)
            .ToList();

        var picked = new List<Hotspot>();
        foreach (var (h, _) in near)
        {
            if (picked.Count >= Math.Max(1, options.MaxCandidatesPerArea)) break;
            if (picked.All(p => Geo.DistanceKm(p.Latitude, p.Longitude, h.Latitude, h.Longitude) >= options.ClusterSeparationKm))
                picked.Add(h);
        }
        return picked;
    }

    private async Task<SimulationResult?> SimulateAsync(Hotspot h, int horizon, Dictionary<string, SimulationResult?> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(h.Id, out var cached)) return cached;
        try
        {
            var weather = await predictions.GetWeatherAsync(h, ct);
            return cache[h.Id] = simulator.Run(h, weather, horizon);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Satu hotspot yang gagal disimulasikan tidak boleh menggagalkan notifikasi untuk yang lain.
            logger?.LogError(ex, "Simulasi hotspot {HotspotId} gagal saat memeriksa notifikasi", h.Id);
            return cache[h.Id] = null;
        }
    }

    // ================================================================== aturan tingkat dan pencegahan pengiriman berulang

    /// <summary>Tingkat peringatan api menurut perkiraan waktu tiba: ≤3 jam bahaya, ≤8 jam waspada, selebihnya perhatian.</summary>
    public static int FireLevel(int etaHours) => etaHours <= 3 ? 3 : etaHours <= 8 ? 2 : 1;

    private static bool IsWorseFire(AreaExposure candidate, AreaExposure? current) =>
        current == null
        || candidate.FireEtaHours < current.FireEtaHours
        || (candidate.FireEtaHours == current.FireEtaHours && candidate.FireMinDistanceKm < current.FireMinDistanceKm);

    private static bool IsWorseSmoke(AreaExposure candidate, AreaExposure? current) =>
        current == null
        || candidate.SmokeMaxLevel > current.SmokeMaxLevel
        || (candidate.SmokeMaxLevel == current.SmokeMaxLevel && candidate.SmokeEtaHours < current.SmokeEtaHours);

    /// <summary>Tingkat tertinggi notifikasi sejenis (lokasi + jenis) yang dikirim dalam masa jeda.</summary>
    private async Task<Dictionary<(string AreaId, NotificationKind Kind), int>> LoadRecentAsync(IReadOnlyList<MonitoringArea> areas, CancellationToken ct)
    {
        var ids = areas.Select(a => a.Id).ToList();
        var since = DateTime.Now.AddHours(-Math.Max(1, options.CooldownHours));
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => n.AreaId != null && ids.Contains(n.AreaId) && n.CreatedAt >= since)
            .Select(n => new { AreaId = n.AreaId!, n.Kind, n.Level })
            .ToListAsync(ct);

        return rows.GroupBy(r => (r.AreaId, r.Kind)).ToDictionary(g => g.Key, g => g.Max(r => r.Level));
    }

    /// <summary>Kirim bila belum ada yang sejenis dalam masa jeda, atau tingkatnya naik (eskalasi).</summary>
    private static bool ShouldNotify(Dictionary<(string AreaId, NotificationKind Kind), int> recent, string areaId, NotificationKind kind, int level) =>
        !recent.TryGetValue((areaId, kind), out var previous) || level > previous;

    // ================================================================== membaca dan mengelola notifikasi milik pengguna

    public async Task<NotificationListDto> ListAsync(string userId, bool unreadOnly, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var unread = await query.CountAsync(n => !n.IsRead, ct);

        var items = await (unreadOnly ? query.Where(n => !n.IsRead) : query)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(ct);

        var areaNames = await db.MonitoringAreas.AsNoTracking().Where(a => a.UserId == userId).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        return new NotificationListDto(unread, items.Select(n => ToDto(n, areaNames)).ToList());
    }

    public static NotificationDto ToDto(Notification n, IReadOnlyDictionary<string, string> areaNames) => new(
        n.Id, n.HotspotId, n.AreaId, n.AreaId != null && areaNames.TryGetValue(n.AreaId, out var name) ? name : null,
        n.Kind, n.Level, n.EtaHours, n.DistanceKm, n.Title, n.Message, n.CreatedAt, n.IsRead);

    /// <summary>Tandai satu notifikasi dibaca. False bila tidak ada atau bukan milik <paramref name="userId"/>.</summary>
    public async Task<bool> MarkReadAsync(string userId, string id, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n == null) return false;
        n.MarkAsRead();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var n in unread) n.MarkAsRead();
        await db.SaveChangesAsync(ct);
        return unread.Count;
    }

    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (n == null) return false;
        db.Notifications.Remove(n);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
