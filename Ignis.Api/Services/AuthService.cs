using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Login/registrasi berhasil: pengguna + token JWT-nya (endpoint memetakannya ke <c>AuthResponse</c>).</summary>
public sealed record AuthResult(User User, string Token, DateTime ExpiresAt);

/// <summary>Kredensial salah, akun nonaktif, atau sesi tidak berlaku. Di API dipetakan ke HTTP 401.</summary>
public sealed class AuthException(string message) : Exception(message);

/// <summary>
/// Autentikasi: registrasi, login email+password, login Google, perpanjangan sesi, logout, dan ganti password.
/// Hasil login selalu token JWT dari <see cref="TokenService"/>.
///
/// Sesi dikendalikan server lewat nomor versi token pada pengguna (<see cref="User.TokenVersion"/>): logout, ganti password,
/// nonaktifkan akun, dan ganti role menaikkannya sehingga semua token lama langsung tidak berlaku (lihat Program.cs, OnTokenValidated).
/// Percobaan login gagal dibatasi oleh <see cref="LoginThrottle"/>.
/// </summary>
public sealed class AuthService(AppDbContext db, TokenService tokens, GoogleOAuthClient google, LoginThrottle throttle,
                                ActivityLogService log, JwtOptions jwt)
{
    private const string InvalidCredentials = "Email atau password salah.";
    private const string Disabled = "Akun ini dinonaktifkan. Hubungi admin.";

    // ================================================================== registrasi dan login

    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        if (Passwords.Validate(req.Password, req.Email, req.Name) is { } problem) throw new DomainException(problem);

        // Pendaftaran publik selalu menjadi RegisteredUser; Admin hanya dibuat lewat konfigurasi (Admin:*), data seed, atau diangkat oleh admin lain.
        var user = await CreateUserAsync(req.Name, req.Email, Passwords.Hash(req.Password), googleSubject: null, ct);
        return Issue(user);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var email = (req.Email ?? string.Empty).Trim();
        var key = "login:" + email;

        // Akun yang sedang terkunci ditolak tanpa memeriksa password (jawaban benar pun tidak boleh membuka kunci).
        if (throttle.RetryAfter(key) is { } wait) throw new TooManyAttemptsException(wait);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

        // Pesan sama untuk email tak dikenal dan password salah, supaya tidak bisa dipakai menebak email terdaftar.
        // Pembatasan juga berlaku sama untuk email yang tidak terdaftar.
        if (user is null || !Passwords.Verify(user.PasswordHash, req.Password ?? string.Empty))
        {
            if (throttle.Failed(key) && user != null)
                await log.AddAsync($"Akun {user.Email} dikunci sementara setelah beberapa percobaan login gagal", ct);
            throw new AuthException(InvalidCredentials);
        }
        if (!user.IsActive) throw new AuthException(Disabled);

        throttle.Succeeded(key);
        return Issue(user);
    }

    public async Task<AuthResult> LoginWithGoogleAsync(GoogleLoginRequest req, CancellationToken ct = default)
    {
        GoogleIdentity identity;
        try { identity = await google.ExchangeCodeAsync(req.Code, req.CodeVerifier, req.RedirectUri, ct); }
        catch (GoogleAuthException ex) { throw new AuthException(ex.Message); }

        // 1) akun yang sudah tertaut ke akun Google ini (kunci: sub, bukan email).
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == identity.Subject, ct);

        if (user is null)
        {
            // 2) email yang sama dengan akun yang sudah ada: tautkan.
            user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == identity.Email.ToLower(), ct);
            if (user is not null)
            {
                if (user.GoogleSubject != null) throw new AuthException("Email ini sudah tertaut ke akun Google lain.");
                user.LinkGoogle(identity.Subject);

                // Pembajakan awal (pre-hijacking): registrasi tidak memverifikasi email, jadi orang lain bisa mendaftar dengan email
                // korban + password. Saat pemilik email sungguhan masuk lewat Google, password itu dibuang dan semua token lama dicabut,
                // sehingga pihak yang mendaftar duluan tidak lagi punya akses. Pemilik tetap masuk lewat Google (dan bisa membuat password baru).
                // Akun admin dikecualikan: admin dibuat operator, bukan lewat registrasi publik.
                if (user.Role == Role.Registered && user.HasPassword)
                {
                    user.ClearPassword();
                    await log.AddAsync($"Akun {user.Email} ditautkan ke Google; password lama dibuang demi keamanan", ct);
                }
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // 3) belum ada: buat akun baru (tanpa password).
                user = await CreateUserAsync(identity.Name, identity.Email, passwordHash: string.Empty, identity.Subject, ct);
            }
        }

        if (!user.IsActive) throw new AuthException(Disabled);
        return Issue(user);
    }

    // ================================================================== sesi

    /// <summary>
    /// Akhiri sesi: naikkan nomor versi token sehingga semua token pengguna ini (di perangkat mana pun) tidak berlaku lagi.
    /// </summary>
    public async Task LogoutAsync(string userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return;
        user.RevokeTokens();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Token baru dengan masa berlaku baru untuk sesi yang masih sah. Waktu login asli dipertahankan: sesi tidak bisa diperpanjang
    /// lebih dari <see cref="JwtOptions.MaxSessionHours"/> sejak login, setelah itu pengguna harus login lagi.
    /// </summary>
    public async Task<AuthResult> RefreshAsync(string userId, DateTime? authTimeUtc, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.IsActive) throw new AuthException("Sesi tidak berlaku lagi. Silakan login ulang.");

        var started = authTimeUtc ?? DateTime.UtcNow;
        if (DateTime.UtcNow - started > TimeSpan.FromHours(Math.Max(1, jwt.MaxSessionHours)))
            throw new AuthException("Sesi sudah terlalu lama. Silakan login ulang.");

        var (token, expires) = tokens.CreateToken(user, started);
        return new AuthResult(user, token, expires);
    }

    /// <summary>
    /// Ganti password. Wajib menyebut password saat ini bila akun sudah punya password. Akun yang hanya masuk lewat Google boleh
    /// membuat password pertamanya tanpa itu (mereka sudah terautentikasi lewat Google). Semua token lama dicabut; yang baru dikembalikan
    /// supaya sesi di perangkat ini berlanjut. Salah memasukkan password saat ini dibatasi seperti login.
    /// Kesalahan input dilaporkan sebagai 400 (bukan 401), supaya aplikasi tidak mengira sesinya berakhir.
    /// </summary>
    public async Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest req, CancellationToken ct = default)
    {
        var key = "pw:" + userId;
        if (throttle.RetryAfter(key) is { } wait) throw new TooManyAttemptsException(wait);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new AuthException("Sesi tidak berlaku lagi. Silakan login ulang.");

        if (user.HasPassword)
        {
            if (!Passwords.Verify(user.PasswordHash, req.CurrentPassword ?? string.Empty))
            {
                throttle.Failed(key);
                throw new DomainException("Password saat ini salah.");
            }
        }

        if (Passwords.Validate(req.NewPassword, user.Email, user.Name) is { } problem) throw new DomainException(problem);
        if (user.HasPassword && Passwords.Verify(user.PasswordHash, req.NewPassword))
            throw new DomainException("Password baru harus berbeda dari password saat ini.");

        throttle.Succeeded(key);
        user.ChangePasswordHash(Passwords.Hash(req.NewPassword));
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"{user.Name} mengganti password", ct);
        return Issue(user);   // login baru: token lama sudah dicabut
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    // ================================================================== pembantu

    private async Task<User> CreateUserAsync(string name, string email, string passwordHash, string? googleSubject, CancellationToken ct)
    {
        email = (email ?? string.Empty).Trim();
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
            throw new DomainException("Email sudah terdaftar.");

        var user = new RegisteredUser(await db.NextIdAsync(IdKind.User, ct), name, email, passwordHash);   // validasi nama/email di konstruktor
        if (googleSubject != null) user.LinkGoogle(googleSubject);
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
