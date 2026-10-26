using System.Windows;
using System.Windows.Controls;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

public partial class PengaturanPage : UserControl
{
    readonly DataStore ds = DataStore.Instance;
    public PengaturanPage() { InitializeComponent(); Load(); }

    void Load()
    {
        var s = ds.Settings;
        CmbInterval.SelectedIndex = Math.Clamp(s.IntervalIndex, 0, 3);
        TxtFirmsUrl.Text = s.FirmsEndpoint; PwdFirms.Password = s.FirmsKey;
        TxtWeatherUrl.Text = s.WeatherEndpoint; PwdWeather.Password = s.WeatherKey; PwdCarto.Password = s.CartoKey;
        TxtRetry.Text = s.RetryCount.ToString(); TxtDelay.Text = s.RetryDelayMinutes.ToString();
        ChkNotif.IsChecked = s.NotificationsEnabled;
    }

    void Reset_Click(object sender, RoutedEventArgs e) => Load();

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtRetry.Text, out var retry) || retry < 0 || retry > 10) { Warn("Jumlah percobaan ulang harus 0 - 10."); return; }
        if (!int.TryParse(TxtDelay.Text, out var delay) || delay < 0 || delay > 120) { Warn("Jeda harus 0 - 120 menit."); return; }
        if (!Uri.IsWellFormedUriString(TxtFirmsUrl.Text.Trim(), UriKind.Absolute) || !Uri.IsWellFormedUriString(TxtWeatherUrl.Text.Trim(), UriKind.Absolute))
        { Warn("Endpoint harus berupa URL lengkap (https://...)."); return; }

        var s = ds.Settings;
        s.IntervalIndex = Math.Max(0, CmbInterval.SelectedIndex);
        s.FirmsEndpoint = TxtFirmsUrl.Text.Trim(); s.FirmsKey = PwdFirms.Password;
        s.WeatherEndpoint = TxtWeatherUrl.Text.Trim(); s.WeatherKey = PwdWeather.Password; s.CartoKey = PwdCarto.Password.Trim();
        s.RetryCount = retry; s.RetryDelayMinutes = delay; s.NotificationsEnabled = ChkNotif.IsChecked == true;
        s.Save();
        ds.RaiseSettingsChanged();   // jadwal sync diperbarui
        ds.Log("Admin menyimpan pengaturan"); ds.Raise();
        MessageBox.Show("Pengaturan disimpan.", "Pengaturan");
    }

    static void Warn(string m) => MessageBox.Show(m, "Input tidak valid", MessageBoxButton.OK, MessageBoxImage.Warning);
}
