using System;
using System.Threading.Tasks;
using System.Windows;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Views.Admin;

public partial class HotspotDetailWindow : Window
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private HotspotRow _h;

    public HotspotDetailWindow(HotspotRow h)
    {
        InitializeComponent();
        _h = h;
        var isAdmin = App.Services.GetRequiredService<IgnisApiClient>().IsAdmin;
        if (!isAdmin)
        {
            BtnEdit.Visibility = BtnDel.Visibility = BtnRecalc.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnEdit.IsEnabled = BtnDel.IsEnabled = h.IsEditable;   // IsEditable berasal dari Hotspot.IsEditable (polimorfik)
            BtnEdit.ToolTip = BtnDel.ToolTip = h.IsEditable ? null : "Hotspot satelit bersumber dari API dan tidak dapat diubah/dihapus.";
        }
        Loaded += async (_, _) => await SafeFillAsync();
    }

    private async Task SafeFillAsync()
    {
        try { await FillAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Detail hotspot", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task FillAsync()
    {
        TxtHeader.Text = $"Detail Hotspot {_h.Id}";
        Map.SetHotspots(new[] { _h });
        Map.FocusOn(_h.Latitude, _h.Longitude, 11);

        RId.Value = _h.Id;
        RCoord.Value = _h.CoordText;
        RTime.Value = _h.DetectedText;
        RConf.Value = _h.ConfidenceText;
        RBright.Value = _h.BrightnessText;
        RSource.Value = _h.SourceDetailText;
        RStatus.Value = _h.StatusText;
        TxtNote.Text = string.IsNullOrWhiteSpace(_h.Note) ? "Tidak ada catatan." : _h.Note;

        var w = await ws.GetWeatherAsync(_h);
        WTemp.Value = $"{w.Temperature:0.0} °C";
        WHum.Value = $"{w.Humidity:0}%";
        WWind.Value = $"{w.WindSpeed:0.0} m/s";
        WDir.Value = $"{w.WindDirection:0}°";
        WPres.Value = $"{w.Pressure:0} hPa";
        WRain.Value = $"{w.Precipitation:0.0} mm";

        ShowPrediction(await ws.GetOrPredictAsync(_h));
    }

    private void ShowPrediction(PredictionRow p)
    {
        PDir.Value = p.DirectionText;
        PSpeed.Value = p.SpeedText;
        PArea.Value = p.AreaText;
        PHours.Value = p.HoursText;
        PRisk.Value = p.RiskText;
    }

    private async void Recalc_Click(object sender, RoutedEventArgs e)
    {
        try { ShowPrediction(await ws.PredictAsync(_h)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Hitung ulang prediksi", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        HotspotActions.Edit(this, _h);
        var again = ws.FindHotspot(_h.Id);
        if (again == null) { Close(); return; }
        _h = again;
        await SafeFillAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (await HotspotActions.DeleteAsync(this, _h)) Close();
    }
}
