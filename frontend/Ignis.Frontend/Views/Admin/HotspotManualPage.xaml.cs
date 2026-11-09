using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;

namespace Ignis.Frontend.Views.Admin;

public partial class HotspotManualPage : UserControl
{
    private readonly DataStore ds = DataStore.Instance;
    private Window? Owner => Window.GetWindow(this);

    public HotspotManualPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    private void Refresh()
    {
        var list = ds.Hotspots.Where(h => h.IsManual).OrderByDescending(h => h.DetectedAt).ToList();
        CTotal.Value = list.Count.ToString();
        CActive.Value = list.Count(h => h.Status != HotspotStatus.Resolved).ToString();
        CDone.Value = list.Count(h => h.Status == HotspotStatus.Resolved).ToString();
        GridManual.ItemsSource = list;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => HotspotActions.Add(Owner);
    private void Detail_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Owner, h); }
    private void Edit_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Edit(Owner, h); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Delete(Owner, h); }
}

