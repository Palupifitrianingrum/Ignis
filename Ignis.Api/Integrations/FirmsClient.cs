using System.Globalization;
using System.Net;

namespace Ignis.Api.Integrations;

/// <summary>
/// Klien NASA FIRMS Area API. Format permintaan (dokumentasi resmi, firms.modaps.eosdis.nasa.gov/api/area/):
/// <c>{endpoint}/{MAP_KEY}/{SOURCE}/{barat,selatan,timur,utara}/{DAY_RANGE}</c>, jawabannya CSV.
///
/// Perilaku yang teramati langsung terhadap server FIRMS: key salah menjawab HTTP 400 dengan teks "Invalid MAP_KEY.";
/// key bermasalah atau melebihi batas transaksi menjawab HTTP 403. Karena key berada di path URL, jangan pernah mencatat URL lengkap
/// (log kategori System.Net.Http.HttpClient dinaikkan ke Warning di appsettings.json untuk itu).
/// </summary>
public sealed class FirmsClient(HttpClient http, IntegrationOptions options)
{
    private const string Provider = "FIRMS";

    /// <summary>Kotak kecil (±22 km) di Riau untuk pemeriksaan koneksi: memvalidasi key dengan data yang sangat sedikit.</summary>
    private const string CheckArea = "101.7,0.2,101.9,0.4";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.FirmsKey);

    /// <summary>URL permintaan untuk satu sumber (berisi key; jangan dicatat atau ditampilkan).</summary>
    public string BuildUrl(string source, string? area = null, int? dayRange = null)
    {
        var days = Math.Clamp(dayRange ?? options.FirmsDayRange, 1, 5);   // dokumentasi: 1 sampai 5 hari
        return $"{options.FirmsEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(options.FirmsKey.Trim())}" +
               $"/{Uri.EscapeDataString(source)}/{area ?? options.FirmsArea}/{days}";
    }

    /// <summary>Ambil CSV hotspot untuk satu sumber (mis. VIIRS_SNPP_NRT). Melempar <see cref="IntegrationException"/> bila gagal.</summary>
    public Task<string> FetchCsvAsync(string source, CancellationToken ct = default) =>
        GetCsvAsync(BuildUrl(source), ct);

    /// <summary>Periksa key dan koneksi dengan satu permintaan ke kotak kecil (1 transaksi, data minim).</summary>
    public async Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var source = options.FirmsSourceList.FirstOrDefault() ?? "VIIRS_SNPP_NRT";
            await GetCsvAsync(BuildUrl(source, CheckArea, 1), ct);
            return new ProviderCheck(Ignis.Shared.ConnectionState.Connected, "FIRMS: terhubung, MAP_KEY valid.");
        }
        catch (IntegrationException ex)
        {
            return ProviderCheck.From(ex);
        }
    }

    /// <summary>
    /// Tanggal data terakhir yang tersedia di FIRMS untuk satu sumber (endpoint data_availability), atau null bila tidak bisa dipastikan.
    /// Dipakai untuk menjelaskan hasil kosong (mis. satelit yang datanya berhenti). Tidak melempar.
    /// </summary>
    public async Task<DateOnly?> GetLatestAvailableDateAsync(string source, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var url = $"{options.FirmsAvailabilityEndpoint.TrimEnd('/')}/{Uri.EscapeDataString(options.FirmsKey.Trim())}/{Uri.EscapeDataString(source)}";
        try
        {
            // Bentuk jawaban (teramati): "data_id,min_date,max_date" lalu satu baris "SUMBER,2026-08-01,2026-11-02".
            var body = await GetTextAsync(url, requireCsvHeader: false, ct);
            var cols = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).FirstOrDefault()?.Split(',');
            return cols is { Length: >= 3 } && DateOnly.TryParse(cols[2].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
        catch (IntegrationException)
        {
            return null;
        }
    }

    private Task<string> GetCsvAsync(string url, CancellationToken ct) => GetTextAsync(url, requireCsvHeader: true, ct);

    private async Task<string> GetTextAsync(string url, bool requireCsvHeader, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new IntegrationException(Provider, IntegrationFailure.MissingKey, "API key FIRMS belum diisi (Integrations__FirmsKey).");

        var key = options.FirmsKey.Trim();
        try
        {
            using var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode) throw Classify(resp.StatusCode, body, key);
            if (requireCsvHeader && !LooksLikeCsv(body))
                throw new IntegrationException(Provider, IntegrationFailure.BadResponse,
                    $"Respons FIRMS bukan CSV yang dikenali: \"{IntegrationText.Snippet(body, key)}\"");
            return body;
        }
        catch (HttpRequestException ex)
        {
            throw new IntegrationException(Provider, IntegrationFailure.Unavailable,
                "Tidak dapat menghubungi FIRMS: " + IntegrationText.Redact(ex.Message, key), ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IntegrationException(Provider, IntegrationFailure.Unavailable, "FIRMS tidak menjawab (timeout).", ex);
        }
    }

    private static IntegrationException Classify(HttpStatusCode status, string body, string key)
    {
        var code = (int)status;

        if (status == HttpStatusCode.BadRequest && body.Contains("Invalid MAP_KEY", StringComparison.OrdinalIgnoreCase))
            return new(Provider, IntegrationFailure.InvalidKey, "Key FIRMS ditolak (Invalid MAP_KEY). Periksa Integrations__FirmsKey.");

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new(Provider, IntegrationFailure.InvalidKey,
                    $"FIRMS menolak permintaan (HTTP {code}): MAP_KEY salah atau batas transaksi (5.000 per 10 menit) terlampaui."),
            HttpStatusCode.TooManyRequests =>
                new(Provider, IntegrationFailure.RateLimited, "Batas transaksi FIRMS terlampaui (5.000 per 10 menit). Coba lagi nanti."),
            >= HttpStatusCode.InternalServerError =>
                new(Provider, IntegrationFailure.Unavailable, $"FIRMS sedang bermasalah (HTTP {code})."),
            _ =>
                new(Provider, IntegrationFailure.BadResponse, $"FIRMS menjawab HTTP {code}: \"{IntegrationText.Snippet(body, key)}\"")
        };
    }

    /// <summary>Jawaban valid selalu diawali baris header yang memuat kolom latitude dan longitude (walau tanpa data).</summary>
    private static bool LooksLikeCsv(string body)
    {
        var first = body.TrimStart('﻿', ' ', '\r', '\n', '\t');
        var newline = first.IndexOf('\n');
        var header = newline < 0 ? first : first[..newline];
        return header.Contains("latitude", StringComparison.OrdinalIgnoreCase)
            && header.Contains("longitude", StringComparison.OrdinalIgnoreCase);
    }
}
