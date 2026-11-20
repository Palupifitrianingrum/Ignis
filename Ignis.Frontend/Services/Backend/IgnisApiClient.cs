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

    /// <summary>Pengguna terdaftar: pemilik lokasi tersimpan dan notifikasi (admin tidak, sesuai ERD).</summary>
    public bool IsRegisteredUser => CurrentUser?.Role == Role.Registered;

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

    /// <summary>
    /// Keluar: minta server mencabut semua token akun ini (best-effort, tidak menunggu), lalu akhiri sesi lokal seketika.
    /// Bila server tidak terjangkau, sesi lokal tetap berakhir dan token lama kedaluwarsa sendiri.
    /// </summary>
    public void Logout()
    {
        var authorization = _http.DefaultRequestHeaders.Authorization;
        EndSession();
        if (authorization == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
                req.Headers.Authorization = authorization;
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                (await _http.SendAsync(req, cts.Token)).Dispose();
            }
            catch (Exception) { /* server tidak terjangkau: token lama habis sendiri */ }
        });
    }

    /// <summary>
    /// Ganti password. Server mencabut semua token lama dan mengembalikan token baru, sehingga sesi di perangkat ini berlanjut.
    /// <paramref name="currentPassword"/> boleh kosong hanya untuk akun yang belum punya password (masuk lewat Google).
    /// </summary>
    public async Task ChangePasswordAsync(string? currentPassword, string newPassword)
    {
        var auth = await SendForAsync<AuthResponse>(HttpMethod.Post, "api/auth/change-password", new ChangePasswordRequest(currentPassword, newPassword));
        ApplyToken(auth);
    }

    private System.Threading.Timer? _refreshTimer;
    private System.Threading.SynchronizationContext? _uiContext;

    private UserDto StartSession(AuthResponse auth)
    {
        _uiContext = System.Threading.SynchronizationContext.Current ?? _uiContext;
        ApplyToken(auth);
        SessionChanged?.Invoke();
        return auth.User;
    }

    private void ApplyToken(AuthResponse auth)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        CurrentUser = auth.User;
        ScheduleRefresh(auth.ExpiresAt);
    }

    /// <summary>Perbarui token pada ~75% masa berlakunya selama pengguna masih memakai aplikasi (server membatasi total lama sesi).</summary>
    private void ScheduleRefresh(DateTime expiresAtUtc)
    {
        var lifetime = expiresAtUtc.ToUniversalTime() - DateTime.UtcNow;
        var due = TimeSpan.FromTicks((long)(lifetime.Ticks * 0.75));
        if (due < TimeSpan.FromSeconds(15)) due = TimeSpan.FromSeconds(15);
        _refreshTimer?.Dispose();
        _refreshTimer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, due, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshAsync()
    {
        if (!IsLoggedIn) return;
        try
        {
            using var resp = await _http.PostAsync("api/auth/refresh", null);
            if (resp.IsSuccessStatusCode)
            {
                var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>(Json);
                if (auth != null && IsLoggedIn) { ApplyToken(auth); return; }
            }
            else if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Sesi dicabut atau sudah melewati batas lama sesi: kembali ke layar login.
                RaiseOnUi(EndSession);
                return;
            }
        }
        catch (Exception) { /* jaringan putus: coba lagi sebentar lagi */ }

        if (IsLoggedIn)
        {
            _refreshTimer?.Dispose();
            _refreshTimer = new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(30), System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void RaiseOnUi(Action action)
    {
        if (_uiContext != null) _uiContext.Post(_ => action(), null);
        else action();
    }

    private void EndSession()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
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

    public Task<SimulationDto> GetSimulationAsync(string hotspotId, int hours = 18) =>
        GetAsync<SimulationDto>($"api/hotspots/{Uri.EscapeDataString(hotspotId)}/simulation?hours={hours}");

    // ================= lokasi tersimpan dan notifikasi (pengguna terdaftar) =================

    public async Task<IReadOnlyList<MonitoringAreaDto>> GetMonitoringAreasAsync() =>
        await GetAsync<List<MonitoringAreaDto>>("api/monitoring-areas");

    /// <summary>Simpan lokasi baru. Server langsung memeriksa dampak simulasi dan membuat notifikasi bila lokasi sudah terancam.</summary>
    public Task<MonitoringAreaDto> CreateMonitoringAreaAsync(MonitoringAreaRequest request) =>
        SendForAsync<MonitoringAreaDto>(HttpMethod.Post, "api/monitoring-areas", request);

    public Task<MonitoringAreaDto> UpdateMonitoringAreaAsync(string id, MonitoringAreaRequest request) =>
        SendForAsync<MonitoringAreaDto>(HttpMethod.Put, $"api/monitoring-areas/{Uri.EscapeDataString(id)}", request);

    public async Task DeleteMonitoringAreaAsync(string id)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/monitoring-areas/{Uri.EscapeDataString(id)}");
    }

    /// <summary>Notifikasi milik pengguna (terbaru di atas) beserta jumlah yang belum dibaca.</summary>
    public Task<NotificationListDto> GetNotificationsAsync(bool unreadOnly = false, int limit = 50) =>
        GetAsync<NotificationListDto>($"api/notifications?unreadOnly={(unreadOnly ? "true" : "false")}&limit={limit}");

    public async Task MarkNotificationReadAsync(string id)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"api/notifications/{Uri.EscapeDataString(id)}/read");
    }

    public async Task MarkAllNotificationsReadAsync()
    {
        using var _ = await SendAsync(HttpMethod.Post, "api/notifications/read-all");
    }

    public async Task DeleteNotificationAsync(string id)
    {
        using var _ = await SendAsync(HttpMethod.Delete, $"api/notifications/{Uri.EscapeDataString(id)}");
    }

    /// <summary>Minta server memeriksa lokasi saya sekarang (mis. setelah cuaca berubah).</summary>
    public Task<CheckResultDto> CheckNotificationsAsync() =>
        SendForAsync<CheckResultDto>(HttpMethod.Post, "api/notifications/check");

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

    /// <summary>Ganti role pengguna. Mengembalikan pesan error aturan bisnis, atau null bila berhasil.</summary>
    public async Task<string?> SetUserRoleAsync(string id, Role role)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Put, $"api/users/{Uri.EscapeDataString(id)}/role", new SetUserRoleRequest(role));
            return null;
        }
        catch (BackendException ex) when (ex.InnerException == null)
        {
            return ex.Message;
        }
    }

    // ================= API key layanan luar (admin) =================

    /// <summary>Keadaan key FIRMS dan OpenWeather: asal (override/.env/kosong) dan versi bertopeng. Key lengkap tidak pernah dikirim ke sini.</summary>
    public async Task<IReadOnlyList<IntegrationKeyDto>> GetIntegrationKeysAsync() =>
        await GetAsync<List<IntegrationKeyDto>>("api/settings/integrations");

    /// <summary>Ganti key ("firms" atau "weather"). Server mengujinya dulu; melempar <see cref="BackendException"/> bila gagal diuji.</summary>
    public Task<IntegrationKeyDto> SetIntegrationKeyAsync(string provider, string key) =>
        SendForAsync<IntegrationKeyDto>(HttpMethod.Put, $"api/settings/integrations/{Uri.EscapeDataString(provider)}", new SetIntegrationKeyRequest(key));

    /// <summary>Buang override sehingga key .env berlaku lagi.</summary>
    public Task<IntegrationKeyDto> ResetIntegrationKeyAsync(string provider) =>
        SendForAsync<IntegrationKeyDto>(HttpMethod.Delete, $"api/settings/integrations/{Uri.EscapeDataString(provider)}");

    /// <summary>Uji key tanpa menyimpannya; <paramref name="key"/> null menguji key yang sedang berlaku.</summary>
    public Task<ProviderStatusDto> TestIntegrationKeyAsync(string provider, string? key) =>
        SendForAsync<ProviderStatusDto>(HttpMethod.Post, $"api/settings/integrations/{Uri.EscapeDataString(provider)}/test", new TestIntegrationKeyRequest(key));

    // ================= log & sinkronisasi (admin) =================

    public async Task<IReadOnlyList<string>> GetActivitiesAsync() => await GetAsync<List<string>>("api/activity");

    public async Task AddActivityAsync(string message)
    {
        using var _ = await SendAsync(HttpMethod.Post, "api/activity", new { message });
    }

    public Task<SyncResult> SyncAsync() => SendForAsync<SyncResult>(HttpMethod.Post, "api/sync");
}
