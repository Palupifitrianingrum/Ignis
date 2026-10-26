using System.Windows;
using System.Windows.Controls;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

public partial class SemuaHotspotPage : UserControl
{
    const int PageSize = 10;
    readonly DataStore ds = DataStore.Instance;
    Region? _reg; HotspotSource? _src; HotspotStatus? _st; int _page;

    public SemuaHotspotPage()
    {
        InitializeComponent();
        CmbRegion.ItemsSource = DataStore.Regions;
        CmbRegion.SelectedIndex = 0; CmbSrc.SelectedIndex = 0; CmbStatus.SelectedIndex = 0;
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    void Refresh()
    {
        var all = ds.Filter(_reg, _src, _st).OrderByDescending(h => h.DetectedAt).ToList();
        int pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, pages - 1);
        GridHotspots.ItemsSource = all.Skip(_page * PageSize).Take(PageSize).ToList();
        TxtPage.Text = $"Halaman {_page + 1} dari {pages} · {all.Count} hotspot";
        BtnPrev.IsEnabled = _page > 0; BtnNext.IsEnabled = _page < pages - 1;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        _reg = CmbRegion.SelectedItem as Region; if (_reg?.IsAll == true) _reg = null;
        _src = CmbSrc.SelectedIndex switch { 1 => HotspotSource.Satellite, 2 => HotspotSource.Manual, _ => (HotspotSource?)null };
        _st = CmbStatus.SelectedIndex switch { 1 => HotspotStatus.Active, 2 => HotspotStatus.Monitoring, 3 => HotspotStatus.Resolved, _ => (HotspotStatus?)null };
        _page = 0; Refresh();
    }
    void Prev_Click(object sender, RoutedEventArgs e) { _page--; Refresh(); }
    void Next_Click(object sender, RoutedEventArgs e) { _page++; Refresh(); }
    void Detail_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Window.GetWindow(this), h); }
}
