using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ignis.Frontend.ViewModels.Auth
{
    public partial class RegisterViewModel : ObservableObject
    {
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string email = string.Empty;
        [ObservableProperty] private string password = string.Empty;
        [ObservableProperty] private string confirmPassword = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string? errorMessage;

        public event Action? GoToLoginRequested;

        [RelayCommand]
        private async Task RegisterAsync()
        {
            ErrorMessage = null;

            if(string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "All field must be filled";
                return;
            }

            if(Password != ConfirmPassword)
            {
                ErrorMessage = "Password is not the same";
                return;
            }

            IsLoading = true;

            await Task.Delay(500);

            IsLoading = false;
            GoToLoginRequested?.Invoke();
        }

        [RelayCommand]
        private void GoToLogin()
        {
            GoToLoginRequested?.Invoke();
        }
    }
}
