using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Navigation;

namespace Ignis.Frontend.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private bool isLoggedIn;
        public MainWindowViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;
        }

        [RelayCommand]
        private void OpenLogin()
        {
            bool? result = _dialogService.ShowLoginDialog();
            if (result == true)
            {
                // Handle successful login
            }
            else
            {
                // Handle login cancellation or failure
            }
        }
    }
}
