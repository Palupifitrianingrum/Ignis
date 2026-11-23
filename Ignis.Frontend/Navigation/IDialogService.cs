using System;
using Ignis.Frontend.Models;

namespace Ignis.Frontend.Navigation
{
    public interface IDialogService
    {
        bool? ShowLoginDialog();
        void ShowAdminWindow();
        void ShowSettingsWindow();
        void ShowMessage(string message);
        /// <summary>Tanya ya/tidak. True bila pengguna memilih Ya.</summary>
        bool Confirm(string message, string title);
        /// <summary>Dialog tambah/ubah lokasi tersimpan. Null bila dibatalkan.</summary>
        SavedLocationDialogResult? ShowSavedLocationDialog(SavedLocationDraft draft);
    }
}
