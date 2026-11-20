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

public sealed record SimulationFrameDto(
    int HourOffset,
    IReadOnlyList<CoordinateDto> BurningCells,
    double AreaHa,
    int NewCells);

/// <summary>Hasil simulasi penyebaran api untuk satu hotspot: kondisi yang dipakai, ukuran sel, dan frame per jam.</summary>
/// <param name="SpreadDirection">Arah rambatan api (derajat, 0 = utara, 90 = timur).</param>
/// <param name="WindFromDirection">Arah asal angin (derajat, 270 = angin dari barat).</param>
/// <param name="WindSpeed">Kecepatan angin (m/s, satuan OpenWeather).</param>
/// <param name="CellSizeDegrees">Sisi satu sel dalam derajat; sel digambar sebagai kotak [lat ± sisi/2, lon ± sisi/2].</param>
/// <param name="Truncated">True bila model mencapai batas sel maksimum sehingga luas tidak bertambah lagi.</param>
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
    IReadOnlyList<SimulationFrameDto> Frames);

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

// ---------- Autentikasi ----------

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Name, string Email, string Password);

/// <summary>Klien mengirim kode otorisasi Google (alur PKCE + loopback); server menukarnya jadi identitas pengguna.</summary>
public sealed record GoogleLoginRequest(string Code, string CodeVerifier, string RedirectUri);

/// <summary>Jawaban login/registrasi: token JWT (kirim sebagai "Authorization: Bearer ...") dan profil pengguna.</summary>
public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

/// <summary>Client ID Google untuk membangun URL login di klien (bukan rahasia). Kosong = login Google belum dikonfigurasi.</summary>
public sealed record GoogleConfigDto(string? ClientId);
