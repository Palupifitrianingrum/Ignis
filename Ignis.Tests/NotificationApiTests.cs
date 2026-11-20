using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ignis.Tests;

/// <summary>Lokasi tersimpan, notifikasi, dan dampak simulasi lewat pipeline HTTP sungguhan (JWT, otorisasi, database InMemory).</summary>
public class NotificationApiTests : IClassFixture<NotificationApiTests.Factory>
{
    private const string Andi = "andi.pratama@example.com", Budi = "budi.santoso@example.com", Admin = "admin@ignis.example";
    private const double Lat = -4.0, Lon = 138.0;   // Papua: jauh dari hotspot demo

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Storage:InMemoryName", "notif-api-" + Guid.NewGuid());
            builder.UseSetting("Jwt:Key", "kunci-rahasia-untuk-tes-minimal-32-karakter!");
            builder.ConfigureServices(services =>
            {
                // Hermetik: tanpa key dari .env dan tanpa internet (cuaca = data simulasi deterministik per koordinat).
                services.RemoveAll<IntegrationOptions>();
                services.AddSingleton(new IntegrationOptions());
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(new FakeHandler()));
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            });
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly Factory _factory;
    public NotificationApiTests(Factory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoData.Password));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    /// <summary>Kelas ini berbagi satu server: bersihkan lokasi dan notifikasi dua pengguna uji serta hotspot manual sebelum tiap tes.</summary>
    private async Task ResetAsync()
    {
        foreach (var email in new[] { Andi, Budi })
        {
            var c = await LoginAsync(email);
            foreach (var a in (await c.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!)
                await c.DeleteAsync($"/api/monitoring-areas/{a.Id}");
            await c.PostAsync("/api/notifications/read-all", null);
            foreach (var n in (await c.GetFromJsonAsync<NotificationListDto>("/api/notifications?limit=200", Json))!.Items)
                await c.DeleteAsync($"/api/notifications/{n.Id}");
        }

        var admin = await LoginAsync(Admin);
        foreach (var h in (await admin.GetFromJsonAsync<List<HotspotDto>>("/api/hotspots", Json))!.Where(h => h.Latitude is > -5 and < -3 && h.Longitude is > 137 and < 139))
            await admin.DeleteAsync($"/api/hotspots/{h.Id}");
    }

    private static Task<HttpResponseMessage> CreateArea(HttpClient c, string name = "Rumah", double lat = Lat, double lon = Lon + 0.03, double radius = 10) =>
        c.PostAsJsonAsync("/api/monitoring-areas", new MonitoringAreaRequest(name, lat, lon, radius));

    private static async Task<HotspotDto> AddHotspot(HttpClient admin, double lat = Lat, double lon = Lon)
    {
        var resp = await admin.PostAsJsonAsync("/api/hotspots/manual", new ManualHotspotRequest(lat, lon, 85, DateTime.Now, "Petugas", null, HotspotStatus.Active));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<HotspotDto>(Json))!;
    }

    private static Task<NotificationListDto> Notifications(HttpClient c) =>
        c.GetFromJsonAsync<NotificationListDto>("/api/notifications", Json)!;

    // ------------------------------------------------------------------ otorisasi

    [Theory]
    [InlineData("GET", "/api/monitoring-areas")]
    [InlineData("POST", "/api/monitoring-areas")]
    [InlineData("PUT", "/api/monitoring-areas/AR-1")]
    [InlineData("DELETE", "/api/monitoring-areas/AR-1")]
    [InlineData("GET", "/api/notifications")]
    [InlineData("POST", "/api/notifications/NT-1/read")]
    [InlineData("POST", "/api/notifications/read-all")]
    [InlineData("DELETE", "/api/notifications/NT-1")]
    [InlineData("POST", "/api/notifications/check")]
    [InlineData("POST", "/api/notifications/evaluate")]
    public async Task WithoutToken_Is401(string method, string url)
    {
        var resp = await _factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotUseTheUserOnlyEndpoints_ButCanTriggerEvaluation()
    {
        var admin = await LoginAsync(Admin);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/monitoring-areas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateArea(admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/notifications/check", null)).StatusCode);

        var evaluate = await admin.PostAsync("/api/notifications/evaluate", null);
        Assert.Equal(HttpStatusCode.OK, evaluate.StatusCode);
        Assert.NotNull(await evaluate.Content.ReadFromJsonAsync<CheckResultDto>(Json));
    }

    [Fact]
    public async Task RegisteredUser_CannotTriggerEvaluationForEveryone()
    {
        var andi = await LoginAsync(Andi);
        Assert.Equal(HttpStatusCode.Forbidden, (await andi.PostAsync("/api/notifications/evaluate", null)).StatusCode);
    }

    // ------------------------------------------------------------------ lokasi tersimpan

    [Fact]
    public async Task CreateArea_ReturnsTheSavedLocation()
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);

        var resp = await CreateArea(andi, "  Rumah Orang Tua ", -4.0, 138.05, 12);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<MonitoringAreaDto>(Json))!;
        Assert.StartsWith("AR-", dto.Id);
        Assert.Equal("Rumah Orang Tua", dto.Name);
        Assert.Equal(138.05, dto.Longitude);
        Assert.Equal(12, dto.RadiusKm);
        Assert.Contains(dto.Id, resp.Headers.Location!.ToString());
        Assert.Single((await andi.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!);
    }

    [Theory]
    [InlineData("", -4, 138, 10)]
    [InlineData("Rumah", 95, 138, 10)]
    [InlineData("Rumah", -4, 200, 10)]
    [InlineData("Rumah", -4, 138, 0)]
    [InlineData("Rumah", -4, 138, 51)]
    public async Task CreateArea_WithInvalidInput_Is400_WithAMessage(string name, double lat, double lon, double radius)
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);

        var resp = await CreateArea(andi, name, lat, lon, radius);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("error", await resp.Content.ReadAsStringAsync());
        Assert.Empty((await andi.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!);
    }

    [Fact]
    public async Task CreateArea_IsLimitedPerUser()
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);
        for (int i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Created, (await CreateArea(andi, $"L{i}", Lat + i, Lon)).StatusCode);

        var resp = await CreateArea(andi, "Kesebelas");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Maksimal 10", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Users_OnlySeeAndChangeTheirOwnAreas()
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);
        var budi = await LoginAsync(Budi);
        var area = (await (await CreateArea(andi)).Content.ReadFromJsonAsync<MonitoringAreaDto>(Json))!;

        Assert.Empty((await budi.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!);
        Assert.Equal(HttpStatusCode.NotFound, (await budi.PutAsJsonAsync($"/api/monitoring-areas/{area.Id}", new MonitoringAreaRequest("Dibajak", 0, 100, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await budi.DeleteAsync($"/api/monitoring-areas/{area.Id}")).StatusCode);
        Assert.Equal("Rumah", (await andi.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!.Single().Name);
    }

    [Fact]
    public async Task UpdateAndDeleteOwnArea_Work_AndUnknownIdsAre404()
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);
        var area = (await (await CreateArea(andi)).Content.ReadFromJsonAsync<MonitoringAreaDto>(Json))!;

        var put = await andi.PutAsJsonAsync($"/api/monitoring-areas/{area.Id}", new MonitoringAreaRequest("Kantor", -3.9, 138.2, 20));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = (await put.Content.ReadFromJsonAsync<MonitoringAreaDto>(Json))!;
        Assert.Equal(("Kantor", 20.0), (updated.Name, updated.RadiusKm));

        Assert.Equal(HttpStatusCode.BadRequest, (await andi.PutAsJsonAsync($"/api/monitoring-areas/{area.Id}", new MonitoringAreaRequest("x", -3.9, 138.2, 99))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await andi.PutAsJsonAsync("/api/monitoring-areas/AR-TIDAK-ADA", new MonitoringAreaRequest("x", 0, 100, 5))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await andi.DeleteAsync($"/api/monitoring-areas/{area.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await andi.DeleteAsync($"/api/monitoring-areas/{area.Id}")).StatusCode);
        Assert.Empty((await andi.GetFromJsonAsync<List<MonitoringAreaDto>>("/api/monitoring-areas", Json))!);
    }

    // ------------------------------------------------------------------ notifikasi

    [Fact]
    public async Task AdminAddingAHotspot_NotifiesTheNearbyUser_AndNobodyElse()
    {
        await ResetAsync();
        var andi = await LoginAsync(Andi);
        var budi = await LoginAsync(Budi);
        var admin = await LoginAsync(Admin);
        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);          // dekat titik api
        await CreateArea(budi, "Rumah Budi", Lat + 2, Lon + 2, 10);    // jauh
        Assert.Equal(0, (await Notifications(andi)).Items.Count);

        var hotspot = await AddHotspot(admin);                          // hook: hotspot manual baru memicu pemeriksaan

        var list = await Notifications(andi);
        Assert.True(list.Items.Count >= 1);
        Assert.Equal(list.Items.Count, list.Unread);
        Assert.All(list.Items, n =>
        {
            Assert.Equal(hotspot.Id, n.HotspotId);
            Assert.Equal("Rumah", n.AreaName);
            Assert.False(n.IsRead);
            Assert.InRange(n.Level, 1, 3);
        });
        Assert.Contains(list.Items, n => n.Kind == NotificationKind.Fire && n.Level == 3 && n.EtaHours == 0);
        Assert.Empty((await Notifications(budi)).Items);
    }

    [Fact]
    public async Task SavingALocationNearAnExistingFire_NotifiesImmediately()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        await AddHotspot(admin);
        var andi = await LoginAsync(Andi);

        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);

        Assert.True((await Notifications(andi)).Items.Count >= 1);
    }

    [Fact]
    public async Task MovingAHotspotNearALocation_AlsoTriggersTheCheck()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);
        var hotspot = await AddHotspot(admin, Lat + 3, Lon + 3);       // awalnya jauh
        Assert.Empty((await Notifications(andi)).Items);

        var move = await admin.PutAsJsonAsync($"/api/hotspots/{hotspot.Id}",
            new ManualHotspotRequest(Lat, Lon, 85, DateTime.Now, "Petugas", null, HotspotStatus.Active));

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.NotEmpty((await Notifications(andi)).Items);
    }

    [Fact]
    public async Task ReadStateAndDeletion_WorkAndAreOwnerOnly()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        var budi = await LoginAsync(Budi);
        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);
        await AddHotspot(admin);
        var list = await Notifications(andi);
        var first = list.Items[0];

        Assert.Equal(HttpStatusCode.NotFound, (await budi.PostAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await budi.DeleteAsync($"/api/notifications/{first.Id}")).StatusCode);
        Assert.Equal(list.Unread, (await Notifications(andi)).Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await andi.PostAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
        Assert.Equal(list.Unread - 1, (await Notifications(andi)).Unread);

        var unreadOnly = (await andi.GetFromJsonAsync<NotificationListDto>("/api/notifications?unreadOnly=true", Json))!;
        Assert.DoesNotContain(unreadOnly.Items, n => n.Id == first.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await andi.PostAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await Notifications(andi)).Unread);

        Assert.Equal(HttpStatusCode.NoContent, (await andi.DeleteAsync($"/api/notifications/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await andi.DeleteAsync($"/api/notifications/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await andi.PostAsync("/api/notifications/NT-TIDAK-ADA/read", null)).StatusCode);
    }

    [Fact]
    public async Task Check_ReportsWhatItDid_AndDoesNotRepeatWithinTheCooldown()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);
        await AddHotspot(admin);
        var before = (await Notifications(andi)).Items.Count;

        var check = (await (await andi.PostAsync("/api/notifications/check", null)).Content.ReadFromJsonAsync<CheckResultDto>(Json))!;

        Assert.Equal(1, check.AreasChecked);
        Assert.Equal(0, check.NotificationsCreated);   // sudah diberi tahu: tidak diulang
        Assert.Equal(before, (await Notifications(andi)).Items.Count);
    }

    [Fact]
    public async Task AdminEvaluate_CoversEveryonesAreas()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        var budi = await LoginAsync(Budi);
        await CreateArea(andi, "A", Lat, Lon + 0.03, 10);
        await CreateArea(budi, "B", Lat, Lon + 0.02, 10);

        var result = (await (await admin.PostAsync("/api/notifications/evaluate", null)).Content.ReadFromJsonAsync<CheckResultDto>(Json))!;

        Assert.Equal(2, result.AreasChecked);
    }

    // ------------------------------------------------------------------ simulasi: asap dan dampak pada lokasi pengguna

    [Fact]
    public async Task Simulation_IncludesSmoke_AndTheViewersImpacts()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        var budi = await LoginAsync(Budi);
        await CreateArea(andi, "Rumah", Lat, Lon + 0.03, 10);
        await CreateArea(budi, "Milik Budi", Lat, Lon + 0.02, 10);
        var hotspot = await AddHotspot(admin);

        var sim = (await andi.GetFromJsonAsync<SimulationDto>($"/api/hotspots/{hotspot.Id}/simulation?hours=12", Json))!;

        // asap
        Assert.Equal(SmokePlumeModel.CellStepDegrees, sim.SmokeCellSizeDegrees);
        Assert.Empty(sim.Frames[0].SmokeCells);
        var last = sim.Frames[^1].SmokeCells;
        Assert.NotEmpty(last);
        Assert.All(last, c =>
        {
            Assert.InRange(c.Level, 1, 3);
            Assert.InRange(c.Row, -SmokePlumeModel.HalfSizeCells, SmokePlumeModel.HalfSizeCells);
        });
        Assert.InRange(sim.SmokeMaxLevel, 1, 3);

        // dampak: hanya lokasi milik yang melihat
        var impact = Assert.Single(sim.Impacts);
        Assert.Equal("Rumah", impact.AreaName);
        Assert.Equal(0, impact.FireEtaHours);   // 3,3 km dari titik api, di dalam radius 10 km
        Assert.InRange(impact.HotspotDistanceKm, 3, 4);
        Assert.DoesNotContain(sim.Impacts, i => i.AreaName == "Milik Budi");
    }

    [Fact]
    public async Task Simulation_ForAdmin_HasNoImpacts_ButStillHasSmoke()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var hotspot = await AddHotspot(admin);

        var sim = (await admin.GetFromJsonAsync<SimulationDto>($"/api/hotspots/{hotspot.Id}/simulation?hours=6", Json))!;

        Assert.Empty(sim.Impacts);
        Assert.True(sim.Frames.Any(f => f.SmokeCells.Count > 0));
    }

    [Fact]
    public async Task Simulation_ImpactReportsFarLocationsAsNotReached()
    {
        await ResetAsync();
        var admin = await LoginAsync(Admin);
        var andi = await LoginAsync(Andi);
        await CreateArea(andi, "Jauh", Lat + 0.6, Lon - 0.6, 5);   // ±95 km
        var hotspot = await AddHotspot(admin);

        var sim = (await andi.GetFromJsonAsync<SimulationDto>($"/api/hotspots/{hotspot.Id}/simulation?hours=12", Json))!;

        var impact = Assert.Single(sim.Impacts);
        Assert.Null(impact.FireEtaHours);
        Assert.Equal(0, impact.SmokeMaxLevel);
        Assert.Null(impact.SmokeEtaHours);
        Assert.InRange(impact.HotspotDistanceKm, 80, 110);
    }
}
