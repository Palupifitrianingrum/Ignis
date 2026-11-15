using Ignis.Api.Domain;

namespace Ignis.Api.Services;

/// <summary>Satu langkah waktu simulasi: sel-sel yang terbakar pada jam ke-<see cref="HourOffset"/>.</summary>
public sealed record SimulationFrame(int HourOffset, IReadOnlyList<Coordinate> BurningCells, double AreaHa);

/// <summary>
/// Simulasi penyebaran api berbasis cellular automata deterministik.
/// Grid sel di sekitar titik asal; tiap jam sel terbakar menyulut tetangganya dengan peluang yang bergantung pada
/// kecepatan dan arah angin (<see cref="WeatherData.GetWindVector"/>), kelembapan, suhu, dan curah hujan.
/// </summary>
public sealed class FireSpreadSimulator
{
    private const double CellStepDeg = 0.005;   // ~0.55 km di khatulistiwa
    private const double CellAreaHa = 30.8;     // ~0.555 km * 0.555 km = ~0.308 km² = 30.8 ha
    private const int MaxCells = 250;

    private static readonly (int dRow, int dCol)[] Neighbors =
    {
        (-1,  0), (-1,  1), (0,  1), (1,  1),
        ( 1,  0), ( 1, -1), (0, -1), (-1, -1)
    };

    public IEnumerable<SimulationFrame> Simulate(Hotspot origin, WeatherData weather, int hours = 6)
    {
        int totalHours = Math.Clamp(hours, 1, 24);
        var wind = weather.GetWindVector();
        double windRad = wind.DirectionDegrees * Math.PI / 180.0;
        double windNorth = Math.Cos(windRad);
        double windEast = Math.Sin(windRad);

        // Faktor dasar lingkungan (0.15 .. 0.85)
        double dryness = Math.Clamp((100.0 - weather.Humidity) / 100.0, 0.05, 1.0);
        double heat = Math.Clamp((weather.Temperature - 20.0) / 25.0, 0.1, 1.0);
        double rainDamp = Math.Max(0.1, 1.0 - weather.Precipitation * 0.15);
        double windFactor = Math.Clamp(wind.Magnitude / 12.0, 0.05, 1.2);
        double baseProb = Math.Clamp((0.22 + 0.35 * dryness + 0.18 * heat) * rainDamp, 0.12, 0.85);

        var burning = new HashSet<(int Row, int Col)> { (0, 0) };
        var frames = new List<SimulationFrame>(totalHours + 1)
        {
            BuildFrame(0, origin.Latitude, origin.Longitude, burning)
        };

        for (int h = 1; h <= totalHours; h++)
        {
            if (burning.Count < MaxCells)
            {
                var snapshot = burning.ToList();
                foreach (var (r, c) in snapshot)
                {
                    foreach (var (dr, dc) in Neighbors)
                    {
                        var next = (Row: r + dr, Col: c + dc);
                        if (burning.Contains(next)) continue;

                        double dist = Math.Sqrt(dr * dr + dc * dc);
                        double dirNorth = dr / dist;
                        double dirEast = dc / dist;

                        // Keselarasan arah rambatan dengan arah tiupan angin (-1 .. +1)
                        double alignment = dirNorth * windNorth + dirEast * windEast;
                        double windBoost = alignment >= 0
                            ? 1.0 + alignment * (0.8 + windFactor * 1.4)
                            : Math.Max(0.15, 1.0 + alignment * 0.75);

                        double distancePenalty = dist > 1.0 ? 0.78 : 1.0;
                        double prob = Math.Clamp(baseProb * windBoost * distancePenalty, 0.05, 0.95);

                        if (DeterministicRoll(origin.Latitude, origin.Longitude, next.Row, next.Col, h) <= prob)
                        {
                            burning.Add(next);
                            if (burning.Count >= MaxCells) break;
                        }
                    }
                    if (burning.Count >= MaxCells) break;
                }
            }

            frames.Add(BuildFrame(h, origin.Latitude, origin.Longitude, burning));
        }

        return frames;
    }

    private static SimulationFrame BuildFrame(int hour, double lat0, double lon0, HashSet<(int Row, int Col)> burning)
    {
        var coords = burning
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .Select(c => new Coordinate(
                Math.Round(lat0 + c.Row * CellStepDeg, 6),
                Math.Round(lon0 + c.Col * CellStepDeg, 6)))
            .ToList();

        return new SimulationFrame(hour, coords, Math.Round(coords.Count * CellAreaHa, 2));
    }

    private static double DeterministicRoll(double lat, double lon, int row, int col, int hour)
    {
        unchecked
        {
            long h = BitConverter.DoubleToInt64Bits(Math.Round(lat, 4)) * 31
                   ^ BitConverter.DoubleToInt64Bits(Math.Round(lon, 4)) * 97
                   ^ (row * 73856093L)
                   ^ (col * 19349663L)
                   ^ (hour * 83492791L);
            h ^= (h >> 33);
            h *= -49064778989728563L;
            h ^= (h >> 33);
            return ((h & 0x7FFFFFFFL) % 10000) / 10000.0;
        }
    }
}
