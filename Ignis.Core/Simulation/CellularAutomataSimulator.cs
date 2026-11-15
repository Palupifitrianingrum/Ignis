using Ignis.Core.Entities;

namespace Ignis.Core.Simulation;

/// <summary>
/// TODO(backend): implementasi simulasi penyebaran api belum dibuat.
/// Rencana: grid sel di sekitar titik asal; tiap jam sel terbakar menyulut tetangganya dengan
/// peluang yang bergantung pada kecepatan &amp; arah angin (WeatherData.GetWindVector), kelembapan, dan bahan bakar.
/// Keluaran: satu SimulationFrame per jam.
/// </summary>
public sealed class CellularAutomataSimulator : IFireSpreadSimulator
{
    public IEnumerable<SimulationFrame> Simulate(Hotspot origin, WeatherData weather, int hours)
    {
        // TODO(backend): bangun grid, terapkan aturan penyebaran, yield return frame per jam.
        throw new NotImplementedException("Simulasi penyebaran api belum diimplementasikan.");
    }
}
