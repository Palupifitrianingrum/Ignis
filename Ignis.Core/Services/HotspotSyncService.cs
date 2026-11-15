using Ignis.Core.Abstractions;
using Ignis.Core.Enums;

namespace Ignis.Core.Services;

public sealed record SyncProgress(string Source, ConnectionState State);

public sealed record SyncResult(
    IReadOnlyList<string> Messages,
    int NewHotspots,
    ConnectionState Firms,
    ConnectionState Weather,
    DateTime FinishedAt);

/// <summary>Sinkronisasi terjadwal (UC-02): impor hotspot NASA FIRMS dan cek koneksi cuaca, dengan retry.</summary>
public sealed class HotspotSyncService
{
    private readonly IFirmsClient _firms;
    private readonly IWeatherProvider _weather;
    private readonly HotspotService _hotspots;
    private readonly ISettingsProvider _settings;
    private readonly IActivityLog _log;
    private static int _running; // satu sinkronisasi pada satu waktu (lintas instance)

    public HotspotSyncService(IFirmsClient firms, IWeatherProvider weather, HotspotService hotspots,
                              ISettingsProvider settings, IActivityLog log)
    {
        _firms = firms;
        _weather = weather;
        _hotspots = hotspots;
        _settings = settings;
        _log = log;
    }

    public async Task<SyncResult> RunAsync(IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new SyncResult(new[] { "Sinkronisasi sedang berjalan." }, 0, ConnectionState.Unknown, ConnectionState.Unknown, DateTime.Now);

        try
        {
            var s = _settings.Current;
            var msg = new List<string>();
            int added = 0;
            ConnectionState firmsState;

            // --- NASA FIRMS ---
            if (string.IsNullOrWhiteSpace(s.FirmsKey))
            {
                firmsState = ConnectionState.MissingKey;
                msg.Add("FIRMS dilewati: API key belum diisi di Pengaturan.");
            }
            else
            {
                progress?.Report(new SyncProgress("firms", ConnectionState.Syncing));
                firmsState = ConnectionState.Failed;
                int attempts = Math.Max(1, s.RetryCount + 1);
                for (int i = 0; i < attempts; i++)
                {
                    try
                    {
                        var csv = await _firms.FetchIndonesiaCsvAsync(ct);
                        added = await _hotspots.ImportSatelliteAsync(FirmsCsvParser.Parse(csv), ct);
                        firmsState = ConnectionState.Connected;
                        msg.Add($"FIRMS: {added} hotspot baru.");
                        break;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        if (i < attempts - 1)
                            await Task.Delay(TimeSpan.FromMinutes(Math.Max(0, s.RetryDelayMinutes)), ct);
                        else
                        {
                            msg.Add("FIRMS gagal: " + ex.Message);
                            await _log.AddAsync("Sync FIRMS gagal: " + ex.Message, ct);
                        }
                    }
                }
            }

            // --- Cuaca (cek koneksi; pengambilan per-hotspot terjadi saat prediksi) ---
            var check = await _weather.CheckConnectionAsync(ct);
            msg.Add(check.Message);

            return new SyncResult(msg, added, firmsState, check.State, DateTime.Now);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
