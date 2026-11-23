using System.Security.Claims;
using Ignis.Api.Services;
using Ignis.Shared;

namespace Ignis.Api.Endpoints;

/// <summary>
/// Lokasi tersimpan dan notifikasi dampak kebakaran. Untuk akun pengguna terdaftar (sesuai ERD, tabel lokasi dan notifikasi merujuk
/// pengguna terdaftar); admin hanya bisa memicu pemeriksaan untuk semua pengguna. Setiap pengguna hanya melihat dan mengubah miliknya
/// sendiri: id milik orang lain dijawab 404, seolah tidak ada.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var user = app.MapGroup("/api").RequireAuthorization(Policies.Registered);
        var admin = app.MapGroup("/api").RequireAuthorization(Policies.Admin);

        // ---------- Lokasi tersimpan ----------
        user.MapGet("/monitoring-areas", async (ClaimsPrincipal principal, MonitoringAreaService areas) =>
            Results.Ok((await areas.GetForUserAsync(UserId(principal))).Select(a => a.ToDto()).ToList()));

        // Setelah lokasi dibuat/diubah langsung diperiksa, supaya pengguna segera tahu bila lokasinya sudah terancam.
        user.MapPost("/monitoring-areas", async (MonitoringAreaRequest req, ClaimsPrincipal principal, MonitoringAreaService areas,
                                                 NotificationService notifications, ILogger<NotificationService> log) =>
        {
            var area = await areas.CreateAsync(UserId(principal), req);
            await TryEvaluateAsync(() => notifications.EvaluateAreaAsync(area.Id), log);
            return Results.Created($"/api/monitoring-areas/{area.Id}", area.ToDto());
        });

        user.MapPut("/monitoring-areas/{id}", async (string id, MonitoringAreaRequest req, ClaimsPrincipal principal, MonitoringAreaService areas,
                                                     NotificationService notifications, ILogger<NotificationService> log) =>
        {
            if (await areas.UpdateAsync(UserId(principal), id, req) is not { } area) return Results.NotFound();
            await TryEvaluateAsync(() => notifications.EvaluateAreaAsync(area.Id), log);
            return Results.Ok(area.ToDto());
        });

        user.MapDelete("/monitoring-areas/{id}", async (string id, ClaimsPrincipal principal, MonitoringAreaService areas) =>
            await areas.DeleteAsync(UserId(principal), id) ? Results.NoContent() : Results.NotFound());

        // ---------- Notifikasi ----------
        user.MapGet("/notifications", async (ClaimsPrincipal principal, NotificationService notifications, bool? unreadOnly, int? limit) =>
            Results.Ok(await notifications.ListAsync(UserId(principal), unreadOnly ?? false, limit ?? 50)));

        user.MapPost("/notifications/{id}/read", async (string id, ClaimsPrincipal principal, NotificationService notifications) =>
            await notifications.MarkReadAsync(UserId(principal), id) ? Results.NoContent() : Results.NotFound());

        user.MapPost("/notifications/read-all", async (ClaimsPrincipal principal, NotificationService notifications) =>
        {
            await notifications.MarkAllReadAsync(UserId(principal));
            return Results.NoContent();
        });

        user.MapDelete("/notifications/{id}", async (string id, ClaimsPrincipal principal, NotificationService notifications) =>
            await notifications.DeleteAsync(UserId(principal), id) ? Results.NoContent() : Results.NotFound());

        // Periksa sekarang untuk lokasi milik sendiri (mis. setelah cuaca berubah).
        user.MapPost("/notifications/check", async (ClaimsPrincipal principal, NotificationService notifications) =>
        {
            var r = await notifications.EvaluateUserAsync(UserId(principal));
            return Results.Ok(new CheckResultDto(r.Areas, r.HotspotsSimulated, r.Created));
        });

        // Admin: periksa semua lokasi semua pengguna.
        admin.MapPost("/notifications/evaluate", async (NotificationService notifications) =>
        {
            var r = await notifications.EvaluateAllAsync();
            return Results.Ok(new CheckResultDto(r.Areas, r.HotspotsSimulated, r.Created));
        });
    }

    private static string UserId(ClaimsPrincipal principal) => principal.FindFirstValue("sub") ?? string.Empty;

    /// <summary>Pemeriksaan notifikasi tidak boleh menggagalkan operasi utamanya (mis. menyimpan lokasi).</summary>
    public static async Task TryEvaluateAsync(Func<Task<EvaluationSummary>> evaluate, ILogger log)
    {
        try
        {
            await evaluate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Pemeriksaan notifikasi gagal");
        }
    }
}
