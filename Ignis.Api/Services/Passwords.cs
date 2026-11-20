using Ignis.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace Ignis.Api.Services;

/// <summary>
/// Hash, verifikasi, dan kebijakan password. Hash memakai PBKDF2 + salt acak bawaan ASP.NET Core Identity; password asli tidak pernah disimpan.
///
/// Kebijakan mengikuti panduan NIST SP 800-63B: yang penting panjang dan bukan password yang sudah umum ditebak, bukan aturan komposisi
/// (huruf besar + angka + simbol) yang mendorong pola mudah ditebak. Panjang maksimum mencegah permintaan raksasa membebani hashing.
/// </summary>
public static class Passwords
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private static readonly PasswordHasher<User> Hasher = new();

    // Sebagian kecil password paling umum di dunia dan yang gampang ditebak untuk aplikasi ini. Dibandingkan tanpa memperhatikan huruf besar/kecil.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "12345678", "123456789", "1234567890", "qwertyui", "qwertyuiop", "qwerty123",
        "11111111", "00000000", "abcd1234", "iloveyou", "admin123", "administrator", "welcome1", "letmein1", "ignis123", "ignisignis",
        "passw0rd", "p@ssw0rd", "kebakaran", "katasandi", "rahasia123"
    };

    public static string Hash(string password) => Hasher.HashPassword(null!, password);   // parameter user tidak dipakai hasher bawaan

    /// <summary>False untuk hash kosong (akun yang dibuat lewat Google tidak punya password).</summary>
    public static bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) &&
        Hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;

    /// <summary>
    /// Pesan kesalahan bila password tidak memenuhi kebijakan, atau null bila lolos.
    /// Tidak boleh sama dengan email atau nama pengguna, dan tidak boleh password yang sangat umum.
    /// </summary>
    public static string? Validate(string? password, string? email = null, string? name = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength) return $"Password minimal {MinLength} karakter.";
        if (password.Length > MaxLength) return $"Password maksimal {MaxLength} karakter.";
        if (string.IsNullOrWhiteSpace(password)) return "Password tidak boleh hanya berisi spasi.";

        var local = (email ?? string.Empty).Split('@')[0];
        if (SameAs(password, email) || (local.Length >= MinLength && SameAs(password, local)) || SameAs(password, name))
            return "Password tidak boleh sama dengan email atau nama Anda.";
        if (Common.Contains(password) || password.Distinct().Count() == 1)
            return "Password terlalu umum atau mudah ditebak. Pilih yang lebih panjang atau unik.";
        return null;
    }

    private static bool SameAs(string password, string? other) =>
        !string.IsNullOrWhiteSpace(other) && string.Equals(password.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);
}
