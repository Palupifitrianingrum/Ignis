namespace Ignis.Shared;

// Bentuk data yang dikirim antara API dan klien (WPF). Dipakai bersama supaya JSON tidak melenceng.
// Entity domain TIDAK dikirim langsung: setter privat dan hierarki kelasnya tidak cocok untuk JSON.

// ---------- Hotspot ----------

/// <summary>Hotspot untuk klien. Kolom khusus subclass bernilai null bila tidak berlaku.</summary>
public sealed record HotspotDto(
    string Id,
    HotspotSource Source,
    double Latitude,
    double Longitude,
    DateTime DetectedAt,
    double Confidence,
    double Brightness,
    HotspotStatus Status,
    string Details,          // hasil Hotspot.GetDetails() (polimorfik)
    bool IsEditable,         // hasil Hotspot.IsEditable (polimorfik)
    string? SatelliteName,
    string? Instrument,
    DateTime? ScanTime,
    string? ReportedBy,
    string? Note,
    DateTime? IncidentTime);

/// <summary>Input tambah/ubah hotspot manual.</summary>
public sealed record ManualHotspotRequest(
    double Latitude,
    double Longitude,
    double Confidence,
    DateTime IncidentTime,
    string ReportedBy,
    string? Note,
    HotspotStatus Status);

// ---------- Cuaca & prediksi ----------

public sealed record WeatherDto(
    string Id,
    string HotspotId,
    double Latitude,
    double Longitude,
    DateTime Timestamp,
    double Temperature,
    double Humidity,
    double WindSpeed,
    double WindDirection,
    double Pressure,
    double Precipitation,
    string Condition);

public sealed record PredictionDto(
    string Id,
    string HotspotId,
    string? WeatherId,
    double Direction,
    double SpeedKmH,
    double AreaHa,
    double Hours,
    RiskLevel Risk,
    DateTime CreatedAt);

public sealed record CoordinateDto(double Latitude, double Longitude);

/// <summary>Sel asap pada grid kasar: lintang = asal + Row × SmokeCellSizeDegrees, bujur = asal + Col × SmokeCellSizeDegrees. Level 1 ringan, 2 sedang, 3 pekat.</summary>
public sealed record SmokeCellDto(int Row, int Col, int Level);

public sealed record SimulationFrameDto(
    int HourOffset,
    IReadOnlyList<CoordinateDto> BurningCells,
    double AreaHa,
    int NewCells,
    IReadOnlyList<SmokeCellDto> SmokeCells);

/// <summary>Hasil simulasi penyebaran api untuk satu hotspot: kondisi yang dipakai, ukuran sel, dan frame per jam.</summary>
/// <param name="SpreadDirection">Arah rambatan api (derajat, 0 = utara, 90 = timur).</param>
/// <param name="WindFromDirection">Arah asal angin (derajat, 270 = angin dari barat).</param>
/// <param name="WindSpeed">Kecepatan angin (m/s, satuan OpenWeather).</param>
/// <param name="CellSizeDegrees">Sisi satu sel dalam derajat; sel digambar sebagai kotak [lat ± sisi/2, lon ± sisi/2].</param>
/// <param name="Truncated">True bila model mencapai batas sel maksimum sehingga luas tidak bertambah lagi.</param>
/// <param name="SmokeCellSizeDegrees">Sisi sel asap dalam derajat (sel kasar, lima kali sel api).</param>
/// <param name="SmokeMaxLevel">Level asap tertinggi selama simulasi: 0 tidak ada, 1 ringan, 2 sedang, 3 pekat.</param>
public sealed record SimulationDto(
    string HotspotId,
    double OriginLatitude,
    double OriginLongitude,
    int Hours,
    double SpreadDirection,
    double WindFromDirection,
    double WindSpeed,
    double Temperature,
    double Humidity,
    double Precipitation,
    string Condition,
    double CellSizeDegrees,
    double CellAreaHa,
    int MaxCells,
    bool Truncated,
    double SmokeCellSizeDegrees,
    int SmokeMaxLevel,
    IReadOnlyList<SimulationFrameDto> Frames,
    IReadOnlyList<AreaImpactDto> Impacts);

// ---------- Pengguna & sinkronisasi ----------

/// <summary>Pengguna untuk klien — sengaja tanpa PasswordHash.</summary>
public sealed record UserDto(
    string Id,
    string Name,
    string Email,
    Role Role,
    bool IsActive,
    DateTime CreatedAt);

