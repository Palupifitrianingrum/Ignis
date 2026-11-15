using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.InMemory;

/// <summary>Data contoh untuk mode demo dan seed awal database (dipindahkan dari DataStore.Seed). Bukan data produksi.</summary>
public static class DemoData
{
    public static List<Hotspot> Hotspots()
    {
        Hotspot S(string id, double la, double lo, int h, int m, double c, double b, HotspotStatus st, string sat = "Suomi NPP")
        {
            var hs = new SatelliteHotspot(la, lo, c, b, new DateTime(2026, 10, 24, h, m, 0), sat, "VIIRS", st);
            hs.AssignId(id);
            return hs;
        }

        Hotspot M(string id, double la, double lo, DateTime when, double c, HotspotStatus st, string by, string note)
        {
            var hs = new ManualHotspot(la, lo, c, when, by, note, st);
            hs.AssignId(id);
            return hs;
        }

        return new List<Hotspot>
        {
            S("HS-0142", -0.3912, 102.0614, 9, 15, 87, 342.5, HotspotStatus.Active),
            S("HS-0141", -2.9500, 104.7000, 8, 40, 75, 318.0, HotspotStatus.Monitoring),
            S("HS-0140", -1.6101, 103.6131, 7, 55, 92, 361.2, HotspotStatus.Active),
            S("HS-0139", 0.5100, 101.4400, 7, 20, 68, 330.4, HotspotStatus.Active, "NOAA-20"),
            M("HS-0138", -2.9871, 104.6320, new DateTime(2026, 10, 24, 6, 30, 0), 75, HotspotStatus.Active, "Petugas Lapangan Banyuasin", "Asap tebal, titik api terhalang awan."),
            S("HS-0137", -2.2161, 113.9135, 5, 45, 81, 349.9, HotspotStatus.Active),
            S("HS-0136", -0.0263, 109.3425, 5, 10, 55, 311.7, HotspotStatus.Monitoring, "NOAA-20"),
            S("HS-0135", -3.3200, 104.9100, 4, 30, 90, 355.0, HotspotStatus.Resolved),
            S("HS-0134", -2.6000, 114.2000, 3, 50, 73, 322.6, HotspotStatus.Active),
            M("HS-0129", -1.5500, 103.5000, new DateTime(2026, 10, 22, 15, 20, 0), 80, HotspotStatus.Resolved, "Relawan Desa Muaro Jambi", "Kebakaran lahan gambut, warga melapor."),
        };
    }

    /// <summary>Urut terbaru di atas. Tanpa baris cuaca (WeatherId null).</summary>
    public static List<Prediction> Predictions() => new()
    {
        new() { Id = "PR-0056", HotspotId = "HS-0136", Direction = 90, SpeedKmH = 0.07, AreaHa = 8.2, Hours = 12, Risk = RiskLevel.Low, CreatedAt = new DateTime(2026, 10, 24, 9, 20, 0) },
        new() { Id = "PR-0055", HotspotId = "HS-0141", Direction = 200, SpeedKmH = 0.16, AreaHa = 35.4, Hours = 8, Risk = RiskLevel.Medium, CreatedAt = new DateTime(2026, 10, 24, 9, 10, 0) },
        new() { Id = "PR-0054", HotspotId = "HS-0140", Direction = 315, SpeedKmH = 0.30, AreaHa = 124.5, Hours = 6, Risk = RiskLevel.High, CreatedAt = new DateTime(2026, 10, 24, 8, 45, 0) },
        new() { Id = "PR-0053", HotspotId = "HS-0142", Direction = 270, SpeedKmH = 0.55, AreaHa = 410.0, Hours = 3, Risk = RiskLevel.Critical, CreatedAt = new DateTime(2026, 10, 24, 8, 30, 0) },
    };

    // Catatan: password hash dikosongkan karena login sungguhan belum diimplementasikan (lihat TODO di AuthService).
    public static List<User> Users()
    {
        var siti = new RegisteredUser("U-003", "Siti Rahma", "siti.rahma@example.com", "");
        siti.Deactivate();
        return new List<User>
        {
            new Admin("U-001", "Admin Ignis", "admin@ignis.example", ""),
            new RegisteredUser("U-002", "Andi Pratama", "andi.pratama@example.com", ""),
            siti,
            new RegisteredUser("U-004", "Budi Santoso", "budi.santoso@example.com", ""),
            new Admin("U-005", "Rina Wulandari", "rina.w@example.com", ""),
        };
    }

    public static List<string> Activities() => new()
    {
        "Andi Pratama login · 08:05",
        "Admin Ignis menambah hotspot manual HS-0138 · 06:31",
        "Siti Rahma menambah wilayah pemantauan Riau · 06:10",
    };
}
