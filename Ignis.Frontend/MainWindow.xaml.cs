using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ignis.Frontend.ViewModels;

namespace Ignis.Frontend;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewMouseDown += OnWindowPreviewMouseDown;
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsProfileMenuOpen)
            return;

        if (e.OriginalSource is DependencyObject source &&
            (IsDescendantOf(source, ProfileMenuCard) || IsDescendantOf(source, ProfileButton)))
        {
            return;
        }

        vm.IsProfileMenuOpen = false;
    }

    private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
    {
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}