namespace Ignis.Frontend.Models;

/// <summary>Isian dialog lokasi tersimpan. Disimpan di sini agar tidak hilang ketika pengguna beralih ke "Pilih di peta" lalu kembali.</summary>
public sealed class SavedLocationDraft
{
    /// <summary>Id lokasi yang sedang diubah; null untuk lokasi baru.</summary>
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double RadiusKm { get; set; } = 10;
}

/// <summary>Hasil dialog: disimpan, dibatalkan, atau pengguna ingin memilih titik langsung di peta.</summary>
public sealed class SavedLocationDialogResult
{
    public required SavedLocationDraft Draft { get; init; }

    /// <summary>True bila pengguna menekan "Pilih di peta": dialog ditutup dan peta masuk mode memilih titik.</summary>
    public bool PickOnMap { get; init; }
}
