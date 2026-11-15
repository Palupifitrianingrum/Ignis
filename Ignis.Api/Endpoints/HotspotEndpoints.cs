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
        admin.MapPost("/hotspots/manual", async (ManualHotspotRequest req, HotspotService svc) =>
        {
            var saved = await svc.AddManualAsync(req);
            return Results.Created($"/api/hotspots/{saved.Id}", saved.ToDto());
        });

        admin.MapPut("/hotspots/{id}", async (string id, ManualHotspotRequest req, HotspotService svc) =>
            Results.Ok((await svc.UpdateManualAsync(id, req)).ToDto()));

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

        api.MapGet("/hotspots/{id}/simulation", async (string id, int? hours, HotspotService hs, PredictionService ps, FireSpreadSimulator sim) =>
        {
            if (await hs.GetByIdAsync(id) is not { } h) return Results.NotFound();
            var weather = await ps.GetWeatherAsync(h);
            var frames = sim.Simulate(h, weather, Math.Clamp(hours ?? 6, 1, 24)).Select(f => f.ToDto()).ToList();
            return Results.Ok(frames);
        });
    }
}
