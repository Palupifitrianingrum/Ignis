using System.Globalization;
using Ignis.Api.Domain;

namespace Ignis.Api.Services;

/// <summary>Pengaturan notifikasi (bagian "Notifications" di appsettings atau env Notifications__...).</summary>
public sealed class NotificationOptions
{
    /// <summary>Berapa jam ke depan simulasi diperiksa terhadap lokasi pengguna (maksimum 24, batas simulasi).</summary>
    public int HorizonHours { get; set; } = 12;

    /// <summary>Notifikasi sejenis untuk lokasi yang sama tidak dikirim lagi selama ini (jam), kecuali tingkatnya naik.</summary>
    public int CooldownHours { get; set; } = 6;

    /// <summary>Hotspot yang lebih jauh dari lokasi daripada ini (km) diabaikan. Domain asap model ±61 km, jadi nilai lebih besar tidak berguna.</summary>
    public double CandidateRadiusKm { get; set; } = 70;

    /// <summary>Paling banyak berapa hotspot yang disimulasikan per lokasi (yang terdekat dari tiap gugus).</summary>
    public int MaxCandidatesPerArea { get; set; } = 4;

    /// <summary>Hotspot yang berjarak kurang dari ini (km) dianggap satu gugus kebakaran yang sama dan hanya yang terdekat disimulasikan.</summary>
    public double ClusterSeparationKm { get; set; } = 5;

    /// <summary>Hotspot satelit yang lebih tua dari ini (jam) dianggap sudah tidak relevan. Hotspot manual yang belum selesai selalu dipakai.</summary>
    public int SatelliteMaxAgeHours { get; set; } = 36;

    /// <summary>Batas jumlah lokasi tersimpan per pengguna.</summary>
    public int MaxAreasPerUser { get; set; } = 10;
}

/// <summary>Teks notifikasi (bahasa Indonesia). Memuat angka dari simulasi dan selalu menyebut bahwa ini perkiraan.</summary>
internal static class NotificationText
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static string LevelWord(int level) => level switch { 3 => "Bahaya", 2 => "Waspada", _ => "Perhatian" };

    public static string SmokeLabel(int level) => level switch { 3 => "pekat", 2 => "sedang", _ => "ringan" };

    private static string Km(double value) => value.ToString("0.#", Id);

    private static string When(int etaHours) => etaHours == 0 ? "saat ini" : $"dalam ±{etaHours} jam";

    private static string Wind(SimulationResult sim) =>
        $"Angin dari {Geo.CompassName(sim.WindFromDirection)} ({sim.WindFromDirection:0}°) {sim.WindSpeed.ToString("0.#", Id)} m/s";

    private static string Footer(SimulationResult sim) =>
        "Ini perkiraan simulasi, bukan prakiraan resmi" + (string.Equals(sim.Condition, "Simulasi", StringComparison.OrdinalIgnoreCase) ? " (memakai data cuaca simulasi)." : ".");

    public static (string Title, string Message) Fire(MonitoringArea area, Hotspot hotspot, AreaExposure e, SimulationResult sim, int horizon, int level)
    {
        var title = level switch
        {
            3 => $"Bahaya: api dekat \"{area.Name}\"",
            2 => $"Waspada: api mendekati \"{area.Name}\"",
            _ => $"Perhatian: api diperkirakan mendekati \"{area.Name}\""
        };

        var eta = e.FireEtaHours ?? 0;
        var core = eta == 0
            ? $"Hotspot {hotspot.Id} berjarak ±{Km(e.HotspotDistanceKm)} km dari \"{area.Name}\". Menurut simulasi, api sudah berada dalam radius {Km(area.Radius)} km dari lokasi Anda (jarak terdekat ±{Km(e.FireMinDistanceKm)} km)."
            : $"Hotspot {hotspot.Id} berjarak ±{Km(e.HotspotDistanceKm)} km dari \"{area.Name}\". Menurut simulasi, api diperkirakan masuk radius {Km(area.Radius)} km {When(eta)} (jarak terdekat ±{Km(e.FireMinDistanceKm)} km dalam {horizon} jam ke depan).";

        return (title, $"{core} {Wind(sim)}. {Footer(sim)}");
    }

    public static (string Title, string Message) Smoke(MonitoringArea area, Hotspot hotspot, AreaExposure e, SimulationResult sim, int horizon)
    {
        var label = SmokeLabel(e.SmokeMaxLevel);
        var title = $"Asap {label} diperkirakan sampai ke \"{area.Name}\"";
        var message =
            $"Asap dari hotspot {hotspot.Id} (±{Km(e.HotspotDistanceKm)} km dari \"{area.Name}\") diperkirakan mencapai radius {Km(area.Radius)} km {When(e.SmokeEtaHours ?? 0)}, " +
            $"dengan kepekatan hingga {label} dalam {horizon} jam ke depan. {Wind(sim)}. {Footer(sim)}";
        return (title, message);
    }
}
