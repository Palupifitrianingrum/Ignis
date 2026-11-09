using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class SemuaHotspotPage : UserControl
{
    private const int PageSize = 10;
    private readonly DataStore ds = DataStore.Instance;
    private Region? _reg;
    private HotspotSource? _src;
    private HotspotStatus? _st;
    private int _page;

    public SemuaHotspotPage()
    {
        InitializeComponent();
        CmbRegion.ItemsSource = DataStore.Regions;
        CmbRegion.SelectedIndex = 0;
        CmbSrc.SelectedIndex = 0;
        CmbStatus.SelectedIndex = 0;
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    private void Refresh()
    {
        var all = ds.Filter(_reg, _src, _st).OrderByDescending(h => h.DetectedAt).ToList();
        int pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, pages - 1);
        GridHotspots.ItemsSource = all.Skip(_page * PageSize).Take(PageSize).ToList();
        TxtPage.Text = $"Halaman {_page + 1} dari {pages} · {all.Count} hotspot";
        BtnPrev.IsEnabled = _page > 0;
        BtnNext.IsEnabled = _page < pages - 1;
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
        _page = 0;
        Refresh();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        Refresh();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        Refresh();
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (HotspotActions.From(sender) is { } h)
            HotspotActions.ShowDetail(Window.GetWindow(this), h);
    }
}

