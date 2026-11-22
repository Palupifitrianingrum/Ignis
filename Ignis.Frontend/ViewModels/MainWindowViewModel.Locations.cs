using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Models;
using Ignis.Frontend.Services.Backend;
using Ignis.Shared;

namespace Ignis.Frontend.ViewModels
{
    /// <summary>
    /// Lokasi tersimpan pengguna (mis. rumah): daftar di sidebar, tambah/ubah/hapus lewat dialog, memilih titik langsung di peta,
    /// dan status dampak simulasi yang sedang dilihat. Semua data milik server; di sini hanya presentasi dan alur dialog.
    /// Hanya untuk akun pengguna terdaftar (admin tidak punya lokasi tersimpan, sesuai ERD).
    /// </summary>
    public partial class MainWindowViewModel
    {
        public ObservableCollection<SavedLocationItem> SavedLocations { get; } = new();

        [ObservableProperty] private bool canUseSavedLocations;

        [ObservableProperty] private bool isSavedLocationsExpanded = true;

        /// <summary>True saat peta menunggu pengguna mengklik titik lokasinya.</summary>
        [ObservableProperty] private bool isPickingLocation;

        [RelayCommand]
        private void ToggleSavedLocations() => IsSavedLocationsExpanded = !IsSavedLocationsExpanded;

        private SavedLocationDraft? _pendingDraft;

        /// <summary>Daftar kosong (pesan ajakan menambah lokasi pertama).</summary>
        public bool HasNoSavedLocations => SavedLocationsCount == 0;

        /// <summary>Sudah login tetapi akunnya bukan pengguna terdaftar (admin): jelaskan bahwa fitur ini bukan untuknya.</summary>
        public bool ShowLocationsHint => IsLoggedIn && !CanUseSavedLocations;

        partial void OnSavedLocationsCountChanged(int value) => OnPropertyChanged(nameof(HasNoSavedLocations));

        partial void OnCanUseSavedLocationsChanged(bool value) => OnPropertyChanged(nameof(ShowLocationsHint));

        partial void OnIsLoggedInChanged(bool value) => OnPropertyChanged(nameof(ShowLocationsHint));

        // =====================================================================
        // Memuat
        // =====================================================================

        private async Task LoadSavedLocationsAsync()
        {
            CanUseSavedLocations = _api.IsRegisteredUser;
            if (!CanUseSavedLocations)
            {
                SavedLocations.Clear();
                SavedLocationsCount = 0;
                RaiseMapDataChanged();
                return;
            }

            try
            {
                var list = await _api.GetMonitoringAreasAsync();
                SavedLocations.Clear();
                foreach (var a in list) SavedLocations.Add(new SavedLocationItem(a));
                SavedLocationsCount = SavedLocations.Count;
                ApplyImpactsFromCurrentSimulation();
                UpdateUnreadPerLocation();
                RaiseMapDataChanged();
            }
            catch (BackendException)
            {
                // Server tidak terjangkau: biarkan daftar apa adanya; dashboard utama sudah menampilkan masalah koneksinya.
            }
        }

        /// <summary>Tampilkan dampak simulasi yang sedang dipilih pada tiap lokasi tersimpan (atau kosongkan bila tidak ada simulasi).</summary>
        private void ApplyImpactsFromCurrentSimulation()
        {
            foreach (var item in SavedLocations)
            {
                var impact = _simulation?.Impacts.FirstOrDefault(i => i.AreaId == item.Id);
                if (_simulation != null && impact != null) item.ApplyImpact(_simulation.HotspotId, impact);
                else item.ClearImpact();
            }
        }

        /// <summary>
        /// Dampak simulasi pada lokasi dihitung server saat simulasi diminta. Setelah lokasi ditambah/diubah/dihapus,
        /// ambil ulang simulasi yang sedang dilihat (tanpa memutar) supaya status tiap lokasi mutakhir.
        /// </summary>
        private async Task RefreshCurrentSimulationAsync()
        {
            if (SimulationHotspotId == null) return;
            _simulationCache.Clear();
            await SelectSimulationTargetAsync(SimulationHotspotId, autoPlay: false);
        }

        // =====================================================================
        // Tambah, ubah, hapus
        // =====================================================================

