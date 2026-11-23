using Ignis.Api.Domain;
using Ignis.Api.Services;
using Xunit;

namespace Ignis.Tests;

/// <summary>Model asap (adveksi-difusi) dan integrasinya di simulator.</summary>
public class SmokeModelTests
{
    private const double Lat0 = 0.32, Lon0 = 101.85;

    private static ManualHotspot Origin() => new(Lat0, Lon0, 85, DateTime.Now, "Petugas");

    private static WeatherData Weather(double windFrom = 270, double speed = 8, double rain = 0, double humidity = 40, double temp = 34) =>
        new() { Temperature = temp, Humidity = humidity, WindSpeed = speed, WindDirection = windFrom, Precipitation = rain };

    private static (double Row, double Col) Centroid(SimulationFrame f) =>
        (f.SmokeCells.Average(c => (double)c.Row), f.SmokeCells.Average(c => (double)c.Col));

    /// <summary>Sel api tetap selama <paramref name="hours"/> jam: bagian terkendali untuk menguji model asap tanpa pengaruh model api.</summary>
    private static IReadOnlyList<IReadOnlyList<(int Row, int Col)>> SteadyFire(int hours, params (int Row, int Col)[] cells) =>
        Enumerable.Range(0, hours + 1).Select(_ => (IReadOnlyList<(int Row, int Col)>)cells).ToList();

    // ------------------------------------------------------------------ integrasi di simulator

