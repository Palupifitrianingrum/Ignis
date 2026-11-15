using Ignis.Api.Services;

namespace Ignis.Api.Endpoints;

public sealed record ActivityRequest(string Message);

/// <summary>Route khusus admin: /api/users, /api/activity, /api/sync.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api").RequireAuthorization(Policies.Admin);

        // Daftar pengguna memuat email, jadi hanya untuk admin. Tanpa PasswordHash (lihat UserDto).
        admin.MapGet("/users", async (UserService svc) =>
            Results.Ok((await svc.GetAllAsync()).Select(u => u.ToDto()).ToList()));

        admin.MapPost("/users/{id}/toggle-active", async (string id, UserService svc) =>
            await svc.ToggleActiveAsync(id) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

        admin.MapGet("/activity", async (ActivityLogService log) => Results.Ok(await log.RecentAsync()));

        admin.MapPost("/activity", async (ActivityRequest req, ActivityLogService log) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest(new { error = "Pesan aktivitas wajib diisi." });
            await log.AddAsync(req.Message.Trim());
            return Results.NoContent();
        });

        admin.MapPost("/sync", async (SyncService sync) => Results.Ok(await sync.RunAsync()));

        // TODO(backend): /api/notifications dan /api/monitoring-areas (entity MonitoringArea/Notification sudah ada di Domain, belum ada layanannya).
    }
}
