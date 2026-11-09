using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class AdminMainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly DataStore ds = DataStore.Instance;

    public AdminMainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) =>
        {
            try { await SyncService.RunAsync(); }
            catch (Exception ex) { ds.Log("Sync terjadwal gagal: " + ex.Message); ds.Raise(); }
        };
        ds.SettingsChanged += ScheduleSync;
        ds.NewHotspots += n =>
        {
            if (!ds.Settings.NotificationsEnabled) return;
            BannerText.Text = $"{n} hotspot baru terdeteksi dari NASA FIRMS.";
            Banner.Visibility = Visibility.Visible;
        };
        ScheduleSync();
        NavDashboard.IsChecked = true;
    }

    private void ScheduleSync()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromHours(ds.Settings.IntervalHours);
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

