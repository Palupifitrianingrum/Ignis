using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Models;
using Ignis.Frontend.Navigation;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Ignis.Shared;

namespace Ignis.Frontend.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;
        private readonly IgnisApiClient _api;
        private readonly AdminWorkspace _workspace;
        private readonly DispatcherTimer _simulationTimer;

        private IReadOnlyList<HotspotDto> _allHotspots = Array.Empty<HotspotDto>();
        private IReadOnlyList<PredictionDto> _allPredictions = Array.Empty<PredictionDto>();

        [ObservableProperty]
        private bool isLoggedIn;

        [ObservableProperty]
        private bool isAdmin;

        [ObservableProperty]
        private string searchText = string.Empty;

        partial void OnSearchTextChanged(string value)
        {
            ApplyFiltersAndRefreshView();

            if (!string.IsNullOrWhiteSpace(value))
            {
                var q = value.Trim();
                var region = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (region != null)
                {
                    FocusLocationRequested?.Invoke(
                        (region.MinLat + region.MaxLat) / 2.0,
                        (region.MinLon + region.MaxLon) / 2.0,
                        8);
                }
            }
        }

        [ObservableProperty]
        private string activeNavTab = "Dashboard";

        // Quick filter chips
        [ObservableProperty]
        private bool isViirsActive = true;

        [ObservableProperty]
        private bool isConfidenceActive = true;

        [ObservableProperty]
        private bool isLast24HoursActive = true;

        [ObservableProperty]
        private bool isConservationActive = false;

        // Header & User info
        [ObservableProperty]
        private bool isProfileMenuOpen;

        [ObservableProperty]
        private bool isAdminDashboardOpen;

        [ObservableProperty]
        private string userName = string.Empty;

        [ObservableProperty]
        private string userEmail = string.Empty;

        [ObservableProperty]
        private string userRole = string.Empty;

        [ObservableProperty]
        private string emergencyStatus = "Klik Profil untuk Login";

        [ObservableProperty]
        private int filterBadgeCount = 3;

        // Sidebar: Saved & Recent Items
        [ObservableProperty]
        private int savedLocationsCount = 0;

        public ObservableCollection<RecentLocationItem> RecentLocations { get; } = new();

        // Fire Risk Index
        [ObservableProperty]
        private int totalMonitoredNodes = 0;

        [ObservableProperty]
        private int lowRiskCount = 0;

        [ObservableProperty]
        private int mediumRiskCount = 0;

        [ObservableProperty]
        private int highRiskCount = 0;

        [ObservableProperty]
        private int criticalRiskCount = 0;

        // Map View status
        [ObservableProperty]
        private double currentZoomLevel = 12.0;

        [ObservableProperty]
        private bool areHotspotsVisible = true;

        public IReadOnlyList<HotspotDto> VisibleHotspots { get; private set; } = Array.Empty<HotspotDto>();

        public event Action<IReadOnlyList<HotspotDto>, SimulationMapState?, string?, IReadOnlyList<SavedLocationItem>>? MapDataChanged;
        public event Action<double, double, int>? FocusLocationRequested;

        public MainWindowViewModel(IDialogService dialogService, IgnisApiClient api, AdminWorkspace workspace)
        {
            _dialogService = dialogService;
            _api = api;
            _workspace = workspace;

            _api.SessionChanged += ApplySession;
            _workspace.Changed += OnWorkspaceChanged;

            _simulationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _simulationTimer.Tick += OnSimulationTick;
            InitNotifications();

            ApplySession();
        }

        private void OnWorkspaceChanged()
        {
            if (!_api.IsLoggedIn) return;
            _simulationCache.Clear();   // hotspot bisa berubah lokasi/cuaca, simulasi lama tidak berlaku lagi
            _allHotspots = _workspace.Hotspots.Select(h => h.Dto).ToList();
            _allPredictions = _workspace.Predictions.Select(p => p.Dto).ToList();
            ApplyFiltersAndRefreshView();
        }

        public async Task LoadDashboardDataAsync()
        {
            if (!_api.IsLoggedIn)
            {
                ResetDashboardData();
                return;
            }

            try
            {
                _allHotspots = await _api.GetHotspotsAsync();
                _allPredictions = await _api.GetPredictionsAsync();

                ApplyFiltersAndRefreshView();

                // Simulasi default: hotspot Active pertama (atau yang pertama), tanpa memutar dan tanpa menggeser peta.
                // Pilihan pengguna yang masih ada di daftar dipertahankan.
                var primary = _allHotspots.FirstOrDefault(h => h.Status == HotspotStatus.Active) ?? _allHotspots.FirstOrDefault();
                if (primary != null && (SimulationHotspotId == null || _allHotspots.All(h => h.Id != SimulationHotspotId)))
                    await SelectSimulationTargetAsync(primary.Id, autoPlay: false);
            }
            catch
            {
                // Jika API belum terjangkau, biarkan data kosong tanpa melempar unhandled exception
                ResetDashboardData();
            }
        }

        private void ResetDashboardData()
        {
            _allHotspots = Array.Empty<HotspotDto>();
            _allPredictions = Array.Empty<PredictionDto>();
            ClearSimulation();
            VisibleHotspots = Array.Empty<HotspotDto>();

            TotalMonitoredNodes = 0;
            LowRiskCount = 0;
            MediumRiskCount = 0;
            HighRiskCount = 0;
            CriticalRiskCount = 0;
            StopUserFeatures();   // lokasi tersimpan dan notifikasi dikosongkan (logout / belum login)
            EmergencyStatus = "Klik Profil untuk Login";
            RecentLocations.Clear();
            RaiseMapDataChanged();
        }

        private void ApplyFiltersAndRefreshView()
        {
            if (!_api.IsLoggedIn)
            {
                ResetDashboardData();
                return;
            }

            IEnumerable<HotspotDto> filtered = _allHotspots;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                var matchedRegion = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
                filtered = filtered.Where(h =>
                    h.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    h.Details.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (h.Note != null && h.Note.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (matchedRegion != null && matchedRegion.Contains(h.Latitude, h.Longitude)));
            }

            var list = filtered.ToList();
            VisibleHotspots = AreHotspotsVisible ? list : Array.Empty<HotspotDto>();

            // Hitung indeks risiko berdasarkan prediksi terbaru tiap hotspot
            var latestPredByHotspot = _allPredictions
                .GroupBy(p => p.HotspotId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First().Risk);

            int low = 0, med = 0, high = 0, crit = 0;
            foreach (var h in list)
            {
                if (latestPredByHotspot.TryGetValue(h.Id, out var risk))
                {
                    switch (risk)
                    {
                        case RiskLevel.Low: low++; break;
                        case RiskLevel.Medium: med++; break;
                        case RiskLevel.High: high++; break;
                        case RiskLevel.Critical: crit++; break;
                    }
                }
                else
                {
                    if (h.Confidence >= 85) crit++;
                    else if (h.Confidence >= 70) high++;
                    else if (h.Confidence >= 50) med++;
                    else low++;
                }
            }

            TotalMonitoredNodes = list.Count;
            LowRiskCount = low;
            MediumRiskCount = med;
            HighRiskCount = high;
            CriticalRiskCount = crit;

            int activeCount = list.Count(h => h.Status == HotspotStatus.Active);
            EmergencyStatus = activeCount > 0
                ? $"Siaga Darurat • {activeCount} Hotspot Aktif"
                : "Status Pantauan Aman";

            RecentLocations.Clear();
            foreach (var h in list.Take(8))
            {
                var regionName = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Contains(h.Latitude, h.Longitude))?.Name ?? "Indonesia";
                var pinColor = h.Status switch
                {
                    HotspotStatus.Active => "#EF4444",
                    HotspotStatus.Monitoring => "#F59E0B",
                    _ => "#10B981"
                };

                RecentLocations.Add(new RecentLocationItem
                {
                    Id = h.Id,
                    Title = $"{h.Id} • {regionName}",
                    Subtitle = string.Create(CultureInfo.InvariantCulture, $"{h.Details} • ({h.Latitude:0.00}, {h.Longitude:0.00})"),
                    PinColorHex = pinColor,
                    Latitude = h.Latitude,
                    Longitude = h.Longitude
                });
            }

            RaiseMapDataChanged();
        }

        private void RaiseMapDataChanged() =>
            MapDataChanged?.Invoke(VisibleHotspots, BuildMapState(), SimulationHotspotId, SavedLocations.ToList());

        [RelayCommand]
        private void SelectRecentLocation(RecentLocationItem? item)
        {
            if (item == null) return;
            FocusLocationRequested?.Invoke(item.Latitude, item.Longitude, 13);   // cukup dekat untuk melihat sel simulasi
            _ = SelectSimulationTargetAsync(item.Id);   // simulasikan hotspot yang dipilih; tidak melempar
        }

        [RelayCommand]
        private void RemoveRecentLocation(RecentLocationItem item)
        {
            if (item != null)
            {
                RecentLocations.Remove(item);
            }
        }

        [RelayCommand]
        private void ClearAllRecent()
        {
            RecentLocations.Clear();
        }

        [RelayCommand]
        private void OpenSettings() => _dialogService.ShowSettingsWindow();

        [RelayCommand]
        private void OpenAdmin() => OpenAdminWindow();

        /// <summary>Panel admin hanya untuk akun Admin: belum login → tampilkan login dulu; bukan admin → tolak dengan pesan.</summary>
        private void OpenAdminWindow()
        {
            if (!_api.IsLoggedIn) OpenLogin();
            if (!_api.IsLoggedIn) return;   // login dibatalkan

            if (!_api.IsAdmin)
            {
                _dialogService.ShowMessage("Panel admin hanya dapat dibuka oleh akun Admin.");
                return;
            }
            IsAdminDashboardOpen = true;
        }

        [RelayCommand]
        private void BackToMap() => IsAdminDashboardOpen = false;

        /// <summary>Samakan tampilan header dan data peta dengan sesi login saat ini.</summary>
        private async void ApplySession()
        {
            IsLoggedIn = _api.IsLoggedIn;
            IsAdmin = _api.IsAdmin;
            IsProfileMenuOpen = false;
            if (!_api.IsAdmin) IsAdminDashboardOpen = false;
            if (_api.CurrentUser is { } user)
            {
                UserName = user.Name;
                UserEmail = user.Email;
                UserRole = user.Role.ToString();
                await LoadDashboardDataAsync();
                await StartUserFeaturesAsync();   // lokasi tersimpan dan notifikasi (hanya pengguna terdaftar)
            }
            else
            {
                UserName = string.Empty;
                UserEmail = string.Empty;
                UserRole = string.Empty;
                ResetDashboardData();
            }
        }

        [RelayCommand]
        private void ToggleProfileMenu()
        {
            if (!_api.IsLoggedIn)
            {
                OpenLogin();
                return;
            }
            IsNotificationsOpen = false;
            IsProfileMenuOpen = !IsProfileMenuOpen;
        }

        [RelayCommand]
        private void AddAccount()
        {
            IsProfileMenuOpen = false;
            OpenLogin();
        }

        [RelayCommand]
        private void OpenSettingsFromMenu()
        {
            IsProfileMenuOpen = false;
            _dialogService.ShowSettingsWindow();
        }

        [RelayCommand]
        private void OpenAdminFromMenu()
        {
            IsProfileMenuOpen = false;
            OpenAdminWindow();
        }

        [RelayCommand]
        private void Logout()
        {
            IsProfileMenuOpen = false;
            _api.Logout();
        }

        [RelayCommand]
        private async Task OpenSaved()
        {
            if (!_api.IsLoggedIn)
            {
                OpenLogin();
                return;
            }
            await LoadDashboardDataAsync();
            await LoadSavedLocationsAsync();
        }

        [RelayCommand]
        private void OpenLogin()
        {
            bool? result = _dialogService.ShowLoginDialog();
            if (result == true)
            {
                IsLoggedIn = _api.IsLoggedIn;
                IsAdmin = _api.IsAdmin;
            }
        }

        [RelayCommand]
        private void SelectNavTab(string tabName)
        {
            ActiveNavTab = tabName;
        }

        [RelayCommand]
        private void ToggleViirs()
        {
            IsViirsActive = !IsViirsActive;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        [RelayCommand]
        private void ToggleConfidence()
        {
            IsConfidenceActive = !IsConfidenceActive;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        [RelayCommand]
        private void ToggleLast24Hours()
        {
            IsLast24HoursActive = !IsLast24HoursActive;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        [RelayCommand]
        private void ToggleConservation()
        {
            IsConservationActive = !IsConservationActive;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        private void UpdateFilterCount()
        {
            int count = 0;
            if (IsViirsActive) count++;
            if (IsConfidenceActive) count++;
            if (IsLast24HoursActive) count++;
            if (IsConservationActive) count++;
            FilterBadgeCount = count;
        }

        public event Action? ZoomInRequested;
        public event Action? ZoomOutRequested;
        public event Action? ResetNorthRequested;
        public event Action? LocateMeRequested;

        [RelayCommand]
        private void ZoomIn()
        {
            CurrentZoomLevel = Math.Min(16.0, CurrentZoomLevel + 1.0);
            ZoomInRequested?.Invoke();
        }

        [RelayCommand]
        private void ZoomOut()
        {
            CurrentZoomLevel = Math.Max(3.0, CurrentZoomLevel - 1.0);
            ZoomOutRequested?.Invoke();
        }

        [RelayCommand]
        private void ResetNorth()
        {
            ResetNorthRequested?.Invoke();
        }

        [RelayCommand]
        private void LocateMe()
        {
            LocateMeRequested?.Invoke();
        }

        [RelayCommand]
        private void ToggleLayers()
        {
            AreHotspotsVisible = !AreHotspotsVisible;
            ApplyFiltersAndRefreshView();
        }
    }
}
