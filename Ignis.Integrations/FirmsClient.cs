using Ignis.Core.Abstractions;

namespace Ignis.Integrations;

/// <summary>Klien NASA FIRMS (CSV area Indonesia, 24 jam terakhir). Dipindahkan dari SyncService di Frontend.</summary>
public sealed class FirmsClient : IFirmsClient
{
    // Kotak Indonesia: barat,selatan,timur,utara
    private const string IndonesiaArea = "94,-11,141,6";

    private readonly HttpClient _http;
    private readonly ISettingsProvider _settings;

    public FirmsClient(HttpClient http, ISettingsProvider settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<string> FetchIndonesiaCsvAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (string.IsNullOrWhiteSpace(s.FirmsKey))
            throw new InvalidOperationException("API key FIRMS belum diisi.");

        var url = $"{s.FirmsEndpoint.TrimEnd('/')}/{s.FirmsKey}/VIIRS_SNPP_NRT/{IndonesiaArea}/1";
        return await _http.GetStringAsync(url, ct);
    }
}
