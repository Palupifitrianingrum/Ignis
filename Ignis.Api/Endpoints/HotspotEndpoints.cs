using System.Security.Claims;
using Ignis.Api.Services;
using Ignis.Shared;

namespace Ignis.Api.Endpoints;

/// <summary>Route /api/hotspots, /api/predictions: data hotspot, cuaca, dan prediksi. Baca = pengguna login; ubah = admin.</summary>
public static class HotspotEndpoints
{
    public static void MapHotspotEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        var admin = app.MapGroup("/api").RequireAuthorization(Policies.Admin);

        // ---------- Hotspot ----------
        api.MapGet("/hotspots", async (HotspotService svc, HotspotSource? source, HotspotStatus? status) =>
            Results.Ok((await svc.GetAllAsync(source, status)).Select(h => h.ToDto()).ToList()));

        api.MapGet("/hotspots/{id}", async (string id, HotspotService svc) =>
            await svc.GetByIdAsync(id) is { } h ? Results.Ok(h.ToDto()) : Results.NotFound());

        // Fitur utama tugas OOP: tambah hotspot manual (lihat HotspotService.AddManualAsync + hierarki Hotspot).
        // Setelah hotspot manual ditambah/diubah, evaluasi notifikasi dijalankan di background agar response cepat.
        admin.MapPost("/hotspots/manual", async (ManualHotspotRequest req, HotspotService svc,
                                                 IServiceScopeFactory scopeFactory, ILogger<NotificationService> log) =>
        {
            var saved = await svc.AddManualAsync(req);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await NotificationEndpoints.TryEvaluateAsync(() => notifications.EvaluateHotspotAsync(saved.Id), log);
            });
            return Results.Created($"/api/hotspots/{saved.Id}", saved.ToDto());
        });

        admin.MapPut("/hotspots/{id}", async (string id, ManualHotspotRequest req, HotspotService svc,
                                              IServiceScopeFactory scopeFactory, ILogger<NotificationService> log) =>
        {
            var updated = await svc.UpdateManualAsync(id, req);
            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await NotificationEndpoints.TryEvaluateAsync(() => notifications.EvaluateHotspotAsync(updated.Id), log);
            });
            return Results.Ok(updated.ToDto());
        });

        admin.MapDelete("/hotspots/{id}", async (string id, HotspotService svc) =>
            await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

        // ---------- Cuaca & prediksi ----------
        api.MapGet("/hotspots/{id}/weather", async (string id, HotspotService hs, PredictionService svc) =>
            await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.GetWeatherAsync(h)).ToDto()) : Results.NotFound());

        api.MapGet("/predictions", async (PredictionService svc) =>
            Results.Ok((await svc.GetAllAsync()).Select(p => p.ToDto()).ToList()));

        api.MapGet("/hotspots/{id}/prediction", async (string id, HotspotService hs, PredictionService svc) =>
            await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.GetOrPredictAsync(h)).ToDto()) : Results.NotFound());

        admin.MapPost("/hotspots/{id}/predictions", async (string id, HotspotService hs, PredictionService svc) =>
            await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.PredictAsync(h)).ToDto()) : Results.NotFound());

        // Simulasi penyebaran api (cellular automata). Untuk semua pengguna yang login. `hours` dibatasi 1..24 (default 6).
        // Memakai cuaca terakhir yang tersimpan untuk hotspot itu (diambil dulu bila belum ada), jadi hasilnya stabil antar-panggilan.
        api.MapGet("/hotspots/{id}/simulation", async (string id, int? hours, ClaimsPrincipal user, HotspotService hs, PredictionService ps,
                                                       FireSpreadSimulator sim, MonitoringAreaService areas) =>
        {
            if (await hs.GetByIdAsync(id) is not { } h) return Results.NotFound();
            var weather = await ps.GetWeatherAsync(h);
            var hoursClamped = Math.Clamp(hours ?? 6, 1, FireSpreadSimulator.MaxHours);
            var result = sim.Run(h, weather, hoursClamped);

            // Dampak pada lokasi tersimpan pengguna yang melihat (hanya untuk pengguna terdaftar): api/asap mencapai radius lokasi?
            var impacts = new List<AreaImpactDto>();
            if (user.IsInRole(nameof(Role.Registered)) && user.FindFirstValue("sub") is { } userId)
            {
                foreach (var a in await areas.GetForUserAsync(userId))
                {
                    var e = ExposureEvaluator.Evaluate(result, a.Latitude, a.Longitude, a.Radius, result.Hours);
                    impacts.Add(new AreaImpactDto(a.Id, a.Name, e.HotspotDistanceKm, e.FireEtaHours, e.FireMinDistanceKm, e.SmokeMaxLevel, e.SmokeEtaHours));
                }
            }
            return Results.Ok(result.ToDto(impacts));
        });
    }
}
