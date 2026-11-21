using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Views.Admin;

public partial class AdminDashboardView : UserControl
{
    private readonly DispatcherTimer _timer = new();
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private readonly IgnisApiClient _api = App.Services.GetRequiredService<IgnisApiClient>();

    public AdminDashboardView()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) =>
        {
            if (!_api.IsAdmin) { _timer.Stop(); return; }
            try { await ws.SyncAsync(); }
            catch (Exception ex)
            {
                try { await ws.LogAsync("Sync terjadwal gagal: " + ex.Message); } catch { /* backend tidak terjangkau */ }
            }
        };
        ws.SettingsChanged += ScheduleSync;
        ws.NewHotspots += n =>
        {
            if (!ws.Settings.NotificationsEnabled) return;
            BannerText.Text = $"{n} hotspot baru terdeteksi dari NASA FIRMS.";
            Banner.Visibility = Visibility.Visible;
        };

        NavDashboard.IsChecked = true;
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && _api.IsAdmin)
            {
                ScheduleSync();
                await LoadAsync();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    private async Task LoadAsync()
    {
        if (!_api.IsAdmin) return;
        try { await ws.ReloadAsync(); }
        catch (Exception ex)
        {
            var owner = Window.GetWindow(this);
            var msg = ex.Message + "\n\nJalankan Ignis.Api (dotnet run --project Ignis.Api) lalu buka ulang halaman ini.";
            if (owner != null)
                MessageBox.Show(owner, msg, "Backend tidak terhubung", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(msg, "Backend tidak terhubung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ScheduleSync()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromHours(ws.Settings.IntervalHours);
        _timer.Start();
    }

    private static UserControl Create(string key) => key switch
    {
        "Kelola" => new KelolaHotspotPage(),
        "Semua" => new SemuaHotspotPage(),
        "Manual" => new HotspotManualPage(),
        "Pengguna" => new UsersPage(),
        "Riwayat" => new RiwayatPage(),
        "Pengaturan" => new PengaturanPage(),
        _ => new DashboardPage()
    };

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Host == null || sender is not RadioButton rb) return;
        Host.Content = Create((string)rb.Tag);
    }

    private void CloseBanner_Click(object sender, RoutedEventArgs e) => Banner.Visibility = Visibility.Collapsed;
}
