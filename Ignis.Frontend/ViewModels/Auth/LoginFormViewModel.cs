using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Services.Backend;

namespace Ignis.Frontend.ViewModels.Auth
{
    public partial class LoginFormViewModel : ObservableObject
    {
        private readonly IgnisApiClient _api;

        [ObservableProperty] private string email = string.Empty;
        [ObservableProperty] private string password = string.Empty;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private string? errorMessage;

        public event Action<bool>? LoginCompleted;
        public event Action? GoToRegisterRequested;

        public LoginFormViewModel(IgnisApiClient api) => _api = api;

        [RelayCommand]
        private async Task LoginAsync()
        {
            ErrorMessage = null;
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "Email dan password wajib diisi";
                return;
            }

            IsLoading = true;
            try
            {
                await _api.LoginAsync(Email.Trim(), Password);
                LoginCompleted?.Invoke(true);
            }
            catch (BackendException ex)
            {
                ErrorMessage = ex.Message;   // mis. "Email atau password salah." / server tidak terjangkau
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Alur: (1) minta Client ID ke server, (2) buka browser ke halaman izin Google dan tunggu kode di alamat loopback,
        /// (3) kirim kode ke server yang menukarnya dan menerbitkan token Ignis. Lihat GoogleSignIn dan docs/google-oauth.md.
        /// </summary>
        [RelayCommand]
        private async Task LoginWithGoogleAsync()
        {
            ErrorMessage = null;
            IsLoading = true;
            try
            {
                var clientId = await _api.GetGoogleClientIdAsync();
                if (string.IsNullOrEmpty(clientId))
                {
                    ErrorMessage = "Login Google belum dikonfigurasi di server";
                    return;
                }

                var result = await GoogleSignIn.RunAsync(clientId);
                await _api.LoginWithGoogleAsync(result.Code, result.CodeVerifier, result.RedirectUri);
                LoginCompleted?.Invoke(true);
            }
            catch (BackendException ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void GoToRegister()
        {
            GoToRegisterRequested?.Invoke();
        }
    }
}