public sealed record SyncResult(
    IReadOnlyList<string> Messages,
    int NewHotspots,
    ConnectionState Firms,
    ConnectionState Weather,
    DateTime FinishedAt);

/// <summary>Status satu layanan luar (hasil pemeriksaan langsung). Pesan tidak pernah memuat API key.</summary>
public sealed record ProviderStatusDto(string Name, bool KeyConfigured, ConnectionState State, string Message);

/// <summary>
/// Keadaan API key satu layanan luar untuk halaman Pengaturan. Key lengkap TIDAK PERNAH dikirim ke desktop;
/// hanya <paramref name="MaskedKey"/> (mis. "••••a3f9"). Prioritas key yang berlaku: override admin, lalu .env, lalu kosong.
/// </summary>
/// <param name="Provider">"firms" atau "weather".</param>
/// <param name="Source">Asal key yang sedang berlaku.</param>
/// <param name="HasOverride">True bila ada override tersimpan (tombol "Kembalikan ke default" relevan).</param>
/// <param name="Warning">Peringatan, mis. override tersimpan tetapi tidak dapat dibaca karena kunci enkripsi server berubah.</param>
public sealed record IntegrationKeyDto(
    string Provider,
    string Name,
    KeySource Source,
    string? MaskedKey,
    bool HasOverride,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    string? Warning);

public sealed record SetIntegrationKeyRequest(string Key);

/// <summary>Uji koneksi: <paramref name="Key"/> kosong berarti menguji key yang sedang berlaku.</summary>
public sealed record TestIntegrationKeyRequest(string? Key);

// ---------- Lokasi tersimpan dan notifikasi ----------

/// <summary>Lokasi tersimpan pengguna (mis. rumah): titik pusat dan radius peringatan.</summary>
public sealed record MonitoringAreaDto(string Id, string Name, double Latitude, double Longitude, double RadiusKm);

public sealed record MonitoringAreaRequest(string Name, double Latitude, double Longitude, double RadiusKm);

/// <summary>
/// Dampak sebuah simulasi pada satu lokasi tersimpan milik pengguna yang melihatnya.
/// <paramref name="FireEtaHours"/>/<paramref name="SmokeEtaHours"/> null berarti api/asap tidak mencapai radius selama simulasi; 0 berarti sudah di dalam radius.
/// </summary>
/// <param name="HotspotDistanceKm">Jarak lokasi ke titik hotspot yang disimulasikan.</param>
/// <param name="FireMinDistanceKm">Jarak terdekat tepi api ke lokasi selama simulasi.</param>
/// <param name="SmokeMaxLevel">Level asap tertinggi di dalam radius selama simulasi (0 tidak ada, 1 ringan, 2 sedang, 3 pekat).</param>
public sealed record AreaImpactDto(
    string AreaId,
    string AreaName,
    double HotspotDistanceKm,
    int? FireEtaHours,
    double FireMinDistanceKm,
    int SmokeMaxLevel,
    int? SmokeEtaHours);

/// <param name="Level">1 perhatian, 2 waspada, 3 bahaya.</param>
/// <param name="EtaHours">Perkiraan jam sampai api/asap mencapai radius lokasi (0 = sudah).</param>
public sealed record NotificationDto(
    string Id,
    string HotspotId,
    string? AreaId,
    string? AreaName,
    NotificationKind Kind,
    int Level,
    int EtaHours,
    double DistanceKm,
    string Title,
    string Message,
    DateTime CreatedAt,
    bool IsRead);

public sealed record NotificationListDto(int Unread, IReadOnlyList<NotificationDto> Items);

/// <summary>Ringkasan satu putaran pemeriksaan notifikasi.</summary>
public sealed record CheckResultDto(int AreasChecked, int HotspotsSimulated, int NotificationsCreated);

// ---------- Autentikasi ----------

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Name, string Email, string Password);

/// <summary>Klien mengirim kode otorisasi Google (alur PKCE + loopback); server menukarnya jadi identitas pengguna.</summary>
public sealed record GoogleLoginRequest(string Code, string CodeVerifier, string RedirectUri);

/// <summary>Jawaban login/registrasi: token JWT (kirim sebagai "Authorization: Bearer ...") dan profil pengguna.</summary>
public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

/// <summary>Client ID Google untuk membangun URL login di klien (bukan rahasia). Kosong = login Google belum dikonfigurasi.</summary>
public sealed record GoogleConfigDto(string? ClientId);
