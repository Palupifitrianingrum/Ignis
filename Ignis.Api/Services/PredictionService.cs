using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Prediksi penyebaran api. Cuaca yang dipakai disimpan per hotspot (tabel weather_data) dan dirujuk prediksi.</summary>
public sealed class PredictionService(AppDbContext db, WeatherClient weather, FwiPredictionModel model, ActivityLogService log,
                                      IntegrationOptions? options = null)
{
    /// <summary>Urut terbaru di atas.</summary>
    public async Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default) =>
        await db.Predictions.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    private Task<Prediction?> LatestAsync(string hotspotId, CancellationToken ct) =>
        db.Predictions.AsNoTracking().Where(p => p.HotspotId == hotspotId).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    private TimeSpan WeatherMaxAge => TimeSpan.FromMinutes(Math.Max(1, options?.WeatherMaxAgeMinutes ?? 60));

    private Task<WeatherData?> LatestWeatherAsync(string hotspotId, CancellationToken ct) =>
        db.WeatherData.AsNoTracking().Where(w => w.HotspotId == hotspotId).OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(ct);

    private async Task<WeatherData> StoreWeatherAsync(Hotspot h, WeatherData w, CancellationToken ct)
    {
        w.Id = await db.NextIdAsync(IdKind.Weather, ct);
        w.HotspotId = h.Id;
        db.WeatherData.Add(w);
        await db.SaveChangesAsync(ct);
        return w;
    }

    /// <summary>
    /// Ambil cuaca terbaru untuk hotspot. Urutan: (1) cuaca nyata dari OpenWeather bila key ada dan panggilan berhasil, disimpan sebagai baris baru;
    /// (2) bila gagal atau tanpa key, cuaca tersimpan yang terakhir dipakai ulang (tidak menumpuk baris yang sama);
    /// (3) bila belum ada sama sekali, data simulasi disimpan sebagai awal.
    /// </summary>
    private async Task<WeatherData> RefreshWeatherAsync(Hotspot h, WeatherData? latest, CancellationToken ct)
    {
        var real = await weather.TryFetchAsync(h.Latitude, h.Longitude, ct);
        if (real != null) return await StoreWeatherAsync(h, real, ct);
        if (latest != null) return latest;
        return await StoreWeatherAsync(h, WeatherClient.Simulate(h.Latitude, h.Longitude), ct);
    }

    /// <summary>
    /// Cuaca untuk hotspot: yang tersimpan bila masih segar (lebih muda dari <c>WeatherMaxAgeMinutes</c>), kalau tidak diambil ulang.
    /// Dengan begitu hasil simulasi stabil dalam rentang itu, tetapi tidak memakai cuaca berhari-hari yang lalu.
    /// </summary>
    public async Task<WeatherData> GetWeatherAsync(Hotspot h, CancellationToken ct = default)
    {
        var latest = await LatestWeatherAsync(h.Id, ct);
        if (latest != null && DateTime.Now - latest.Timestamp < WeatherMaxAge) return latest;
        return await RefreshWeatherAsync(h, latest, ct);
    }

    /// <summary>Hitung prediksi baru (dengan cuaca terbaru) untuk hotspot lalu simpan.</summary>
    public async Task<Prediction> PredictAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        var w = await RefreshWeatherAsync(hotspot, await LatestWeatherAsync(hotspot.Id, ct), ct);
        var prediction = model.Predict(hotspot, w);
        prediction.Id = await db.NextIdAsync(IdKind.Prediction, ct);
        db.Predictions.Add(prediction);
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"Prediksi {prediction.Id} dibuat untuk {hotspot.Id}", ct);
        return prediction;
    }

    /// <summary>Prediksi terakhir bila ada, kalau belum ada hitung baru.</summary>
    public async Task<Prediction> GetOrPredictAsync(Hotspot hotspot, CancellationToken ct = default) =>
        await LatestAsync(hotspot.Id, ct) ?? await PredictAsync(hotspot, ct);
}
