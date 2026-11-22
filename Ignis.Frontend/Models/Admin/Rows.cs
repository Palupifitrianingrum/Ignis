using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ignis.Shared;

namespace Ignis.Frontend.Models.Admin;

/// <summary>Pemformat teks tampilan (budaya Indonesia).</summary>
public static class Fmt
{
    private static readonly CultureInfo Id = new("id-ID");
    public static string Date(DateTime d) => d.ToString("dd MMM yyyy, HH:mm", Id);
    public static string Inv(double v, string f = "0.0000") => v.ToString(f, CultureInfo.InvariantCulture);
}

// Baris tampilan atas DTO dari backend. XAML (DataGrid) mem-bind properti *Text, jadi teks/format UI ditaruh di sini,
// bukan di entity domain maupun DTO.

public sealed partial class HotspotRow : ObservableObject
{
    public HotspotDto Dto { get; }
    public HotspotRow(HotspotDto dto) => Dto = dto;

    [ObservableProperty] private bool isSelected;

    public string Id => Dto.Id;
    public double Latitude => Dto.Latitude;
    public double Longitude => Dto.Longitude;
    public DateTime DetectedAt => Dto.DetectedAt;
    public double Confidence => Dto.Confidence;
    public double Brightness => Dto.Brightness;
    public HotspotStatus Status => Dto.Status;
    public HotspotSource Source => Dto.Source;

    public bool IsManual => Dto.Source == HotspotSource.Manual;
    public bool IsEditable => Dto.IsEditable;
    public string ReportedBy => Dto.ReportedBy ?? string.Empty;
    public string Note => Dto.Note ?? string.Empty;

    public string CoordText => $"{Fmt.Inv(Latitude)}, {Fmt.Inv(Longitude)}";
    public string SourceText => IsManual ? "Manual" : "Satelit";
    public string SourceDetailText => Dto.Details;   // dari Hotspot.GetDetails() di backend (polimorfik)
    public string StatusText => Status switch
    {
        HotspotStatus.Active => "Aktif",
        HotspotStatus.Monitoring => "Dipantau",
        _ => "Selesai"
    };
    public string DetectedText => Fmt.Date(DetectedAt);
    public string ConfidenceText => $"{Confidence:0}%";
    public string BrightnessText => Brightness > 0 ? $"{Brightness:0.0} K" : "-";
}

public sealed class PredictionRow
{
    public PredictionDto Dto { get; }
    public PredictionRow(PredictionDto dto) => Dto = dto;

    public string Id => Dto.Id;
    public string HotspotId => Dto.HotspotId;
    public double Direction => Dto.Direction;
    public double SpeedKmH => Dto.SpeedKmH;
    public double AreaHa => Dto.AreaHa;
    public double Hours => Dto.Hours;
    public RiskLevel Risk => Dto.Risk;
    public DateTime CreatedAt => Dto.CreatedAt;

    public string DirectionText => $"{Direction:0}°";
    public string SpeedText => $"{SpeedKmH:0.00} km/jam";
    public string AreaText => $"{AreaHa:0.0} ha";
    public string HoursText => $"{Hours:0} jam";
    public string RiskText => Risk.ToString().ToUpperInvariant();
    public string CreatedText => Fmt.Date(CreatedAt);
}

public sealed class UserRow
{
    public UserDto Dto { get; }
    public UserRow(UserDto dto) => Dto = dto;

    public string Id => Dto.Id;
    public string Name => Dto.Name;
    public string Email => Dto.Email;
    public bool IsActive => Dto.IsActive;
    public bool IsAdmin => Dto.Role == Ignis.Shared.Role.Admin;

    /// <summary>Label peran untuk tampilan ("Admin" | "Terdaftar").</summary>
    public string Role => IsAdmin ? "Admin" : "Terdaftar";
    public string StatusText => IsActive ? "Aktif" : "Non-aktif";
}
