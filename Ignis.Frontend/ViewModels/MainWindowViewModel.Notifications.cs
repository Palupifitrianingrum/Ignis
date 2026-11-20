using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Models;
using Ignis.Frontend.Services.Backend;

namespace Ignis.Frontend.ViewModels
{
    /// <summary>
    /// Notifikasi dampak kebakaran untuk pengguna terdaftar: lonceng dengan jumlah belum dibaca, panel daftar, dan banner saat ada yang baru.
    /// Aplikasi menanyakan server tiap menit (server yang menilai dampak simulasi pada lokasi tersimpan dan membuat notifikasinya).
    /// Banner mengikuti pengaturan "Tampilkan notifikasi"; lonceng dan daftar selalu tampil.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private static readonly TimeSpan NotificationPollInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(9);

        private readonly DispatcherTimer _notificationTimer = new() { Interval = NotificationPollInterval };
        private readonly DispatcherTimer _toastTimer = new() { Interval = ToastDuration };
        private readonly HashSet<string> _seenNotificationIds = new();
        private bool _notificationsPrimed;
        private NotificationItem? _toastItem;

        public ObservableCollection<NotificationItem> Notifications { get; } = new();

        [ObservableProperty, NotifyPropertyChangedFor(nameof(HasUnreadNotifications)), NotifyPropertyChangedFor(nameof(UnreadBadgeText))]
        private int unreadNotificationCount;

