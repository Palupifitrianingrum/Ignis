using System.Text.Json;
using Google.Apis.Auth;

namespace Ignis.Api.Integrations;

/// <summary>
/// Kredensial OAuth Google (bagian "Google" di appsettings, atau env Google__ClientId / Google__ClientSecret).
/// Cara membuatnya: lihat docs/google-oauth.md. Client ID kosong = tombol "Login dengan Google" ditolak dengan pesan jelas.
///
/// Client secret OPSIONAL: dokumentasi Google menyebut aplikasi desktop sebagai "public client" yang tidak bisa merahasiakan secret
/// (pengamannya PKCE), dan parameter <c>client_secret</c> di endpoint token bersifat opsional. Bila diisi, dikirim; bila Google
/// menolak karena kurang secret, pesannya meminta Anda mengisinya.
/// </summary>
public sealed class GoogleOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Identitas pengguna yang sudah diverifikasi Google. <see cref="Subject"/> (klaim "sub") adalah pengenal akun yang stabil;
/// dokumentasi Google menyarankannya sebagai kunci pengguna, bukan email (email bisa berubah dan tidak dijamin unik).
/// </summary>
public sealed record GoogleIdentity(string Subject, string Email, string Name);

/// <summary>Memverifikasi id_token Google dan mengembalikan identitasnya. Dipisah supaya jalur sukses bisa diuji tanpa jaringan.</summary>
public interface IGoogleIdTokenVerifier
{
    /// <exception cref="GoogleAuthException">Token tidak valid, bukan untuk client ini, kedaluwarsa, atau email belum terverifikasi.</exception>
    Task<GoogleIdentity> VerifyAsync(string idToken, string clientId, CancellationToken ct = default);
}

/// <summary>
/// Verifikasi resmi lewat Google.Apis.Auth: tanda tangan (kunci publik Google), penerbit (accounts.google.com), audience (= Client ID kita),
/// dan masa berlaku, sesuai dokumentasi "OpenID Connect" Google. Email wajib sudah terverifikasi.
/// </summary>
public sealed class GoogleLibraryVerifier : IGoogleIdTokenVerifier
{
    public async Task<GoogleIdentity> VerifyAsync(string idToken, string clientId, CancellationToken ct = default)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
        }
        catch (InvalidJwtException)
        {
            throw new GoogleAuthException("Token Google tidak valid.");
        }

        if (string.IsNullOrWhiteSpace(payload.Subject)) throw new GoogleAuthException("Token Google tidak memuat id akun.");
        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            throw new GoogleAuthException("Email akun Google belum terverifikasi.");

        return new GoogleIdentity(payload.Subject, payload.Email, string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name);
    }
}

/// <summary>
/// Menukar kode otorisasi Google (alur desktop: PKCE + loopback 127.0.0.1) menjadi identitas pengguna.
/// Penukaran dilakukan di server supaya tidak ada rahasia di aplikasi desktop.
/// </summary>
public sealed class GoogleOAuthClient(HttpClient http, GoogleOptions options, IGoogleIdTokenVerifier? verifier = null)
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private readonly IGoogleIdTokenVerifier _verifier = verifier ?? new GoogleLibraryVerifier();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ClientId);
    public string? ClientId => IsConfigured ? options.ClientId.Trim() : null;

    /// <summary>Melempar <see cref="GoogleAuthException"/> bila kode ditolak Google, id_token tidak valid, atau email belum terverifikasi.</summary>
    public async Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        if (!IsConfigured) throw new GoogleAuthException("Login Google belum dikonfigurasi di server (Google__ClientId kosong).");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(codeVerifier) || string.IsNullOrWhiteSpace(redirectUri))
            throw new GoogleAuthException("Permintaan login Google tidak lengkap.");

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.ClientId.Trim(),
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        };
        if (!string.IsNullOrWhiteSpace(options.ClientSecret)) form["client_secret"] = options.ClientSecret.Trim();

        HttpResponseMessage resp;
        string body;
        try
        {
            resp = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            throw new GoogleAuthException("Tidak dapat menghubungi Google. Periksa koneksi internet server.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleAuthException("Google tidak menjawab (timeout). Coba lagi.");
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode) throw new GoogleAuthException(ExplainError(body));
        }

        string? idToken;
        try
        {
            using var doc = JsonDocument.Parse(body);
            idToken = doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            idToken = null;
        }
        if (string.IsNullOrEmpty(idToken)) throw new GoogleAuthException("Jawaban Google tidak berisi id_token.");

        return await _verifier.VerifyAsync(idToken, options.ClientId.Trim(), ct);
    }

    /// <summary>
    /// Terjemahkan jawaban error endpoint token Google ({"error": "...", "error_description": "..."}) menjadi pesan yang bisa ditindaklanjuti.
    /// Bentuk teramati langsung: HTTP 401 {"error":"invalid_client","error_description":"The OAuth client was not found."}.
    /// Pesan tidak memuat client secret.
    /// </summary>
    public static string ExplainError(string body)
    {
        string error = string.Empty, description = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            description = doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            // bukan JSON: pakai pesan umum di bawah
        }

        if (description.Contains("client_secret", StringComparison.OrdinalIgnoreCase))
            return "Google meminta client secret. Isi Google__ClientSecret di server (salin dari halaman klien OAuth di Google Cloud).";

        return error switch
        {
            "invalid_grant" => "Kode login Google tidak berlaku lagi (kedaluwarsa atau sudah dipakai). Coba login ulang.",
            "invalid_client" => "Google tidak mengenali klien OAuth ini. Periksa Google__ClientId dan Google__ClientSecret di server.",
            "redirect_uri_mismatch" => "Alamat pengalihan tidak cocok dengan klien OAuth. Pastikan jenis klien di Google Cloud adalah Desktop app.",
            "invalid_request" => "Permintaan ke Google tidak valid. Coba login ulang.",
            "" => "Google menolak kode login. Coba login ulang.",
            _ => $"Google menolak login ({error}). Coba login ulang."
        };
    }
}

public sealed class GoogleAuthException(string message) : Exception(message);
