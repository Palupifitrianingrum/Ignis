using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Ignis.Shared;

namespace Ignis.Frontend.Models;

/// <summary>Lokasi tersimpan pengguna (mis. rumah) untuk daftar di sidebar dan penanda di peta, beserta status dampak simulasi yang sedang dilihat.</summary>
public partial class SavedLocationItem : ObservableObject
{
    public SavedLocationItem(MonitoringAreaDto dto)
    {
        Id = dto.Id;
        Name = dto.Name;
        Latitude = dto.Latitude;
        Longitude = dto.Longitude;
        RadiusKm = dto.RadiusKm;
    }

    public string Id { get; }
    public string Name { get; }
    public double Latitude { get; }
    public double Longitude { get; }
    public double RadiusKm { get; }

    public string Subtitle => FormattableString.Invariant($"Radius {RadiusKm:0.#} km  ·  {Latitude:0.000}, {Longitude:0.000}");

    /// <summary>Ringkasan dampak simulasi hotspot yang sedang dipilih untuk lokasi ini. Kosong bila belum ada simulasi.</summary>
    [ObservableProperty] private string statusText = string.Empty;

    [ObservableProperty] private string statusColorHex = "#94A3B8";

    [ObservableProperty] private bool hasStatus;

    /// <summary>Jumlah notifikasi belum dibaca untuk lokasi ini.</summary>
    [ObservableProperty] private int unreadCount;

    public bool HasUnread => UnreadCount > 0;

    partial void OnUnreadCountChanged(int value) => OnPropertyChanged(nameof(HasUnread));

    /// <summary>Terapkan dampak simulasi: api dan asap, dengan warna menurut yang terburuk.</summary>
    public void ApplyImpact(string hotspotId, AreaImpactDto impact)
    {
        var parts = new System.Collections.Generic.List<string>();
        int severity = 0;   // 0 aman, 1 perhatian, 2 waspada, 3 bahaya

        if (impact.FireEtaHours is { } fire)
        {
            parts.Add(fire == 0 ? "api sudah dalam radius" : $"api ±{fire} jam");
            severity = Math.Max(severity, fire <= 3 ? 3 : fire <= 8 ? 2 : 1);
        }

        if (impact.SmokeMaxLevel > 0)
        {
            var label = impact.SmokeMaxLevel switch { 3 => "pekat", 2 => "sedang", _ => "ringan" };
            parts.Add(impact.SmokeEtaHours is { } eta and > 0 ? $"asap {label} ±{eta} jam" : $"asap {label}");
            severity = Math.Max(severity, impact.SmokeMaxLevel);
        }

        StatusText = parts.Count == 0
            ? $"{hotspotId}: aman pada simulasi ini"
            : $"{hotspotId}: {string.Join(" · ", parts)}";
        StatusColorHex = severity switch { 3 => "#DC2626", 2 => "#F97316", 1 => "#CA8A04", _ => "#16A34A" };
        HasStatus = true;
    }

    public void ClearImpact()
    {
        StatusText = string.Empty;
        HasStatus = false;
    }
}
