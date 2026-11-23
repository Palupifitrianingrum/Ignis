using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Shared;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class KelolaHotspotPage : UserControl
{
    private readonly AdminWorkspace ws = AdminWorkspace.Current;
    private Region? _reg;
    private HotspotSource? _src;
    private HotspotStatus? _st;
    private Window? Owner => Window.GetWindow(this);

    public KelolaHotspotPage()
    {
        InitializeComponent();
        CmbRegion.ItemsSource = Region.Defaults;
        CmbRegion.SelectedIndex = 0;
        CmbSrc.SelectedIndex = 0;
        CmbStatus.SelectedIndex = 0;

        Loaded += (_, _) => { ws.Changed -= Refresh; ws.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ws.Changed -= Refresh;
    }

    private void Refresh()
    {
        var all = ws.Hotspots.ToList();
        CTotal.Value = all.Count.ToString();
        CFirms.Value = all.Count(h => !h.IsManual).ToString();
        CManual.Value = all.Count(h => h.IsManual).ToString();

        var filtered = ws.Filter(_reg, _src, _st).OrderByDescending(h => h.DetectedAt).ToList();
        GridHotspots.ItemsSource = filtered;
        TxtCount.Text = $"Menampilkan {filtered.Count} dari {all.Count} hotspot";
        Map.SetHotspots(filtered);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _reg = CmbRegion.SelectedItem as Region;
        if (_reg?.IsAll == true) _reg = null;
        _src = CmbSrc.SelectedIndex switch
        {
            1 => HotspotSource.Satellite,
            2 => HotspotSource.Manual,
            _ => null
        };
        _st = CmbStatus.SelectedIndex switch
        {
            1 => HotspotStatus.Active,
            2 => HotspotStatus.Monitoring,
            3 => HotspotStatus.Resolved,
            _ => null
        };
        Refresh();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        CmbRegion.SelectedIndex = 0;
        CmbSrc.SelectedIndex = 0;
        CmbStatus.SelectedIndex = 0;
        _reg = null;
        _src = null;
        _st = null;
        Refresh();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => HotspotActions.Add(Owner);

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Owner, h);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (HotspotActions.From(sender) is { } h) HotspotActions.Edit(Owner, h);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (HotspotActions.From(sender) is { } h) await HotspotActions.DeleteAsync(Owner, h);
    }
}

