using System.Security.Claims;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;

namespace Ignis.Api.Endpoints;

/// <summary>
/// Route admin untuk mengatur API key layanan luar dari aplikasi: /api/settings/integrations.
/// Key lengkap tidak pernah dikembalikan. Penjelasan prioritas dan keamanan: <see cref="IntegrationKeyStore"/>.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/settings/integrations").RequireAuthorization(Policies.Admin);

        // Keadaan key tiap layanan: asal (override/.env/kosong) dan versi bertopeng.
        admin.MapGet("/", async (IntegrationKeyStore store) => Results.Ok(await store.GetAllAsync()));

        // Uji key tanpa menyimpannya. Key kosong = uji key yang sedang berlaku.
        admin.MapPost("/{provider}/test", async (string provider, TestIntegrationKeyRequest? req, IntegrationKeyStore store) =>
        {
            if (!IntegrationKeyStore.IsKnown(provider)) return Results.NotFound();
            var key = req?.Key?.Trim();
            if (!string.IsNullOrEmpty(key) && !IntegrationKeyStore.IsPlausibleKey(key))
                return Results.BadRequest(new { error = InvalidFormat });

            var check = await store.TestAsync(provider, key);
            return Results.Ok(new ProviderStatusDto(store.NameOf(provider), true, check.State, check.Message));
        });

        // Ganti key: diuji dulu, disimpan hanya bila valid. Key yang gagal tidak pernah menimpa key yang sedang jalan.
        admin.MapPut("/{provider}", async (string provider, SetIntegrationKeyRequest req, IntegrationKeyStore store,
                                           ActivityLogService log, ClaimsPrincipal user) =>
        {
            if (!IntegrationKeyStore.IsKnown(provider)) return Results.NotFound();

            var key = req.Key?.Trim() ?? string.Empty;
            if (!IntegrationKeyStore.IsPlausibleKey(key)) return Results.BadRequest(new { error = InvalidFormat });

            var check = await store.TestAsync(provider, key);
            if (check.State != ConnectionState.Connected)
                return Results.BadRequest(new { error = $"Key tidak disimpan karena gagal diuji: {check.Message}" });

            var admin = AdminName(user);
            await store.SetAsync(provider, key, admin);
            await log.AddAsync($"Admin {admin} mengganti key {store.NameOf(provider)} (override; nilai tidak dicatat)");
            return Results.Ok(await store.GetAsync(provider));
        });

        // Kembalikan ke default: buang override, key .env berlaku lagi.
        admin.MapDelete("/{provider}", async (string provider, IntegrationKeyStore store, ActivityLogService log, ClaimsPrincipal user) =>
        {
            if (!IntegrationKeyStore.IsKnown(provider)) return Results.NotFound();

            if (await store.ResetAsync(provider))
                await log.AddAsync($"Admin {AdminName(user)} mengembalikan key {store.NameOf(provider)} ke default (.env)");
            return Results.Ok(await store.GetAsync(provider));
        });
    }

    private const string InvalidFormat =
        "Format key tidak valid: harus 8 sampai 200 karakter tanpa spasi. Salin ulang key dari email/akun penyedia.";

    private static string AdminName(ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value ?? user.Identity?.Name ?? "tidak diketahui";
}
