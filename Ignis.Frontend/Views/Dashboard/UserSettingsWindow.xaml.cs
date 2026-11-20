using System;
using System.Windows;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Dashboard;

public partial class UserSettingsWindow : Window
{
    private readonly AdminWorkspace _ws = AdminWorkspace.Current;

    public UserSettingsWindow()
    {
        InitializeComponent();
        var s = _ws.Settings;
        TxtApiUrl.Text = s.ApiBaseUrl;
        PwdCarto.Password = s.CartoKey;
        ChkNotif.IsChecked = s.NotificationsEnabled;

        SecurityCard.Visibility = _ws.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        var hasPassword = _ws.HasPassword;   // akun Google murni belum punya password: boleh membuat yang pertama
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
        var apiUrl = TxtApiUrl.Text.Trim();
        if (!Uri.IsWellFormedUriString(apiUrl, UriKind.Absolute))
        {
            MessageBox.Show(this, "Alamat Server API harus berupa URL lengkap (misalnya http://localhost:5000/).",
                "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = _ws.Settings;
        s.ApiBaseUrl = apiUrl.EndsWith('/') ? apiUrl : apiUrl + "/";
        s.CartoKey = PwdCarto.Password.Trim();
        s.NotificationsEnabled = ChkNotif.IsChecked == true;
        s.Save();

        _ws.RaiseSettingsChanged();
        MessageBox.Show(this, "Pengaturan berhasil disimpan.", "Pengaturan", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

