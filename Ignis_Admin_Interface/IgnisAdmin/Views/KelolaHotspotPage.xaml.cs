using System.Windows;
using System.Windows.Controls;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

public partial class KelolaHotspotPage : UserControl
{
    readonly DataStore ds = DataStore.Instance;
    Region? _reg; HotspotSource? _src; HotspotStatus? _st;
    Window? Owner => Window.GetWindow(this);

    public KelolaHotspotPage()
    {
        InitializeComponent();
        CmbRegion.ItemsSource = DataStore.Regions;
        CmbRegion.SelectedIndex = 0; CmbSrc.SelectedIndex = 0; CmbStatus.SelectedIndex = 0;
        Map.HotspotClicked += id => { var h = ds.Hotspots.FirstOrDefault(x => x.Id == id); if (h != null) HotspotActions.ShowDetail(Owner, h); };
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    void Refresh()
    {
        CTotal.Value = ds.Hotspots.Count.ToString();
        CFirms.Value = ds.Hotspots.Count(h => !h.IsManual).ToString();
        CManual.Value = ds.Hotspots.Count(h => h.IsManual).ToString();
        var list = ds.Filter(_reg, _src, _st).ToList();
        GridHotspots.ItemsSource = list;
        Map.SetHotspots(list);
        TxtCount.Text = $"{list.Count} hotspot ditampilkan";
    }

    void ReadFilter()
    {
        _reg = CmbRegion.SelectedItem as Region; if (_reg?.IsAll == true) _reg = null;
        _src = CmbSrc.SelectedIndex switch { 1 => HotspotSource.Satellite, 2 => HotspotSource.Manual, _ => (HotspotSource?)null };
        _st = CmbStatus.SelectedIndex switch { 1 => HotspotStatus.Active, 2 => HotspotStatus.Monitoring, 3 => HotspotStatus.Resolved, _ => (HotspotStatus?)null };
    }
    void Apply_Click(object sender, RoutedEventArgs e) { ReadFilter(); Refresh(); }
    void Clear_Click(object sender, RoutedEventArgs e) { CmbRegion.SelectedIndex = 0; CmbSrc.SelectedIndex = 0; CmbStatus.SelectedIndex = 0; ReadFilter(); Refresh(); }

    void Add_Click(object sender, RoutedEventArgs e) => HotspotActions.Add(Owner);
    void Detail_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Owner, h); }
    void Edit_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Edit(Owner, h); }
    void Delete_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Delete(Owner, h); }
}
