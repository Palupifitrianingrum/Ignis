using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ignis.Frontend.ViewModels.Auth
{
    public partial class LoginWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private object currentContent = null!;

        public event Action<bool>? RequestClose;

        public LoginWindowViewModel(LoginFormViewModel loginFormVm)
        {
            loginFormVm.LoginCompleted += (success) => RequestClose?.Invoke(success);

            currentContent = loginFormVm;
        }

    }
}
