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
    /// Interaction logic for IconPasswordBox.xaml
    /// </summary>
    public partial class IconPasswordBox : UserControl
    {
        public event Action<string> PasswordChanged;
        public IconPasswordBox()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(IconPasswordBox));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public static readonly DependencyProperty HasPasswordProperty =
        DependencyProperty.Register(nameof(HasPassword), typeof(bool), typeof(IconPasswordBox), new PropertyMetadata(false));

        public bool HasPassword
        {
            get => (bool)GetValue(HasPasswordProperty);
            private set => SetValue(HasPasswordProperty, value);
        }

        private void InnerPassowrdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            HasPassword = InnerPassowrdBox.Password.Length > 0;
            PasswordChanged?.Invoke(InnerPassowrdBox.Password);
        }
    }
}
