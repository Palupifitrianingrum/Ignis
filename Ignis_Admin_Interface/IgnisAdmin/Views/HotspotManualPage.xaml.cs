using System.Windows;
using System.Windows.Controls;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
namespace IgnisAdmin.Views;

public partial class HotspotManualPage : UserControl
{
    readonly DataStore ds = DataStore.Instance;
    Window? Owner => Window.GetWindow(this);

    public HotspotManualPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    void Refresh()
    {
        var list = ds.Hotspots.Where(h => h.IsManual).OrderByDescending(h => h.DetectedAt).ToList();
        CTotal.Value = list.Count.ToString();
        CActive.Value = list.Count(h => h.Status != HotspotStatus.Resolved).ToString();
        CDone.Value = list.Count(h => h.Status == HotspotStatus.Resolved).ToString();
        GridManual.ItemsSource = list;
    }

    void Add_Click(object sender, RoutedEventArgs e) => HotspotActions.Add(Owner);
    void Detail_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.ShowDetail(Owner, h); }
    void Edit_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Edit(Owner, h); }
    void Delete_Click(object sender, RoutedEventArgs e) { if (HotspotActions.From(sender) is { } h) HotspotActions.Delete(Owner, h); }
}
