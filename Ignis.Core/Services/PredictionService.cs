using Ignis.Core.Abstractions;
using Ignis.Core.Entities;

namespace Ignis.Core.Services;

/// <summary>Prediksi penyebaran api. Cuaca yang dipakai disimpan per hotspot (tabel WEATHER_DATA) dan dirujuk prediksi.</summary>
public sealed class PredictionService
{
    private readonly IPredictionRepository _predictions;
    private readonly IWeatherRepository _weatherRepo;
    private readonly IWeatherProvider _weather;
    private readonly IPredictionModel _model;
    private readonly IActivityLog _log;

    public PredictionService(IPredictionRepository predictions, IWeatherRepository weatherRepo,
                             IWeatherProvider weather, IPredictionModel model, IActivityLog log)
    {
        _predictions = predictions;
        _weatherRepo = weatherRepo;
        _weather = weather;
        _model = model;
        _log = log;
    }

    public Task<IReadOnlyList<Prediction>> GetAllAsync(CancellationToken ct = default) => _predictions.GetAllAsync(ct);
    public Task<Prediction?> GetLatestAsync(string hotspotId, CancellationToken ct = default) => _predictions.GetLatestForHotspotAsync(hotspotId, ct);

    /// <summary>Ambil dan simpan data cuaca baru untuk hotspot.</summary>
    private async Task<WeatherData> FetchAndStoreWeatherAsync(Hotspot h, CancellationToken ct)
    {
        var w = await _weather.GetWeatherAsync(h.Latitude, h.Longitude, ct);
        w.Id = string.Empty;          // diberikan repository
        w.HotspotId = h.Id;
        return await _weatherRepo.AddAsync(w, ct);
    }

    /// <summary>Cuaca terakhir yang tersimpan untuk hotspot; bila belum ada, diambil lalu disimpan.</summary>
    public async Task<WeatherData> GetWeatherAsync(Hotspot h, CancellationToken ct = default) =>
        await _weatherRepo.GetLatestForHotspotAsync(h.Id, ct) ?? await FetchAndStoreWeatherAsync(h, ct);

    /// <summary>Hitung prediksi baru (dengan cuaca terbaru) untuk hotspot lalu simpan.</summary>
    public async Task<Prediction> PredictAsync(Hotspot hotspot, CancellationToken ct = default)
    {
        var weather = await FetchAndStoreWeatherAsync(hotspot, ct);
        var saved = await _predictions.AddAsync(_model.Predict(hotspot, weather), ct);
        await _log.AddAsync($"Prediksi {saved.Id} dibuat untuk {hotspot.Id}", ct);
        return saved;
    }

    /// <summary>Prediksi terakhir bila ada, kalau belum ada hitung baru.</summary>
    public async Task<Prediction> GetOrPredictAsync(Hotspot hotspot, CancellationToken ct = default) =>
        await _predictions.GetLatestForHotspotAsync(hotspot.Id, ct) ?? await PredictAsync(hotspot, ct);
}
