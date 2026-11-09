using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class UsersPage : UserControl
{
    private readonly DataStore ds = DataStore.Instance;
    private int _role, _status;

    public UsersPage()
    {
        InitializeComponent();
        CmbRole.SelectedIndex = 0;
        CmbStatus.SelectedIndex = 0;
        LstAct.ItemsSource = ds.Activities;
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed -= Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    private void Refresh()
    {
        CTotal.Value = ds.Users.Count.ToString();
        CActive.Value = ds.Users.Count(u => u.IsActive).ToString();
        CInactive.Value = ds.Users.Count(u => !u.IsActive).ToString();
        CAdmin.Value = ds.Users.Count(u => u.Role == "Admin").ToString();
        GridUsers.ItemsSource = ds.Users.Where(u =>
            (_role == 0 || u.Role == (_role == 1 ? "Admin" : "Terdaftar")) &&
            (_status == 0 || u.IsActive == (_status == 1))).ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _role = CmbRole.SelectedIndex;
        _status = CmbStatus.SelectedIndex;
        Refresh();
    }

    private static UserAccount? From(object s) => (s as FrameworkElement)?.DataContext as UserAccount;

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (From(sender) is not { } u) return;
        MessageBox.Show($"ID: {u.Id}\nNama: {u.Name}\nEmail: {u.Email}\nRole: {u.Role}\nStatus: {u.StatusText}", "Detail Pengguna");
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (From(sender) is not { } u) return;
        var next = u.IsActive ? "menonaktifkan" : "mengaktifkan";
        if (MessageBox.Show($"Yakin ingin {next} {u.Name}?", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var err = ds.ToggleUser(u.Id);
        if (err != null) MessageBox.Show(err, "Tidak dapat diproses", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

