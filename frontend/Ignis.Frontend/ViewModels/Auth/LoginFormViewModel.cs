using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ignis.Frontend.ViewModels.Auth
{
    public partial class LoginFormViewModel : ObservableObject
    {
        [ObservableProperty] private string username = string.Empty;
        [ObservableProperty] private string password = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string? errorMessage;

        public event Action<bool>? LoginCompleted;

        [RelayCommand]
        private async Task LoginAsync()
        {
            errorMessage = null;
            isLoading = true;

            await Task.Delay(500);

            if (username == "admin" && password == "admin")
            {
                isLoading = false;
                LoginCompleted?.Invoke(true);
            }
            else
            {
                isLoading = false;
                errorMessage = "Username atau password salah";
            }
        }
    }
}
