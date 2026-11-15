using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ignis.Core.Contracts;
using Ignis.Core.Services;

namespace Ignis.Frontend.Services.Backend;

/// <summary>Mode API: semua data lewat Ignis.Api, yang menyimpan ke PostgreSQL. Frontend tidak memegang kredensial database.</summary>
public sealed class ApiAdminBackend : IAdminBackend
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public ApiAdminBackend(HttpClient http) => _http = http;

    public string Name => $"API ({_http.BaseAddress})";

    // ---------- util ----------
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = JsonContent.Create(body, options: Json);
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return resp;

            var message = await ReadErrorAsync(resp);
            resp.Dispose();
            throw new BackendException(message);
        }
        catch (HttpRequestException ex)
        {
            throw new BackendException($"Tidak dapat terhubung ke API di {_http.BaseAddress}. Pastikan Ignis.Api sedang berjalan.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new BackendException($"API di {_http.BaseAddress} tidak menjawab (timeout).", ex);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var e = await resp.Content.ReadFromJsonAsync<ErrorBody>(Json);
            if (!string.IsNullOrWhiteSpace(e?.Error)) return e.Error;
        }
        catch (JsonException) { }
        return $"API menjawab HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).";
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var resp = await SendAsync(HttpMethod.Get, url);
        return await resp.Content.ReadFromJsonAsync<T>(Json) ?? throw new BackendException("Jawaban API kosong.");
    }

    private async Task<T> SendForAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var resp = await SendAsync(method, url, body);
        return await resp.Content.ReadFromJsonAsync<T>(Json) ?? throw new BackendException("Jawaban API kosong.");
    }

    private sealed record ErrorBody(string? Error);

    // ---------- hotspot ----------
    public async Task<IReadOnlyList<HotspotDto>> GetHotspotsAsync() => await GetAsync<List<HotspotDto>>("api/hotspots");

    public Task<HotspotDto> AddManualHotspotAsync(ManualHotspotRequest request) =>
        SendForAsync<HotspotDto>(HttpMethod.Post, "api/hotspots/manual", request);

    public Task<HotspotDto> UpdateManualHotspotAsync(string id, ManualHotspotRequest request) =>
        SendForAsync<HotspotDto>(HttpMethod.Put, $"api/hotspots/{Uri.EscapeDataString(id)}", request);

    public async Task DeleteHotspotAsync(string id)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/hotspots/{Uri.EscapeDataString(id)}");
    }

    // ---------- cuaca & prediksi ----------
    public Task<WeatherDto> GetWeatherAsync(string hotspotId) => GetAsync<WeatherDto>($"api/hotspots/{Uri.EscapeDataString(hotspotId)}/weather");

    public async Task<IReadOnlyList<PredictionDto>> GetPredictionsAsync() => await GetAsync<List<PredictionDto>>("api/predictions");

    public Task<PredictionDto> GetOrPredictAsync(string hotspotId) => GetAsync<PredictionDto>($"api/hotspots/{Uri.EscapeDataString(hotspotId)}/prediction");

    public Task<PredictionDto> PredictAsync(string hotspotId) =>
        SendForAsync<PredictionDto>(HttpMethod.Post, $"api/hotspots/{Uri.EscapeDataString(hotspotId)}/predictions");

    // ---------- pengguna ----------
    public async Task<IReadOnlyList<UserDto>> GetUsersAsync() => await GetAsync<List<UserDto>>("api/users");

    public async Task<string?> ToggleUserAsync(string id)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Post, $"api/users/{Uri.EscapeDataString(id)}/toggle-active");
            return null;
        }
        catch (BackendException ex) when (ex.InnerException == null)
        {
            return ex.Message;   // pesan aturan bisnis dari server (mis. admin aktif terakhir)
        }
    }

    // ---------- log & sinkronisasi ----------
    public async Task<IReadOnlyList<string>> GetActivitiesAsync() => await GetAsync<List<string>>("api/activity");

    public async Task AddActivityAsync(string message)
    {
        using var _ = await SendAsync(HttpMethod.Post, "api/activity", new { message });
    }

    public async Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress)
    {
        progress?.Report(new SyncProgress("firms", Ignis.Core.Enums.ConnectionState.Syncing));
        return await SendForAsync<SyncResult>(HttpMethod.Post, "api/sync");
    }
}
