using Ignis.Frontend.ViewModels.Auth;
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

namespace Ignis.Frontend.Views.Auth
{
    /// <summary>
    /// Interaction logic for RegisterView.xaml
    /// </summary>
    public partial class RegisterView : UserControl
    {
        public RegisterView()
        {
            InitializeComponent();
        }

        public RegisterView(RegisterViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void PasswordInput_PasswordChanged(string password)
        {
            if (DataContext is RegisterViewModel vm) 
                vm.Password = password;
        }

        private void ConfirmPasswordInput_PasswordChanged(string password)
        {
            if (DataContext is RegisterViewModel vm)
                vm.ConfirmPassword = password;
        }
    }
}
