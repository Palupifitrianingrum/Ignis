using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ignis.Shared;

namespace Ignis.Frontend.Models;

/// <summary>Satu notifikasi untuk panel lonceng dan banner. Membungkus <see cref="NotificationDto"/> dengan teks dan warna siap tampil.</summary>
public partial class NotificationItem : ObservableObject
{
    public NotificationItem(NotificationDto dto)
    {
        Dto = dto;
        IsRead = dto.IsRead;
    }

    public NotificationDto Dto { get; }

    public string Id => Dto.Id;
    public string HotspotId => Dto.HotspotId;
    public string? AreaId => Dto.AreaId;
    public string Title => Dto.Title;
    public string Message => Dto.Message;
    public int Level => Dto.Level;

    /// <summary>"API" atau "ASAP".</summary>
    public string KindLabel => Dto.Kind == NotificationKind.Fire ? "API" : "ASAP";

    public string LevelLabel => Dto.Level switch { 3 => "BAHAYA", 2 => "WASPADA", _ => "PERHATIAN" };

    /// <summary>Merah = bahaya, oranye = waspada, kuning tua = perhatian.</summary>
    public string LevelColorHex => Dto.Level switch { 3 => "#DC2626", 2 => "#F97316", _ => "#CA8A04" };

    public string LevelBackgroundHex => Dto.Level switch { 3 => "#FEF2F2", 2 => "#FFF7ED", _ => "#FEFCE8" };

    /// <summary>"±3 jam · api terdekat 2,9 km" (api) atau "Saat ini · sumber asap 13 km" (asap): untuk asap, jaraknya ke hotspot sumber.</summary>
    public string Detail =>
        string.Create(CultureInfo.GetCultureInfo("id-ID"),
            $"{(Dto.EtaHours == 0 ? "Saat ini" : $"±{Dto.EtaHours} jam")}  ·  {(Dto.Kind == NotificationKind.Fire ? "api terdekat" : "sumber asap")} {Dto.DistanceKm:0.#} km");

    public string TimeText => RelativeTime(Dto.CreatedAt, DateTime.Now);

    [ObservableProperty] private bool isRead;

    public bool IsUnread => !IsRead;

    partial void OnIsReadChanged(bool value) => OnPropertyChanged(nameof(IsUnread));

    /// <summary>"baru saja", "5 menit lalu", "3 jam lalu", "kemarin 14:05", "12 Nov 14:05".</summary>
    public static string RelativeTime(DateTime when, DateTime now)
    {
        var span = now - when;
        if (span < TimeSpan.Zero || span < TimeSpan.FromMinutes(1)) return "baru saja";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} menit lalu";
        if (span < TimeSpan.FromHours(24)) return $"{(int)span.TotalHours} jam lalu";
        var id = CultureInfo.GetCultureInfo("id-ID");
        return when.Date == now.Date.AddDays(-1) ? $"kemarin {when:HH:mm}" : when.ToString("d MMM HH:mm", id);
    }
}
