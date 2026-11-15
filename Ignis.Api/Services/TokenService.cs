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
    public int ExpiresMinutes { get; set; } = 60;
}

/// <summary>Membuat token JWT untuk pengguna yang sudah lolos login. Validasi token dilakukan middleware (lihat Program.cs).</summary>
public sealed class TokenService(JwtOptions options)
{
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));

    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);
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
            })
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
