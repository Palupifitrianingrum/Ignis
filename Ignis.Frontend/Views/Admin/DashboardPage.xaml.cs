using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class DashboardPage : UserControl
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;

    public DashboardPage()
    {
        InitializeComponent();
        Map.HotspotClicked += id =>
        {
            var h = ws.FindHotspot(id);
            if (h != null) HotspotActions.ShowDetail(Window.GetWindow(this), h);
        };
        Loaded += (_, _) => { ws.Changed -= Refresh; ws.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ws.Changed -= Refresh;
    }

    private void Refresh()
    {
        var open = ws.Hotspots.Where(h => h.Status != HotspotStatus.Resolved).ToList();
        CActive.Value = open.Count.ToString();
        CFirms.Value = ws.Hotspots.Count(h => !h.IsManual).ToString();
        CManual.Value = ws.Hotspots.Count(h => h.IsManual).ToString();
        CPred.Value = ws.Predictions.Count.ToString();
        CUser.Value = ws.Users.Count.ToString();
        Map.SetHotspots(open);
        GridRecent.ItemsSource = ws.Hotspots.OrderByDescending(h => h.DetectedAt).Take(5).ToList();
        GridPred.ItemsSource = ws.Predictions.Take(5).ToList();
        RowFirms.Value = ws.FirmsStatus;
        RowFirms.ValueBrush = Ui.StatusBrush(ws.FirmsStatus);
        RowWeather.Value = ws.WeatherStatus;
        RowWeather.ValueBrush = Ui.StatusBrush(ws.WeatherStatus);
        RowSync.Value = ws.LastSync is { } t ? Fmt.Date(t) : "Belum pernah";
    }

    private void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridRecent.SelectedItem is HotspotRow h)
            HotspotActions.ShowDetail(Window.GetWindow(this), h);
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false;
        BtnSync.Content = "Menyinkronkan...";
        try
        {
            MessageBox.Show(await ws.SyncAsync(), "Sinkronisasi");
        }
        finally
        {
            BtnSync.Content = "Sync Ulang";
            BtnSync.IsEnabled = true;
            Refresh();
        }
    }
}
