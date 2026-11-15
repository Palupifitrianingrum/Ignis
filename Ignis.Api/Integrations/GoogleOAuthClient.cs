using System.Text.Json;
using Google.Apis.Auth;

namespace Ignis.Api.Integrations;

/// <summary>
/// Kredensial OAuth Google (bagian "Google" di appsettings, atau env Google__ClientId / Google__ClientSecret).
/// Cara membuatnya: lihat docs/google-oauth.md. Kosong = tombol "Login dengan Google" ditolak dengan pesan jelas.
/// </summary>
public sealed class GoogleOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Identitas pengguna yang sudah diverifikasi Google.</summary>
public sealed record GoogleIdentity(string Email, string Name);

/// <summary>
/// Menukar kode otorisasi Google (alur desktop: PKCE + loopback) menjadi identitas pengguna.
/// Penukaran dilakukan di server supaya client secret tidak berada di aplikasi desktop; id_token diverifikasi tanda tangan,
/// audience, dan masa berlakunya oleh Google.Apis.Auth.
/// </summary>
public sealed class GoogleOAuthClient(HttpClient http, GoogleOptions options)
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);
    public string? ClientId => IsConfigured ? options.ClientId : null;

    /// <summary>Melempar <see cref="GoogleAuthException"/> bila kode ditolak Google, id_token tidak valid, atau email belum terverifikasi.</summary>
    public async Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new GoogleAuthException("Login Google belum dikonfigurasi di server.");

        using var resp = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        }), ct);
        if (!resp.IsSuccessStatusCode) throw new GoogleAuthException("Google menolak kode login. Coba login ulang.");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("id_token", out var idToken) || idToken.GetString() is not { } jwt)
            throw new GoogleAuthException("Jawaban Google tidak berisi id_token.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(jwt, new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { options.ClientId } });
        }
        catch (InvalidJwtException)
        {
            throw new GoogleAuthException("Token Google tidak valid.");
        }

        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            throw new GoogleAuthException("Email akun Google belum terverifikasi.");

        return new GoogleIdentity(payload.Email, string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name);
    }
}

public sealed class GoogleAuthException(string message) : Exception(message);
