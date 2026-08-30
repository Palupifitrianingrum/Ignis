using ignis.Api.Modules.FireDetection;
using ignis.Api.Modules.Weather;

namespace ignis.Api.Modules.PredictionEngine;

public class SpreadPrediction
{
    public int HotspotId { get; set; }
    public double RateOfSpreadKmPerHour { get; set; }
    public double? DirectionDegrees { get; set; } // null untuk api gambut (menjalar di bawah permukaan)
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

// Strategy pattern: tiap jenis lahan punya cara hitung penyebaran api
// yang beda — gampang nambah model baru nanti tanpa ubah kode yang sudah ada
public interface IPredictionStrategy
{
    string LandType { get; }
    SpreadPrediction Predict(FireHotspot hotspot, WeatherReading weather);
}

// Model fisik Rothermel — untuk kebakaran vegetasi biasa,
// dipengaruhi kecepatan angin & kelembaban bahan bakar
public class RothermelPredictionStrategy : IPredictionStrategy
{
    public string LandType => "Vegetasi";

    public SpreadPrediction Predict(FireHotspot hotspot, WeatherReading weather)
    {
        double rateOfSpread = weather.WindSpeed * (1 - weather.Humidity / 100) * 0.5;
        return new SpreadPrediction
        {
            HotspotId = hotspot.Id,
            RateOfSpreadKmPerHour = rateOfSpread,
            DirectionDegrees = weather.WindDirection
        };
    }
}

// Api gambut menjalar di bawah permukaan & dipengaruhi kelembaban tanah,
// bukan arah angin — butuh model terpisah dari Rothermel
public class PeatFirePredictionStrategy : IPredictionStrategy
{
    public string LandType => "Gambut";

    public SpreadPrediction Predict(FireHotspot hotspot, WeatherReading weather)
    {
        double smolderRate = (100 - weather.Humidity) * 0.1;
        return new SpreadPrediction
        {
            HotspotId = hotspot.Id,
            RateOfSpreadKmPerHour = smolderRate,
            DirectionDegrees = null
        };
    }
}

// Orchestrator: pilih strategi yang cocok berdasarkan jenis lahan
// hotspot-nya, lalu delegasikan perhitungan ke strategi itu
public class PredictionEngine
{
    private readonly IEnumerable<IPredictionStrategy> _strategies;

    public PredictionEngine(IEnumerable<IPredictionStrategy> strategies)
    {
        _strategies = strategies;
    }

    public SpreadPrediction Predict(FireHotspot hotspot, WeatherReading weather)
    {
        var strategy = _strategies.FirstOrDefault(s => s.LandType == hotspot.LandType)
            ?? throw new NotSupportedException($"Belum ada strategi untuk land type: {hotspot.LandType}");

        return strategy.Predict(hotspot, weather);
    }
}