using IgnisAdmin.Models;
namespace IgnisAdmin.Services;

/// <summary>Scheduler/sinkronisasi (UC-02). FIRMS: impor hotspot area Indonesia 24 jam terakhir. Cuaca: cek koneksi API.</summary>
public static class SyncService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static bool _running;

    public static async Task<string> RunAsync()
    {
        if (_running) return "Sinkronisasi sedang berjalan.";
        _running = true;
        var ds = DataStore.Instance; var s = ds.Settings; var msg = new List<string>();
        try
        {
            // --- NASA FIRMS ---
            if (string.IsNullOrWhiteSpace(s.FirmsKey))
            {
                ds.FirmsStatus = "API key belum diisi"; msg.Add("FIRMS dilewati: API key belum diisi di Pengaturan.");
            }
            else
            {
                ds.FirmsStatus = "Menyinkronkan..."; ds.Raise();
                int attempts = Math.Max(1, s.RetryCount + 1);
                for (int i = 0; i < attempts; i++)
                {
                    try
                    {
                        var url = $"{s.FirmsEndpoint.TrimEnd('/')}/{s.FirmsKey}/VIIRS_SNPP_NRT/94,-11,141,6/1";
                        var csv = await Http.GetStringAsync(url);
                        int added = ds.ImportFirmsCsv(csv);
                        ds.FirmsStatus = "Terhubung"; msg.Add($"FIRMS: {added} hotspot baru.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (i < attempts - 1) await Task.Delay(TimeSpan.FromMinutes(Math.Max(0, s.RetryDelayMinutes)));
                        else { ds.FirmsStatus = "Gagal"; msg.Add("FIRMS gagal: " + ex.Message); ds.Log("Sync FIRMS gagal: " + ex.Message); }
                    }
                }
            }

            // --- Cuaca (cek koneksi; pengambilan per-hotspot = langkah berikutnya) ---
            var wKey = string.IsNullOrWhiteSpace(s.WeatherKey) ? Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY") : s.WeatherKey;
            if (string.IsNullOrWhiteSpace(wKey))
            {
                ds.WeatherStatus = "API key belum diisi"; msg.Add("Weather dilewati: API key belum diisi.");
            }
            else
            {
                try
                {
                    var r = await Http.GetAsync($"{s.WeatherEndpoint}?q=Yogyakarta,ID&appid={wKey}&units=metric&lang=id");
                    ds.WeatherStatus = r.IsSuccessStatusCode ? "Terhubung" : "Gagal";
                    msg.Add(r.IsSuccessStatusCode ? "Weather API: terhubung." : $"Weather API: HTTP {(int)r.StatusCode}.");
                }
                catch (Exception ex) { ds.WeatherStatus = "Gagal"; msg.Add("Weather gagal: " + ex.Message); }
            }
            ds.LastSync = DateTime.Now; ds.Raise();
            return string.Join("\n", msg);
        }
        finally { _running = false; }
    }
}
