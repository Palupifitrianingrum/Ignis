using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class UsersPage : UserControl
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private int _role, _status;

    public UsersPage()
    {
        InitializeComponent();
        CmbRole.SelectedIndex = 0;
        CmbStatus.SelectedIndex = 0;
        LstAct.ItemsSource = ws.Activities;
        Loaded += (_, _) => { ws.Changed -= Refresh; ws.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ws.Changed -= Refresh;
    }

    private void Refresh()
    {
        CTotal.Value = ws.Users.Count.ToString();
        CActive.Value = ws.Users.Count(u => u.IsActive).ToString();
        CInactive.Value = ws.Users.Count(u => !u.IsActive).ToString();
        CAdmin.Value = ws.Users.Count(u => u.Role == "Admin").ToString();
        GridUsers.ItemsSource = ws.Users.Where(u =>
            (_role == 0 || u.Role == (_role == 1 ? "Admin" : "Terdaftar")) &&
            (_status == 0 || u.IsActive == (_status == 1))).ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _role = CmbRole.SelectedIndex;
        _status = CmbStatus.SelectedIndex;
        Refresh();
    }

    private static UserRow? From(object s) => (s as FrameworkElement)?.DataContext as UserRow;

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (From(sender) is not { } u) return;
        MessageBox.Show($"ID: {u.Id}\nNama: {u.Name}\nEmail: {u.Email}\nRole: {u.Role}\nStatus: {u.StatusText}", "Detail Pengguna");
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (From(sender) is not { } u) return;
        var next = u.IsActive ? "menonaktifkan" : "mengaktifkan";
        if (MessageBox.Show($"Yakin ingin {next} {u.Name}?", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var err = await ws.ToggleUserAsync(u.Id);
        if (err != null) MessageBox.Show(err, "Tidak dapat diproses", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void Role_Click(object sender, RoutedEventArgs e)
    {
        if (From(sender) is not { } u) return;
        var target = u.IsAdmin ? Role.Registered : Role.Admin;
        var warning = target == Role.Admin
            ? $"Jadikan {u.Name} ADMIN?\n\nAdmin tidak punya lokasi tersimpan dan notifikasi, sehingga data itu milik {u.Name} akan DIHAPUS. {u.Name} harus login ulang."
            : $"Turunkan {u.Name} menjadi pengguna terdaftar?\n\n{u.Name} langsung kehilangan akses admin dan harus login ulang.";
        if (MessageBox.Show(warning, "Konfirmasi ubah role", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var err = await ws.SetUserRoleAsync(u.Id, target);
        if (err != null) MessageBox.Show(err, "Tidak dapat diproses", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
