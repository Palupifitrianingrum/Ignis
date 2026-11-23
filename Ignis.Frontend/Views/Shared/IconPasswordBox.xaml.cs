using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ignis.Frontend.Views.Shared
{
    /// <summary>
    /// Interaction logic for IconPasswordBox.xaml
    /// </summary>
    public partial class IconPasswordBox : UserControl
    {
        public event Action<string>? PasswordChanged;
        public event Action? EnterPressed;
        private bool _syncing;

        public IconPasswordBox()
        {
            InitializeComponent();
        }

        public void FocusInput()
        {
            if (IsPasswordVisible)
            {
                InnerVisibleTextBox.Focus();
                InnerVisibleTextBox.CaretIndex = InnerVisibleTextBox.Text.Length;
            }
            else
            {
                InnerPassowrdBox.Focus();
            }
        }

        private void InnerInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                EnterPressed?.Invoke();
            }
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

        public static readonly DependencyProperty IsPasswordVisibleProperty =
            DependencyProperty.Register(nameof(IsPasswordVisible), typeof(bool), typeof(IconPasswordBox), new PropertyMetadata(false));

        public bool IsPasswordVisible
        {
            get => (bool)GetValue(IsPasswordVisibleProperty);
            set => SetValue(IsPasswordVisibleProperty, value);
        }

        private void InnerPassowrdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            InnerVisibleTextBox.Text = InnerPassowrdBox.Password;
            _syncing = false;

            HasPassword = InnerPassowrdBox.Password.Length > 0;
            PasswordChanged?.Invoke(InnerPassowrdBox.Password);
        }

        private void InnerVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            InnerPassowrdBox.Password = InnerVisibleTextBox.Text;
            _syncing = false;

            HasPassword = InnerVisibleTextBox.Text.Length > 0;
            PasswordChanged?.Invoke(InnerVisibleTextBox.Text);
        }

        private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            IsPasswordVisible = !IsPasswordVisible;
            FocusInput();
        }
    }
}
