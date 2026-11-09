using System.Linq;
using System.Windows;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class HotspotDetailWindow : Window
{
    private readonly DataStore ds = DataStore.Instance;
    private Hotspot _h;

    public HotspotDetailWindow(Hotspot h)
    {
        InitializeComponent();
        _h = h;
        BtnEdit.IsEnabled = BtnDel.IsEnabled = h.IsManual;
        BtnEdit.ToolTip = BtnDel.ToolTip = h.IsManual ? null : "Hotspot satelit bersumber dari API dan tidak dapat diubah/dihapus.";
        Fill();
    }

    private void Fill()
    {
        TxtHeader.Text = $"Detail Hotspot {_h.Id}";
        Map.SetHotspots(new[] { _h });
        Map.FocusOn(_h.Latitude, _h.Longitude, 11);

        var w = ds.GetWeather(_h);
        WTemp.Value = $"{w.Temperature:0.0} °C";
        WHum.Value = $"{w.Humidity:0}%";
        WWind.Value = $"{w.WindSpeed:0.0} m/s";
        WDir.Value = $"{w.WindDirection:0}°";
        WPres.Value = $"{w.Pressure:0} hPa";
        WRain.Value = $"{w.Precipitation:0.0} mm";

        RId.Value = _h.Id;
        RCoord.Value = _h.CoordText;
        RTime.Value = _h.DetectedText;
        RConf.Value = _h.ConfidenceText;
        RBright.Value = _h.BrightnessText;
        RSource.Value = _h.IsManual ? $"Manual ({_h.ReportedBy})" : $"Satelit ({_h.SatelliteName} · {_h.Instrument})";
        RStatus.Value = _h.StatusText;
        TxtNote.Text = string.IsNullOrWhiteSpace(_h.Note) ? "Tidak ada catatan." : _h.Note;
        ShowPrediction(ds.LatestPrediction(_h.Id) ?? ds.Predict(_h));
    }

    private void ShowPrediction(Prediction p)
    {
        PDir.Value = p.DirectionText;
        PSpeed.Value = p.SpeedText;
        PArea.Value = p.AreaText;
        PHours.Value = p.HoursText;
        PRisk.Value = p.RiskText;
    }

    private void Recalc_Click(object sender, RoutedEventArgs e) => ShowPrediction(ds.Predict(_h));
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        HotspotActions.Edit(this, _h);
        var again = ds.Hotspots.FirstOrDefault(x => x.Id == _h.Id);
        if (again == null) { Close(); return; }
        _h = again;
        Fill();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (HotspotActions.Delete(this, _h)) Close();
    }
}

