using Ignis.Api.Domain;

namespace Ignis.Api.Services;

/// <summary>Satu langkah waktu simulasi: sel-sel yang terbakar (kumulatif) pada jam ke-<see cref="HourOffset"/>.</summary>
/// <param name="NewCells">Jumlah sel yang baru menyala pada jam ini (jam 0 = 1, titik asal).</param>
public sealed record SimulationFrame(int HourOffset, IReadOnlyList<Coordinate> BurningCells, double AreaHa, int NewCells = 0);

/// <summary>Hasil lengkap satu simulasi: kondisi yang dipakai, ukuran sel, dan frame per jam.</summary>
/// <param name="SpreadDirection">Arah RAMBATAN api dalam derajat (0 = utara, 90 = timur), yaitu kebalikan arah asal angin.</param>
/// <param name="WindFromDirection">Arah ASAL angin menurut konvensi meteorologi (270 = angin dari barat).</param>
/// <param name="Truncated">True bila jumlah sel mencapai batas <see cref="FireSpreadSimulator.MaxCells"/> sehingga rambatan berhenti dibatasi model.</param>
public sealed record SimulationResult(
    string HotspotId,
    double OriginLatitude,
    double OriginLongitude,
    int Hours,
    double SpreadDirection,
    double WindFromDirection,
    double WindSpeed,
    double Temperature,
    double Humidity,
    double Precipitation,
    string Condition,
    double CellSizeDegrees,
    double CellAreaHa,
    int MaxCells,
    bool Truncated,
    IReadOnlyList<SimulationFrame> Frames);

/// <summary>
/// Simulasi penyebaran api berbasis cellular automata deterministik.
/// Grid sel persegi di sekitar titik asal. Tiap jam, sel yang bertetangga dengan sel terbakar (8 arah) menyala bila
/// "undian" tetapnya (hash dari lokasi, sel, dan jam) tidak melebihi peluang penyalaan. Peluang bergantung pada
/// kekeringan (kelembapan), suhu, hujan, dan keselarasan arah rambatan dengan angin. Input sama selalu memberi hasil sama.
///
/// Konvensi arah: <see cref="WeatherData.WindDirection"/> adalah arah ASAL angin (seperti OpenWeather). Angin dari barat
/// (270°) mendorong api ke TIMUR, jadi arah rambatan = (WindDirection + 180) mod 360. Pada angin tenang rambatan merata.
///
/// Batasan model: tidak memakai jenis lahan/bahan bakar, medan, atau data historis; hasilnya ilustrasi, bukan prakiraan operasional.
/// </summary>
public sealed class FireSpreadSimulator
{
    public const double CellStepDegrees = 0.005;     // ±0,557 km di lintang mana pun (1° lintang ≈ 111,32 km)
    public const int MaxCells = 400;
    public const int MaxHours = 24;
    private const double KmPerDegree = 111.32;
    private const double SpreadRateScale = 0.30;   // kalibrasi laju: tanpa ini api memenuhi grid dalam ±10 jam

    private static readonly (int dRow, int dCol)[] Neighbors =
    {
        (-1,  0), (-1,  1), (0,  1), (1,  1),
        ( 1,  0), ( 1, -1), (0, -1), (-1, -1)
    };

    /// <summary>Frame per jam (jam 0 sampai <paramref name="hours"/>). Pembungkus ringkas dari <see cref="Run"/>.</summary>
    public IEnumerable<SimulationFrame> Simulate(Hotspot origin, WeatherData weather, int hours = 6) =>
        Run(origin, weather, hours).Frames;

