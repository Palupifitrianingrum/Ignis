namespace Ignis.Api.Integrations;

/// <summary>Klien NASA FIRMS (CSV area Indonesia, 24 jam terakhir).</summary>
public sealed class FirmsClient(HttpClient http, IntegrationOptions options)
{
    // Kotak Indonesia: barat,selatan,timur,utara
    private const string IndonesiaArea = "94,-11,141,6";

    /// <summary>Ambil CSV hotspot VIIRS SNPP NRT. Melempar exception bila key kosong atau permintaan gagal.</summary>
    public async Task<string> FetchIndonesiaCsvAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.FirmsKey))
            throw new InvalidOperationException("API key FIRMS belum diisi.");

        var url = $"{options.FirmsEndpoint.TrimEnd('/')}/{options.FirmsKey}/VIIRS_SNPP_NRT/{IndonesiaArea}/1";
        return await http.GetStringAsync(url, ct);
    }
}
