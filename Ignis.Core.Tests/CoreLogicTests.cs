using Ignis.Core.Abstractions;
using Ignis.Core.Common;
using Ignis.Core.Contracts;
using Ignis.Core.Entities;
using Ignis.Core.Enums;
using Ignis.Core.InMemory;
using Ignis.Core.Services;
using Ignis.Core.ValueObjects;
using Xunit;

namespace Ignis.Core.Tests;

// ====================== Konsep OOP pada fitur manajemen hotspot ======================

public class HotspotOopTests
{
    private static readonly DateTime T = new(2026, 10, 24, 9, 15, 0);

    private static ManualHotspot Manual() => new(-2.9, 104.6, 80, T, "Petugas A", "asap tebal");
    private static SatelliteHotspot Satellite() => new(-0.39, 102.06, 87, 342.5, T, "Suomi NPP", "VIIRS");

    // ---- INHERITANCE ----
    [Fact]
    public void Subclasses_AreHotspots()
    {
        Assert.IsAssignableFrom<Hotspot>(Manual());
        Assert.IsAssignableFrom<Hotspot>(Satellite());
        Assert.Equal(HotspotSource.Manual, Manual().Source);
        Assert.Equal(HotspotSource.Satellite, Satellite().Source);
    }

    [Fact]
    public void Users_InheritFromUser()
    {
        Assert.IsAssignableFrom<User>(new Admin("A", "Admin", "a@x.id", ""));
        Assert.IsAssignableFrom<User>(new RegisteredUser("R", "Reg", "r@x.id", ""));
    }

    // ---- POLYMORPHISM ----
    [Fact]
    public void SameCall_DifferentBehavior_PerSubclass()
    {
        Hotspot[] all = { Manual(), Satellite() };   // dipanggil lewat tipe dasar

        Assert.Equal(new[] { "Manual (Petugas A)", "Satelit (Suomi NPP · VIIRS)" }, all.Select(h => h.GetDetails()));
        Assert.Equal(new[] { true, false }, all.Select(h => h.IsEditable));
    }

    [Fact]
    public void Users_HaveDifferentPermissions()
    {
        User[] users = { new Admin("A", "Admin", "a@x.id", ""), new RegisteredUser("R", "Reg", "r@x.id", "") };
        Assert.Equal(new[] { true, false }, users.Select(u => u.CanManageHotspots));
    }

    // ---- ENCAPSULATION ----
    [Theory]
    [InlineData(91, 100, 50)]
    [InlineData(-91, 100, 50)]
    [InlineData(0, 181, 50)]
    [InlineData(0, 100, 101)]
    [InlineData(0, 100, -1)]
    public void Constructor_RejectsInvalidInput(double lat, double lon, double conf) =>
        Assert.Throws<DomainException>(() => new ManualHotspot(lat, lon, conf, T, "x"));

    [Fact]
    public void Constructor_RequiresReporter() =>
        Assert.Throws<DomainException>(() => new ManualHotspot(0, 100, 50, T, "  "));

    [Fact]
    public void Id_CanOnlyBeAssignedOnce()
    {
        var h = Manual();
        h.AssignId("HS-0001");
        Assert.Throws<DomainException>(() => h.AssignId("HS-0002"));
        Assert.Equal("HS-0001", h.Id);
    }

    [Fact]
    public void Update_IsAtomic_InvalidInputChangesNothing()
    {
        var h = Manual();
        Assert.Throws<DomainException>(() => h.UpdateReport(-1, 999, 50, T, "Baru", null, HotspotStatus.Resolved));

        Assert.Equal(-2.9, h.Latitude);
        Assert.Equal("Petugas A", h.ReportedBy);
        Assert.Equal(HotspotStatus.Active, h.Status);
    }

    [Fact]
    public void Update_AppliesValidInput()
    {
        var h = Manual();
        h.UpdateReport(1.5, 101.5, 60, T.AddHours(1), " Relawan B ", " catatan ", HotspotStatus.Monitoring);

        Assert.Equal(1.5, h.Latitude);
        Assert.Equal("Relawan B", h.ReportedBy);
        Assert.Equal("catatan", h.Note);
        Assert.Equal(T.AddHours(1), h.DetectedAt);
        Assert.Equal(HotspotStatus.Monitoring, h.Status);
    }

    [Fact]
    public void Inactive_OnlyChangesThroughMethods()
    {
        var u = new RegisteredUser("R", "Reg", "r@x.id", "");
        Assert.True(u.IsActive);
        u.Deactivate();
        Assert.False(u.IsActive);
        u.Activate();
        Assert.True(u.IsActive);
    }

    [Fact]
    public void User_RequiresValidEmail() =>
        Assert.Throws<DomainException>(() => new RegisteredUser("R", "Reg", "bukan-email", ""));
}

// ====================== Use case ======================

public class HotspotServiceTests
{
    private static HotspotService Make(out InMemoryHotspotRepository repo)
    {
        repo = new InMemoryHotspotRepository(DemoData.Hotspots());
        return new HotspotService(repo, new InMemoryActivityLog());
    }

