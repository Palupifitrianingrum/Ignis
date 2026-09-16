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

        [ObservableProperty]
        private string headerTitle = "Nice to see you again";

        public event Action<bool>? RequestClose;

        public LoginWindowViewModel(LoginFormViewModel loginFormVm, RegisterViewModel registerVm)
        {
            loginFormVm.LoginCompleted += (success) => RequestClose?.Invoke(success);
            loginFormVm.GoToRegisterRequested += () =>
            {
                CurrentContent = registerVm;
                HeaderTitle = "Welcome to Ignis";   
            };
            registerVm.GoToLoginRequested += () =>
            {
                CurrentContent = loginFormVm;
                HeaderTitle = "Nice to see you again!";
            };

            currentContent = loginFormVm;
        }
    }
}
