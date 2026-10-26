using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IgnisAdmin.Models;
namespace IgnisAdmin.Services;

/// <summary>Penyimpanan data in-memory (data contoh). Ganti dengan akses database/backend saat siap.</summary>
public sealed class DataStore
{
    public static DataStore Instance { get; } = new();

    public static readonly List<Region> Regions = new()
    {
        new("Semua daerah", -90, 90, -180, 180),
        new("Riau", -1.2, 2.6, 100.0, 104.6),
        new("Jambi", -2.6, -0.7, 101.0, 104.6),
        new("Sumatera Selatan", -4.9, -1.9, 102.0, 106.0),
        new("Kalimantan Barat", -3.1, 2.1, 108.6, 114.2),
        new("Kalimantan Tengah", -3.6, 0.8, 111.0, 115.8),
        new("DI Yogyakarta", -8.2, -7.5, 110.0, 110.9),
    };

    public ObservableCollection<Hotspot> Hotspots { get; } = new();
    public ObservableCollection<Prediction> Predictions { get; } = new();
    public ObservableCollection<UserAccount> Users { get; } = new();
    public ObservableCollection<string> Activities { get; } = new();
    public AppSettings Settings { get; set; } = AppSettings.Load();

    public DateTime? LastSync { get; set; }
    public string FirmsStatus { get; set; } = "Belum dicek";
    public string WeatherStatus { get; set; } = "Belum dicek";

    public event Action? Changed;
    public event Action? SettingsChanged;
    public event Action<int>? NewHotspots;
    public void Raise() => Changed?.Invoke();
    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    DataStore() { Seed(); }

    static int Num(string id) => int.TryParse(id.Length > 3 ? id[3..] : "", out var n) ? n : 0;
    string NextHotspotId() => $"HS-{(Hotspots.Count == 0 ? 1 : Hotspots.Max(h => Num(h.Id)) + 1):0000}";
    string NextPredictionId() => $"PR-{(Predictions.Count == 0 ? 1 : Predictions.Max(p => Num(p.Id)) + 1):0000}";

    public void Log(string msg)
    {
        Activities.Insert(0, $"{msg} · {DateTime.Now:HH:mm}");
        while (Activities.Count > 30) Activities.RemoveAt(Activities.Count - 1);
    }

    // ---------- Hotspot ----------
    public IEnumerable<Hotspot> Filter(Region? r, HotspotSource? s, HotspotStatus? st) =>
        Hotspots.Where(h => (r == null || r.Contains(h.Latitude, h.Longitude))
                         && (s == null || h.Source == s) && (st == null || h.Status == st));

    public void AddManual(Hotspot h)
    {
        h.Id = NextHotspotId(); h.Source = HotspotSource.Manual;
        Hotspots.Insert(0, h);
        Log($"Admin menambah hotspot manual {h.Id}"); Raise();
    }
    public void UpdateHotspot(Hotspot h)
    {
        var i = Hotspots.IndexOf(h);
        if (i >= 0) Hotspots[i] = h;
        Log($"Admin mengubah hotspot {h.Id}"); Raise();
    }
    public void DeleteHotspot(string id)
    {
        var h = Hotspots.FirstOrDefault(x => x.Id == id);
        if (h == null) return;
        Hotspots.Remove(h); Log($"Admin menghapus hotspot {id}"); Raise();
    }

