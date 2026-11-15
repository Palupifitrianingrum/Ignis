using Ignis.Api.Domain;

namespace Ignis.Api.Services;

/// <summary>Satu langkah waktu simulasi: sel-sel yang terbakar pada jam ke-<see cref="HourOffset"/>.</summary>
public sealed record SimulationFrame(int HourOffset, IReadOnlyList<Coordinate> BurningCells, double AreaHa);

/// <summary>
/// TODO(backend): simulasi penyebaran api (cellular automata) belum diimplementasikan.
/// Rencana: grid sel di sekitar titik asal; tiap jam sel terbakar menyulut tetangganya dengan peluang yang bergantung pada
/// kecepatan dan arah angin (WeatherData.GetWindVector), kelembapan, dan bahan bakar. Keluaran: satu SimulationFrame per jam,
/// lalu dibuka lewat endpoint GET /api/hotspots/{id}/simulation?hours=N.
/// </summary>
public sealed class FireSpreadSimulator
{
    public IEnumerable<SimulationFrame> Simulate(Hotspot origin, WeatherData weather, int hours) =>
        throw new NotImplementedException("Simulasi penyebaran api belum diimplementasikan.");
}