    [Fact]
    public void Hour0_HasNoSmoke_AndSmokeAppearsWithinAFewHours()
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(), 6);

        Assert.Empty(r.Frames[0].SmokeCells);   // api baru menyala: belum ada asap yang terbawa
        Assert.True(r.Frames[3].SmokeCells.Count > 0, "asap harus sudah terlihat dalam beberapa jam");
        Assert.True(r.Frames[6].SmokeCells.Count >= r.Frames[3].SmokeCells.Count);
    }

    [Fact]
    public void Result_ReportsSmokeCellSize_AndTheHighestLevel()
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(speed: 6), 18);

        Assert.Equal(SmokePlumeModel.CellStepDegrees, r.SmokeCellSizeDegrees);
        Assert.Equal(r.Frames.SelectMany(f => f.SmokeCells).Max(c => c.Level), r.SmokeMaxLevel);
        Assert.Equal(5 * FireSpreadSimulator.CellStepDegrees, SmokePlumeModel.CellStepDegrees, 9);   // grid asap selaras dengan grid api
    }

    // Angin DARI barat membawa asap KE timur (sama dengan konvensi api).
    [Theory]
    [InlineData(270, "timur")]
    [InlineData(90, "barat")]
    [InlineData(0, "selatan")]
    [InlineData(180, "utara")]
    public void Smoke_DriftsAwayFromWhereWindComesFrom(double windFrom, string expected)
    {
        var f = new FireSpreadSimulator().Run(Origin(), Weather(windFrom, speed: 8), 12).Frames[12];
        var (row, col) = Centroid(f);

        var actual = Math.Abs(col) > Math.Abs(row) ? (col > 0 ? "timur" : "barat") : (row > 0 ? "utara" : "selatan");
        Assert.Equal(expected, actual);
        Assert.True(Math.Max(Math.Abs(row), Math.Abs(col)) > 2, "pusat asap harus bergeser jauh dari titik asal");
    }

    [Fact]
    public void CalmWind_SpreadsSmokeEvenly_InEveryDirection()
    {
        var f = new FireSpreadSimulator().Run(Origin(), Weather(speed: 0), 12).Frames[12];
        var (row, col) = Centroid(f);

        Assert.True(Math.Abs(row) < 1.0 && Math.Abs(col) < 1.0, $"pusat asap harus tetap di dekat asal (baris {row:0.0}, kolom {col:0.0})");
        // Tersebar ke semua arah, bukan satu sisi.
        Assert.True(f.SmokeCells.Any(c => c.Col > 1) && f.SmokeCells.Any(c => c.Col < -1));
        Assert.True(f.SmokeCells.Any(c => c.Row > 1) && f.SmokeCells.Any(c => c.Row < -1));
    }

    [Fact]
    public void StrongerWind_CarriesSmokeFarther()
    {
        var sim = new FireSpreadSimulator();
        int Reach(double speed) => sim.Run(Origin(), Weather(speed: speed), 6).Frames[6].SmokeCells.Max(c => c.Col);

        Assert.True(Reach(12) > Reach(2) + 3, "angin kencang harus membawa asap jauh lebih jauh pada jam yang sama");
    }

    // ------------------------------------------------------------------ model asap terisolasi (sumber api dikendalikan)

    [Fact]
    public void Rain_WashesSmokeOut()
    {
        var fire = SteadyFire(18, (0, 0), (0, 1), (1, 0), (1, 1));
        int Count(double rain) => SmokePlumeModel.Compute(Lat0, fire, windSpeedMs: 5, spreadDirectionDegrees: 90, precipitationMm: rain)[18].Count;

        Assert.True(Count(5) < Count(0) * 0.7, "hujan harus mengurangi asap secara berarti");
    }

    [Fact]
    public void WindBlowingDueEast_GivesASmokeTrailSymmetricNorthSouth()
    {
        // Blok 3x3 sel api (±1,7 km): satu sel tunggal menghasilkan jejak sempit yang cepat encer oleh difusi lateral.
        var block = Enumerable.Range(-1, 3).SelectMany(r => Enumerable.Range(-1, 3).Select(c => (r, c))).ToArray();
        var frames = SmokePlumeModel.Compute(Lat0, SteadyFire(10, block), windSpeedMs: 5, spreadDirectionDegrees: 90, precipitationMm: 0);
        var cells = frames[10];

        Assert.NotEmpty(cells);
        Assert.InRange(cells.Average(c => (double)c.Row), -0.3, 0.3);   // simetris utara-selatan
        Assert.True(cells.Average(c => (double)c.Col) > 3);              // jejak di sisi timur
        Assert.True(cells.Max(c => c.Col) >= 8);
    }

    [Fact]
    public void BiggerFire_MakesDenserSmoke()
    {
        // Satu sel api: asap ringan. Banyak sel api sekaligus (diam di tempat, angin tenang): asap pekat di sekitar sumber.
        var small = SmokePlumeModel.Compute(Lat0, SteadyFire(12, (0, 0)), 0, 0, 0)[12];
        var bigCells = Enumerable.Range(-6, 13).SelectMany(r => Enumerable.Range(-6, 13).Select(c => (r, c))).ToArray();   // 169 sel api
        var big = SmokePlumeModel.Compute(Lat0, SteadyFire(12, bigCells), 0, 0, 0)[12];

        Assert.True(small.Max(c => c.Level) <= 2);
        Assert.Equal(3, big.Max(c => c.Level));
        Assert.True(big.Count(c => c.Level == 3) > small.Count(c => c.Level == 3));
    }

    [Fact]
    public void Smoke_StartsOnlyAfterEmission_NotBefore()
    {
        var frames = SmokePlumeModel.Compute(Lat0, SteadyFire(4, (0, 0)), 5, 90, 0);

        Assert.Empty(frames[0]);
        Assert.NotEmpty(frames[1]);   // sudah ada setelah satu jam emisi
        Assert.Equal(5, frames.Count);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(SmokePlumeModel.LightThreshold - 0.001, 0)]
    [InlineData(SmokePlumeModel.LightThreshold, 1)]
    [InlineData(SmokePlumeModel.ModerateThreshold - 0.001, 1)]
    [InlineData(SmokePlumeModel.ModerateThreshold, 2)]
    [InlineData(SmokePlumeModel.DenseThreshold - 0.001, 2)]
    [InlineData(SmokePlumeModel.DenseThreshold, 3)]
    [InlineData(1000.0, 3)]
    public void LevelOf_FollowsTheThresholds(double density, int expected) =>
        Assert.Equal(expected, SmokePlumeModel.LevelOf(density));

    [Fact]
    public void Thresholds_AreOrdered() =>
        Assert.True(SmokePlumeModel.LightThreshold < SmokePlumeModel.ModerateThreshold && SmokePlumeModel.ModerateThreshold < SmokePlumeModel.DenseThreshold);

    // ------------------------------------------------------------------ jaminan bentuk dan batas

    [Fact]
    public void Smoke_IsDeterministic()
    {
        var sim = new FireSpreadSimulator();
        var a = sim.Run(Origin(), Weather(), 12);
        var b = sim.Run(Origin(), Weather(), 12);

        for (int h = 0; h <= 12; h++) Assert.Equal(a.Frames[h].SmokeCells, b.Frames[h].SmokeCells);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    [InlineData(25)]   // angin sangat kencang: asap keluar dari domain tanpa galat
    public void Smoke_StaysInsideTheDomain_WithValidLevels_AndUnderTheCap(double speed)
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(speed: speed, humidity: 25), 24);

        Assert.Equal(25, r.Frames.Count);
        foreach (var f in r.Frames)
        {
            Assert.True(f.SmokeCells.Count <= SmokePlumeModel.MaxCellsPerFrame);
            Assert.All(f.SmokeCells, c =>
            {
                Assert.InRange(c.Level, 1, 3);
                Assert.InRange(c.Row, -SmokePlumeModel.HalfSizeCells, SmokePlumeModel.HalfSizeCells);
                Assert.InRange(c.Col, -SmokePlumeModel.HalfSizeCells, SmokePlumeModel.HalfSizeCells);
            });
            Assert.Equal(f.SmokeCells.Count, f.SmokeCells.Select(c => (c.Row, c.Col)).Distinct().Count());   // tidak ada sel ganda
        }
    }

    [Fact]
    public void CellArea_ShrinksWithLatitude()
    {
        var equator = SmokePlumeModel.CellAreaKm2(0);
        Assert.InRange(equator, 7.5, 8.0);   // 2,78 km × 2,78 km
        Assert.True(SmokePlumeModel.CellAreaKm2(-8) < equator);
    }

    [Fact]
    public void FireModel_IsUnchangedBySmoke()
    {
        // Menambah asap tidak boleh mengubah hasil api (sel dan luas tetap sama untuk masukan yang sama).
        var sim = new FireSpreadSimulator();
        var a = sim.Run(Origin(), Weather(), 12);
        var b = sim.Run(Origin(), Weather(), 12);
        Assert.Equal(a.Frames.Select(f => f.AreaHa), b.Frames.Select(f => f.AreaHa));
        Assert.Equal(a.Frames[^1].BurningCells, b.Frames[^1].BurningCells);
    }
}
