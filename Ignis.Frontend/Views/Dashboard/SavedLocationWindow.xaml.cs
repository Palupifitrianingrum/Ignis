using System;
using System.Globalization;
using System.Windows;
using Ignis.Frontend.Models;

namespace Ignis.Frontend.Views.Dashboard;

/// <summary>Dialog menambah/mengubah lokasi tersimpan. Hanya memvalidasi isian; penyimpanan dilakukan pemanggil lewat API.</summary>
public partial class SavedLocationWindow : Window
{
    private readonly SavedLocationDraft _draft;

    /// <summary>Null bila dibatalkan.</summary>
    public SavedLocationDialogResult? Result { get; private set; }

    public SavedLocationWindow(SavedLocationDraft draft)
    {
        InitializeComponent();
        _draft = draft;

        TxtTitle.Text = draft.Id == null ? "Tambah Lokasi" : "Ubah Lokasi";
        TxtName.Text = draft.Name;
        TxtLat.Text = draft.Latitude is { } la ? la.ToString("0.#####", CultureInfo.InvariantCulture) : string.Empty;
        TxtLon.Text = draft.Longitude is { } lo ? lo.ToString("0.#####", CultureInfo.InvariantCulture) : string.Empty;
        SldRadius.Value = Math.Clamp(draft.RadiusKm, 1, 50);
        UpdateRadiusLabel();
    }

    private void Radius_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateRadiusLabel();

    private void UpdateRadiusLabel()
    {
        if (TxtRadiusLabel != null) TxtRadiusLabel.Text = $"Radius peringatan: {SldRadius.Value:0} km";
    }

    /// <summary>Angka desimal dengan titik atau koma; selalu dibaca dengan budaya invarian.</summary>
    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Salin isian ke draft tanpa memvalidasi (untuk "Pilih di peta" supaya ketikan tidak hilang).</summary>
    private void CopyToDraft()
    {
        _draft.Name = TxtName.Text.Trim();
        _draft.Latitude = TryParse(TxtLat.Text, out var la) ? la : null;
        _draft.Longitude = TryParse(TxtLon.Text, out var lo) ? lo : null;
        _draft.RadiusKm = SldRadius.Value;
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        CopyToDraft();
        Result = new SavedLocationDialogResult { Draft = _draft, PickOnMap = true };
        DialogResult = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = Validate(out var lat, out var lon);
        if (error != null)
        {
            TxtError.Text = error;
            TxtError.Visibility = Visibility.Visible;
            return;
        }

        _draft.Name = TxtName.Text.Trim();
        _draft.Latitude = lat;
        _draft.Longitude = lon;
        _draft.RadiusKm = SldRadius.Value;
        Result = new SavedLocationDialogResult { Draft = _draft };
        DialogResult = true;
    }

    private string? Validate(out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(TxtName.Text)) return "Nama lokasi wajib diisi.";
        if (!TryParse(TxtLat.Text, out lat) || lat is < -90 or > 90) return "Lintang harus berupa angka antara -90 dan 90 (mis. -2.9871).";
        if (!TryParse(TxtLon.Text, out lon) || lon is < -180 or > 180) return "Bujur harus berupa angka antara -180 dan 180 (mis. 104.632).";
        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }
}
