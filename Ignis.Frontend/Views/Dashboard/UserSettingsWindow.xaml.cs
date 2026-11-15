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

