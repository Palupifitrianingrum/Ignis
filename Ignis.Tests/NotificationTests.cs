using System.Net;
using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ignis.Tests;

// ======================================================================================================================
// Lokasi tersimpan dan notifikasi dampak kebakaran (api dan asap). Semua tanpa jaringan: cuaca diambil dari baris tersimpan.
// Lokasi uji ada di Papua supaya tidak bersinggungan dengan hotspot manual aktif pada data demo (Banyuasin).
// ======================================================================================================================

public class MonitoringAreaDomainTests
{
    [Fact]
    public void Valid_TrimsName_AndComputesBoundingBoxFromRadius()
    {
        var a = new MonitoringArea("U-002", "  Rumah  ", -4.0, 138.0, 10);

        Assert.Equal("Rumah", a.Name);
        Assert.Equal("U-002", a.UserId);
        Assert.Equal(-4.0, a.Latitude);
        Assert.Equal(138.0, a.Longitude);
        Assert.Equal(10, a.Radius);
        Assert.Equal(10 / 111.32, a.MaxLatitude - a.Latitude, 6);
        Assert.Equal(a.Latitude - a.MinLatitude, a.MaxLatitude - a.Latitude, 9);   // simetris
        Assert.True(a.Contains(-4.0, 138.0));
        Assert.True(a.Contains(-4.05, 138.05));
        Assert.False(a.Contains(-4.5, 138.0));   // ±55 km: di luar kotak 10 km
    }

    [Theory]
    [InlineData("", -4, 138, 10, "Nama lokasi wajib diisi")]
    [InlineData("   ", -4, 138, 10, "Nama lokasi wajib diisi")]
    [InlineData("Rumah", 95, 138, 10, "Latitude")]
    [InlineData("Rumah", -4, 181, 10, "Longitude")]
    [InlineData("Rumah", double.NaN, 138, 10, "Latitude")]
    [InlineData("Rumah", -4, 138, 0.5, "Radius")]
    [InlineData("Rumah", -4, 138, 51, "Radius")]
    [InlineData("Rumah", -4, 138, double.NaN, "Radius")]
    public void Invalid_IsRejected(string name, double lat, double lon, double radius, string messagePart)
    {
        var ex = Assert.Throws<DomainException>(() => new MonitoringArea("U-002", name, lat, lon, radius));
        Assert.Contains(messagePart, ex.Message);
    }

    [Fact]
    public void NameLongerThan120_IsRejected() =>
        Assert.Throws<DomainException>(() => new MonitoringArea("U-002", new string('a', 121), -4, 138, 10));

    [Fact]
    public void RadiusBoundaries_AreInclusive()
    {
        Assert.Equal(1, new MonitoringArea("U-002", "a", 0, 100, MonitoringArea.MinRadiusKm).Radius);
        Assert.Equal(50, new MonitoringArea("U-002", "a", 0, 100, MonitoringArea.MaxRadiusKm).Radius);
    }

    [Fact]
    public void Owner_IsRequired() => Assert.Throws<DomainException>(() => new MonitoringArea("", "Rumah", 0, 100, 10));

    [Fact]
    public void Update_IsAtomic_InvalidInputChangesNothing()
    {
        var a = new MonitoringArea("U-002", "Rumah", -4, 138, 10);

        Assert.Throws<DomainException>(() => a.Update("Kantor", -3, 139, 999));   // radius salah, yang lain benar

        Assert.Equal("Rumah", a.Name);
        Assert.Equal(-4, a.Latitude);
        Assert.Equal(138, a.Longitude);
        Assert.Equal(10, a.Radius);
    }

    [Fact]
    public void Update_AppliesValidInput_AndRecomputesTheBox()
    {
        var a = new MonitoringArea("U-002", "Rumah", -4, 138, 10);
        a.Update("Kantor", -3, 139, 20);

        Assert.Equal("Kantor", a.Name);
        Assert.True(a.Contains(-3, 139));
        Assert.False(a.Contains(-4, 138));
        Assert.Equal(20 / 111.32, a.MaxLatitude - a.Latitude, 6);
    }

    [Fact]
    public void Id_CanOnlyBeAssignedOnce()
    {
        var a = new MonitoringArea("U-002", "Rumah", -4, 138, 10);
        a.AssignId("AR-1001");
        Assert.Throws<DomainException>(() => a.AssignId("AR-1002"));
        Assert.Throws<DomainException>(() => new MonitoringArea("U-002", "x", 0, 100, 5).AssignId(" "));
    }

    [Fact]
    public void BoundingBox_StaysInsideValidRanges_NearTheEdges()
    {
        var a = new MonitoringArea("U-002", "Kutub", 89.9, 179.9, 50);
        Assert.InRange(a.MaxLatitude, -90, 90);
        Assert.InRange(a.MaxLongitude, -180, 180);
        Assert.InRange(a.MinLongitude, -180, 180);
    }
}

public class NotificationDomainTests
{
    private static Notification Make(int level = 2, int eta = 3, double distance = 8.24, string? title = "Judul", string? message = "Isi") =>
        new("U-002", "HS-1", "AR-1", NotificationKind.Fire, level, eta, distance, title!, message!);

