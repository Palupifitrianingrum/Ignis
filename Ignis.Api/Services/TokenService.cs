using System.Security.Claims;
using System.Text;
using Ignis.Api.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ignis.Api.Services;

/// <summary>Pengaturan JWT (bagian "Jwt" di appsettings / env Jwt__Key). Key kosong = dibuat acak saat start (token mati saat server restart).</summary>
public sealed class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ignis-api";
    public string Audience { get; set; } = "ignis-desktop";

    /// <summary>Masa berlaku satu token (menit). Aplikasi memperbaruinya (refresh) sebelum habis selama sesi masih boleh berlanjut.</summary>
    public int ExpiresMinutes { get; set; } = 60;

    /// <summary>Sesi terpanjang sejak login (jam). Sesudahnya refresh ditolak dan pengguna harus login lagi.</summary>
    public int MaxSessionHours { get; set; } = 12;
}

/// <summary>
/// Membuat token JWT untuk pengguna yang sudah lolos login. Validasi tanda tangan dan masa berlaku dilakukan middleware;
/// pemeriksaan "masih boleh" (akun aktif, role sama, versi token sama) dilakukan di Program.cs (OnTokenValidated).
/// </summary>
public sealed class TokenService(JwtOptions options)
{
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    /// <summary>Nomor versi token pengguna saat token dibuat. Token tidak berlaku bila nomornya tertinggal (dicabut).</summary>
    public const string VersionClaim = "ver";

    /// <summary>Waktu login asli (detik Unix). Dipertahankan saat refresh supaya sesi tidak bisa diperpanjang tanpa batas.</summary>
    public const string AuthTimeClaim = "auth_time";

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));

    /// <param name="authTimeUtc">Waktu login asli; null = sekarang (login baru).</param>
    public (string Token, DateTime ExpiresAt) CreateToken(User user, DateTime? authTimeUtc = null)
    {
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);
        var authTime = new DateTimeOffset(authTimeUtc ?? DateTime.UtcNow, TimeSpan.Zero).ToUnixTimeSeconds();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = expires,
            SigningCredentials = new SigningCredentials(SigningKey(options), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(NameClaim, user.Name),
                new Claim(RoleClaim, user.Role.ToString()),
                new Claim(VersionClaim, user.TokenVersion.ToString(), ClaimValueTypes.Integer32),
                new Claim(AuthTimeClaim, authTime.ToString(), ClaimValueTypes.Integer64),
            })
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    /// <summary>Waktu login asli dari token; null bila klaimnya tidak ada atau rusak.</summary>
    public static DateTime? AuthTimeOf(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(AuthTimeClaim), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
