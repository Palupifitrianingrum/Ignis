using System.Collections.Concurrent;

namespace Ignis.Api.Services;

/// <summary>Pengaturan keamanan login (bagian "Auth" di appsettings, atau env Auth__...).</summary>
public sealed class AuthOptions
{
    /// <summary>Berapa kali berturut-turut gagal sebelum akun/kunci dikunci sementara.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Lama kunci pertama (menit). Kunci berikutnya berlipat dua, paling lama 12 kali lipat.</summary>
    public int LockoutMinutes { get; set; } = 5;

    /// <summary>Batas permintaan login/registrasi/Google per alamat IP per menit. Nonaktifkan HANYA untuk pengujian.</summary>
    public bool RateLimitEnabled { get; set; } = true;

    public int RateLimitPerMinute { get; set; } = 20;
}

/// <summary>Terlalu banyak percobaan gagal: akun terkunci sementara. Di API dipetakan ke HTTP 429 dengan header Retry-After.</summary>
public sealed class TooManyAttemptsException(TimeSpan retryAfter) : Exception(Describe(retryAfter))
{
    public TimeSpan RetryAfter { get; } = retryAfter;

    private static string Describe(TimeSpan wait) =>
        wait.TotalMinutes >= 1
            ? $"Terlalu banyak percobaan gagal. Coba lagi dalam {Math.Ceiling(wait.TotalMinutes):0} menit."
            : $"Terlalu banyak percobaan gagal. Coba lagi dalam {Math.Max(1, Math.Ceiling(wait.TotalSeconds)):0} detik.";
}

/// <summary>
/// Pembatas percobaan gagal per kunci (email untuk login, id pengguna untuk ganti password). Setelah
/// <see cref="AuthOptions.MaxFailedAttempts"/> kegagalan berturut-turut, kunci dikunci sementara; kunci berikutnya makin lama.
/// Berlaku sama untuk email yang tidak terdaftar, supaya perilakunya tidak membocorkan email mana yang ada.
///
/// Disimpan di memori proses (hilang saat server restart; tidak dibagi antar-instance). Kompromi yang disengaja: penyerang bisa
/// "mengunci" akun orang lain dengan sengaja gagal login; karena kuncinya singkat dan per akun, dampaknya terbatas.
/// </summary>
public sealed class LoginThrottle(AuthOptions options, TimeProvider? clock = null)
{
    private sealed class Entry
    {
        public int Failures;
        public int Locks;
        public DateTimeOffset LastFailure;
        public DateTimeOffset? LockedUntil;
    }

    private const int MaxEntries = 20_000;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public static string Normalize(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Sisa waktu kunci, atau null bila tidak sedang terkunci.</summary>
    public TimeSpan? RetryAfter(string key)
    {
        if (!_entries.TryGetValue(Normalize(key), out var e)) return null;
        lock (e)
        {
            var now = _clock.GetUtcNow();
            return e.LockedUntil is { } until && until > now ? until - now : null;
        }
    }

    /// <summary>Catat satu kegagalan. True bila kegagalan ini membuat kunci terkunci.</summary>
    public bool Failed(string key)
    {
        if (_entries.Count > MaxEntries) Purge();

        var now = _clock.GetUtcNow();
        var e = _entries.GetOrAdd(Normalize(key), _ => new Entry());
        lock (e)
        {
            // Kegagalan lama (di luar jendela) tidak ikut dihitung.
            var window = TimeSpan.FromMinutes(Math.Max(1, options.LockoutMinutes) * 3);
            bool stillLocked = e.LockedUntil is { } until && until > now;
            if (!stillLocked && now - e.LastFailure > window) e.Failures = 0;

            e.Failures++;
            e.LastFailure = now;
            if (e.Failures < Math.Max(1, options.MaxFailedAttempts)) return false;

            e.Locks++;
            e.Failures = 0;
            var factor = Math.Min(12, 1 << Math.Min(e.Locks - 1, 4));   // 1, 2, 4, 8, 12 (maks)
            e.LockedUntil = now + TimeSpan.FromMinutes(Math.Max(1, options.LockoutMinutes) * factor);
            return true;
        }
    }

    /// <summary>Berhasil: hapus catatan kegagalan.</summary>
    public void Succeeded(string key) => _entries.TryRemove(Normalize(key), out _);

    private void Purge()
    {
        var now = _clock.GetUtcNow();
        var stale = TimeSpan.FromHours(1);
        foreach (var (k, e) in _entries)
            lock (e)
                if ((e.LockedUntil is null || e.LockedUntil <= now) && now - e.LastFailure > stale) _entries.TryRemove(k, out _);
    }
}
