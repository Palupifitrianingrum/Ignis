using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ignis.Frontend.Views.Admin;
using Ignis.Frontend.Views.Auth;
using Ignis.Frontend.Views.Dashboard;

namespace Ignis.Frontend.Navigation
{
    public class DialogService : IDialogService
    {
        private readonly IServiceProvider _serviceProvider;

        public DialogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public bool? ShowLoginDialog()
        {
            var loginWindow = _serviceProvider.GetRequiredService<LoginView>();
            return loginWindow.ShowDialog();
        }

        public void ShowAdminWindow()
        {
            var adminWindow = _serviceProvider.GetRequiredService<AdminMainWindow>();
            adminWindow.Show();
        }

        public void ShowSettingsWindow()
        {
            var settingsWindow = _serviceProvider.GetRequiredService<UserSettingsWindow>();
            if (Application.Current?.MainWindow is { IsVisible: true } main)
            {
                settingsWindow.Owner = main;
            }
            settingsWindow.ShowDialog();
        }

        public void ShowMessage(string message) =>
            MessageBox.Show(message, "Ignis", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
