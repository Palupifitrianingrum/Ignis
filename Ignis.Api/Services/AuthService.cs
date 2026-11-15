using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Login/registrasi berhasil: pengguna + token JWT-nya (endpoint memetakannya ke <c>AuthResponse</c>).</summary>
public sealed record AuthResult(User User, string Token, DateTime ExpiresAt);

/// <summary>Login/registrasi gagal karena kredensial salah atau akun nonaktif. Di API dipetakan ke HTTP 401.</summary>
public sealed class AuthException(string message) : Exception(message);

/// <summary>
/// Autentikasi: registrasi, login email+password, dan login Google. Hasil akhirnya selalu sama: token JWT dari <see cref="TokenService"/>.
/// Logout tidak butuh endpoint: JWT bersifat stateless, klien cukup membuang tokennya (token kedaluwarsa sendiri sesuai Jwt:ExpiresMinutes).
/// </summary>
public sealed class AuthService(AppDbContext db, TokenService tokens, GoogleOAuthClient google)
{
    private const string InvalidCredentials = "Email atau password salah.";

    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < Passwords.MinLength)
            throw new DomainException($"Password minimal {Passwords.MinLength} karakter.");

        // Pendaftaran publik selalu menjadi RegisteredUser; Admin hanya dibuat lewat konfigurasi (Admin:*) atau data seed.
        var user = await CreateUserAsync(req.Name, req.Email, Passwords.Hash(req.Password), ct);
        return Issue(user);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var email = (req.Email ?? string.Empty).Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

        // Pesan sama untuk email tak dikenal dan password salah, supaya tidak bisa dipakai menebak email terdaftar.
        if (user is null || !Passwords.Verify(user.PasswordHash, req.Password ?? string.Empty))
            throw new AuthException(InvalidCredentials);
        if (!user.IsActive) throw new AuthException("Akun ini dinonaktifkan. Hubungi admin.");

        return Issue(user);
    }

    public async Task<AuthResult> LoginWithGoogleAsync(GoogleLoginRequest req, CancellationToken ct = default)
    {
        GoogleIdentity identity;
        try { identity = await google.ExchangeCodeAsync(req.Code, req.CodeVerifier, req.RedirectUri, ct); }
        catch (GoogleAuthException ex) { throw new AuthException(ex.Message); }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == identity.Email.ToLower(), ct)
                   ?? await CreateUserAsync(identity.Name, identity.Email, passwordHash: string.Empty, ct);   // tanpa password: hanya bisa masuk lewat Google
        if (!user.IsActive) throw new AuthException("Akun ini dinonaktifkan. Hubungi admin.");

        return Issue(user);
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    private async Task<User> CreateUserAsync(string name, string email, string passwordHash, CancellationToken ct)
    {
        email = (email ?? string.Empty).Trim();
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
            throw new DomainException("Email sudah terdaftar.");

        var user = new RegisteredUser(await db.NextIdAsync(IdKind.User, ct), name, email, passwordHash);   // validasi nama/email di konstruktor
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private AuthResult Issue(User user)
    {
        var (token, expires) = tokens.CreateToken(user);
        return new AuthResult(user, token, expires);
    }
}
