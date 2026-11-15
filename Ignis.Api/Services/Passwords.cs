using Ignis.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace Ignis.Api.Services;

/// <summary>Hash dan verifikasi password (PBKDF2 + salt acak bawaan ASP.NET Core Identity). Password asli tidak pernah disimpan.</summary>
public static class Passwords
{
    public const int MinLength = 8;

    private static readonly PasswordHasher<User> Hasher = new();

    public static string Hash(string password) => Hasher.HashPassword(null!, password);   // parameter user tidak dipakai hasher bawaan

    /// <summary>False untuk hash kosong (akun yang dibuat lewat Google tidak punya password).</summary>
    public static bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) &&
        Hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
