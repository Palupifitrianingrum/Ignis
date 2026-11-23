using System.Security.Claims;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;

namespace Ignis.Api.Endpoints;

/// <summary>Route /api/auth: registrasi, login (email atau Google), dan profil pengguna yang sedang login.</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // ---- Publik (tanpa token) ----
        auth.MapPost("/register", async (RegisterRequest req, AuthService svc) =>
            Results.Ok(ToResponse(await svc.RegisterAsync(req)))).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/login", async (LoginRequest req, AuthService svc) =>
            Results.Ok(ToResponse(await svc.LoginAsync(req)))).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/google", async (GoogleLoginRequest req, AuthService svc) =>
            Results.Ok(ToResponse(await svc.LoginWithGoogleAsync(req)))).AllowAnonymous().RequireRateLimiting("auth");

        // Client ID bukan rahasia; klien membutuhkannya untuk membuka halaman login Google.
        auth.MapGet("/google/config", (GoogleOAuthClient google) =>
            Results.Ok(new GoogleConfigDto(google.ClientId))).AllowAnonymous();

        // ---- Perlu token ----
        auth.MapGet("/me", async (ClaimsPrincipal principal, AuthService svc) =>
            await svc.GetByIdAsync(principal.FindFirstValue("sub") ?? string.Empty) is { } u
                ? Results.Ok(u.ToDto())
                : Results.Unauthorized()).RequireAuthorization();

        // Akhiri sesi di server: semua token pengguna ini dicabut (bukan hanya dihapus dari aplikasi).
        auth.MapPost("/logout", async (ClaimsPrincipal principal, AuthService svc) =>
        {
            await svc.LogoutAsync(principal.FindFirstValue("sub") ?? string.Empty);
            return Results.NoContent();
        }).RequireAuthorization();

        // Perpanjang sesi yang masih sah (dibatasi Jwt:MaxSessionHours sejak login).
        auth.MapPost("/refresh", async (ClaimsPrincipal principal, AuthService svc) =>
            Results.Ok(ToResponse(await svc.RefreshAsync(principal.FindFirstValue("sub") ?? string.Empty, TokenService.AuthTimeOf(principal)))))
            .RequireAuthorization();

        auth.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal, AuthService svc) =>
            Results.Ok(ToResponse(await svc.ChangePasswordAsync(principal.FindFirstValue("sub") ?? string.Empty, req))))
            .RequireAuthorization().RequireRateLimiting("auth");
    }

    private static AuthResponse ToResponse(AuthResult r) => new(r.Token, r.ExpiresAt, r.User.ToDto());
}
