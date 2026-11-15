using Ignis.Core.Abstractions;
using Ignis.Core.Entities;

namespace Ignis.Core.Services;

/// <summary>
/// Pakai penyedia cuaca sungguhan bila API key tersedia; kalau tidak (atau gagal) jatuh ke penyedia cadangan (data simulasi).
/// Dengan begitu aplikasi tetap berjalan tanpa key.
/// </summary>
public sealed class FallbackWeatherProvider : IWeatherProvider
{
    private readonly IWeatherProvider _primary;
    private readonly IWeatherProvider _fallback;

    public FallbackWeatherProvider(IWeatherProvider primary, IWeatherProvider fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public bool IsConfigured => _primary.IsConfigured;

    public async Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        if (_primary.IsConfigured)
        {
            try { return await _primary.GetWeatherAsync(latitude, longitude, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* jatuh ke cadangan */ }
        }
        return await _fallback.GetWeatherAsync(latitude, longitude, ct);
    }

    public Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default) => _primary.CheckConnectionAsync(ct);
}
