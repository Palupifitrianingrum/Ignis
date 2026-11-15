using Ignis.Core.Entities;
using Ignis.Core.ValueObjects;

namespace Ignis.Core.Simulation;

/// <summary>Satu langkah waktu simulasi: sel-sel yang terbakar pada jam ke-<see cref="HourOffset"/>.</summary>
public sealed record SimulationFrame(int HourOffset, IReadOnlyList<Coordinate> BurningCells, double AreaHa);

/// <summary>Mesin simulasi penyebaran api (mis. cellular automata) — keluarannya urutan frame untuk animasi peta.</summary>
public interface IFireSpreadSimulator
{
    IEnumerable<SimulationFrame> Simulate(Hotspot origin, WeatherData weather, int hours);
}
