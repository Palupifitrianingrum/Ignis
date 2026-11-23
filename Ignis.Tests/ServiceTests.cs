using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Endpoints;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Xunit;

namespace Ignis.Tests;

// ====================== Use case di atas database (EF InMemory) ======================

public class HotspotServiceTests
{
    private static HotspotService Make(AppDbContext db) => new(db, new ActivityLogService(db));

    private static ManualHotspotRequest Req(string by = "Tes") =>
        new(-2.9, 104.6, 80, new DateTime(2026, 10, 25, 8, 0, 0), by, "n", HotspotStatus.Active);

    [Fact]
    public async Task AddManual_AssignsGeneratedId_AndIsManual()
    {
        using var db = TestDb.Create();
        var saved = await Make(db).AddManualAsync(Req());

        Assert.Matches(@"^HS-\d{4,}$", saved.Id);
        Assert.True(int.Parse(saved.Id[3..]) >= AppDbContext.FirstGeneratedNumber);
        Assert.Equal(HotspotSource.Manual, saved.Source);
        Assert.Equal(saved.Id, (await Make(db).GetByIdAsync(saved.Id))!.Id);
    }

    [Fact]
    public async Task GeneratedIds_AreUnique_AndDoNotCollideWithDemoData()
    {
        using var db = TestDb.Create();
        var ids = new List<string>();
        for (int i = 0; i < 5; i++) ids.Add((await Make(db).AddManualAsync(Req())).Id);

        Assert.Equal(5, ids.Distinct().Count());
        Assert.DoesNotContain(ids, id => DemoData.Hotspots().Any(h => h.Id == id));
    }

    [Fact]
    public async Task AddManual_InvalidInput_ThrowsDomainException_AndSavesNothing()
    {
        using var db = TestDb.Create();
        var svc = Make(db);
        var before = (await svc.GetAllAsync()).Count;

        await Assert.ThrowsAsync<DomainException>(() => svc.AddManualAsync(Req() with { Latitude = 200 }));
        Assert.Equal(before, (await svc.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Update_SatelliteHotspot_IsRejected()
    {
        using var db = TestDb.Create();
        await Assert.ThrowsAsync<DomainException>(() => Make(db).UpdateManualAsync("HS-0142", Req()));
    }

    [Fact]
    public async Task Delete_UsesPolymorphicIsEditable()
    {
        using var db = TestDb.Create();
        var svc = Make(db);

        await Assert.ThrowsAsync<DomainException>(() => svc.DeleteAsync("HS-0142"));   // satelit
        Assert.True(await svc.DeleteAsync("HS-0138"));                                  // manual
        Assert.Null(await svc.GetByIdAsync("HS-0138"));
        Assert.False(await svc.DeleteAsync("HS-9999"));                                 // tidak ada
    }

    [Fact]
    public async Task Import_SkipsDuplicates()
    {
        using var db = TestDb.Create(seedDemo: false);
        var svc = Make(db);
        SatelliteHotspot New() => new(1, 2, 70, 300, new DateTime(2026, 1, 1, 1, 0, 0), "N", "VIIRS");

        Assert.Equal(1, await svc.ImportSatelliteAsync(new[] { New() }));
        Assert.Equal(0, await svc.ImportSatelliteAsync(new[] { New() }));
        Assert.Single(await svc.GetAllAsync());
    }

    [Fact]
    public async Task GetAll_FiltersBySourceAndStatus()
    {
        using var db = TestDb.Create();
        var manualResolved = await Make(db).GetAllAsync(HotspotSource.Manual, HotspotStatus.Resolved);
        Assert.Equal("HS-0129", Assert.Single(manualResolved).Id);
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

public class PredictionServiceTests
{
    private static PredictionService Make(AppDbContext db) =>
        new(db, new WeatherClient(new HttpClient(), new IntegrationOptions()), new FwiPredictionModel(), new ActivityLogService(db));

    [Fact]
    public async Task Predict_StoresWeatherForHotspot_AndLinksIt()
    {
        using var db = TestDb.Create();
        var h = (await new HotspotService(db, new ActivityLogService(db)).GetByIdAsync("HS-0142"))!;

        var p = await Make(db).PredictAsync(h);

        var w = Assert.Single(db.WeatherData.Where(x => x.HotspotId == "HS-0142"));
        Assert.Equal(w.Id, p.WeatherId);
        Assert.Matches(@"^PR-\d{4,}$", p.Id);
    }

    [Fact]
    public async Task GetWeather_FetchesOnce_ThenReusesStored()
    {
        using var db = TestDb.Create();
        var svc = Make(db);
        var h = DemoData.Hotspots().First();

        var a = await svc.GetWeatherAsync(h);
        var b = await svc.GetWeatherAsync(h);
        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public async Task GetOrPredict_ReusesLatestPrediction()
    {
        using var db = TestDb.Create();
        var h = DemoData.Hotspots().First(x => x.Id == "HS-0142");   // sudah punya PR-0053 di data demo

        var p = await Make(db).GetOrPredictAsync(h);
        Assert.Equal("PR-0053", p.Id);
    }
}

public class UserServiceTests
{
    private static (UserService svc, AppDbContext db) Make(params User[] users)
    {
        var db = TestDb.Create(seedDemo: false);
        db.Users.AddRange(users);
        db.SaveChanges();
        return (new UserService(db, new ActivityLogService(db)), db);
    }

    [Fact]
    public async Task CannotDeactivateLastActiveAdmin()
    {
        var (svc, db) = Make(new Admin("A", "A", "a@x.id", ""), new RegisteredUser("U", "U", "u@x.id", ""));

        Assert.NotNull(await svc.ToggleActiveAsync("A"));
        Assert.True(db.Users.Single(u => u.Id == "A").IsActive);
    }

    [Fact]
    public async Task CanDeactivateAdminWhenAnotherActiveAdminExists()
    {
        var (svc, db) = Make(new Admin("A", "A", "a@x.id", ""), new Admin("B", "B", "b@x.id", ""));

        Assert.Null(await svc.ToggleActiveAsync("A"));
        Assert.False(db.Users.Single(u => u.Id == "A").IsActive);
    }

    [Fact]
    public async Task UnknownUser_ReturnsError()
    {
        var (svc, _) = Make();
        Assert.NotNull(await svc.ToggleActiveAsync("nope"));
    }
}
