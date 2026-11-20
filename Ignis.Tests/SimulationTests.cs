using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Services;
using Ignis.Shared;
using Xunit;

namespace Ignis.Tests;

/// <summary>Perilaku simulator penyebaran api (tanpa HTTP/database).</summary>
public class FireSpreadBehaviorTests
{
    private const double Lat0 = 0.32, Lon0 = 101.85;

    private static ManualHotspot Origin(double lat = Lat0, double lon = Lon0)
    {
        var h = new ManualHotspot(lat, lon, 85, DateTime.Now, "Petugas");
        h.AssignId("HS-0001");
        return h;
    }

    private static WeatherData Weather(double windFrom = 270, double speed = 10, double temp = 34, double humidity = 40, double rain = 0) =>
        new() { Id = "WX-0001", Temperature = temp, Humidity = humidity, WindSpeed = speed, WindDirection = windFrom, Precipitation = rain };

    private static (double dLat, double dLon) CentroidShift(SimulationFrame f, double lat0 = Lat0, double lon0 = Lon0) =>
        (f.BurningCells.Average(c => c.Latitude) - lat0, f.BurningCells.Average(c => c.Longitude) - lon0);

    // Angin DARI barat mendorong api KE timur (konvensi meteorologi: WindDirection = arah asal).
    [Theory]
    [InlineData(270, "timur")]
    [InlineData(90, "barat")]
    [InlineData(0, "selatan")]
    [InlineData(180, "utara")]
    public void Fire_SpreadsAwayFromWhereWindComesFrom(double windFrom, string expected)
    {
        var last = new FireSpreadSimulator().Simulate(Origin(), Weather(windFrom), 18).Last();
        var (dLat, dLon) = CentroidShift(last);

        var actual = Math.Abs(dLon) > Math.Abs(dLat)
            ? (dLon > 0 ? "timur" : "barat")
            : (dLat > 0 ? "utara" : "selatan");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(270, 90)]
    [InlineData(90, 270)]
    [InlineData(0, 180)]
    [InlineData(180, 0)]
    [InlineData(350, 170)]
    public void Result_ReportsSpreadDirection_AsOppositeOfWindOrigin(double windFrom, double expectedSpread)
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(windFrom), 4);
        Assert.Equal(expectedSpread, r.SpreadDirection, 1);
        Assert.Equal(windFrom, r.WindFromDirection, 1);
    }

    [Fact]
    public void CalmWind_SpreadsEvenly_StrongWindDrifts()
    {
        var sim = new FireSpreadSimulator();
        var calm = CentroidShift(sim.Simulate(Origin(), Weather(speed: 0), 18).Last());
        var windy = CentroidShift(sim.Simulate(Origin(), Weather(speed: 12), 18).Last());

        // Angin tenang: pusat massa tetap di sekitar titik asal (kurang dari 2 sel). Angin kencang: bergeser jauh ke timur.
        Assert.True(Math.Abs(calm.dLon) < 2 * FireSpreadSimulator.CellStepDegrees && Math.Abs(calm.dLat) < 2 * FireSpreadSimulator.CellStepDegrees);
        Assert.True(windy.dLon > 4 * FireSpreadSimulator.CellStepDegrees);
        Assert.True(windy.dLon > Math.Abs(calm.dLon) * 3);
    }

    [Fact]
    public void HeavyRain_AndHumidity_SlowTheSpread()
    {
        var sim = new FireSpreadSimulator();
        var dry = sim.Run(Origin(), Weather(humidity: 35, rain: 0), 12).Frames[^1].AreaHa;
        var wet = sim.Run(Origin(), Weather(humidity: 90, rain: 6), 12).Frames[^1].AreaHa;
        Assert.True(wet < dry / 2, $"hujan lebat seharusnya jauh lebih lambat (kering {dry} ha, basah {wet} ha)");
    }

    [Fact]
    public void Frames_AreCumulative_FirstIsOriginOnly_AndCountsAreConsistent()
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(), 10);

        Assert.Equal(11, r.Frames.Count);
        Assert.Equal(Enumerable.Range(0, 11), r.Frames.Select(f => f.HourOffset));

        var first = Assert.Single(r.Frames[0].BurningCells);
        Assert.Equal(Lat0, first.Latitude, 6);
        Assert.Equal(Lon0, first.Longitude, 6);
        Assert.Equal(1, r.Frames[0].NewCells);

        for (int i = 1; i < r.Frames.Count; i++)
        {
            var prev = r.Frames[i - 1].BurningCells.ToHashSet();
            var cur = r.Frames[i].BurningCells.ToHashSet();
            Assert.True(prev.IsSubsetOf(cur), $"jam {i}: sel yang sudah terbakar tidak boleh padam");
            Assert.Equal(cur.Count - prev.Count, r.Frames[i].NewCells);
        }

        foreach (var f in r.Frames)
            Assert.Equal(Math.Round(f.BurningCells.Count * r.CellAreaHa, 2), f.AreaHa, 2);
    }

    [Fact]
    public void Cells_AlignToGrid_AroundOrigin()
    {
        var last = new FireSpreadSimulator().Simulate(Origin(), Weather(), 12).Last();
        foreach (var c in last.BurningCells)
        {
            Assert.Equal(0, Math.Abs(Math.IEEERemainder(c.Latitude - Lat0, FireSpreadSimulator.CellStepDegrees)), 5);
            Assert.Equal(0, Math.Abs(Math.IEEERemainder(c.Longitude - Lon0, FireSpreadSimulator.CellStepDegrees)), 5);
        }
    }

    [Theory]
    [InlineData(0, 1)]       // di bawah batas -> 1 jam
    [InlineData(-5, 1)]
    [InlineData(6, 6)]
    [InlineData(24, 24)]
    [InlineData(999, 24)]    // di atas batas -> 24 jam
    public void Hours_AreClamped(int requested, int expected)
    {
        var r = new FireSpreadSimulator().Run(Origin(), Weather(), requested);
        Assert.Equal(expected, r.Hours);
        Assert.Equal(expected + 1, r.Frames.Count);
    }

    [Fact]
    public void CellCount_NeverExceedsMax_AndTruncationIsReported()
    {
        // Kondisi paling ekstrem selama 24 jam harus mentok di batas dan melaporkannya.
        var r = new FireSpreadSimulator().Run(Origin(), Weather(speed: 15, temp: 40, humidity: 20), 24);
        Assert.All(r.Frames, f => Assert.True(f.BurningCells.Count <= FireSpreadSimulator.MaxCells));
        Assert.True(r.Truncated);
        Assert.Equal(FireSpreadSimulator.MaxCells, r.Frames[^1].BurningCells.Count);

        // Kondisi ringan tidak mentok.
        var mild = new FireSpreadSimulator().Run(Origin(), Weather(speed: 2, humidity: 85, rain: 5), 6);
        Assert.False(mild.Truncated);
    }

    [Fact]
    public void SameInput_GivesIdenticalOutput_AndDifferentLocationGivesDifferentShape()
    {
        var sim = new FireSpreadSimulator();
        var a = sim.Run(Origin(), Weather(), 12);
        var b = sim.Run(Origin(), Weather(), 12);
        Assert.Equal(a.Frames.Select(f => f.AreaHa), b.Frames.Select(f => f.AreaHa));
        Assert.Equal(a.Frames[^1].BurningCells, b.Frames[^1].BurningCells);

        var other = sim.Run(Origin(1.5, 103.0), Weather(), 12);
        Assert.NotEqual(a.Frames[^1].AreaHa, other.Frames[^1].AreaHa);
    }

    [Fact]
    public void CellArea_ShrinksWithLatitude()
    {
        var sim = new FireSpreadSimulator();
        var equator = sim.Run(Origin(0, 100), Weather(), 1).CellAreaHa;
        var south = sim.Run(Origin(-8, 100), Weather(), 1).CellAreaHa;   // Jawa/Nusa Tenggara
        Assert.InRange(equator, 30.5, 31.5);
        Assert.True(south < equator);
        Assert.InRange(south / equator, 0.98, 0.995);   // cos(8°) ≈ 0,990
    }
}

