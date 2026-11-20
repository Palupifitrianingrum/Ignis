namespace Ignis.Api.Services;

/// <summary>
/// Dampak sebuah simulasi pada satu titik (lokasi tersimpan) dengan radius peringatan.
/// <see cref="FireEtaHours"/>/<see cref="SmokeEtaHours"/> null = tidak mencapai radius selama horizon; 0 = sudah di dalam radius.
/// </summary>
public sealed record AreaExposure(
    double HotspotDistanceKm,
    int? FireEtaHours,
    double FireMinDistanceKm,
    int SmokeMaxLevel,
    int? SmokeEtaHours,
    int? SmokeMaxLevelEtaHours)
{
    public bool FireReaches => FireEtaHours.HasValue;
    public bool SmokeReaches => SmokeEtaHours.HasValue;
}

/// <summary>
/// Menentukan apakah api atau asap hasil simulasi mencapai radius peringatan sebuah lokasi, dan kapan.
/// Fungsi murni (tanpa database), dipakai oleh notifikasi dan oleh panel simulasi di desktop.
///
/// "Mencapai" berarti jarak dari lokasi ke TEPI terdekat sebuah sel yang terbakar (atau berasap) tidak lebih dari radius.
/// Sel api berukuran ±0,56 km dan sel asap ±2,8 km, jadi titik yang berada di dalam sel dihitung berjarak nol.
/// </summary>
public static class ExposureEvaluator
{
    public static AreaExposure Evaluate(SimulationResult sim, double latitude, double longitude, double radiusKm, int horizonHours)
    {
        int last = Math.Clamp(horizonHours, 0, sim.Hours);
        double fireHalf = FireSpreadSimulator.CellStepDegrees / 2.0;
        double smokeHalf = sim.SmokeCellSizeDegrees / 2.0;

        int? fireEta = null;
        double fireMin = double.PositiveInfinity;
        int smokeMax = 0;
        int? smokeEta = null, smokeMaxEta = null;

        for (int h = 0; h <= last; h++)
        {
            var frame = sim.Frames[h];

            foreach (var c in frame.BurningCells)
            {
                var d = Geo.DistanceToCellKm(latitude, longitude, c.Latitude, c.Longitude, fireHalf);
                if (d < fireMin) fireMin = d;
            }
            if (fireEta == null && fireMin <= radiusKm) fireEta = h;

            int frameMax = 0;
            foreach (var c in frame.SmokeCells)
            {
                var cellLat = sim.OriginLatitude + c.Row * sim.SmokeCellSizeDegrees;
                var cellLon = sim.OriginLongitude + c.Col * sim.SmokeCellSizeDegrees;
                if (c.Level > frameMax && Geo.DistanceToCellKm(latitude, longitude, cellLat, cellLon, smokeHalf) <= radiusKm)
                    frameMax = c.Level;
            }
            if (frameMax > 0)
            {
                smokeEta ??= h;
                if (frameMax > smokeMax)
                {
                    smokeMax = frameMax;
                    smokeMaxEta = h;
                }
            }
        }

        return new AreaExposure(
            Math.Round(Geo.DistanceKm(latitude, longitude, sim.OriginLatitude, sim.OriginLongitude), 1),
            fireEta,
            double.IsPositiveInfinity(fireMin) ? 0 : Math.Round(fireMin, 1),
            smokeMax,
            smokeEta,
            smokeMaxEta);
    }
}
