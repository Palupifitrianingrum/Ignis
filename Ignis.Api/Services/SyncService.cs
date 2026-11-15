using Ignis.Api.Integrations;
using Ignis.Shared;

namespace Ignis.Api.Services;

/// <summary>Sinkronisasi (UC-02): impor hotspot NASA FIRMS dan cek koneksi cuaca, dengan retry.</summary>
public sealed class SyncService(FirmsClient firms, WeatherClient weather, HotspotService hotspots,
                                IntegrationOptions options, ActivityLogService log)
{
    private static int _running; // satu sinkronisasi pada satu waktu (lintas instance)

    public async Task<SyncResult> RunAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new SyncResult(new[] { "Sinkronisasi sedang berjalan." }, 0, ConnectionState.Unknown, ConnectionState.Unknown, DateTime.Now);

        try
        {
            var msg = new List<string>();
            int added = 0;
            ConnectionState firmsState;

            // --- NASA FIRMS ---
            if (string.IsNullOrWhiteSpace(options.FirmsKey))
            {
                firmsState = ConnectionState.MissingKey;
                msg.Add("FIRMS dilewati: API key belum diisi (Integrations__FirmsKey).");
            }
            else
            {
                firmsState = ConnectionState.Failed;
                int attempts = Math.Max(1, options.RetryCount + 1);
                for (int i = 0; i < attempts; i++)
                {
                    try
                    {
                        var csv = await firms.FetchIndonesiaCsvAsync(ct);
                        added = await hotspots.ImportSatelliteAsync(FirmsCsvParser.Parse(csv), ct);
                        firmsState = ConnectionState.Connected;
                        msg.Add($"FIRMS: {added} hotspot baru.");
                        break;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        if (i < attempts - 1)
                            await Task.Delay(TimeSpan.FromMinutes(Math.Max(0, options.RetryDelayMinutes)), ct);
                        else
                        {
                            msg.Add("FIRMS gagal: " + ex.Message);
                            await log.AddAsync("Sync FIRMS gagal: " + ex.Message, ct);
                        }
                    }
                }
            }

            // --- Cuaca (cek koneksi; pengambilan per-hotspot terjadi saat prediksi) ---
            var check = await weather.CheckConnectionAsync(ct);
            msg.Add(check.Message);

            return new SyncResult(msg, added, firmsState, check.State, DateTime.Now);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}

/// <summary>Menjalankan <see cref="SyncService"/> secara berkala. Mati secara default (Sync:Enabled=false). Belum diuji dengan API key FIRMS sungguhan.</summary>
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
