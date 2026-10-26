using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IgnisAdmin.Services;
using IgnisAdmin.Views;
namespace IgnisAdmin;

public partial class MainWindow : Window
{
    readonly DispatcherTimer _timer = new();
    readonly DataStore ds = DataStore.Instance;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) =>   // scheduler UC-02
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

    void ScheduleSync()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromHours(ds.Settings.IntervalHours);
        _timer.Start();
    }

    static UserControl Create(string key) => key switch
    {
        "Kelola" => new KelolaHotspotPage(),
        "Semua" => new SemuaHotspotPage(),
        "Manual" => new HotspotManualPage(),
        "Pengguna" => new UsersPage(),
        "Riwayat" => new RiwayatPage(),
        "Pengaturan" => new PengaturanPage(),
        _ => new DashboardPage()
    };

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Host == null || sender is not RadioButton rb) return;
        Host.Content = Create((string)rb.Tag);
    }

    void CloseBanner_Click(object sender, RoutedEventArgs e) => Banner.Visibility = Visibility.Collapsed;
}
