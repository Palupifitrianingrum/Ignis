using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Services.Admin;
using Microsoft.Win32;

namespace Ignis.Frontend.Views.Admin;

public partial class RiwayatPage : UserControl
{
    private readonly DataStore ds = DataStore.Instance;
    private List<Prediction> _shown = new();
    private int _risk;
    private DateTime? _from, _to;

    public RiwayatPage()
    {
        InitializeComponent();
        CmbRisk.SelectedIndex = 0;
        Loaded += (_, _) => { ds.Changed -= Refresh; ds.Changed -= Refresh; Refresh(); };
        Unloaded += (_, _) => ds.Changed -= Refresh;
    }

    private void Refresh()
    {
        _shown = ds.Predictions.Where(p =>
            (_risk == 0 || p.Risk == (RiskLevel)(_risk - 1)) &&
            (_from == null || p.CreatedAt.Date >= _from.Value.Date) &&
            (_to == null || p.CreatedAt.Date <= _to.Value.Date))
            .OrderByDescending(p => p.CreatedAt).ToList();
        GridPred.ItemsSource = _shown;
        TxtCount.Text = $"Hasil Prediksi ({_shown.Count})";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _risk = CmbRisk.SelectedIndex;
        _from = DpFrom.SelectedDate;
        _to = DpTo.SelectedDate;
        Refresh();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        CmbRisk.SelectedIndex = 0;
        DpFrom.SelectedDate = null;
        DpTo.SelectedDate = null;
        Apply_Click(sender, e);
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Prediction p) return;
        var h = ds.Hotspots.FirstOrDefault(x => x.Id == p.HotspotId);
        if (h == null)
        {
            MessageBox.Show($"Hotspot {p.HotspotId} sudah dihapus.", "Riwayat Prediksi");
            return;
        }
        HotspotActions.ShowDetail(Window.GetWindow(this), h);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "riwayat-prediksi.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder("id,hotspot,arah_derajat,kecepatan_kmjam,luas_ha,waktu_jam,risiko,dibuat\n");
        foreach (var p in _shown)
        {
            sb.AppendLine(string.Join(",", p.Id, p.HotspotId, Fmt.Inv(p.Direction, "0"), Fmt.Inv(p.SpeedKmH, "0.00"), Fmt.Inv(p.AreaHa, "0.0"), Fmt.Inv(p.Hours, "0"), p.RiskText, p.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        }
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show($"{_shown.Count} baris diekspor.", "Ekspor CSV");
    }
}