    public SimulationResult Run(Hotspot origin, WeatherData weather, int hours = 6)
    {
        int totalHours = Math.Clamp(hours, 1, MaxHours);

        // Arah ASAL angin -> arah RAMBATAN (ke mana api didorong).
        double spreadDirection = (weather.WindDirection % 360 + 360 + 180) % 360;
        double spreadRad = spreadDirection * Math.PI / 180.0;
        double spreadNorth = Math.Cos(spreadRad);
        double spreadEast = Math.Sin(spreadRad);

        // Kekuatan angin 0..1,3: angin tenang tidak punya arah pilihan (rambatan merata), angin kencang sangat searah.
        double windStrength = Math.Clamp(weather.WindSpeed / 10.0, 0.0, 1.3);

        double dryness = Math.Clamp((100.0 - weather.Humidity) / 100.0, 0.05, 1.0);
        double heat = Math.Clamp((weather.Temperature - 20.0) / 25.0, 0.1, 1.0);
        double rainDamp = Math.Max(0.1, 1.0 - weather.Precipitation * 0.15);
        double baseProb = Math.Clamp((0.22 + 0.35 * dryness + 0.18 * heat) * rainDamp, 0.12, 0.85);

        double cellAreaHa = Math.Round(
            (CellStepDegrees * KmPerDegree) * (CellStepDegrees * KmPerDegree * Math.Cos(origin.Latitude * Math.PI / 180.0)) * 100.0, 2);

        var burning = new HashSet<(int Row, int Col)> { (0, 0) };
        var frames = new List<SimulationFrame>(totalHours + 1)
        {
            BuildFrame(0, origin.Latitude, origin.Longitude, burning, cellAreaHa, newCells: 1)
        };
        bool truncated = false;

        for (int h = 1; h <= totalHours; h++)
        {
            // Peluang tertinggi tiap sel kandidat, dari semua tetangganya yang sedang terbakar.
            var candidates = new Dictionary<(int Row, int Col), double>();
            foreach (var (r, c) in burning)
            {
                foreach (var (dr, dc) in Neighbors)
                {
                    var next = (Row: r + dr, Col: c + dc);
                    if (burning.Contains(next)) continue;

                    double dist = Math.Sqrt(dr * dr + dc * dc);
                    double alignment = (dr / dist) * spreadNorth + (dc / dist) * spreadEast;   // -1 .. +1

                    double windBoost = alignment >= 0
                        ? 1.0 + alignment * 2.6 * windStrength
                        : Math.Max(0.2, 1.0 + alignment * 0.9 * Math.Min(1.0, windStrength));

                    double distancePenalty = dist > 1.0 ? 0.78 : 1.0;
                    double prob = Math.Clamp(baseProb * SpreadRateScale * windBoost * distancePenalty, 0.01, 0.9);

                    if (!candidates.TryGetValue(next, out var best) || prob > best) candidates[next] = prob;
                }
            }

            var ignited = new List<(int Row, int Col, double Score)>();
            foreach (var (cell, prob) in candidates)
            {
                double roll = DeterministicRoll(origin.Latitude, origin.Longitude, cell.Row, cell.Col, h);
                if (roll <= prob) ignited.Add((cell.Row, cell.Col, roll / prob));
            }

            // Bila melebihi batas, pertahankan yang paling "pasti" menyala. Urutan tetap: skor, lalu baris, lalu kolom.
            int room = MaxCells - burning.Count;
            if (ignited.Count > room)
            {
                truncated = true;
                ignited = ignited.OrderBy(i => i.Score).ThenBy(i => i.Row).ThenBy(i => i.Col).Take(Math.Max(0, room)).ToList();
            }
            foreach (var i in ignited) burning.Add((i.Row, i.Col));
            if (burning.Count >= MaxCells) truncated = true;

            frames.Add(BuildFrame(h, origin.Latitude, origin.Longitude, burning, cellAreaHa, ignited.Count));
        }

        return new SimulationResult(
            origin.Id,
            origin.Latitude,
            origin.Longitude,
            totalHours,
            Math.Round(spreadDirection, 1),
            Math.Round((weather.WindDirection % 360 + 360) % 360, 1),
            weather.WindSpeed,
            weather.Temperature,
            weather.Humidity,
            weather.Precipitation,
            weather.Condition,
            CellStepDegrees,
            cellAreaHa,
            MaxCells,
            truncated,
            frames);
    }

    private static SimulationFrame BuildFrame(int hour, double lat0, double lon0, HashSet<(int Row, int Col)> burning,
                                              double cellAreaHa, int newCells)
    {
        var coords = burning
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .Select(c => new Coordinate(
                Math.Round(lat0 + c.Row * CellStepDegrees, 6),
                Math.Round(lon0 + c.Col * CellStepDegrees, 6)))
            .ToList();

        return new SimulationFrame(hour, coords, Math.Round(coords.Count * cellAreaHa, 2), newCells);
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
