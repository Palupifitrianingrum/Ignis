using System.Threading.Tasks;
using System.Windows;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

/// <summary>Aksi bersama (Detail/Tambah/Edit/Hapus) yang dipakai banyak halaman.</summary>
public static class HotspotActions
{
    public static HotspotRow? From(object sender) => (sender as FrameworkElement)?.DataContext as HotspotRow;

    public static void ShowDetail(Window? owner, HotspotRow h) => new HotspotDetailWindow(h) { Owner = owner }.ShowDialog();
    public static void Add(Window? owner) => new HotspotFormWindow { Owner = owner }.ShowDialog();
    public static void Edit(Window? owner, HotspotRow h) => new HotspotFormWindow(h) { Owner = owner }.ShowDialog();

    public static async Task<bool> DeleteAsync(Window? owner, HotspotRow h)
    {
        var r = MessageBox.Show(owner, $"Hapus hotspot {h.Id}? Tindakan ini tidak dapat dibatalkan.", "Konfirmasi hapus",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return false;
        await AdminWorkspace.Current.DeleteHotspotAsync(h.Id);
        return true;
    }
}
