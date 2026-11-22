using System;
using System.Globalization;
using System.Windows;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

/// <summary>Admin input hotspot manual (juga dipakai untuk edit).</summary>
public partial class HotspotFormWindow : Window
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private readonly HotspotRow? _edit;

    public HotspotFormWindow(HotspotRow? edit = null)
    {
        InitializeComponent();
        _edit = edit is { IsEditable: true } ? edit : null;
        CmbStatus.SelectedIndex = 0;
        DpDate.SelectedDate = DateTime.Today;
        TxtTime.Text = DateTime.Now.ToString("HH:mm");
        Map.SetHotspots(ws.Hotspots);
        Map.MapClicked += (la, lo) =>
        {
            TxtLat.Text = Fmt.Inv(la, "0.000000");
            TxtLon.Text = Fmt.Inv(lo, "0.000000");
            Map.SetPick(la, lo);
        };

        if (_edit != null)
        {
            var when = _edit.Dto.IncidentTime ?? _edit.DetectedAt;
            Title = TxtHeader.Text = $"Edit Hotspot {_edit.Id}";
            TxtLat.Text = Fmt.Inv(_edit.Latitude, "0.000000");
            TxtLon.Text = Fmt.Inv(_edit.Longitude, "0.000000");
            TxtConf.Text = Fmt.Inv(_edit.Confidence, "0.##");
            DpDate.SelectedDate = when.Date;
            TxtTime.Text = when.ToString("HH:mm");
            TxtReporter.Text = _edit.ReportedBy;
            TxtNote.Text = _edit.Note;
            CmbStatus.SelectedIndex = (int)_edit.Status;
            Map.SetPick(_edit.Latitude, _edit.Longitude);
            Map.FocusOn(_edit.Latitude, _edit.Longitude, 10);
        }
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void Err(string m) => MessageBox.Show(this, m, "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryD(TxtLat.Text, out var lat) || lat < -90 || lat > 90) { Err("Latitude harus berupa angka antara -90 dan 90."); return; }
        if (!TryD(TxtLon.Text, out var lon) || lon < -180 || lon > 180) { Err("Longitude harus berupa angka antara -180 dan 180."); return; }
        if (!TryD(TxtConf.Text, out var conf) || conf < 0 || conf > 100) { Err("Tingkat keyakinan harus antara 0 dan 100."); return; }
        if (string.IsNullOrWhiteSpace(TxtReporter.Text)) { Err("Pelapor / sumber laporan wajib diisi."); return; }
        if (DpDate.SelectedDate is not DateTime day) { Err("Tanggal kejadian wajib diisi."); return; }
        if (!TimeSpan.TryParseExact(TxtTime.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var ts)) { Err("Jam harus berformat HH:mm, misalnya 08:30."); return; }

        var request = new ManualHotspotRequest(
            lat, lon, conf, day.Date + ts,
            TxtReporter.Text.Trim(), TxtNote.Text.Trim(),
            (HotspotStatus)Math.Max(0, CmbStatus.SelectedIndex));

        var btn = sender as System.Windows.Controls.Button;
        if (btn != null) btn.IsEnabled = false;
        try
        {
            // Aturan domain divalidasi ulang di backend (entity ManualHotspot); pelanggaran kembali sebagai pesan error.
            if (_edit == null) await ws.AddManualAsync(request);
            else await ws.UpdateManualAsync(_edit.Id, request);
            Close();
        }
        catch (Exception ex)
        {
            if (btn != null) btn.IsEnabled = true;
            Err("Gagal menyimpan hotspot: " + ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
