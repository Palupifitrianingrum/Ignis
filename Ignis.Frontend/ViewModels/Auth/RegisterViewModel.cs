using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Services.Backend;

namespace Ignis.Frontend.ViewModels.Auth
{
    public partial class RegisterViewModel : ObservableObject
    {
        private readonly IgnisApiClient _api;

        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string email = string.Empty;
        [ObservableProperty] private string password = string.Empty;
        [ObservableProperty] private string confirmPassword = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string? errorMessage;

        public event Action? GoToLoginRequested;

        public RegisterViewModel(IgnisApiClient api) => _api = api;

        [RelayCommand]
        private async Task RegisterAsync()
        {
            ErrorMessage = null;

            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "All field must be filled";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Password is not the same";
                return;
            }

            IsLoading = true;
            try
            {
                await _api.RegisterAsync(Name.Trim(), Email.Trim(), Password);
                GoToLoginRequested?.Invoke();   // akun jadi; lanjut login
            }
            catch (BackendException ex)
            {
                ErrorMessage = ex.Message;   // mis. "Password minimal 8 karakter." / "Email sudah terdaftar."
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void GoToLogin()
        {
            GoToLoginRequested?.Invoke();
        }
    }
}