/// <summary>Endpoint /api/hotspots/{id}/simulation lewat pipeline HTTP sungguhan (JWT, database InMemory).</summary>
public class SimulationApiTests : IClassFixture<AuthApiTests.Factory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly AuthApiTests.Factory _factory;
    public SimulationApiTests(AuthApiTests.Factory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoData.Password));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private const string RegularUser = "andi.pratama@example.com";
    private const string AdminUser = "admin@ignis.example";

    [Fact]
    public async Task WithoutToken_Is401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/hotspots/HS-0142/simulation");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownHotspot_Is404()
    {
        var client = await LoginAsync(RegularUser);
        var resp = await client.GetAsync("/api/hotspots/HS-TIDAK-ADA/simulation");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RegularUser_CanRead_AndPayloadIsComplete()
    {
        var client = await LoginAsync(RegularUser);
        var resp = await client.GetAsync("/api/hotspots/HS-0142/simulation?hours=6");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var sim = (await resp.Content.ReadFromJsonAsync<SimulationDto>(Json))!;
        Assert.Equal("HS-0142", sim.HotspotId);
        Assert.Equal(6, sim.Hours);
        Assert.Equal(7, sim.Frames.Count);
        Assert.Single(sim.Frames[0].BurningCells);
        Assert.InRange(sim.SpreadDirection, 0, 360);
        Assert.InRange(sim.WindFromDirection, 0, 360);
        Assert.Equal((sim.WindFromDirection + 180) % 360, sim.SpreadDirection, 1);
        Assert.True(sim.CellSizeDegrees > 0 && sim.CellAreaHa > 0 && sim.MaxCells > 0);
        Assert.False(string.IsNullOrEmpty(sim.Condition) && sim.Temperature == 0 && sim.Humidity == 0);
    }

    [Theory]
    [InlineData("", 6)]              // default
    [InlineData("?hours=1", 1)]
    [InlineData("?hours=0", 1)]      // dibatasi ke bawah
    [InlineData("?hours=999", 24)]   // dibatasi ke atas
    public async Task Hours_AreClamped(string query, int expectedHours)
    {
        var client = await LoginAsync(AdminUser);
        var sim = (await client.GetFromJsonAsync<SimulationDto>($"/api/hotspots/HS-0142/simulation{query}", Json))!;
        Assert.Equal(expectedHours, sim.Hours);
        Assert.Equal(expectedHours + 1, sim.Frames.Count);
    }

    [Fact]
    public async Task RepeatedCalls_AreIdentical()
    {
        var client = await LoginAsync(RegularUser);
        var a = (await client.GetFromJsonAsync<SimulationDto>("/api/hotspots/HS-0142/simulation?hours=12", Json))!;
        var b = (await client.GetFromJsonAsync<SimulationDto>("/api/hotspots/HS-0142/simulation?hours=12", Json))!;

        Assert.Equal(a.WindFromDirection, b.WindFromDirection);   // cuaca tersimpan dipakai ulang, tidak diundi ulang
        Assert.Equal(a.Frames.Select(f => f.AreaHa), b.Frames.Select(f => f.AreaHa));
    }
}
