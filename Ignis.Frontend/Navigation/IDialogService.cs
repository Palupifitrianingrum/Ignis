using System;

namespace Ignis.Frontend.Navigation
{
    public interface IDialogService
    {
        bool? ShowLoginDialog();
        void ShowAdminWindow();
        void ShowSettingsWindow();
        void ShowMessage(string message);
    }
}
