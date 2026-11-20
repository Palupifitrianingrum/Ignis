using Ignis.Api.Domain;
using Ignis.Api.Services;

namespace Ignis.Api.Support;

/// <summary>
/// Menerjemahkan exception aturan bisnis menjadi respons HTTP yang pesannya langsung bisa ditampilkan klien:
/// DomainException -> 400, AuthException -> 401, TooManyAttemptsException -> 429 + Retry-After. Format: { "error": "pesan" }.
/// </summary>
public static class ErrorHandling
{
    public static void UseDomainErrors(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        try { await next(); }
        catch (DomainException ex) { await Write(ctx, StatusCodes.Status400BadRequest, ex.Message); }
        catch (AuthException ex) { await Write(ctx, StatusCodes.Status401Unauthorized, ex.Message); }
        catch (TooManyAttemptsException ex)
        {
            ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(ex.RetryAfter.TotalSeconds)).ToString();
            await Write(ctx, StatusCodes.Status429TooManyRequests, ex.Message);
        }
    });

    private static Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }
}