    [Fact]
    public void Valid_IsUnreadAndTrimmed()
    {
        var n = new Notification("U-002", "HS-1", "AR-1", NotificationKind.Smoke, 3, 0, 8.26, "  Judul  ", "  Isi  ");

        Assert.False(n.IsRead);
        Assert.Equal("Judul", n.Title);
        Assert.Equal("Isi", n.Message);
        Assert.Equal(8.3, n.DistanceKm);   // dibulatkan satu desimal
        Assert.Equal(NotificationKind.Smoke, n.Kind);
        Assert.InRange((DateTime.Now - n.CreatedAt).TotalSeconds, -1, 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void LevelOutsideOneToThree_IsRejected(int level) => Assert.Throws<DomainException>(() => Make(level: level));

    [Fact]
    public void NegativeEtaOrDistance_IsRejected()
    {
        Assert.Throws<DomainException>(() => Make(eta: -1));
        Assert.Throws<DomainException>(() => Make(distance: -0.1));
        Assert.Throws<DomainException>(() => Make(distance: double.NaN));
    }

    [Fact]
    public void BlankTitleOrMessage_IsRejected()
    {
        Assert.Throws<DomainException>(() => Make(title: ""));
        Assert.Throws<DomainException>(() => Make(message: "  "));
        Assert.Throws<DomainException>(() => Make(title: new string('a', 201)));
    }

    [Fact]
    public void MarkAsRead_AndIdOnlyOnce()
    {
        var n = Make();
        n.MarkAsRead();
        Assert.True(n.IsRead);

        n.AssignId("NT-1001");
        Assert.Throws<DomainException>(() => n.AssignId("NT-1002"));
    }

    [Fact]
    public void Recipient_AndHotspot_AreRequired()
    {
        Assert.Throws<DomainException>(() => new Notification("", "HS-1", null, NotificationKind.Fire, 1, 0, 1, "t", "m"));
        Assert.Throws<DomainException>(() => new Notification("U-1", "", null, NotificationKind.Fire, 1, 0, 1, "t", "m"));
    }
}

public class GeoTests
{
    [Fact]
    public void DistanceKm_MatchesKnownValues()
    {
        Assert.Equal(111.19, Geo.DistanceKm(0, 100, 1, 100), 1);          // 1° lintang
        Assert.Equal(111.19, Geo.DistanceKm(0, 100, 0, 101), 1);          // 1° bujur di khatulistiwa
        Assert.Equal(55.6, Geo.DistanceKm(60, 10, 60, 11), 1);            // 1° bujur di lintang 60 ≈ setengahnya
        Assert.Equal(0, Geo.DistanceKm(-4, 138, -4, 138), 9);
        Assert.Equal(Geo.DistanceKm(1, 2, 3, 4), Geo.DistanceKm(3, 4, 1, 2), 9);   // simetris
    }

    [Fact]
    public void DistanceToCell_IsZeroInside_AndMeasuresToTheEdgeOutside()
    {
        const double half = 0.0125;   // sel asap ±1,39 km
        Assert.Equal(0, Geo.DistanceToCellKm(0, 100, 0, 100, half), 9);
        Assert.Equal(0, Geo.DistanceToCellKm(0.01, 100.01, 0, 100, half), 9);   // di dalam sel

        // 0,05° ke utara = 5,566 km dari pusat; tepi sel di 1,39 km -> 4,18 km
        Assert.Equal(0.05 * 111.32 - half * 111.32, Geo.DistanceToCellKm(0.05, 100, 0, 100, half), 3);
    }

    [Theory]
    [InlineData(0, "Utara")]
    [InlineData(45, "Timur Laut")]
    [InlineData(90, "Timur")]
    [InlineData(135, "Tenggara")]
    [InlineData(180, "Selatan")]
    [InlineData(225, "Barat Daya")]
    [InlineData(270, "Barat")]
    [InlineData(315, "Barat Laut")]
    [InlineData(350, "Utara")]
    [InlineData(-90, "Barat")]
    [InlineData(720, "Utara")]
    public void CompassName(double degrees, string expected) => Assert.Equal(expected, Geo.CompassName(degrees));
}

/// <summary>Evaluator dampak dengan simulasi buatan tangan, supaya geometri dan waktunya bisa dihitung persis.</summary>
public class ExposureEvaluatorTests
{
    private const double Lat0 = 0, Lon0 = 100;

    private static SimulationResult Craft(
        IReadOnlyList<(double DLon, double DLat)[]> burning,
        IReadOnlyList<(int Row, int Col, int Level)[]>? smoke = null)
    {
        var frames = new List<SimulationFrame>();
        for (int h = 0; h < burning.Count; h++)
        {
            var coords = burning[h].Select(c => new Coordinate(Lat0 + c.DLat, Lon0 + c.DLon)).ToList();
            var cells = (smoke != null && h < smoke.Count ? smoke[h] : Array.Empty<(int Row, int Col, int Level)>()).Select(c => new SmokeCell(c.Row, c.Col, c.Level)).ToList();
            frames.Add(new SimulationFrame(h, coords, coords.Count * 30.0) { SmokeCells = cells });
        }
        int maxLevel = frames.SelectMany(f => f.SmokeCells).Select(c => c.Level).DefaultIfEmpty(0).Max();
        return new SimulationResult("HS-T", Lat0, Lon0, burning.Count - 1, 90, 270, 5, 30, 50, 0, "cerah", 0.005, 30, 400, false,
                                    SmokePlumeModel.CellStepDegrees, maxLevel, frames);
    }

    /// <summary>Api merambat ke timur satu sel (0,005°) per jam mulai dari asal.</summary>
    private static SimulationResult EastwardFire(int hours) =>
        Craft(Enumerable.Range(0, hours + 1).Select(h => Enumerable.Range(0, h + 1).Select(i => (i * 0.005, 0.0)).ToArray()).ToList());

    [Fact]
    public void Fire_PointInsideABurningCell_IsReachedAtHourZero()
    {
        var e = ExposureEvaluator.Evaluate(EastwardFire(3), Lat0, Lon0, 1, 3);

        Assert.Equal(0, e.FireEtaHours);
        Assert.True(e.FireReaches);
        Assert.Equal(0, e.FireMinDistanceKm);
    }

    [Fact]
    public void Fire_EtaIsTheFirstHourAnEdgeEntersTheRadius()
    {
        // Titik 0,02° (2,23 km) di timur asal, radius 1 km. Tepi sel di 0,01° berjarak 0,835 km (jam 2); sel 0,005° (jam 1) masih 1,39 km.
        var e = ExposureEvaluator.Evaluate(EastwardFire(4), Lat0, Lon0 + 0.02, 1, 4);

        Assert.Equal(2, e.FireEtaHours);
    }

    [Fact]
    public void Fire_MinDistance_DependsOnTheHorizon()
    {
        var sim = EastwardFire(4);
        // Sampai jam 2 terdekat = tepi sel 0,01° (0,835 km); sampai jam 4 = sel 0,02° tepat di titik (0 km).
        Assert.Equal(0.8, ExposureEvaluator.Evaluate(sim, Lat0, Lon0 + 0.02, 1, 2).FireMinDistanceKm, 1);
        Assert.Equal(0.0, ExposureEvaluator.Evaluate(sim, Lat0, Lon0 + 0.02, 1, 4).FireMinDistanceKm, 1);
    }

    [Fact]
    public void Fire_NotReaching_HasNullEta_ButStillReportsTheClosestDistance()
    {
        // Titik 50 km di barat, api hanya ke timur.
        var e = ExposureEvaluator.Evaluate(EastwardFire(4), Lat0, Lon0 - 0.45, 5, 4);

        Assert.Null(e.FireEtaHours);
        Assert.False(e.FireReaches);
        Assert.InRange(e.FireMinDistanceKm, 49, 51);
    }

    [Fact]
    public void Fire_HorizonLimitsWhichHoursCount()
    {
        var sim = EastwardFire(4);
        Assert.Equal(2, ExposureEvaluator.Evaluate(sim, Lat0, Lon0 + 0.02, 1, 4).FireEtaHours);
        Assert.Null(ExposureEvaluator.Evaluate(sim, Lat0, Lon0 + 0.02, 1, 1).FireEtaHours);   // sampai jam 1 belum sampai
    }

    [Fact]
    public void BiggerRadius_NeverArrivesLater()
    {
        var sim = EastwardFire(6);
        int? Eta(double radius) => ExposureEvaluator.Evaluate(sim, Lat0, Lon0 + 0.04, radius, 6).FireEtaHours;

        Assert.True(Eta(1) >= Eta(3));
        Assert.True(Eta(3) >= Eta(10));
        Assert.Equal(0, Eta(10));   // 4,45 km dari asal, di dalam radius 10 km sejak jam 0
    }

    [Fact]
    public void HotspotDistance_IsReported()
    {
        var e = ExposureEvaluator.Evaluate(EastwardFire(1), Lat0, Lon0 + 0.1, 5, 1);
        Assert.Equal(11.1, e.HotspotDistanceKm, 1);
    }

    // Asap: sel (baris 0, kolom 1) = 0,025° ≈ 2,78 km di timur asal; (0, 2) = 5,57 km; setengah sisi sel 1,39 km.
    private static SimulationResult WithSmoke() =>
        Craft(Enumerable.Repeat(new[] { (0.0, 0.0) }, 5).ToList(),
              new (int, int, int)[][]
              {
                  Array.Empty<(int, int, int)>(),                          // jam 0
                  new[] { (0, 1, 1) },                                     // jam 1: ringan di kolom 1
                  new[] { (0, 1, 1), (0, 2, 2) },                          // jam 2: sedang di kolom 2
                  new[] { (0, 2, 3) },                                     // jam 3: pekat di kolom 2
                  new[] { (0, 2, 2) }                                      // jam 4
              });

    [Fact]
    public void Smoke_EtaMaxLevel_AndItsHour()
    {
        // Titik di kolom 2, radius 1 km: hanya sel kolom 2 yang masuk.
        var e = ExposureEvaluator.Evaluate(WithSmoke(), Lat0, Lon0 + 0.05, 1, 4);

        Assert.True(e.SmokeReaches);
        Assert.Equal(2, e.SmokeEtaHours);          // jam pertama ada asap di dalam radius
        Assert.Equal(3, e.SmokeMaxLevel);
        Assert.Equal(3, e.SmokeMaxLevelEtaHours);
    }

    [Fact]
    public void Smoke_BiggerRadiusPullsInTheEarlierCell()
    {
        // Radius 2 km: sel kolom 1 (tepi 1,39 km dari titik) ikut masuk, jadi asap tiba pada jam 1.
        var e = ExposureEvaluator.Evaluate(WithSmoke(), Lat0, Lon0 + 0.05, 2, 4);
        Assert.Equal(1, e.SmokeEtaHours);
        Assert.Equal(3, e.SmokeMaxLevel);
    }

    [Fact]
    public void Smoke_OutsideTheRadius_IsIgnored()
    {
        var e = ExposureEvaluator.Evaluate(WithSmoke(), Lat0 + 0.5, Lon0 + 0.05, 5, 4);   // 55 km di utara
        Assert.False(e.SmokeReaches);
        Assert.Equal(0, e.SmokeMaxLevel);
        Assert.Null(e.SmokeEtaHours);
        Assert.Null(e.SmokeMaxLevelEtaHours);
    }

    [Fact]
    public void Smoke_HorizonCutsOffLaterHours()
    {
        var e = ExposureEvaluator.Evaluate(WithSmoke(), Lat0, Lon0 + 0.05, 1, 2);
        Assert.Equal(2, e.SmokeEtaHours);
        Assert.Equal(2, e.SmokeMaxLevel);   // level 3 baru jam 3: di luar horizon
    }

    [Fact]
    public void FireAndSmoke_AreIndependent()
    {
        var e = ExposureEvaluator.Evaluate(WithSmoke(), Lat0, Lon0 + 0.05, 1, 4);
        Assert.False(e.FireReaches);   // api tetap di asal (5,6 km dari titik)
        Assert.True(e.SmokeReaches);
    }
}

/// <summary>Layanan lokasi tersimpan dan notifikasi di atas database EF InMemory.</summary>
public class NotificationServiceTests
{
    private const string Andi = "U-002", Budi = "U-004";
    private const double Lat = -4.0, Lon = 138.0;   // Papua: jauh dari hotspot demo

    private sealed class Rig
    {
        public required AppDbContext Db { get; init; }
        public required NotificationService Notifications { get; init; }
        public required MonitoringAreaService Areas { get; init; }
        public required NotificationOptions Options { get; init; }

        public static Rig Create(Action<NotificationOptions>? tweak = null)
        {
            var db = TestDb.Create();
            var integrations = new IntegrationOptions();   // tanpa key: cuaca tidak pernah memanggil jaringan
            var weather = new WeatherClient(new HttpClient(new FakeHandler()), integrations, new IntegrationStatus());
            var predictions = new PredictionService(db, weather, new FwiPredictionModel(), new ActivityLogService(db), integrations);
            var options = new NotificationOptions();
            tweak?.Invoke(options);
            return new Rig
            {
                Db = db,
                Options = options,
                Notifications = new NotificationService(db, predictions, new FireSpreadSimulator(), options),
                Areas = new MonitoringAreaService(db, options)
            };
        }

        /// <summary>Hotspot manual aktif beserta cuaca segar yang diketahui (angin dari <paramref name="windFrom"/>), supaya simulasinya pasti.</summary>
        public ManualHotspot AddHotspot(string id, double lat, double lon, HotspotStatus status = HotspotStatus.Active,
                                        DateTime? incident = null, double windFrom = 270, double windSpeed = 8)
        {
            var h = new ManualHotspot(lat, lon, 85, incident ?? DateTime.Now, "Petugas", null, status);
            h.AssignId(id);
            Db.Hotspots.Add(h);
            AddWeather(id, windFrom, windSpeed);
            Db.SaveChanges();
            return h;
        }

        public SatelliteHotspot AddSatellite(string id, double lat, double lon, DateTime detectedAt, HotspotStatus status = HotspotStatus.Active)
        {
            var h = new SatelliteHotspot(lat, lon, 80, 340, detectedAt, "NOAA-20", "VIIRS", status);
            h.AssignId(id);
            Db.Hotspots.Add(h);
            AddWeather(id, 270, 8);
            Db.SaveChanges();
            return h;
        }

        private void AddWeather(string hotspotId, double windFrom, double windSpeed) =>
            Db.WeatherData.Add(new WeatherData
            {
                Id = "WX-" + hotspotId, HotspotId = hotspotId, Timestamp = DateTime.Now, Temperature = 34, Humidity = 40,
                WindSpeed = windSpeed, WindDirection = windFrom, Pressure = 1010, Precipitation = 0, Condition = "cerah"
            });

        public async Task<MonitoringArea> AddAreaAsync(string userId, string name, double lat, double lon, double radius = 10)
        {
            var a = new MonitoringArea(userId, name, lat, lon, radius);
            a.AssignId(await Db.NextIdAsync(IdKind.Area));
            Db.MonitoringAreas.Add(a);
            await Db.SaveChangesAsync();
            return a;
        }

        public Task<List<Notification>> AllAsync(string? userId = null) =>
            Db.Notifications.AsNoTracking().Where(n => userId == null || n.UserId == userId).OrderBy(n => n.Id).ToListAsync();
    }

    // ------------------------------------------------------------------ kapan notifikasi dikirim

    [Fact]
    public async Task NearAndDownwind_ProducesFireAndSmokeNotifications_ForTheOwner()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);                        // angin dari barat -> api dan asap ke timur
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.10, 10);   // ±11 km di timur

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(new EvaluationSummary(1, 1, 2), summary);
        var list = await rig.AllAsync();
        Assert.Equal(2, list.Count);
        Assert.All(list, n =>
        {
            Assert.Equal(Andi, n.UserId);
            Assert.Equal("HS-9001", n.HotspotId);
            Assert.Equal(home.Id, n.AreaId);
            Assert.InRange(n.Level, 1, 3);
            Assert.InRange(n.EtaHours, 0, rig.Options.HorizonHours);
            Assert.False(n.IsRead);
            Assert.Contains("\"Rumah\"", n.Title);
            Assert.Contains("HS-9001", n.Message);
            Assert.Contains("bukan prakiraan resmi", n.Message);
            Assert.Contains("Angin dari Barat", n.Message);
        });
        Assert.Single(list, n => n.Kind == NotificationKind.Fire);
        Assert.Single(list, n => n.Kind == NotificationKind.Smoke);
    }

    [Fact]
    public async Task FireAlreadyInsideTheRadius_IsBahaya_WithEtaZero()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);   // 3,3 km dari hotspot, di dalam radius 10 km

        await rig.Notifications.EvaluateAllAsync();

        var fire = Assert.Single(await rig.AllAsync(), n => n.Kind == NotificationKind.Fire);
        Assert.Equal(3, fire.Level);
        Assert.Equal(0, fire.EtaHours);
        Assert.StartsWith("Bahaya", fire.Title);
        Assert.Contains("sudah berada dalam radius", fire.Message);
    }

    [Fact]
    public async Task Upwind_GetsNoNotification()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);                                    // asap dan api ke timur
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon - 0.30, 5);              // ±33 km di barat (hulu)

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(0, summary.Created);
        Assert.Empty(await rig.AllAsync());
    }

    [Fact]
    public async Task WindDirectionDecides_WhoIsNotified()
    {
        // Rumah di timur: angin dari barat mengancam, angin dari timur tidak.
        foreach (var (windFrom, expected) in new[] { (270.0, true), (90.0, false) })
        {
            var rig = Rig.Create();
            rig.AddHotspot("HS-9001", Lat, Lon, windFrom: windFrom, windSpeed: 10);
            await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.25, 5);   // ±28 km di timur

            var summary = await rig.Notifications.EvaluateAllAsync();

            Assert.Equal(expected, summary.Created > 0);
        }
    }

    [Fact]
    public async Task BeyondTheCandidateRadius_IsNotSimulatedAtAll()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Jauh", Lat, Lon + 1.0, 10);   // ±111 km

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(new EvaluationSummary(1, 0, 0), summary);
    }

    [Fact]
    public async Task OnlyTheOwner_IsNotified_OtherUsersAreNot()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah Andi", Lat, Lon + 0.03, 10);       // dekat
        await rig.AddAreaAsync(Budi, "Rumah Budi", Lat + 2.0, Lon + 2.0, 10);   // jauh (±310 km)

        await rig.Notifications.EvaluateAllAsync();

        Assert.NotEmpty(await rig.AllAsync(Andi));
        Assert.Empty(await rig.AllAsync(Budi));
    }

    [Fact]
    public async Task OneUserWithTwoAreas_GetsNotificationsPerArea()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var near = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        var far = await rig.AddAreaAsync(Andi, "Kantor", Lat + 3, Lon + 3, 10);

        await rig.Notifications.EvaluateAllAsync();

        var list = await rig.AllAsync(Andi);
        Assert.All(list, n => Assert.Equal(near.Id, n.AreaId));
        Assert.DoesNotContain(list, n => n.AreaId == far.Id);
    }

    // ------------------------------------------------------------------ hotspot yang dipakai

    [Fact]
    public async Task ResolvedHotspots_AreIgnored()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon, status: HotspotStatus.Resolved);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        Assert.Equal(0, (await rig.Notifications.EvaluateAllAsync()).Created);
    }

    [Fact]
    public async Task MonitoringStatus_IsStillConsidered()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon, status: HotspotStatus.Monitoring);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        Assert.True((await rig.Notifications.EvaluateAllAsync()).Created > 0);
    }

    [Fact]
    public async Task OldSatelliteHotspots_AreIgnored_ButRecentOnesAreUsed()
    {
        var old = Rig.Create();
        old.AddSatellite("HS-9001", Lat, Lon, DateTime.Now.AddHours(-50));
        await old.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        Assert.Equal(0, (await old.Notifications.EvaluateAllAsync()).Created);

        var recent = Rig.Create();
        recent.AddSatellite("HS-9001", Lat, Lon, DateTime.Now.AddHours(-2));
        await recent.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        Assert.True((await recent.Notifications.EvaluateAllAsync()).Created > 0);
    }

    [Fact]
    public async Task OldManualHotspot_StillActive_IsUsed()
    {
        // Laporan manual yang belum ditutup tetap berlaku walau sudah beberapa hari.
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon, incident: DateTime.Now.AddDays(-5));
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        Assert.True((await rig.Notifications.EvaluateAllAsync()).Created > 0);
    }

    [Fact]
    public async Task HotspotsOfTheSameCluster_AreSimulatedOnce()
    {
        var rig = Rig.Create();
        for (int i = 0; i < 6; i++) rig.AddHotspot($"HS-90{i:00}", Lat + i * 0.004, Lon);   // ±0,4 km satu sama lain
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(1, summary.HotspotsSimulated);   // satu gugus = satu simulasi
        Assert.Equal(2, summary.Created);             // dan tetap hanya satu notifikasi per jenis, bukan enam
    }

    [Fact]
    public async Task CandidatesPerArea_AreCapped_NearestFirst()
    {
        var rig = Rig.Create();
        for (int i = 1; i <= 6; i++) rig.AddHotspot($"HS-90{i:00}", Lat, Lon + i * 0.1);   // ±11 km satu sama lain, 11–66 km dari rumah
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon, 5);

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(rig.Options.MaxCandidatesPerArea, summary.HotspotsSimulated);
    }

    [Fact]
    public async Task OneAreaWithManyThreateningFires_StillGetsAtMostOneNotificationPerKind()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon + 0.03);
        rig.AddHotspot("HS-9002", Lat + 0.08, Lon + 0.04);   // ±9 km dari yang pertama: gugus berbeda
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.05, 10);

        await rig.Notifications.EvaluateAllAsync();

        var list = await rig.AllAsync();
        Assert.True(list.Count(n => n.Kind == NotificationKind.Fire) <= 1);
        Assert.True(list.Count(n => n.Kind == NotificationKind.Smoke) <= 1);
    }

    [Fact]
    public async Task InactiveUsers_AreNotNotified()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        rig.Db.Users.OfType<RegisteredUser>().First(u => u.Id == Andi).Deactivate();
        await rig.Db.SaveChangesAsync();

        var summary = await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(new EvaluationSummary(0, 0, 0), summary);
    }

    [Fact]
    public async Task WithoutAnyAreas_NothingHappens()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        Assert.Equal(new EvaluationSummary(0, 0, 0), await rig.Notifications.EvaluateAllAsync());
    }

    // ------------------------------------------------------------------ pencegahan pengiriman berulang dan eskalasi

    [Fact]
    public async Task RepeatedEvaluation_DoesNotSpamTheUser()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        var first = await rig.Notifications.EvaluateAllAsync();
        var second = await rig.Notifications.EvaluateAllAsync();
        var third = await rig.Notifications.EvaluateAllAsync();

        Assert.True(first.Created > 0);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, third.Created);
        Assert.Equal(first.Created, (await rig.AllAsync()).Count);
    }

    [Fact]
    public async Task HigherSeverity_Escalates_WithinTheCooldown()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);   // api sudah di dalam radius -> tingkat 3

        // Notifikasi api tingkat 2 yang baru dikirim: tingkat 3 yang baru harus tetap dikirim (eskalasi).
        rig.Db.Notifications.Add(Seed(home, Andi, NotificationKind.Fire, level: 2, hoursAgo: 1));
        await rig.Db.SaveChangesAsync();

        await rig.Notifications.EvaluateAllAsync();

        Assert.Contains(await rig.AllAsync(), n => n.Kind == NotificationKind.Fire && n.Level == 3);
    }

    [Fact]
    public async Task SameOrLowerSeverity_IsSuppressed_WithinTheCooldown()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        rig.Db.Notifications.Add(Seed(home, Andi, NotificationKind.Fire, level: 3, hoursAgo: 1));
        await rig.Db.SaveChangesAsync();

        await rig.Notifications.EvaluateAllAsync();

        Assert.Single(await rig.AllAsync(), n => n.Kind == NotificationKind.Fire);   // hanya yang lama
    }

    [Fact]
    public async Task AfterTheCooldown_TheUserIsRemindedAgain()
    {
        var rig = Rig.Create(o => o.CooldownHours = 6);
        rig.AddHotspot("HS-9001", Lat, Lon);
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        rig.Db.Notifications.Add(Seed(home, Andi, NotificationKind.Fire, level: 3, hoursAgo: 7));
        await rig.Db.SaveChangesAsync();

        await rig.Notifications.EvaluateAllAsync();

        Assert.Equal(2, (await rig.AllAsync()).Count(n => n.Kind == NotificationKind.Fire));   // lama + pengingat
    }

    [Fact]
    public async Task Cooldown_IsPerKind_FireDoesNotSuppressSmoke()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        rig.Db.Notifications.Add(Seed(home, Andi, NotificationKind.Fire, level: 3, hoursAgo: 1));
        await rig.Db.SaveChangesAsync();

        await rig.Notifications.EvaluateAllAsync();

        Assert.Contains(await rig.AllAsync(), n => n.Kind == NotificationKind.Smoke);
    }

    private static Notification Seed(MonitoringArea area, string userId, NotificationKind kind, int level, double hoursAgo)
    {
        var n = new Notification(userId, "HS-9001", area.Id, kind, level, 0, 1, "Lama", "Notifikasi lama", DateTime.Now.AddHours(-hoursAgo));
        n.AssignId("NT-" + Guid.NewGuid().ToString("N")[..8]);
        return n;
    }

    // ------------------------------------------------------------------ lingkup pemeriksaan

    [Fact]
    public async Task EvaluateHotspot_OnlyConsidersThatHotspot()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        rig.AddHotspot("HS-9002", Lat + 0.2, Lon);   // gugus lain, juga dekat rumah
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        var summary = await rig.Notifications.EvaluateHotspotAsync("HS-9002");

        Assert.Equal(1, summary.HotspotsSimulated);
        Assert.All(await rig.AllAsync(), n => Assert.Equal("HS-9002", n.HotspotId));
    }

    [Fact]
    public async Task EvaluateUser_AndEvaluateArea_AreScoped()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var andiHome = await rig.AddAreaAsync(Andi, "Rumah Andi", Lat, Lon + 0.03, 10);
        await rig.AddAreaAsync(Budi, "Rumah Budi", Lat, Lon + 0.03, 10);

        var forAndi = await rig.Notifications.EvaluateUserAsync(Andi);
        Assert.Equal(1, forAndi.Areas);
        Assert.Empty(await rig.AllAsync(Budi));

        var forBudiArea = await rig.Notifications.EvaluateAreaAsync(andiHome.Id);   // sudah diberi tahu: tidak ada yang baru
        Assert.Equal(1, forBudiArea.Areas);
        Assert.Equal(0, forBudiArea.Created);
    }

    [Fact]
    public async Task ParallelEvaluations_DoNotCreateDuplicates()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        // Satu DbContext tidak boleh dipakai paralel; ini menguji gerbang lewat dua pemanggilan beruntun yang bersamaan dimulai.
        var a = rig.Notifications.EvaluateAllAsync();
        var b = Task.Run(() => rig.Notifications.EvaluateAllAsync());
        var results = await Task.WhenAll(a, b);

        Assert.Equal(results.Sum(r => r.Created), (await rig.AllAsync()).Count);
        Assert.Equal(2, (await rig.AllAsync()).Count);   // satu api + satu asap, bukan empat
    }

    // ------------------------------------------------------------------ lokasi tersimpan

    [Fact]
    public async Task Areas_AreCreatedWithIds_ListedPerOwner_AndOrderedByName()
    {
        var rig = Rig.Create();
        var b = await rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("Kantor", -4, 138, 10));
        var a = await rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("Rumah", -4.1, 138.1, 5));
        await rig.Areas.CreateAsync(Budi, new MonitoringAreaRequest("Rumah Budi", -3, 137, 5));

        Assert.StartsWith("AR-", a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(new[] { "Kantor", "Rumah" }, (await rig.Areas.GetForUserAsync(Andi)).Select(x => x.Name).ToArray());
        Assert.Single(await rig.Areas.GetForUserAsync(Budi));
    }

    [Fact]
    public async Task Areas_AreLimitedPerUser()
    {
        var rig = Rig.Create(o => o.MaxAreasPerUser = 3);
        for (int i = 0; i < 3; i++) await rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest($"L{i}", -4, 138, 5));

        var ex = await Assert.ThrowsAsync<DomainException>(() => rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("L3", -4, 138, 5)));
        Assert.Contains("Maksimal 3", ex.Message);

        await rig.Areas.CreateAsync(Budi, new MonitoringAreaRequest("Milik Budi", -4, 138, 5));   // batas per pengguna, bukan global
    }

    [Fact]
    public async Task InvalidArea_IsRejected_AndNothingIsStored()
    {
        var rig = Rig.Create();
        await Assert.ThrowsAsync<DomainException>(() => rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("", -4, 138, 5)));
        await Assert.ThrowsAsync<DomainException>(() => rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("x", 95, 138, 5)));
        Assert.Empty(await rig.Areas.GetForUserAsync(Andi));
    }

    [Fact]
    public async Task Update_AndDelete_AreOwnerOnly()
    {
        var rig = Rig.Create();
        var area = await rig.Areas.CreateAsync(Andi, new MonitoringAreaRequest("Rumah", -4, 138, 5));

        Assert.Null(await rig.Areas.UpdateAsync(Budi, area.Id, new MonitoringAreaRequest("Dibajak", 0, 100, 5)));   // bukan miliknya
        Assert.False(await rig.Areas.DeleteAsync(Budi, area.Id));
        Assert.Equal("Rumah", (await rig.Areas.GetForUserAsync(Andi)).Single().Name);

        var updated = await rig.Areas.UpdateAsync(Andi, area.Id, new MonitoringAreaRequest("Rumah Baru", -4.5, 138.5, 20));
        Assert.Equal("Rumah Baru", updated!.Name);
        Assert.Equal(20, updated.Radius);

        Assert.Null(await rig.Areas.UpdateAsync(Andi, "AR-TIDAK-ADA", new MonitoringAreaRequest("x", 0, 100, 5)));
        Assert.True(await rig.Areas.DeleteAsync(Andi, area.Id));
        Assert.Empty(await rig.Areas.GetForUserAsync(Andi));
    }

    [Fact]
    public async Task DeletingAnArea_AlsoDeletesItsNotifications()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        var other = await rig.AddAreaAsync(Andi, "Kantor", Lat, Lon + 0.02, 10);
        await rig.Notifications.EvaluateAllAsync();
        Assert.Contains(await rig.AllAsync(), n => n.AreaId == home.Id);

        await rig.Areas.DeleteAsync(Andi, home.Id);

        var left = await rig.AllAsync();
        Assert.DoesNotContain(left, n => n.AreaId == home.Id);
        Assert.Contains(left, n => n.AreaId == other.Id);   // lokasi lain tidak ikut terhapus
    }

    // ------------------------------------------------------------------ membaca dan mengelola notifikasi

    private static async Task<Rig> RigWithNotificationsAsync()
    {
        var rig = Rig.Create();
        rig.AddHotspot("HS-9001", Lat, Lon);
        await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);
        await rig.AddAreaAsync(Budi, "Rumah Budi", Lat, Lon + 0.02, 10);
        await rig.Notifications.EvaluateAllAsync();
        return rig;
    }

    [Fact]
    public async Task List_ReturnsOwnNotificationsOnly_WithUnreadCount_AndAreaName()
    {
        var rig = await RigWithNotificationsAsync();

        var list = await rig.Notifications.ListAsync(Andi, unreadOnly: false, limit: 50);

        Assert.Equal(2, list.Items.Count);
        Assert.Equal(2, list.Unread);
        Assert.All(list.Items, n => Assert.Equal("Rumah", n.AreaName));
        Assert.DoesNotContain(list.Items, n => n.AreaName == "Rumah Budi");
    }

    [Fact]
    public async Task List_IsNewestFirst_AndLimited()
    {
        var rig = Rig.Create();
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon, 10);
        for (int i = 0; i < 5; i++)
        {
            var n = new Notification(Andi, "HS-1", home.Id, NotificationKind.Fire, 1, 0, 1, $"N{i}", "m", DateTime.Now.AddMinutes(-i * 10));
            n.AssignId($"NT-{i}");
            rig.Db.Notifications.Add(n);
        }
        await rig.Db.SaveChangesAsync();

        var list = await rig.Notifications.ListAsync(Andi, false, 3);

        Assert.Equal(new[] { "N0", "N1", "N2" }, list.Items.Select(i => i.Title).ToArray());
        Assert.Equal(5, list.Unread);   // jumlah belum dibaca tidak ikut terpotong oleh batas
    }

    [Fact]
    public async Task MarkRead_UpdatesTheCount_AndUnreadOnlyFilters()
    {
        var rig = await RigWithNotificationsAsync();
        var first = (await rig.Notifications.ListAsync(Andi, false, 50)).Items[0];

        Assert.True(await rig.Notifications.MarkReadAsync(Andi, first.Id));

        var after = await rig.Notifications.ListAsync(Andi, false, 50);
        Assert.Equal(1, after.Unread);
        Assert.True(after.Items.Single(i => i.Id == first.Id).IsRead);
        Assert.Single((await rig.Notifications.ListAsync(Andi, true, 50)).Items);
    }

    [Fact]
    public async Task OtherUsers_CannotTouchSomeoneElsesNotifications()
    {
        var rig = await RigWithNotificationsAsync();
        var andiId = (await rig.Notifications.ListAsync(Andi, false, 50)).Items[0].Id;

        Assert.False(await rig.Notifications.MarkReadAsync(Budi, andiId));
        Assert.False(await rig.Notifications.DeleteAsync(Budi, andiId));
        Assert.Equal(2, (await rig.Notifications.ListAsync(Andi, false, 50)).Unread);   // tidak berubah
        Assert.False(await rig.Notifications.MarkReadAsync(Andi, "NT-TIDAK-ADA"));
    }

    [Fact]
    public async Task MarkAllRead_OnlyAffectsTheCaller()
    {
        var rig = await RigWithNotificationsAsync();

        Assert.Equal(2, await rig.Notifications.MarkAllReadAsync(Andi));

        Assert.Equal(0, (await rig.Notifications.ListAsync(Andi, false, 50)).Unread);
        Assert.Equal(2, (await rig.Notifications.ListAsync(Budi, false, 50)).Unread);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatNotification()
    {
        var rig = await RigWithNotificationsAsync();
        var items = (await rig.Notifications.ListAsync(Andi, false, 50)).Items;

        Assert.True(await rig.Notifications.DeleteAsync(Andi, items[0].Id));

        Assert.Single((await rig.Notifications.ListAsync(Andi, false, 50)).Items);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 1)]
    [InlineData(24, 1)]
    public void FireLevel_FollowsTheArrivalTime(int eta, int expected) => Assert.Equal(expected, NotificationService.FireLevel(eta));

    // ------------------------------------------------------------------ setelah sinkronisasi hotspot

    [Fact]
    public async Task SyncingANearbyHotspot_NotifiesTheUser_AndReportsItInTheResult()
    {
        var rig = Rig.Create();
        var home = await rig.AddAreaAsync(Andi, "Rumah", Lat, Lon + 0.03, 10);

        var options = Samples.Options(firmsKey: "KUNCI-RAHASIA-123", weatherKey: "");
        options.FirmsSources = "S1";
        var firms = new FakeHandler();
        var utcNow = DateTime.UtcNow.AddHours(-1);
        firms.Enqueue(HttpStatusCode.OK, Samples.Csv(
            Samples.ViirsRow(Lat, Lon, time: utcNow.ToString("HHmm"), date: utcNow.ToString("yyyy-MM-dd"))));

        var integrations = new IntegrationOptions();
        var weather = new WeatherClient(new HttpClient(new FakeHandler().Enqueue(HttpStatusCode.OK, Samples.WeatherJson)), options, new IntegrationStatus());
        var predictions = new PredictionService(rig.Db, weather, new FwiPredictionModel(), new ActivityLogService(rig.Db), integrations);
        var notifications = new NotificationService(rig.Db, predictions, new FireSpreadSimulator(), rig.Options);
        var log = new ActivityLogService(rig.Db);
        var sync = new SyncService(new FirmsClient(new HttpClient(firms), options), weather, new HotspotService(rig.Db, log), options, log,
                                   new IntegrationStatus(), notifications: notifications);

        var result = await sync.RunAsync();

        Assert.Equal(1, result.NewHotspots);
        Assert.Contains(result.Messages, m => m.StartsWith("Notifikasi:") && m.Contains("lokasi tersimpan"));
        var mine = await rig.AllAsync(Andi);
        Assert.NotEmpty(mine);
        Assert.All(mine, n => Assert.Equal(home.Id, n.AreaId));
    }

    [Fact]
    public async Task ANotificationFailure_DoesNotFailTheSync()
    {
        var rig = Rig.Create();
        var options = Samples.Options(firmsKey: "KUNCI-RAHASIA-123", weatherKey: "");
        options.FirmsSources = "S1";
        var firms = new FakeHandler().Enqueue(HttpStatusCode.OK, Samples.Csv(Samples.ViirsRow(Lat, Lon)));
        var integrations = new IntegrationOptions();
        var weather = new WeatherClient(new HttpClient(new FakeHandler()), options, new IntegrationStatus());
        var log = new ActivityLogService(rig.Db);

        // DbContext kedua yang sudah dibuang membuat pemeriksaan notifikasi gagal; sinkronisasi harus tetap berhasil.
        var brokenDb = TestDb.Create();
        await brokenDb.DisposeAsync();
        var broken = new NotificationService(brokenDb,
            new PredictionService(brokenDb, weather, new FwiPredictionModel(), new ActivityLogService(brokenDb), integrations),
            new FireSpreadSimulator(), rig.Options);

        var sync = new SyncService(new FirmsClient(new HttpClient(firms), options), weather, new HotspotService(rig.Db, log), options, log,
                                   new IntegrationStatus(), notifications: broken);

        var result = await sync.RunAsync();

        Assert.Equal(ConnectionState.Connected, result.Firms);
        Assert.Contains(result.Messages, m => m.Contains("Pemeriksaan notifikasi gagal"));
    }
}
