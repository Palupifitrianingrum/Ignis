using Ignis.Api.Integrations;
using Ignis.Shared;

namespace Ignis.Api.Services;

/// <summary>
/// Sinkronisasi (UC-02): impor hotspot NASA FIRMS dan cek koneksi cuaca.
///
/// Aturan penanganan kegagalan:
///  - Hanya gangguan sementara (jaringan, timeout, HTTP 5xx) yang dicoba ulang, dengan jeda berlipat dua.
///  - Key salah atau batas transaksi terlampaui tidak diulang (tidak akan membaik) dan menghentikan sumber lain,
///    karena memakai key yang sama.
///  - Satu sumber gagal tidak menggagalkan sumber lain (kecuali gangguan koneksi di sumber pertama); hasil sebagian dilaporkan per sumber.
/// </summary>
public sealed class SyncService(FirmsClient firms, WeatherClient weather, HotspotService hotspots,
                                IntegrationOptions options, ActivityLogService log, IntegrationStatus status,
                                ILogger<SyncService>? logger = null, NotificationService? notifications = null)
{
    public async Task<SyncResult> RunAsync(CancellationToken ct = default)
    {
        if (!status.TryBeginSync())
            return new SyncResult(new[] { "Sinkronisasi sedang berjalan." }, 0, ConnectionState.Unknown, ConnectionState.Unknown, DateTime.Now);

        try
        {
            var messages = new List<string>();
            int added = 0;
            ConnectionState firmsState;

            // --- NASA FIRMS ---
            if (!firms.IsConfigured)
            {
                firmsState = ConnectionState.MissingKey;
                messages.Add("FIRMS dilewati: API key belum diisi (Integrations__FirmsKey). Lihat docs/integrasi-data.md.");
            }
            else
            {
                int succeeded = 0;
                foreach (var source in options.FirmsSourceList)
                {
                    try
                    {
                        added += await SyncSourceAsync(source, messages, ct);
                        succeeded++;
                    }
                    catch (IntegrationException ex) when (!ct.IsCancellationRequested)
                    {
                        messages.Add($"FIRMS {source} gagal: {ex.Message}");
                        await log.AddAsync($"Sync FIRMS {source} gagal: {ex.Message}", ct);

                        // Key yang sama dipakai semua sumber: bila ditolak atau kena batas, sumber lain pasti sama.
                        // Gangguan koneksi pada sumber pertama (belum ada yang berhasil) juga berarti host-nya bermasalah.
                        if (ex.Failure is IntegrationFailure.InvalidKey or IntegrationFailure.RateLimited ||
                            (ex.Failure == IntegrationFailure.Unavailable && succeeded == 0))
                        {
                            messages.Add("Sumber FIRMS lainnya dilewati.");
                            break;
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        logger?.LogError(ex, "Sync FIRMS {Source}: gagal memproses atau menyimpan hasil", source);
                        messages.Add($"FIRMS {source} gagal memproses atau menyimpan hasil ({ex.GetType().Name}). Lihat log server.");
                        await log.AddAsync($"Sync FIRMS {source} gagal menyimpan hasil ({ex.GetType().Name})", ct);
                    }
                }
                firmsState = succeeded > 0 ? ConnectionState.Connected : ConnectionState.Failed;
            }

            // --- Cuaca (cek koneksi; pengambilan per-hotspot terjadi saat simulasi/prediksi) ---
            var check = await weather.CheckConnectionAsync(ct);
            messages.Add(check.Message);

            // --- Notifikasi: hotspot baru dan cuaca terbaru bisa mengubah dampak pada lokasi tersimpan pengguna ---
            if (notifications != null)
            {
                try
                {
                    var evaluation = await notifications.EvaluateAllAsync(ct);
                    if (evaluation.Areas > 0)
                        messages.Add($"Notifikasi: {evaluation.Created} baru untuk {evaluation.Areas} lokasi tersimpan ({evaluation.HotspotsSimulated} hotspot disimulasikan).");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogError(ex, "Pemeriksaan notifikasi setelah sinkronisasi gagal");
                    messages.Add("Pemeriksaan notifikasi gagal (lihat log server); sinkronisasi hotspot tetap berhasil.");
                }
            }

            return new SyncResult(messages, added, firmsState, check.State, DateTime.Now);
        }
        finally
        {
            status.EndSync();
        }
    }

    /// <summary>Ambil dan impor satu sumber. Mengembalikan jumlah hotspot baru; melempar <see cref="IntegrationException"/> bila pengambilan gagal.</summary>
    private async Task<int> SyncSourceAsync(string source, List<string> messages, CancellationToken ct)
    {
        var csv = await FetchWithRetryAsync(source, ct);
        var parsed = FirmsCsvParser.ParseDetailed(csv, options.FirmsMaxRows);
        var imported = await hotspots.ImportSatelliteAsync(parsed.Hotspots, ct);

        var line = $"FIRMS {source}: {parsed.TotalRows} baris, {imported} hotspot baru";
        if (parsed.TotalRows == 0)
        {
            // Hasil kosong bisa wajar (belum ada deteksi) atau satelitnya sedang tidak mengirim data: beri tahu tanggal data terakhir.
            var latest = await firms.GetLatestAvailableDateAsync(source, ct);
            if (latest is { } d) line += $" (data terakhir yang tersedia di FIRMS untuk sumber ini: {d:yyyy-MM-dd})";
        }
        if (parsed.Truncated) line += $" (dibatasi {options.FirmsMaxRows} baris dengan keyakinan tertinggi)";
        if (parsed.Invalid > 0) line += $", {parsed.Invalid} baris tidak valid dilewati";
        messages.Add(line + ".");
        return imported;
    }

    private async Task<string> FetchWithRetryAsync(string source, CancellationToken ct)
    {
        int attempts = Math.Max(0, options.RetryCount) + 1;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await firms.FetchCsvAsync(source, ct);
            }
            catch (IntegrationException ex) when (ex.IsTransient && attempt < attempts - 1 && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Max(0, options.RetryDelaySeconds) * Math.Pow(2, attempt));
                logger?.LogWarning("FIRMS {Source} percobaan {Attempt}/{Max} gagal ({Message}); ulang dalam {Delay}", source, attempt + 1, attempts, ex.Message, delay);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
        }
    }
}

/// <summary>Menjalankan <see cref="SyncService"/> secara berkala. Mati secara default (Sync:Enabled=false).</summary>
public sealed class SyncBackgroundService(IServiceScopeFactory scopes, IConfiguration config, ILogger<SyncBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Sync:Enabled", false)) return;
        var interval = TimeSpan.FromHours(Math.Max(1, config.GetValue("Sync:IntervalHours", 1)));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<SyncService>().RunAsync(stoppingToken);
                log.LogInformation("Sync selesai: {Messages}", string.Join(" | ", result.Messages));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Sync terjadwal gagal");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
