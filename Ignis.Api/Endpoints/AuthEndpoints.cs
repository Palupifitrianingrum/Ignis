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
            Results.Ok(ToResponse(await svc.RegisterAsync(req)))).AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest req, AuthService svc) =>
            Results.Ok(ToResponse(await svc.LoginAsync(req)))).AllowAnonymous();

        auth.MapPost("/google", async (GoogleLoginRequest req, AuthService svc) =>
            Results.Ok(ToResponse(await svc.LoginWithGoogleAsync(req)))).AllowAnonymous();

        // Client ID bukan rahasia; klien membutuhkannya untuk membuka halaman login Google.
        auth.MapGet("/google/config", (GoogleOAuthClient google) =>
            Results.Ok(new GoogleConfigDto(google.ClientId))).AllowAnonymous();

        // ---- Perlu token ----
        auth.MapGet("/me", async (ClaimsPrincipal principal, AuthService svc) =>
            await svc.GetByIdAsync(principal.FindFirstValue("sub") ?? string.Empty) is { } u
                ? Results.Ok(u.ToDto())
                : Results.Unauthorized()).RequireAuthorization();
    }

    private static AuthResponse ToResponse(AuthResult r) => new(r.Token, r.ExpiresAt, r.User.ToDto());
}
