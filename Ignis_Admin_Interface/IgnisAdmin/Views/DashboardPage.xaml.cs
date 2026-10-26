using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

public partial class DashboardPage : UserControl
{
    readonly DataStore ds = DataStore.Instance;

    public DashboardPage()
    {
        InitializeComponent();
        Map.HotspotClicked += id =>
        {
            var h = ds.Hotspots.FirstOrDefault(x => x.Id == id);
            if (h != null) HotspotActions.ShowDetail(Window.GetWindow(this), h);
        };
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    void Refresh()
    {
        var open = ds.Hotspots.Where(h => h.Status != HotspotStatus.Resolved).ToList();
        CActive.Value = open.Count.ToString();
        CFirms.Value = ds.Hotspots.Count(h => !h.IsManual).ToString();
        CManual.Value = ds.Hotspots.Count(h => h.IsManual).ToString();
        CPred.Value = ds.Predictions.Count.ToString();
        CUser.Value = ds.Users.Count.ToString();
        Map.SetHotspots(open);
        GridRecent.ItemsSource = ds.Hotspots.OrderByDescending(h => h.DetectedAt).Take(5).ToList();
        GridPred.ItemsSource = ds.Predictions.Take(5).ToList();
        RowFirms.Value = ds.FirmsStatus; RowFirms.ValueBrush = Ui.StatusBrush(ds.FirmsStatus);
        RowWeather.Value = ds.WeatherStatus; RowWeather.ValueBrush = Ui.StatusBrush(ds.WeatherStatus);
        RowSync.Value = ds.LastSync is DateTime t ? Fmt.Date(t) : "Belum pernah";
    }

    void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridRecent.SelectedItem is Hotspot h) HotspotActions.ShowDetail(Window.GetWindow(this), h);
    }

    async void Sync_Click(object sender, RoutedEventArgs e)
    {
        BtnSync.IsEnabled = false; BtnSync.Content = "Menyinkronkan...";
        try { MessageBox.Show(await SyncService.RunAsync(), "Sinkronisasi"); }
        finally { BtnSync.Content = "Sync Ulang"; BtnSync.IsEnabled = true; Refresh(); }
    }
}
