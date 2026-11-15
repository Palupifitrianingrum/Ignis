using Ignis.Core.Abstractions;
using Ignis.Core.Services;

namespace Ignis.Api;

/// <summary>
/// Muat berkas .env ke environment. Dicari di folder kerja, folder aplikasi, lalu folder-folder induknya,
/// sehingga .env di root repo terbaca walau API dijalankan dari folder mana pun. Nilai yang sudah ada di environment tidak ditimpa.
/// </summary>
public static class DotEnv
{
    public static void Load(string fileName)
    {
        var path = Find(fileName, Directory.GetCurrentDirectory()) ?? Find(fileName, AppContext.BaseDirectory);
        if (path == null) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, parts[1].Trim().Trim('"'));
        }
    }

    private static string? Find(string fileName, string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

/// <summary>Konfigurasi integrasi dari appsettings / environment (bagian "Integrations").</summary>
public sealed class ConfigSettingsProvider : ISettingsProvider
{
    private readonly IConfiguration _config;
    public ConfigSettingsProvider(IConfiguration config) => _config = config;

    public IntegrationSettings Current
    {
        get
        {
            var s = _config.GetSection("Integrations");
            var d = new IntegrationSettings();
            return new IntegrationSettings
            {
                FirmsEndpoint = s["FirmsEndpoint"] ?? d.FirmsEndpoint,
                FirmsKey = s["FirmsKey"] ?? string.Empty,
                WeatherEndpoint = s["WeatherEndpoint"] ?? d.WeatherEndpoint,
                WeatherKey = s["WeatherKey"] ?? string.Empty,   // fallback ke env OPENWEATHER_API_KEY ada di OpenWeatherClient
                RetryCount = s.GetValue("RetryCount", d.RetryCount),
                RetryDelayMinutes = s.GetValue("RetryDelayMinutes", d.RetryDelayMinutes)
            };
        }
    }
}

/// <summary>
/// Sinkronisasi terjadwal di server. Mati secara default (Sync:Enabled=false).
/// Catatan: belum diuji dengan API key FIRMS sungguhan.
/// </summary>
public sealed class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<SyncBackgroundService> _log;

    public SyncBackgroundService(IServiceScopeFactory scopes, IConfiguration config, ILogger<SyncBackgroundService> log)
    {
        _scopes = scopes;
        _config = config;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Sync:Enabled", false)) return;
        var interval = TimeSpan.FromHours(Math.Max(1, _config.GetValue("Sync:IntervalHours", 1)));

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<HotspotSyncService>().RunAsync(null, stoppingToken);
                _log.LogInformation("Sync selesai: {Messages}", string.Join(" | ", result.Messages));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(ex, "Sync terjadwal gagal");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed record ActivityRequest(string Message);
