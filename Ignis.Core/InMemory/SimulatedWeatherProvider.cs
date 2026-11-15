using Ignis.Core.Abstractions;
using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.InMemory;

/// <summary>Data cuaca simulasi yang deterministik per koordinat (dipindahkan dari DataStore.GetWeather).</summary>
public sealed class SimulatedWeatherProvider : IWeatherProvider
{
    public bool IsConfigured => false;

    public Task<WeatherData> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var seed = (int)(Math.Abs(latitude * 1000) + Math.Abs(longitude * 1000));
        var r = new Random(seed);
        return Task.FromResult(new WeatherData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = DateTime.Now,
            Temperature = Math.Round(28.0 + r.NextDouble() * 7.0, 1),
            Humidity = Math.Round(50.0 + r.NextDouble() * 35.0, 0),
            WindSpeed = Math.Round(1.5 + r.NextDouble() * 7.0, 1),
            WindDirection = Math.Round(r.NextDouble() * 360, 0),
            Pressure = Math.Round(1008.0 + r.NextDouble() * 6.0, 0),
            Precipitation = r.NextDouble() < 0.25 ? Math.Round(r.NextDouble() * 4.0, 1) : 0,
            Condition = "Simulasi"
        });
    }

    public Task<ProviderCheck> CheckConnectionAsync(CancellationToken ct = default) =>
        Task.FromResult(new ProviderCheck(ConnectionState.MissingKey, "Weather dilewati: API key belum diisi."));
}
