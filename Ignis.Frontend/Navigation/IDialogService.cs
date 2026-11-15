using System;

namespace Ignis.Frontend.Navigation
{
    public interface IDialogService
    {
        bool? ShowLoginDialog();
        void ShowAdminWindow();
        void ShowMessage(string message);
    }
}