        [RelayCommand]
        private async Task AddLocation()
        {
            if (!_api.IsLoggedIn)
            {
                OpenLogin();
                return;
            }
            if (!_api.IsRegisteredUser)
            {
                _dialogService.ShowMessage("Lokasi tersimpan dan notifikasi tersedia untuk akun pengguna terdaftar. Masuk dengan akun pengguna (bukan admin).");
                return;
            }
            await RunLocationDialogAsync(new SavedLocationDraft());
        }

        [RelayCommand]
        private async Task EditLocation(SavedLocationItem? item)
        {
            if (item == null) return;
            await RunLocationDialogAsync(new SavedLocationDraft
            {
                Id = item.Id, Name = item.Name, Latitude = item.Latitude, Longitude = item.Longitude, RadiusKm = item.RadiusKm
            });
        }

        /// <summary>
        /// Tampilkan dialog dan simpan hasilnya. "Pilih di peta" menutup dialog dan masuk mode memilih titik; setelah titik dipilih
        /// dialog dibuka lagi dengan isian yang sama. Bila server menolak (mis. melebihi batas lokasi), pesannya ditampilkan
        /// dan dialog dibuka lagi supaya isian tidak hilang.
        /// </summary>
        private async Task RunLocationDialogAsync(SavedLocationDraft draft)
        {
            while (true)
            {
                var result = _dialogService.ShowSavedLocationDialog(draft);
                if (result == null) return;

                draft = result.Draft;
                if (result.PickOnMap)
                {
                    _pendingDraft = draft;
                    IsPickingLocation = true;
                    return;
                }

                try
                {
                    var request = new MonitoringAreaRequest(draft.Name, draft.Latitude!.Value, draft.Longitude!.Value, draft.RadiusKm);
                    var saved = draft.Id == null
                        ? await _api.CreateMonitoringAreaAsync(request)
                        : await _api.UpdateMonitoringAreaAsync(draft.Id, request);

                    await LoadSavedLocationsAsync();
                    await RefreshCurrentSimulationAsync();
                    FocusLocationRequested?.Invoke(saved.Latitude, saved.Longitude, 12);

                    // Server sudah memeriksa dampak simulasi untuk lokasi ini; ambil hasilnya.
                    var created = await RefreshNotificationsAsync(showToast: true);
                    if (created == 0)
                        ShowToast("Lokasi disimpan", $"\"{saved.Name}\" dipantau dalam radius {saved.RadiusKm:0.#} km. Belum ada ancaman api atau asap dari simulasi saat ini.", level: 0);
                    return;
                }
                catch (BackendException ex)
                {
                    _dialogService.ShowMessage(ex.Message);   // lalu dialog dibuka lagi dengan isian yang sama
                }
            }
        }

        /// <summary>Dipanggil peta saat pengguna mengklik titik dalam mode memilih lokasi.</summary>
        public void CompletePick(double latitude, double longitude)
        {
            if (!IsPickingLocation) return;
            IsPickingLocation = false;

            var draft = _pendingDraft ?? new SavedLocationDraft();
            _pendingDraft = null;
            draft.Latitude = Math.Round(latitude, 5);
            draft.Longitude = Math.Round(longitude, 5);
            _ = RunLocationDialogAsync(draft);
        }

        [RelayCommand]
        private void CancelPick()
        {
            if (!IsPickingLocation) return;
            IsPickingLocation = false;

            var draft = _pendingDraft;
            _pendingDraft = null;
            if (draft != null) _ = RunLocationDialogAsync(draft);   // kembali ke dialog supaya ketikan tidak hilang
        }

        [RelayCommand]
        private async Task DeleteLocation(SavedLocationItem? item)
        {
            if (item == null) return;
            if (!_dialogService.Confirm($"Hapus lokasi \"{item.Name}\"?\n\nNotifikasi untuk lokasi ini ikut dihapus.", "Hapus lokasi")) return;

            try
            {
                await _api.DeleteMonitoringAreaAsync(item.Id);
                await LoadSavedLocationsAsync();
                await RefreshCurrentSimulationAsync();
                await RefreshNotificationsAsync(showToast: false);
            }
            catch (BackendException ex)
            {
                _dialogService.ShowMessage(ex.Message);
            }
        }

        [RelayCommand]
        private void FocusLocation(SavedLocationItem? item)
        {
            if (item != null) FocusLocationRequested?.Invoke(item.Latitude, item.Longitude, 12);
        }
    }
}