    public int ImportFirmsCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return 0;
        var head = lines[0].Split(',').ToList();
        int iLat = head.IndexOf("latitude"), iLon = head.IndexOf("longitude");
        int iBr = head.IndexOf("bright_ti4"); if (iBr < 0) iBr = head.IndexOf("brightness");
        int iD = head.IndexOf("acq_date"), iT = head.IndexOf("acq_time");
        int iSat = head.IndexOf("satellite"), iIns = head.IndexOf("instrument"), iC = head.IndexOf("confidence");
        if (iLat < 0 || iLon < 0 || iD < 0 || iT < 0) return 0;
        var inv = CultureInfo.InvariantCulture; int added = 0;
        foreach (var line in lines.Skip(1).Take(500))
        {
            var c = line.Split(',');
            if (c.Length <= Math.Max(iD, iT)) continue;
            if (!double.TryParse(c[iLat], NumberStyles.Float, inv, out var lat) ||
                !double.TryParse(c[iLon], NumberStyles.Float, inv, out var lon)) continue;
            if (!DateTime.TryParse(c[iD], inv, DateTimeStyles.None, out var day)) continue;
            var t = c[iT].PadLeft(4, '0');
            if (!int.TryParse(t[..2], out var hh) || !int.TryParse(t[2..4], out var mm)) continue;
            var when = day.AddHours(hh).AddMinutes(mm);
            if (Hotspots.Any(x => !x.IsManual && x.DetectedAt == when && Math.Abs(x.Latitude - lat) < 1e-5 && Math.Abs(x.Longitude - lon) < 1e-5)) continue;
            double br = 0; if (iBr >= 0) double.TryParse(c[iBr], NumberStyles.Float, inv, out br);
            Hotspots.Insert(0, new Hotspot
            {
                Id = NextHotspotId(), Latitude = lat, Longitude = lon, DetectedAt = when,
                Confidence = iC >= 0 ? ParseConf(c[iC]) : 50, Brightness = br,
                Source = HotspotSource.Satellite, Status = HotspotStatus.Active,
                SatelliteName = iSat >= 0 ? c[iSat] : "", Instrument = iIns >= 0 ? c[iIns] : ""
            });
            added++;
        }
        if (added > 0) { Log($"Sinkronisasi FIRMS: {added} hotspot baru"); Raise(); NewHotspots?.Invoke(added); }
        return added;
    }
    static double ParseConf(string s) => s.Trim().ToLowerInvariant() switch
    {
        "l" => 30, "n" => 60, "h" => 90,
        var v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 50
    };

    // ---------- Cuaca & prediksi (disederhanakan; ganti dengan data API & model sebenarnya) ----------
    public WeatherData GetWeather(Hotspot h)
    {
        var r = new Random(h.Id.Sum(c => (int)c));
        return new WeatherData
        {
            Temperature = Math.Round(29 + r.NextDouble() * 6, 1), Humidity = Math.Round(45 + r.NextDouble() * 40),
            WindSpeed = Math.Round(1.5 + r.NextDouble() * 5, 1), WindDirection = Math.Round(r.NextDouble() * 360),
            Pressure = Math.Round(1004 + r.NextDouble() * 8), Precipitation = 0
        };
    }
    public Prediction? LatestPrediction(string hotspotId) => Predictions.FirstOrDefault(p => p.HotspotId == hotspotId);

    public Prediction Predict(Hotspot h)
    {
        var w = GetWeather(h);
        double speed = Math.Round(w.WindSpeed * 3.6 * 0.02 * (1.3 - w.Humidity / 100), 2); // km/jam
        const int hours = 6; double d = speed * hours;
        double area = Math.Round(0.5 * Math.PI * d * (d / 2.5) * 100, 1); // ha, setengah elips
        var risk = area < 20 ? RiskLevel.Low : area < 80 ? RiskLevel.Medium : area < 250 ? RiskLevel.High : RiskLevel.Critical;
        var p = new Prediction
        {
            Id = NextPredictionId(), HotspotId = h.Id, Direction = (w.WindDirection + 180) % 360,
            SpeedKmH = speed, AreaHa = area, Hours = hours, Risk = risk, CreatedAt = DateTime.Now
        };
        Predictions.Insert(0, p); Log($"Prediksi {p.Id} dibuat untuk {h.Id}"); Raise();
        return p;
    }

    // ---------- Pengguna ----------
    public string? ToggleUser(string id)
    {
        var i = Users.ToList().FindIndex(u => u.Id == id);
        if (i < 0) return "Pengguna tidak ditemukan.";
        var u = Users[i];
        if (u.IsActive && u.Role == "Admin" && Users.Count(x => x.Role == "Admin" && x.IsActive) <= 1)
            return "Tidak bisa menonaktifkan satu-satunya admin aktif.";
        u.IsActive = !u.IsActive; Users[i] = u;
        Log($"{u.Name} {(u.IsActive ? "diaktifkan" : "dinonaktifkan")}"); Raise();
        return null;
    }

    void Seed()
    {
        Hotspot S(string id, double la, double lo, int h, int m, double c, double b, HotspotStatus st, string sat = "Suomi NPP") =>
            new() { Id = id, Latitude = la, Longitude = lo, DetectedAt = new DateTime(2026, 10, 24, h, m, 0), Confidence = c, Brightness = b, Source = HotspotSource.Satellite, Status = st, SatelliteName = sat, Instrument = "VIIRS" };
        Hotspots.Add(S("HS-0142", -0.3912, 102.0614, 9, 15, 87, 342.5, HotspotStatus.Active));
        Hotspots.Add(S("HS-0141", -2.9500, 104.7000, 8, 40, 75, 318.0, HotspotStatus.Monitoring));
        Hotspots.Add(S("HS-0140", -1.6101, 103.6131, 7, 55, 92, 361.2, HotspotStatus.Active));
        Hotspots.Add(S("HS-0139", 0.5100, 101.4400, 7, 20, 68, 330.4, HotspotStatus.Active, "NOAA-20"));
        Hotspots.Add(new Hotspot { Id = "HS-0138", Latitude = -2.9871, Longitude = 104.6320, DetectedAt = new DateTime(2026, 10, 24, 6, 30, 0), Confidence = 75, Source = HotspotSource.Manual, Status = HotspotStatus.Active, ReportedBy = "Petugas Lapangan Banyuasin", Note = "Asap tebal, titik api terhalang awan." });
        Hotspots.Add(S("HS-0137", -2.2161, 113.9135, 5, 45, 81, 349.9, HotspotStatus.Active));
        Hotspots.Add(S("HS-0136", -0.0263, 109.3425, 5, 10, 55, 311.7, HotspotStatus.Monitoring, "NOAA-20"));
        Hotspots.Add(S("HS-0135", -3.3200, 104.9100, 4, 30, 90, 355.0, HotspotStatus.Resolved));
        Hotspots.Add(S("HS-0134", -2.6000, 114.2000, 3, 50, 73, 322.6, HotspotStatus.Active));
        Hotspots.Add(new Hotspot { Id = "HS-0129", Latitude = -1.5500, Longitude = 103.5000, DetectedAt = new DateTime(2026, 10, 22, 15, 20, 0), Confidence = 80, Source = HotspotSource.Manual, Status = HotspotStatus.Resolved, ReportedBy = "Relawan Desa Muaro Jambi", Note = "Kebakaran lahan gambut, warga melapor." });

        // urut terbaru di atas
        Predictions.Add(new() { Id = "PR-0056", HotspotId = "HS-0136", Direction = 90, SpeedKmH = 0.07, AreaHa = 8.2, Hours = 12, Risk = RiskLevel.Low, CreatedAt = new DateTime(2026, 10, 24, 9, 20, 0) });
        Predictions.Add(new() { Id = "PR-0055", HotspotId = "HS-0141", Direction = 200, SpeedKmH = 0.16, AreaHa = 35.4, Hours = 8, Risk = RiskLevel.Medium, CreatedAt = new DateTime(2026, 10, 24, 9, 10, 0) });
        Predictions.Add(new() { Id = "PR-0054", HotspotId = "HS-0140", Direction = 315, SpeedKmH = 0.30, AreaHa = 124.5, Hours = 6, Risk = RiskLevel.High, CreatedAt = new DateTime(2026, 10, 24, 8, 45, 0) });
        Predictions.Add(new() { Id = "PR-0053", HotspotId = "HS-0142", Direction = 270, SpeedKmH = 0.55, AreaHa = 410.0, Hours = 3, Risk = RiskLevel.Critical, CreatedAt = new DateTime(2026, 10, 24, 8, 30, 0) });

        Users.Add(new() { Id = "U-001", Name = "Admin Ignis", Email = "admin@ignis.example", Role = "Admin" });
        Users.Add(new() { Id = "U-002", Name = "Andi Pratama", Email = "andi.pratama@example.com" });
        Users.Add(new() { Id = "U-003", Name = "Siti Rahma", Email = "siti.rahma@example.com", IsActive = false });
        Users.Add(new() { Id = "U-004", Name = "Budi Santoso", Email = "budi.santoso@example.com" });
        Users.Add(new() { Id = "U-005", Name = "Rina Wulandari", Email = "rina.w@example.com", Role = "Admin" });

        Activities.Add("Andi Pratama login · 08:05");
        Activities.Add("Admin Ignis menambah hotspot manual HS-0138 · 06:31");
        Activities.Add("Siti Rahma menambah wilayah pemantauan Riau · 06:10");
    }
}

public static class Ui
{
    public static Brush StatusBrush(string s)
    {
        var key = s == "Terhubung" ? "Green" : (s is "Belum dicek" or "Menyinkronkan..." or "API key belum diisi") ? "Muted" : "Red";
        return (Brush)Application.Current.FindResource(key);
    }
}
