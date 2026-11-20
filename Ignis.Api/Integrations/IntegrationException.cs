using Ignis.Shared;

namespace Ignis.Api.Integrations;

/// <summary>Penyebab kegagalan memanggil layanan luar; menentukan apakah layak dicoba ulang.</summary>
public enum IntegrationFailure
{
    /// <summary>API key belum diisi.</summary>
    MissingKey,
    /// <summary>Key ditolak (salah, belum aktif, atau tidak berhak). Mengulang tidak akan membantu.</summary>
    InvalidKey,
    /// <summary>Batas panggilan terlampaui. Mengulang segera hanya memperburuk.</summary>
    RateLimited,
    /// <summary>Gangguan sementara: jaringan, timeout, atau HTTP 5xx. Layak dicoba ulang.</summary>
    Unavailable,
    /// <summary>Layanan menjawab tetapi isinya tidak sesuai dokumentasi.</summary>
    BadResponse
}

/// <summary>Kegagalan memanggil layanan luar. Pesannya sudah dalam bahasa Indonesia, aman ditampilkan, dan tidak memuat API key.</summary>
public sealed class IntegrationException(string provider, IntegrationFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Provider { get; } = provider;
    public IntegrationFailure Failure { get; } = failure;

    /// <summary>Hanya gangguan sementara yang dicoba ulang.</summary>
    public bool IsTransient => Failure == IntegrationFailure.Unavailable;
}

/// <summary>Hasil pemeriksaan koneksi ke satu layanan.</summary>
public sealed record ProviderCheck(ConnectionState State, string Message)
{
    public static ProviderCheck From(IntegrationException ex) =>
        new(ex.Failure == IntegrationFailure.MissingKey ? ConnectionState.MissingKey : ConnectionState.Failed, ex.Message);
}

/// <summary>Pembantu bersama untuk klien layanan luar.</summary>
internal static class IntegrationText
{
    /// <summary>Hapus API key dari teks apa pun yang akan ditampilkan atau dicatat.</summary>
    public static string Redact(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***", StringComparison.Ordinal)
                                                  .Replace(Uri.EscapeDataString(secret), "***", StringComparison.Ordinal);

    /// <summary>Cuplikan pendek isi respons untuk pesan error: satu baris, tanpa key.</summary>
    public static string Snippet(string body, string? secret, int max = 120)
    {
        var oneLine = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (oneLine.Length > max) oneLine = oneLine[..max] + "…";
        return Redact(oneLine, secret);
    }
}

/// <summary>
/// Status integrasi bersama (singleton): penjaga "satu sinkronisasi pada satu waktu" dan hasil terakhir pengambilan cuaca.
/// </summary>
public sealed class IntegrationStatus
{
    private int _syncRunning;
    private volatile string? _weatherLastError;
    private long _weatherLastSuccessTicks;

    /// <summary>True bila sinkronisasi boleh mulai. Panggil <see cref="EndSync"/> setelah selesai.</summary>
    public bool TryBeginSync() => Interlocked.Exchange(ref _syncRunning, 1) == 0;

    public void EndSync() => Interlocked.Exchange(ref _syncRunning, 0);

    /// <summary>Pesan kegagalan OpenWeather terakhir; null bila pemanggilan terakhir berhasil (atau belum pernah dicoba).</summary>
    public string? WeatherLastError => _weatherLastError;

    public DateTime? WeatherLastSuccess =>
        Interlocked.Read(ref _weatherLastSuccessTicks) is var t and > 0 ? new DateTime(t) : null;

    public void WeatherSucceeded()
    {
        _weatherLastError = null;
        Interlocked.Exchange(ref _weatherLastSuccessTicks, DateTime.Now.Ticks);
    }

    public void WeatherFailed(string message) => _weatherLastError = message;
}
