using System.Windows.Controls;
using System.Windows.Media;
namespace IgnisAdmin.Controls;
public partial class StatCard : UserControl
{
    public StatCard() { InitializeComponent(); }
    public string Label { get => LText.Text; set => LText.Text = value; }
    public string Value { get => VText.Text; set => VText.Text = value; }
    public Brush Accent { get => Bar.Background; set => Bar.Background = value; }
}
