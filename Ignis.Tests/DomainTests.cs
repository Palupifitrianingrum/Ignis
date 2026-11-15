using Ignis.Api.Domain;
using Ignis.Shared;
using Xunit;

namespace Ignis.Tests;

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

public class RegionAndAreaTests
{
    [Fact]
    public void Region_Contains()
    {
        var riau = Region.Defaults.First(r => r.Name == "Riau");
        Assert.True(riau.Contains(0.5, 101.4));
        Assert.False(riau.Contains(-7.8, 110.4)); // Yogyakarta
    }

    [Fact]
    public void MonitoringArea_Contains()
    {
        var a = new MonitoringArea { MinLatitude = -1, MaxLatitude = 1, MinLongitude = 100, MaxLongitude = 102 };
        Assert.True(a.Contains(0, 101));
        Assert.False(a.Contains(2, 101));
    }
}
