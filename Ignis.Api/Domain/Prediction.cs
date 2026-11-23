using Ignis.Shared;

namespace Ignis.Api.Domain;

/// <summary>Hasil prediksi penyebaran api untuk satu hotspot, berdasarkan satu data cuaca.</summary>
public class Prediction
{
    public string Id { get; set; } = string.Empty;
    public string HotspotId { get; set; } = string.Empty;

    /// <summary>Cuaca yang dipakai (nullable: data demo lama tidak punya baris cuaca).</summary>
    public string? WeatherId { get; set; }

    public double Direction { get; set; }   // spreadDirection
    public double SpeedKmH { get; set; }    // spreadSpeed
    public double AreaHa { get; set; }      // estimatedArea
    public double Hours { get; set; }       // estimatedTime
    public RiskLevel Risk { get; set; }     // riskLevel
    public DateTime CreatedAt { get; set; }
}
