using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Backend;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Services.Admin;

/// <summary>
/// Sisi-klien untuk modul admin: cache data dari <see cref="IgnisApiClient"/> (Ignis.Api),
/// dibungkus jadi baris tampilan, plus event perubahan untuk UI. Tanpa logika bisnis — itu ada di Ignis.Api.
/// </summary>
public sealed class AdminWorkspace
{
    public static AdminWorkspace Current => App.Services.GetRequiredService<AdminWorkspace>();

    private readonly IgnisApiClient _backend;

    public AdminWorkspace(IgnisApiClient backend, AppSettings settings)
    {
        _backend = backend;
        Settings = settings;
    }

    public AppSettings Settings { get; }
    public string BackendName => _backend.Name;

    public IReadOnlyList<HotspotRow> Hotspots { get; private set; } = Array.Empty<HotspotRow>();
    public IReadOnlyList<PredictionRow> Predictions { get; private set; } = Array.Empty<PredictionRow>();
    public IReadOnlyList<UserRow> Users { get; private set; } = Array.Empty<UserRow>();
    public ObservableCollection<string> Activities { get; } = new();

    public DateTime? LastSync { get; private set; }
    public string FirmsStatus { get; private set; } = StatusText(ConnectionState.Unknown);
    public string WeatherStatus { get; private set; } = StatusText(ConnectionState.Unknown);

    public event Action? Changed;
    public event Action? SettingsChanged;
    public event Action<int>? NewHotspots;

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    public static string StatusText(ConnectionState s) => s switch
    {
        ConnectionState.Syncing => "Menyinkronkan...",
        ConnectionState.Connected => "Terhubung",
        ConnectionState.Failed => "Gagal",
        ConnectionState.MissingKey => "API key belum diisi",
        _ => "Belum dicek"
    };

    // ---------- Muat ulang ----------
    public async Task ReloadAsync()
    {
        var hotspots = await _backend.GetHotspotsAsync();
        var predictions = await _backend.GetPredictionsAsync();
        var users = await _backend.GetUsersAsync();
        var activities = await _backend.GetActivitiesAsync();

        Hotspots = hotspots.Select(h => new HotspotRow(h)).ToList();
        Predictions = predictions.Select(p => new PredictionRow(p)).ToList();
        Users = users.Select(u => new UserRow(u)).ToList();

        Activities.Clear();
        foreach (var a in activities) Activities.Add(a);
        Changed?.Invoke();
    }

    public HotspotRow? FindHotspot(string id) => Hotspots.FirstOrDefault(h => h.Id == id);

    public IEnumerable<HotspotRow> Filter(Region? region, HotspotSource? src, HotspotStatus? status) =>
        Hotspots.Where(h => (region == null || region.Contains(h.Latitude, h.Longitude))
                         && (src == null || h.Source == src)
                         && (status == null || h.Status == status));

    // ---------- Hotspot ----------
    public async Task AddManualAsync(ManualHotspotRequest request) { await _backend.AddManualHotspotAsync(request); await ReloadAsync(); }
    public async Task UpdateManualAsync(string id, ManualHotspotRequest request) { await _backend.UpdateManualHotspotAsync(id, request); await ReloadAsync(); }
    public async Task DeleteHotspotAsync(string id) { await _backend.DeleteHotspotAsync(id); await ReloadAsync(); }

    // ---------- Cuaca & prediksi ----------
    public Task<WeatherDto> GetWeatherAsync(HotspotRow h) => _backend.GetWeatherAsync(h.Id);

    public async Task<PredictionRow> GetOrPredictAsync(HotspotRow h)
    {
        var p = new PredictionRow(await _backend.GetOrPredictAsync(h.Id));
        if (_backend.IsAdmin && Predictions.All(x => x.Id != p.Id)) await ReloadAsync();   // prediksi baru dibuat di backend
        return p;
    }

    public async Task<PredictionRow> PredictAsync(HotspotRow h)
    {
        var p = new PredictionRow(await _backend.PredictAsync(h.Id));
        await ReloadAsync();
        return p;
    }

    // ---------- Pengguna ----------
    public async Task<string?> SetUserRoleAsync(string id, Ignis.Shared.Role role)
    {
        var err = await _backend.SetUserRoleAsync(id, role);
        await ReloadAsync();
        return err;
    }

    /// <summary>Ganti password akun yang sedang login. Pesan error aturan bisnis dikembalikan (null = berhasil).</summary>
    public async Task<string?> ChangePasswordAsync(string? current, string next)
    {
        try { await _backend.ChangePasswordAsync(current, next); return null; }
        catch (BackendException ex) { return ex.Message; }
    }

    public bool IsLoggedIn => _backend.IsLoggedIn;
    public bool HasPassword => _backend.CurrentUser?.HasPassword ?? true;

    public async Task<string?> ToggleUserAsync(string id)
    {
        var err = await _backend.ToggleUserAsync(id);
        await ReloadAsync();
        return err;
    }

    // ---------- Log & sinkronisasi ----------
    public async Task LogAsync(string message)
    {
        await _backend.AddActivityAsync(message);
        await ReloadAsync();
    }

    public async Task<string> SyncAsync()
    {
        var previousFirms = FirmsStatus;
        FirmsStatus = StatusText(ConnectionState.Syncing);
        Changed?.Invoke();

        SyncResult result;
        try { result = await _backend.SyncAsync(); }
        catch
        {
            FirmsStatus = previousFirms;   // gagal menghubungi server: jangan tinggalkan status "Menyinkronkan..."
            Changed?.Invoke();
            throw;
        }

        if (result.Firms != ConnectionState.Unknown || result.Weather != ConnectionState.Unknown)
        {
            FirmsStatus = StatusText(result.Firms);
            WeatherStatus = StatusText(result.Weather);
            LastSync = result.FinishedAt;
        }
        else
        {
            FirmsStatus = previousFirms;   // "sedang berjalan": status lama dipertahankan
        }
        if (result.NewHotspots > 0) NewHotspots?.Invoke(result.NewHotspots);
        await ReloadAsync();
        return string.Join("\n", result.Messages);
    }
}
