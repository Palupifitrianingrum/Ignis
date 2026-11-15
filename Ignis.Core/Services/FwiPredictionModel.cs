using Ignis.Core.Abstractions;
using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.Services;

/// <summary>Model prediksi sederhana berbasis indeks cuaca kebakaran (FWI). Rumus dipindahkan apa adanya dari DataStore.</summary>
public sealed class FwiPredictionModel : IPredictionModel
{
    public string ModelName => "Simple FWI";
    public string ModelVersion => "1.0";

    public Prediction Predict(Hotspot hotspot, WeatherData w)
    {
        double fwi = Math.Max(1.0, (w.Temperature - 20) * 0.8 + (100 - w.Humidity) * 0.4 + w.WindSpeed * 1.5 - w.Precipitation * 4);
        double speed = Math.Round(Math.Clamp(0.05 + (w.WindSpeed / 10.0) * (fwi / 50.0) * 0.8, 0.05, 3.5), 2);
        double hours = speed > 0.5 ? 3 : speed > 0.2 ? 6 : speed > 0.1 ? 8 : 12;
        double d = speed * hours;
        double area = Math.Round(0.5 * Math.PI * d * (d / 2.5) * 100, 1); // ha, setengah elips
        var risk = area < 20 ? RiskLevel.Low : area < 80 ? RiskLevel.Medium : area < 250 ? RiskLevel.High : RiskLevel.Critical;

        return new Prediction
        {
            HotspotId = hotspot.Id,
            Direction = (w.WindDirection + 180) % 360,
            SpeedKmH = speed,
            AreaHa = area,
            Hours = hours,
            Risk = risk,
            CreatedAt = DateTime.Now,
            WeatherId = string.IsNullOrEmpty(w.Id) ? null : w.Id
        };
    }
}
