using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ignis.Frontend.Views.Shared
{
    /// <summary>
    /// Interaction logic for IconTextBox.xaml
    /// </summary>
    public partial class IconTextBox : UserControl
    {
        public IconTextBox()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty InputTextProperty = DependencyProperty.Register(nameof(InputText), typeof(string), typeof(IconTextBox), 
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string InputText
        {
            get => (string)GetValue(InputTextProperty);
            set => SetValue(InputTextProperty, value);
        }

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(IconTextBox));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }
    }
}
