using System.Windows.Controls;
using System.Windows.Media;
namespace IgnisAdmin.Controls;
public partial class InfoRow : UserControl
{
    public InfoRow() { InitializeComponent(); }
    public string Label { get => LText.Text; set => LText.Text = value; }
    public string Value { get => VText.Text; set => VText.Text = value; }
    public Brush ValueBrush { set => VText.Foreground = value; }
}
