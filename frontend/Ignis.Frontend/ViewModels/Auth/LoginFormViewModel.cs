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
            ErrorMessage = null;
            IsLoading = true;

            await Task.Delay(500);

            if (Username == "admin" && Password == "admin")
            {
                IsLoading = false;
                LoginCompleted?.Invoke(true);
            }
            else
            {
                IsLoading = false;
                ErrorMessage = "Username atau password salah";
            }
        }

        private async Task LoginWithGoogleAsync()
        {
            ErrorMessage = null;
            IsLoading = true;
            await Task.Delay(500); // simulasi proses OAuth (nanti diganti alur OAuth beneran)

            // TODO: nanti diganti dengan:
            // 1. Buka browser/webview ke halaman consent Google
            // 2. Terima authorization code
            // 3. Kirim code itu ke backend buat ditukar jadi token
            // 4. Backend balikin data user

            IsLoading = false;

            // Untuk sekarang, anggap selalu sukses (dummy)
            LoginCompleted?.Invoke(true);
        }
    }
}
