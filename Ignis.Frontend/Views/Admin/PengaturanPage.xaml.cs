using System;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class PengaturanPage : UserControl
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;

    public PengaturanPage()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var s = ws.Settings;
        CmbInterval.SelectedIndex = Math.Clamp(s.IntervalIndex, 0, 3);
        TxtApiUrl.Text = s.ApiBaseUrl;
        PwdCarto.Password = s.CartoKey;
        ChkNotif.IsChecked = s.NotificationsEnabled;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Load();

    /// <summary>Menyimpan pengaturan LOKAL desktop. API key layanan data diatur dan disimpan oleh kontrol editornya masing-masing (di server).</summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var apiUrl = TxtApiUrl.Text.Trim();
        if (!Uri.IsWellFormedUriString(apiUrl, UriKind.Absolute))
        {
            Warn("Alamat Server API harus berupa URL lengkap (misalnya http://localhost:5000/).");
            return;
        }

        var s = ws.Settings;
        s.IntervalIndex = Math.Max(0, CmbInterval.SelectedIndex);
        s.ApiBaseUrl = apiUrl.EndsWith('/') ? apiUrl : apiUrl + "/";
        s.CartoKey = PwdCarto.Password.Trim();
        s.NotificationsEnabled = ChkNotif.IsChecked == true;
        s.Save();
        ws.RaiseSettingsChanged();   // jadwal sync diperbarui
        await ws.LogAsync("Admin menyimpan pengaturan");
        MessageBox.Show("Pengaturan disimpan.", "Pengaturan");
    }

    private static void Warn(string m) => MessageBox.Show(m, "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);
}

