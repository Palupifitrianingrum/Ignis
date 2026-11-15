using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ignis.Core.Abstractions;
using Ignis.Core.Common;
using Ignis.Core.Contracts;
using Ignis.Core.Entities;
using Ignis.Core.Services;

namespace Ignis.Frontend.Services.Backend;

/// <summary>Mode demo/offline: service Ignis.Core di atas penyimpanan in-memory. Tidak menyentuh database.</summary>
public sealed class LocalAdminBackend : IAdminBackend
{
    private readonly HotspotService _hotspots;
    private readonly PredictionService _predictions;
    private readonly UserService _users;
    private readonly HotspotSyncService _sync;
    private readonly IActivityLog _activity;

    public LocalAdminBackend(HotspotService hotspots, PredictionService predictions, UserService users,
                             HotspotSyncService sync, IActivityLog activity)
    {
        _hotspots = hotspots;
        _predictions = predictions;
        _users = users;
        _sync = sync;
        _activity = activity;
    }

    public string Name => "Demo (data di memori)";

    private async Task<Hotspot> RequireAsync(string id) =>
        await _hotspots.GetByIdAsync(id) ?? throw new DomainException($"Hotspot {id} tidak ditemukan.");

    public async Task<IReadOnlyList<HotspotDto>> GetHotspotsAsync() =>
        (await _hotspots.GetAllAsync()).Select(h => h.ToDto()).ToList();

    public async Task<HotspotDto> AddManualHotspotAsync(ManualHotspotRequest request) =>
        (await _hotspots.AddManualAsync(request)).ToDto();

    public async Task<HotspotDto> UpdateManualHotspotAsync(string id, ManualHotspotRequest request) =>
        (await _hotspots.UpdateManualAsync(id, request)).ToDto();

    public async Task DeleteHotspotAsync(string id) => await _hotspots.DeleteAsync(id);

    public async Task<WeatherDto> GetWeatherAsync(string hotspotId) =>
        (await _predictions.GetWeatherAsync(await RequireAsync(hotspotId))).ToDto();

    public async Task<IReadOnlyList<PredictionDto>> GetPredictionsAsync() =>
        (await _predictions.GetAllAsync()).Select(p => p.ToDto()).ToList();

    public async Task<PredictionDto> GetOrPredictAsync(string hotspotId) =>
        (await _predictions.GetOrPredictAsync(await RequireAsync(hotspotId))).ToDto();

    public async Task<PredictionDto> PredictAsync(string hotspotId) =>
        (await _predictions.PredictAsync(await RequireAsync(hotspotId))).ToDto();

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync() =>
        (await _users.GetAllAsync()).Select(u => u.ToDto()).ToList();

    public Task<string?> ToggleUserAsync(string id) => _users.ToggleActiveAsync(id);

    public Task<IReadOnlyList<string>> GetActivitiesAsync() => _activity.RecentAsync();
    public Task AddActivityAsync(string message) => _activity.AddAsync(message);

    public Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress) => _sync.RunAsync(progress);
}
