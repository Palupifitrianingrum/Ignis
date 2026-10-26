using System.Windows;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

/// <summary>Aksi bersama (Detail/Tambah/Edit/Hapus) yang dipakai banyak halaman.</summary>
public static class HotspotActions
{
    public static Hotspot? From(object sender) => (sender as FrameworkElement)?.DataContext as Hotspot;

    public static void ShowDetail(Window? owner, Hotspot h) => new HotspotDetailWindow(h) { Owner = owner }.ShowDialog();
    public static void Add(Window? owner) => new HotspotFormWindow { Owner = owner }.ShowDialog();
    public static void Edit(Window? owner, Hotspot h) => new HotspotFormWindow(h) { Owner = owner }.ShowDialog();

    public static bool Delete(Window? owner, Hotspot h)
    {
        var r = MessageBox.Show(owner, $"Hapus hotspot {h.Id}? Tindakan ini tidak dapat dibatalkan.", "Konfirmasi hapus",
                                MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return false;
        DataStore.Instance.DeleteHotspot(h.Id);
        return true;
    }
}
