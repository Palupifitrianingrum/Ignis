using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Ignis.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Views.Dashboard;

public partial class UserSettingsWindow : Window
{
    private readonly AdminWorkspace _ws = AdminWorkspace.Current;
    private readonly IgnisApiClient _api = App.Services.GetRequiredService<IgnisApiClient>();

    public UserSettingsWindow()
    {
        InitializeComponent();
        var s = _ws.Settings;

        // Ringkasan Profil & Akun
        if (_api.CurrentUser is { } user)
        {
            ProfileCard.Visibility = Visibility.Visible;
            TxtProfileName.Text = user.Name;
            TxtProfileEmail.Text = user.Email;
            TxtProfileRole.Text = user.Role.ToString();
            TxtGoogleStatus.Text = user.GoogleLinked ? "Terhubung dengan Google" : "Belum terhubung";
        }
        else
        {
            ProfileCard.Visibility = Visibility.Collapsed;
        }

        // Preferensi Tampilan Peta
        CmbDefaultRegion.ItemsSource = Region.Defaults.Select(r => r.Name).ToList();
        CmbDefaultRegion.SelectedItem = Region.Defaults.Any(r => r.Name == s.DefaultRegionName)
            ? s.DefaultRegionName
            : "Riau";
        ChkHideResolved.IsChecked = s.HideResolvedHotspots;

        // Auto-Refresh Data
        var refreshIndex = s.AutoRefreshMinutes switch
        {
            5 => 1,
            15 => 2,
            30 => 3,
            _ => 0
        };
        CmbAutoRefresh.SelectedIndex = refreshIndex;

        // Notifikasi & Keamanan
        ChkNotif.IsChecked = s.NotificationsEnabled;
        SecurityCard.Visibility = _ws.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        var hasPassword = _ws.HasPassword;
        LblCurrent.Visibility = PwdCurrent.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (PwdNew.Password != PwdNew2.Password)
        {
            MessageBox.Show(this, "Konfirmasi password baru tidak sama.", "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var err = await _ws.ChangePasswordAsync(_ws.HasPassword ? PwdCurrent.Password : null, PwdNew.Password);
        if (err != null)
        {
            MessageBox.Show(this, err, "Password tidak diganti", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PwdCurrent.Clear(); PwdNew.Clear(); PwdNew2.Clear();
        MessageBox.Show(this, "Password berhasil diganti.", "Keamanan Akun", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _ws.Settings;
        if (CmbDefaultRegion.SelectedItem is string regionName && !string.IsNullOrWhiteSpace(regionName))
            s.DefaultRegionName = regionName;

        s.HideResolvedHotspots = ChkHideResolved.IsChecked == true;
        s.AutoRefreshMinutes = CmbAutoRefresh.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var mins)
            ? mins
            : 0;
        s.NotificationsEnabled = ChkNotif.IsChecked == true;
        s.Save();

        _ws.RaiseSettingsChanged();
        MessageBox.Show(this, "Pengaturan berhasil disimpan.", "Pengaturan", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