        [ObservableProperty] private bool isNotificationsOpen;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCheckNotifications))] private bool isCheckingNotifications;

        public bool CanCheckNotifications => !IsCheckingNotifications;

        // ---- Banner ----
        [ObservableProperty] private bool isToastVisible;
        [ObservableProperty] private string toastTitle = string.Empty;
        [ObservableProperty] private string toastMessage = string.Empty;
        [ObservableProperty] private string toastColorHex = "#16A34A";
        [ObservableProperty] private string toastBackgroundHex = "#F0FDF4";

        public bool HasUnreadNotifications => UnreadNotificationCount > 0;
        public string UnreadBadgeText => UnreadNotificationCount > 9 ? "9+" : UnreadNotificationCount.ToString();
        public bool HasNoNotifications => Notifications.Count == 0;

        private bool NotificationBannersEnabled => _workspace.Settings.NotificationsEnabled;

        private void InitNotifications()
        {
            _notificationTimer.Tick += async (_, _) => await RefreshNotificationsAsync(showToast: true);
            _toastTimer.Tick += (_, _) => DismissToast();
            Notifications.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoNotifications));
        }

        // =====================================================================
        // Siklus pengguna
        // =====================================================================

        /// <summary>Mulai fitur pengguna terdaftar setelah login: muat lokasi, ambil notifikasi, dan mulai memeriksa berkala.</summary>
        private async Task StartUserFeaturesAsync()
        {
            CanUseSavedLocations = _api.IsRegisteredUser;
            if (!CanUseSavedLocations)
            {
                StopUserFeatures();
                return;
            }

            await LoadSavedLocationsAsync();
            await RefreshNotificationsAsync(showToast: true);
            _notificationTimer.Start();
        }

        /// <summary>Logout atau akun bukan pengguna terdaftar: hentikan pemeriksaan dan kosongkan semuanya.</summary>
        private void StopUserFeatures()
        {
            _notificationTimer.Stop();
            DismissToast();
            _seenNotificationIds.Clear();
            _notificationsPrimed = false;
            IsNotificationsOpen = false;
            IsPickingLocation = false;
            _pendingDraft = null;
            Notifications.Clear();
            UnreadNotificationCount = 0;
            SavedLocations.Clear();
            SavedLocationsCount = 0;
            CanUseSavedLocations = false;
        }

        // =====================================================================
        // Mengambil
        // =====================================================================

        /// <summary>Ambil notifikasi dari server. Mengembalikan jumlah notifikasi belum dibaca yang baru muncul sejak pengambilan terakhir.</summary>
        private async Task<int> RefreshNotificationsAsync(bool showToast)
        {
            if (!_api.IsRegisteredUser) return 0;

            try
            {
                var list = await _api.GetNotificationsAsync();
                var items = list.Items.Select(d => new NotificationItem(d)).ToList();
                var fresh = items.Where(i => i.IsUnread && !_seenNotificationIds.Contains(i.Id)).ToList();

                Notifications.Clear();
                foreach (var i in items) Notifications.Add(i);
                UnreadNotificationCount = list.Unread;
                foreach (var i in items) _seenNotificationIds.Add(i.Id);
                UpdateUnreadPerLocation();

                if (showToast && NotificationBannersEnabled)
                {
                    if (!_notificationsPrimed)
                    {
                        // Pertama kali setelah login: ringkas yang belum dibaca, jangan menampilkan banner satu per satu.
                        if (list.Unread > 0) ShowToastForSummary(items.Where(i => i.IsUnread).ToList());
                    }
                    else if (fresh.Count > 0)
                    {
                        ShowToastForSummary(fresh);
                    }
                }

                _notificationsPrimed = true;
                return fresh.Count;
            }
            catch (BackendException)
            {
                return 0;   // server tidak terjangkau: lonceng tetap menampilkan keadaan terakhir
            }
        }

        private void UpdateUnreadPerLocation()
        {
            var unreadByArea = Notifications.Where(n => n.IsUnread && n.AreaId != null).GroupBy(n => n.AreaId!).ToDictionary(g => g.Key, g => g.Count());
            foreach (var location in SavedLocations) location.UnreadCount = unreadByArea.GetValueOrDefault(location.Id);
        }

        // =====================================================================
        // Banner
        // =====================================================================

        private void ShowToastForSummary(IReadOnlyList<NotificationItem> items)
        {
            var worst = items.OrderByDescending(i => i.Level).ThenByDescending(i => i.Dto.CreatedAt).First();
            _toastItem = worst;
            var title = items.Count == 1 ? worst.Title : $"{items.Count} notifikasi: {worst.Title}";
            ShowToast(title, Shorten(worst.Message, 150), worst.Level, worst);
        }

        private void ShowToast(string title, string message, int level, NotificationItem? source = null)
        {
            _toastItem = source;
            ToastTitle = title;
            ToastMessage = message;
            (ToastColorHex, ToastBackgroundHex) = level switch
            {
                3 => ("#DC2626", "#FEF2F2"),
                2 => ("#F97316", "#FFF7ED"),
                1 => ("#CA8A04", "#FEFCE8"),
                _ => ("#16A34A", "#F0FDF4")
            };
            IsToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        [RelayCommand]
        private void DismissToast()
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        }

        [RelayCommand]
        private async Task OpenToast()
        {
            var item = _toastItem;
            DismissToast();
            if (item != null) await OpenNotification(item);
        }

        private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

        // =====================================================================
        // Panel lonceng
        // =====================================================================

        [RelayCommand]
        private async Task ToggleNotifications()
        {
            IsProfileMenuOpen = false;
            IsNotificationsOpen = !IsNotificationsOpen;
            if (IsNotificationsOpen) await RefreshNotificationsAsync(showToast: false);
        }

        [RelayCommand]
        private async Task OpenNotification(NotificationItem? item)
        {
            if (item == null) return;
            IsNotificationsOpen = false;
            DismissToast();

            if (item.IsUnread)
            {
                try
                {
                    await _api.MarkNotificationReadAsync(item.Id);
                    item.IsRead = true;
                    UnreadNotificationCount = Math.Max(0, UnreadNotificationCount - 1);
                    UpdateUnreadPerLocation();
                }
                catch (BackendException)
                {
                    // Gagal menandai dibaca bukan alasan menahan pengguna melihat peta.
                }
            }

            // Tunjukkan di peta: fokus ke hotspot (atau lokasi bila hotspot tidak ada di daftar) dan jalankan simulasinya.
            var hotspot = _allHotspots.FirstOrDefault(h => h.Id == item.HotspotId);
            var location = SavedLocations.FirstOrDefault(l => l.Id == item.AreaId);
            if (hotspot != null) FocusLocationRequested?.Invoke(hotspot.Latitude, hotspot.Longitude, 12);
            else if (location != null) FocusLocationRequested?.Invoke(location.Latitude, location.Longitude, 12);

            await SelectSimulationTargetAsync(item.HotspotId);
        }

        [RelayCommand]
        private async Task MarkAllNotificationsRead()
        {
            try
            {
                await _api.MarkAllNotificationsReadAsync();
                foreach (var n in Notifications) n.IsRead = true;
                UnreadNotificationCount = 0;
                UpdateUnreadPerLocation();
            }
            catch (BackendException ex)
            {
                _dialogService.ShowMessage(ex.Message);
            }
        }

        [RelayCommand]
        private async Task DeleteNotification(NotificationItem? item)
        {
            if (item == null) return;
            try
            {
                await _api.DeleteNotificationAsync(item.Id);
                Notifications.Remove(item);
                if (item.IsUnread) UnreadNotificationCount = Math.Max(0, UnreadNotificationCount - 1);
                UpdateUnreadPerLocation();
            }
            catch (BackendException ex)
            {
                _dialogService.ShowMessage(ex.Message);
            }
        }

        /// <summary>Minta server memeriksa lokasi saya sekarang, lalu tampilkan hasilnya.</summary>
        [RelayCommand]
        private async Task CheckNotificationsNow()
        {
            if (IsCheckingNotifications || !_api.IsRegisteredUser) return;
            IsCheckingNotifications = true;
            try
            {
                var result = await _api.CheckNotificationsAsync();
                var fresh = await RefreshNotificationsAsync(showToast: false);

                if (result.AreasChecked == 0)
                    ShowToast("Belum ada lokasi", "Tambahkan lokasi (mis. rumah) di sidebar agar Anda bisa diberi tahu bila api atau asap mendekat.", 0);
                else if (fresh == 0)
                    ShowToast("Pemeriksaan selesai", $"{result.AreasChecked} lokasi diperiksa ({result.HotspotsSimulated} hotspot disimulasikan). Tidak ada ancaman baru.", 0);
                else
                    ShowToastForSummary(Notifications.Where(n => n.IsUnread).Take(fresh).ToList());
            }
            catch (BackendException ex)
            {
                ShowToast("Pemeriksaan gagal", ex.Message, 2);
            }
            finally
            {
                IsCheckingNotifications = false;
            }
        }
    }
}
