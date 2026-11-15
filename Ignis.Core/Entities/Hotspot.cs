using Ignis.Core.Common;
using Ignis.Core.Enums;
using Ignis.Core.ValueObjects;

namespace Ignis.Core.Entities;

/// <summary>
/// Titik api. Kelas abstrak (INHERITANCE): <see cref="SatelliteHotspot"/> dan <see cref="ManualHotspot"/> menurunkannya.
///
/// ENCAPSULATION: semua field bersetter privat. Nilai hanya berubah lewat konstruktor/method yang memvalidasi
/// invarian (lat/lon, confidence, dsb.), sehingga objek Hotspot tidak mungkin berada dalam keadaan tidak valid.
///
/// POLYMORPHISM: <see cref="IsEditable"/> dan <see cref="GetDetails"/> abstrak, tiap subclass memberi perilaku sendiri;
/// kode pemanggil cukup memperlakukan semuanya sebagai Hotspot.
/// </summary>
public abstract class Hotspot
{
    /// <summary>Dipakai EF Core (materialisasi) dan subclass.</summary>
    protected Hotspot(HotspotSource source) => Source = source;

    protected Hotspot(HotspotSource source, double latitude, double longitude, double confidence,
                      double brightness, DateTime detectedAt, HotspotStatus status) : this(source)
    {
        Guard.Location(latitude, longitude);
        Guard.Confidence(confidence);
        if (double.IsNaN(brightness) || brightness < 0) throw new DomainException("Brightness tidak boleh negatif.");

        Latitude = latitude;
        Longitude = longitude;
        Confidence = confidence;
        Brightness = brightness;
        DetectedAt = detectedAt;
        ChangeStatus(status);
    }

    public string Id { get; private set; } = string.Empty;
    public HotspotSource Source { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public DateTime DetectedAt { get; protected set; }
    public double Confidence { get; private set; }
    public double Brightness { get; private set; }
    public HotspotStatus Status { get; private set; }

    /// <summary>Id hanya boleh ditetapkan sekali (oleh repository saat disimpan).</summary>
    public void AssignId(string id)
    {
        if (!string.IsNullOrEmpty(Id)) throw new DomainException("Id hotspot sudah ditetapkan.");
        Guard.NotBlank(id, "Id hotspot tidak boleh kosong.");
        Id = id;
    }

    public void ChangeStatus(HotspotStatus status)
    {
        if (!Enum.IsDefined(status)) throw new DomainException("Status hotspot tidak dikenal.");
        Status = status;
    }

    public Coordinate GetLocation() => new(Latitude, Longitude);

    /// <summary>Ubah posisi, keyakinan, dan waktu sekaligus (divalidasi lebih dulu, baru diterapkan).</summary>
    protected void ApplyMeasurement(double latitude, double longitude, double confidence, DateTime detectedAt)
    {
        Guard.Location(latitude, longitude);
        Guard.Confidence(confidence);
        Latitude = latitude;
        Longitude = longitude;
        Confidence = confidence;
        DetectedAt = detectedAt;
    }

    /// <summary>Apakah admin boleh mengubah/menghapus hotspot ini? (polimorfik)</summary>
    public abstract bool IsEditable { get; }

    /// <summary>Deskripsi sumber hotspot untuk ditampilkan. (polimorfik)</summary>
    public abstract string GetDetails();
}

/// <summary>Hotspot hasil deteksi satelit (NASA FIRMS). Data dari API, tidak boleh diubah admin.</summary>
public class SatelliteHotspot : Hotspot
{
    private SatelliteHotspot() : base(HotspotSource.Satellite) { }   // untuk EF Core

    public SatelliteHotspot(double latitude, double longitude, double confidence, double brightness,
                            DateTime scanTime, string satelliteName, string instrument,
                            HotspotStatus status = HotspotStatus.Active)
        : base(HotspotSource.Satellite, latitude, longitude, confidence, brightness, scanTime, status)
    {
        Guard.NotBlank(satelliteName, "Nama satelit wajib diisi.");
        Guard.NotBlank(instrument, "Instrumen wajib diisi.");
        SatelliteName = satelliteName.Trim();
        Instrument = instrument.Trim();
        ScanTime = scanTime;
    }

    public string SatelliteName { get; private set; } = string.Empty;
    public string Instrument { get; private set; } = string.Empty;
    public DateTime ScanTime { get; private set; }

    public override bool IsEditable => false;
    public override string GetDetails() => $"Satelit ({SatelliteName} · {Instrument})";
}

/// <summary>Hotspot hasil laporan manual (petugas/relawan), diinput dan dikelola admin.</summary>
public class ManualHotspot : Hotspot
{
    private ManualHotspot() : base(HotspotSource.Manual) { }   // untuk EF Core

    public ManualHotspot(double latitude, double longitude, double confidence, DateTime incidentTime,
                         string reportedBy, string? note = null, HotspotStatus status = HotspotStatus.Active)
        : base(HotspotSource.Manual, latitude, longitude, confidence, 0, incidentTime, status)
    {
        Guard.NotBlank(reportedBy, "Pelapor / sumber laporan wajib diisi.");
        ReportedBy = reportedBy.Trim();
        Note = (note ?? string.Empty).Trim();
        IncidentTime = incidentTime;
    }

    public string ReportedBy { get; private set; } = string.Empty;
    public string Note { get; private set; } = string.Empty;
    public DateTime IncidentTime { get; private set; }

    public override bool IsEditable => true;
    public override string GetDetails() => $"Manual ({ReportedBy})";

    /// <summary>Ubah isi laporan. Semua input divalidasi dulu supaya tidak ada perubahan setengah jalan.</summary>
    public void UpdateReport(double latitude, double longitude, double confidence, DateTime incidentTime,
                             string reportedBy, string? note, HotspotStatus status)
    {
        Guard.NotBlank(reportedBy, "Pelapor / sumber laporan wajib diisi.");
        ApplyMeasurement(latitude, longitude, confidence, incidentTime);   // melempar bila tidak valid
        ChangeStatus(status);
        IncidentTime = incidentTime;
        ReportedBy = reportedBy.Trim();
        Note = (note ?? string.Empty).Trim();
    }
}
