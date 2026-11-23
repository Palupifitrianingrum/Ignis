using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class HotspotManualPage : UserControl
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private List<HotspotRow> _currentList = new();
    private Window? Owner => Window.GetWindow(this);

    public HotspotManualPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { ws.Changed -= Refresh; ws.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ws.Changed -= Refresh;
    }

    private void Refresh()
    {
        _currentList = ws.Hotspots.Where(h => h.IsManual).OrderByDescending(h => h.DetectedAt).ToList();
        CTotal.Value = _currentList.Count.ToString();
        CActive.Value = _currentList.Count(h => h.Status != HotspotStatus.Resolved).ToString();
        CDone.Value = _currentList.Count(h => h.Status == HotspotStatus.Resolved).ToString();
        GridManual.ItemsSource = _currentList;
        UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        int selected = _currentList.Count(h => h.IsSelected);
        BtnDeleteSelected.Visibility = selected > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnDeleteSelected.Content = $"Hapus Terpilih ({selected})";
        ChkSelectAll.IsChecked = _currentList.Count == 0 || selected == 0
            ? false
            : selected == _currentList.Count ? true : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool check = ChkSelectAll.IsChecked == true;
        foreach (var row in _currentList) row.IsSelected = check;
        UpdateSelectionUi();
    }

    private void RowCheck_Click(object sender, RoutedEventArgs e) => UpdateSelectionUi();

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _currentList.Where(h => h.IsSelected && h.IsManual).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(Owner,
            $"Hapus {selected.Count} hotspot manual terpilih? Tindakan ini tidak dapat dibatalkan.",
            "Konfirmasi hapus", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        BtnDeleteSelected.IsEnabled = false;
        try
        {
            await ws.DeleteManualHotspotsAsync(selected.Select(h => h.Id));
        }
        finally
        {
            BtnDeleteSelected.IsEnabled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => HotspotActions.Add(Owner);
    private void Detail_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Owner, h); }
    private void Edit_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Edit(Owner, h); }
    private async void Delete_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) await HotspotActions.DeleteAsync(Owner, h); }
}

