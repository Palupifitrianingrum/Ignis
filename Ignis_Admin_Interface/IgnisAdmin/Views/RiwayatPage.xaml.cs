using System.Text;
using System.Windows;
using System.Windows.Controls;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
using Microsoft.Win32;
namespace IgnisAdmin.Views;

public partial class RiwayatPage : UserControl
{
    readonly DataStore ds = DataStore.Instance;
    List<Prediction> _shown = new();
    int _risk; DateTime? _from, _to;

    public RiwayatPage()
    {
        InitializeComponent();
        CmbRisk.SelectedIndex = 0;
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    void Refresh()
    {
        _shown = ds.Predictions.Where(p =>
            (_risk == 0 || p.Risk == (RiskLevel)(_risk - 1)) &&
            (_from == null || p.CreatedAt.Date >= _from.Value.Date) &&
            (_to == null || p.CreatedAt.Date <= _to.Value.Date))
            .OrderByDescending(p => p.CreatedAt).ToList();
        GridPred.ItemsSource = _shown;
        TxtCount.Text = $"Hasil Prediksi ({_shown.Count})";
    }

    void Apply_Click(object sender, RoutedEventArgs e) { _risk = CmbRisk.SelectedIndex; _from = DpFrom.SelectedDate; _to = DpTo.SelectedDate; Refresh(); }
    void Reset_Click(object sender, RoutedEventArgs e) { CmbRisk.SelectedIndex = 0; DpFrom.SelectedDate = null; DpTo.SelectedDate = null; Apply_Click(sender, e); }

    void View_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Prediction p) return;
        var h = ds.Hotspots.FirstOrDefault(x => x.Id == p.HotspotId);
        if (h == null) { MessageBox.Show($"Hotspot {p.HotspotId} sudah dihapus.", "Riwayat Prediksi"); return; }
        HotspotActions.ShowDetail(Window.GetWindow(this), h);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "riwayat-prediksi.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder("id,hotspot,arah_derajat,kecepatan_kmjam,luas_ha,waktu_jam,risiko,dibuat\n");
        foreach (var p in _shown)
            sb.AppendLine(string.Join(",", p.Id, p.HotspotId, Fmt.Inv(p.Direction, "0"), Fmt.Inv(p.SpeedKmH, "0.00"), Fmt.Inv(p.AreaHa, "0.0"), Fmt.Inv(p.Hours, "0"), p.RiskText, p.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show($"{_shown.Count} baris diekspor.", "Ekspor CSV");
    }
}