    private static ManualHotspotRequest Req(string by = "Tes") =>
        new(-2.9, 104.6, 80, new DateTime(2026, 10, 25, 8, 0, 0), by, "n", HotspotStatus.Active);

    [Fact]
    public async Task AddManual_AssignsNextId_AndIsManual()
    {
        var svc = Make(out _);
        var saved = await svc.AddManualAsync(Req());

        Assert.Equal("HS-0143", saved.Id);
        Assert.Equal(HotspotSource.Manual, saved.Source);
    }

    [Fact]
    public async Task AddManual_InvalidInput_ThrowsDomainException()
    {
        var svc = Make(out var repo);
        var before = (await repo.GetAllAsync()).Count;

        await Assert.ThrowsAsync<DomainException>(() => svc.AddManualAsync(Req() with { Latitude = 200 }));
        Assert.Equal(before, (await repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Update_SatelliteHotspot_IsRejected()
    {
        var svc = Make(out _);
        await Assert.ThrowsAsync<DomainException>(() => svc.UpdateManualAsync("HS-0142", Req()));
    }

    [Fact]
    public async Task Delete_UsesPolymorphicIsEditable()
    {
        var svc = Make(out var repo);

        await Assert.ThrowsAsync<DomainException>(() => svc.DeleteAsync("HS-0142"));   // satelit
        Assert.True(await svc.DeleteAsync("HS-0138"));                                  // manual
        Assert.Null(await repo.GetByIdAsync("HS-0138"));
        Assert.False(await svc.DeleteAsync("HS-9999"));                                 // tidak ada
    }

    [Fact]
    public async Task Import_SkipsDuplicates()
    {
        var repo = new InMemoryHotspotRepository();
        var svc = new HotspotService(repo, new InMemoryActivityLog());
        SatelliteHotspot Make() => new(1, 2, 70, 300, new DateTime(2026, 1, 1, 1, 0, 0), "N", "VIIRS");

        Assert.Equal(1, await svc.ImportSatelliteAsync(new[] { Make() }));
        Assert.Equal(0, await svc.ImportSatelliteAsync(new[] { Make() }));
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public void Dto_ExposesPolymorphicResults()
    {
        var dto = DemoData.Hotspots().First(h => h.Id == "HS-0138").ToDto();
        Assert.Equal("Manual (Petugas Lapangan Banyuasin)", dto.Details);
        Assert.True(dto.IsEditable);
        Assert.Null(dto.SatelliteName);
    }
}

// ====================== Parser, prediksi, cuaca ======================

public class FirmsCsvParserTests
{
    private const string Header = "latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight";

    [Fact]
    public void Parse_ReadsRow()
    {
        var csv = Header + "\n-0.39,102.06,342.5,0.4,0.4,2026-10-24,0915,N,VIIRS,87,2.0NRT,290,5,D\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));

        Assert.Equal(-0.39, h.Latitude, 5);
        Assert.Equal(new DateTime(2026, 10, 24, 9, 15, 0), h.ScanTime);
        Assert.Equal(new DateTime(2026, 10, 24, 9, 15, 0), h.DetectedAt);
        Assert.Equal(342.5, h.Brightness, 3);
        Assert.Equal("N", h.SatelliteName);
    }

    [Fact]
    public void Parse_PadsShortTime_UsesDefaultConfidence_SkipsBadRows()
    {
        var csv = Header + "\n1,2,300,0,0,2026-10-24,5,N,VIIRS,x,v,0,0,D\nbad,row\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));

        Assert.Equal(new DateTime(2026, 10, 24, 0, 5, 0), h.ScanTime);
        Assert.Equal(70, h.Confidence);
    }

    [Fact]
    public void Parse_SkipsRowsViolatingDomainRules()
    {
        var csv = Header + "\n95,2,300,0,0,2026-10-24,0900,N,VIIRS,80,v,0,0,D\n";   // latitude 95 tidak valid
        Assert.Empty(FirmsCsvParser.Parse(csv));
    }

    [Fact]
    public void Parse_MissingRequiredColumns_ReturnsEmpty() =>
        Assert.Empty(FirmsCsvParser.Parse("foo,bar\n1,2\n"));
}

public class FwiPredictionModelTests
{
    private static Prediction Run(double temp, double hum, double wind, double dir, double rain)
    {
        var h = new ManualHotspot(0, 100, 50, DateTime.Now, "x");
        h.AssignId("HS-0001");
        var w = new WeatherData { Id = "WX-0001", Temperature = temp, Humidity = hum, WindSpeed = wind, WindDirection = dir, Precipitation = rain };
        return new FwiPredictionModel().Predict(h, w);
    }

    [Fact]
    public void Direction_IsOppositeOfWind_AndLinksWeather()
    {
        var p = Run(30, 60, 5, 90, 0);
        Assert.Equal(270, p.Direction);
        Assert.Equal("WX-0001", p.WeatherId);
        Assert.Equal("HS-0001", p.HotspotId);
    }

    [Fact]
    public void CalmWetWeather_UsesMinimumSpeed()
    {
        var p = Run(25, 95, 0.5, 0, 4);
        Assert.Equal(0.05, p.SpeedKmH);
        Assert.Equal(12, p.Hours);
        Assert.Equal(22.6, p.AreaHa);
        Assert.Equal(RiskLevel.Medium, p.Risk);
    }

    [Fact]
    public void HotDryWindyWeather_IsCritical()
    {
        var p = Run(40, 30, 9, 0, 0);
        Assert.Equal(RiskLevel.Critical, p.Risk);
        Assert.Equal(3, p.Hours);
    }

    [Fact]
    public void Speed_IsClamped() => Assert.InRange(Run(60, 0, 50, 0, 0).SpeedKmH, 0.05, 3.5);
}

public class PredictionServiceTests
{
    [Fact]
    public async Task Predict_StoresWeatherForHotspot_AndLinksIt()
    {
        var hotspots = new InMemoryHotspotRepository(DemoData.Hotspots());
        var weatherRepo = new InMemoryWeatherRepository();
        var svc = new PredictionService(new InMemoryPredictionRepository(), weatherRepo, new SimulatedWeatherProvider(),
                                        new FwiPredictionModel(), new InMemoryActivityLog());
        var h = (await hotspots.GetByIdAsync("HS-0142"))!;

        var p = await svc.PredictAsync(h);

        var w = await weatherRepo.GetLatestForHotspotAsync("HS-0142");
        Assert.NotNull(w);
        Assert.Equal("HS-0142", w!.HotspotId);
        Assert.Equal(w.Id, p.WeatherId);
        Assert.Equal("PR-0001", p.Id);
    }

    [Fact]
    public async Task GetWeather_FetchesOnce_ThenReusesStored()
    {
        var weatherRepo = new InMemoryWeatherRepository();
        var svc = new PredictionService(new InMemoryPredictionRepository(), weatherRepo, new SimulatedWeatherProvider(),
                                        new FwiPredictionModel(), new InMemoryActivityLog());
        var h = DemoData.Hotspots().First();

        var a = await svc.GetWeatherAsync(h);
        var b = await svc.GetWeatherAsync(h);
        Assert.Equal(a.Id, b.Id);
    }
}

public class SimulatedWeatherTests
{
    [Fact]
    public async Task SameCoordinate_GivesSameWeather()
    {
        var p = new SimulatedWeatherProvider();
        var a = await p.GetWeatherAsync(-2.9, 104.7);
        var b = await p.GetWeatherAsync(-2.9, 104.7);
        Assert.Equal(a.Temperature, b.Temperature);
        Assert.Equal(a.WindDirection, b.WindDirection);
    }
}

// ====================== Pengguna & filter ======================

public class UserServiceTests
{
    private static (UserService svc, InMemoryUserRepository repo) Make(params User[] users)
    {
        var repo = new InMemoryUserRepository(users);
        return (new UserService(repo, new InMemoryActivityLog()), repo);
    }

    [Fact]
    public async Task CannotDeactivateLastActiveAdmin()
    {
        var (svc, repo) = Make(new Admin("A", "A", "a@x.id", ""), new RegisteredUser("U", "U", "u@x.id", ""));

        Assert.NotNull(await svc.ToggleActiveAsync("A"));
        Assert.True((await repo.GetByIdAsync("A"))!.IsActive);
    }

    [Fact]
    public async Task CanDeactivateAdminWhenAnotherActiveAdminExists()
    {
        var (svc, repo) = Make(new Admin("A", "A", "a@x.id", ""), new Admin("B", "B", "b@x.id", ""));

        Assert.Null(await svc.ToggleActiveAsync("A"));
        Assert.False((await repo.GetByIdAsync("A"))!.IsActive);
    }

    [Fact]
    public async Task UnknownUser_ReturnsError()
    {
        var (svc, _) = Make();
        Assert.NotNull(await svc.ToggleActiveAsync("nope"));
    }
}

public class RegionAndFilterTests
{
    [Fact]
    public void Region_Contains()
    {
        var riau = Region.Defaults.First(r => r.Name == "Riau");
        Assert.True(riau.Contains(0.5, 101.4));
        Assert.False(riau.Contains(-7.8, 110.4)); // Yogyakarta
    }

    [Fact]
    public void Filter_BySourceAndStatus()
    {
        var manualResolved = HotspotFilter.Apply(DemoData.Hotspots(), null, HotspotSource.Manual, HotspotStatus.Resolved).ToList();
        Assert.Equal("HS-0129", Assert.Single(manualResolved).Id);
    }

    [Fact]
    public void MonitoringArea_Contains()
    {
        var a = new MonitoringArea { MinLatitude = -1, MaxLatitude = 1, MinLongitude = 100, MaxLongitude = 102 };
        Assert.True(a.Contains(0, 101));
        Assert.False(a.Contains(2, 101));
    }
}
