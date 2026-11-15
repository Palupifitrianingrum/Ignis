using Ignis.Core.Enums;

namespace Ignis.Core.Contracts;

// Kontrak data antara API dan klien (WPF). Dipakai bersama supaya bentuk JSON tidak melenceng.
// Entity domain TIDAK dikirim langsung: setter privat/enkapsulasi dan hierarki kelas tidak cocok untuk JSON.

/// <summary>Representasi hotspot untuk klien. Kolom khusus subclass bernilai null bila tidak berlaku.</summary>
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

/// <summary>Pengguna untuk klien — sengaja tanpa PasswordHash.</summary>
public sealed record UserDto(
    string Id,
    string Name,
    string Email,
    Role Role,
    bool IsActive,
    DateTime CreatedAt);
