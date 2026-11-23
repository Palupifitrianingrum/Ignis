using System.Windows;
using System.Windows.Controls;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.ViewModels;
using Ignis.Frontend.Views.Admin;

namespace Ignis.Frontend.Views.Dashboard;

/// <summary>Panel visualisasi simulasi penyebaran api: arah rambatan, luas per jam, grafik, dan legenda warna peta.</summary>
public partial class SimulationPanel : UserControl
{
    public SimulationPanel()
    {
        InitializeComponent();
    }

    /// <summary>Buka detail hotspot yang sedang disimulasikan (pengganti klik pin yang kini memilih target simulasi).</summary>
    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedSimulationHotspot is not { } dto) return;

        new HotspotDetailWindow(new HotspotRow(dto)) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
