using System.Globalization;
using System.Windows;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

/// <summary>UC-09: Admin input hotspot manual (juga dipakai untuk edit).</summary>
public partial class HotspotFormWindow : Window
{
    readonly DataStore ds = DataStore.Instance;
    readonly Hotspot? _edit;

    public HotspotFormWindow(Hotspot? edit = null)
    {
        InitializeComponent();
        _edit = edit;
        CmbStatus.SelectedIndex = 0; DpDate.SelectedDate = DateTime.Today; TxtTime.Text = DateTime.Now.ToString("HH:mm");
        Map.SetHotspots(ds.Hotspots);
        Map.MapClicked += (la, lo) => { TxtLat.Text = Fmt.Inv(la, "0.000000"); TxtLon.Text = Fmt.Inv(lo, "0.000000"); Map.SetPick(la, lo); };

        if (edit != null)
        {
            Title = TxtHeader.Text = $"Edit Hotspot {edit.Id}";
            TxtLat.Text = Fmt.Inv(edit.Latitude, "0.000000"); TxtLon.Text = Fmt.Inv(edit.Longitude, "0.000000");
            TxtConf.Text = Fmt.Inv(edit.Confidence, "0.##");
            DpDate.SelectedDate = edit.DetectedAt.Date; TxtTime.Text = edit.DetectedAt.ToString("HH:mm");
            TxtReporter.Text = edit.ReportedBy; TxtNote.Text = edit.Note; CmbStatus.SelectedIndex = (int)edit.Status;
            Map.SetPick(edit.Latitude, edit.Longitude); Map.FocusOn(edit.Latitude, edit.Longitude, 10);
        }
    }

    static bool TryD(string s, out double v) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    void Err(string m) => MessageBox.Show(this, m, "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryD(TxtLat.Text, out var lat) || lat < -90 || lat > 90) { Err("Latitude harus berupa angka antara -90 dan 90."); return; }
        if (!TryD(TxtLon.Text, out var lon) || lon < -180 || lon > 180) { Err("Longitude harus berupa angka antara -180 dan 180."); return; }
        if (!TryD(TxtConf.Text, out var conf) || conf < 0 || conf > 100) { Err("Tingkat keyakinan harus antara 0 dan 100."); return; }
        if (string.IsNullOrWhiteSpace(TxtReporter.Text)) { Err("Pelapor / sumber laporan wajib diisi."); return; }
        if (DpDate.SelectedDate is not DateTime day) { Err("Tanggal kejadian wajib diisi."); return; }
        if (!TimeSpan.TryParseExact(TxtTime.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var ts)) { Err("Jam harus berformat HH:mm, misalnya 08:30."); return; }

        var status = (HotspotStatus)Math.Max(0, CmbStatus.SelectedIndex);
        var when = day.Date + ts;
        if (_edit == null)
        {
            ds.AddManual(new Hotspot { Latitude = lat, Longitude = lon, Confidence = conf, DetectedAt = when, ReportedBy = TxtReporter.Text.Trim(), Note = TxtNote.Text.Trim(), Status = status });
        }
        else
        {
            _edit.Latitude = lat; _edit.Longitude = lon; _edit.Confidence = conf; _edit.DetectedAt = when;
            _edit.ReportedBy = TxtReporter.Text.Trim(); _edit.Note = TxtNote.Text.Trim(); _edit.Status = status;
            ds.UpdateHotspot(_edit);
        }
        Close();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
