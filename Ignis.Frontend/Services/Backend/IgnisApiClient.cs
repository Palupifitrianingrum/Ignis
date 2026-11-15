using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ignis.Shared;

namespace Ignis.Frontend.Services.Backend;

/// <summary>Ignis.Api tidak dapat dihubungi, menolak permintaan, atau menjawab di luar kontrak. Pesannya siap ditampilkan ke pengguna.</summary>
public sealed class BackendException : Exception
{
    public BackendException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Satu-satunya pintu aplikasi desktop ke server. Semua data lewat Ignis.Api (yang menyimpan ke PostgreSQL);
/// desktop tidak memegang kredensial database maupun API key layanan luar.
/// Kelas ini juga menyimpan sesi login (token JWT + profil pengguna) dan melampirkannya ke setiap permintaan.
/// </summary>
public sealed class IgnisApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public IgnisApiClient(HttpClient http) => _http = http;

    public string Name => $"API ({_http.BaseAddress})";

    // ================= Sesi login =================

    public UserDto? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null;
    public bool IsAdmin => CurrentUser?.Role == Role.Admin;

    /// <summary>Dipicu saat pengguna login, logout, atau sesi berakhir (token kedaluwarsa).</summary>
    public event Action? SessionChanged;

    public async Task<UserDto> LoginAsync(string email, string password) =>
        StartSession(await SendForAsync<AuthResponse>(HttpMethod.Post, "api/auth/login", new LoginRequest(email, password)));

    /// <summary>Daftar akun baru (selalu role Registered). Tidak otomatis login: pengguna masuk lewat layar login.</summary>
    public async Task RegisterAsync(string name, string email, string password)
    {
        using var _ = await SendAsync(HttpMethod.Post, "api/auth/register", new RegisterRequest(name, email, password));
    }

    /// <summary>Client ID Google dari server; null bila login Google belum dikonfigurasi di server.</summary>
    public async Task<string?> GetGoogleClientIdAsync() => (await GetAsync<GoogleConfigDto>("api/auth/google/config")).ClientId;

    public async Task<UserDto> LoginWithGoogleAsync(string code, string codeVerifier, string redirectUri) =>
        StartSession(await SendForAsync<AuthResponse>(HttpMethod.Post, "api/auth/google", new GoogleLoginRequest(code, codeVerifier, redirectUri)));

    public void Logout() => EndSession();

    private UserDto StartSession(AuthResponse auth)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        CurrentUser = auth.User;
        SessionChanged?.Invoke();
        return auth.User;
    }

    private void EndSession()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        CurrentUser = null;
        SessionChanged?.Invoke();
    }

    // ================= util HTTP =================

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body != null) req.Content = JsonContent.Create(body, options: Json);
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return resp;

            // 401 saat sedang login = token kedaluwarsa/ditolak server: akhiri sesi supaya UI meminta login ulang.
            var expired = resp.StatusCode == HttpStatusCode.Unauthorized && IsLoggedIn;
            var message = expired ? "Sesi berakhir. Silakan login ulang." : await ReadErrorAsync(resp);
            resp.Dispose();
            if (expired) EndSession();
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

        return resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Anda belum login.",
            HttpStatusCode.Forbidden => "Akun Anda tidak memiliki izin untuk aksi ini.",
            _ => $"API menjawab HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})."
        };
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

    // ================= hotspot =================

    public async Task<IReadOnlyList<HotspotDto>> GetHotspotsAsync() => await GetAsync<List<HotspotDto>>("api/hotspots");

    public Task<HotspotDto> AddManualHotspotAsync(ManualHotspotRequest request) =>
        SendForAsync<HotspotDto>(HttpMethod.Post, "api/hotspots/manual", request);

    public Task<HotspotDto> UpdateManualHotspotAsync(string id, ManualHotspotRequest request) =>
        SendForAsync<HotspotDto>(HttpMethod.Put, $"api/hotspots/{Uri.EscapeDataString(id)}", request);

    public async Task DeleteHotspotAsync(string id)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/hotspots/{Uri.EscapeDataString(id)}");
    }

    // ================= cuaca & prediksi =================

    public Task<WeatherDto> GetWeatherAsync(string hotspotId) => GetAsync<WeatherDto>($"api/hotspots/{Uri.EscapeDataString(hotspotId)}/weather");

    public async Task<IReadOnlyList<PredictionDto>> GetPredictionsAsync() => await GetAsync<List<PredictionDto>>("api/predictions");

    public Task<PredictionDto> GetOrPredictAsync(string hotspotId) => GetAsync<PredictionDto>($"api/hotspots/{Uri.EscapeDataString(hotspotId)}/prediction");

    public Task<PredictionDto> PredictAsync(string hotspotId) =>
        SendForAsync<PredictionDto>(HttpMethod.Post, $"api/hotspots/{Uri.EscapeDataString(hotspotId)}/predictions");

    // ================= pengguna (admin) =================

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync() => await GetAsync<List<UserDto>>("api/users");

    /// <summary>Aktif/nonaktifkan pengguna. Mengembalikan pesan error aturan bisnis, atau null bila berhasil.</summary>
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

    // ================= log & sinkronisasi (admin) =================

    public async Task<IReadOnlyList<string>> GetActivitiesAsync() => await GetAsync<List<string>>("api/activity");

    public async Task AddActivityAsync(string message)
    {
        using var _ = await SendAsync(HttpMethod.Post, "api/activity", new { message });
    }

    public Task<SyncResult> SyncAsync() => SendForAsync<SyncResult>(HttpMethod.Post, "api/sync");
}
