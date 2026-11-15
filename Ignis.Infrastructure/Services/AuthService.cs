using Ignis.Core.Abstractions;
using Ignis.Core.Entities;

namespace Ignis.Infrastructure.Services;

/// <summary>
/// TODO(backend): autentikasi belum diimplementasikan. Rencana:
///  - Authenticate: cari user via IUserRepository.GetByEmailAsync, verifikasi hash password
///    (BCrypt/Argon2 atau Microsoft.AspNetCore.Identity PasswordHasher), tolak bila !IsActive.
///  - Register: validasi, hash password, simpan user baru (Role.User) — butuh IUserRepository.AddAsync.
///  - Logout: cabut refresh token/sesi bila memakai token berumur panjang.
///  - Di Ignis.Api: terbitkan JWT (Microsoft.AspNetCore.Authentication.JwtBearer) dan lindungi endpoint admin.
///  - Login Google: tukar authorization code di server (lihat TODO di LoginFormViewModel).
/// </summary>
public sealed class AuthService : IAuthService
{
    public Task<User?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        // var user = await _users.GetByEmailAsync(email, ct);
        // if (user is null || !user.IsActive || !_hasher.Verify(password, user.PasswordHash)) return null;
        // return user;
        throw new NotImplementedException("Autentikasi belum diimplementasikan.");
    }

    public Task<User?> RegisterAsync(string name, string email, string password, CancellationToken ct = default)
    {
        // var user = new User { Id = ..., Name = name, Email = email, PasswordHash = _hasher.Hash(password), Role = Role.User };
        // await _users.AddAsync(user, ct);
        // return user;
        throw new NotImplementedException("Registrasi belum diimplementasikan.");
    }

    public Task LogoutAsync(string userId, CancellationToken ct = default)
    {
        // TODO(backend): invalidate sesi/refresh token.
        throw new NotImplementedException("Logout belum diimplementasikan.");
    }
}
