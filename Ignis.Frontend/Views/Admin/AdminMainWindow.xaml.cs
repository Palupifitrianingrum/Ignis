using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ignis.Frontend.Navigation;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Views.Admin;

public partial class AdminMainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private readonly IgnisApiClient _api = App.Services.GetRequiredService<IgnisApiClient>();
    private readonly IDialogService _dialogService = App.Services.GetRequiredService<IDialogService>();

    public AdminMainWindow()
    {
        InitializeComponent();
        _api.SessionChanged += OnSessionChanged;
        Closed += (_, _) => _api.SessionChanged -= OnSessionChanged;
        _timer.Tick += async (_, _) =>
        {
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
        ScheduleSync();
        NavDashboard.IsChecked = true;
        Loaded += async (_, _) => await LoadAsync();   // halaman yang aktif menyegarkan diri lewat event Changed
        Title = $"{Title} — {ws.BackendName}";
    }

    private void OnSessionChanged()
    {
        if (_api.IsLoggedIn) return;
        _timer.Stop();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded) return;
            Close();
            _dialogService.ShowLoginDialog();
        }));
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try { await ws.ReloadAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                ex.Message + "\n\nJalankan Ignis.Api (dotnet run --project Ignis.Api) lalu buka ulang halaman ini,\n" +
                "atau pakai mode demo: set environment IGNIS_BACKEND=Demo.",
                "Backend tidak terhubung", MessageBoxButton.OK, MessageBoxImage.Warning);
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
