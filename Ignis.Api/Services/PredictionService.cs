using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Prediksi penyebaran api. Cuaca yang dipakai disimpan per hotspot (tabel weather_data) dan dirujuk prediksi.</summary>
public sealed class PredictionService(AppDbContext db, WeatherClient weather, FwiPredictionModel model, ActivityLogService log)
{
    /// <summary>Urut terbaru di atas.</summary>
    public async Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default) =>
        await db.Predictions.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    private Task<Prediction?> LatestAsync(string hotspotId, CancellationToken ct) =>
        db.Predictions.AsNoTracking().Where(p => p.HotspotId == hotspotId).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    /// <summary>Ambil dan simpan data cuaca baru untuk hotspot.</summary>
    private async Task<WeatherData> FetchAndStoreWeatherAsync(Hotspot h, CancellationToken ct)
    {
        var w = await weather.GetWeatherAsync(h.Latitude, h.Longitude, ct);
        w.Id = await db.NextIdAsync(IdKind.Weather, ct);
        w.HotspotId = h.Id;
        db.WeatherData.Add(w);
        await db.SaveChangesAsync(ct);
        return w;
    }

    /// <summary>Cuaca terakhir yang tersimpan untuk hotspot; bila belum ada, diambil lalu disimpan.</summary>
    public async Task<WeatherData> GetWeatherAsync(Hotspot h, CancellationToken ct = default) =>
        await db.WeatherData.AsNoTracking().Where(w => w.HotspotId == h.Id).OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(ct)
        ?? await FetchAndStoreWeatherAsync(h, ct);

    /// <summary>Hitung prediksi baru (dengan cuaca terbaru) untuk hotspot lalu simpan.</summary>
    public async Task<Prediction> PredictAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        var w = await FetchAndStoreWeatherAsync(hotspot, ct);
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
