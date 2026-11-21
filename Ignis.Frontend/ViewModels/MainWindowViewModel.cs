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
        private readonly DispatcherTimer _autoRefreshTimer;
        private bool _isRequiringLogin;

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

        // Quick filter chips & popup
        [ObservableProperty]
        private bool isFilterMenuOpen;

        // 1. Hotspot Classification
        [ObservableProperty]
        private bool isViirsActive = true;

        [ObservableProperty]
        private bool isManualActive = true;

        [ObservableProperty]
        private bool isConfidenceActive = false;

        public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "Semua Status", "Active", "Monitoring", "Resolved" };

        [ObservableProperty]
        private string selectedStatusFilter = "Semua Status";

        partial void OnSelectedStatusFilterChanged(string value)
        {
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        // 2. Time & Date
        public IReadOnlyList<string> TimeFilterOptions { get; } = new[] { "Semua Waktu", "24 Jam Terakhir", "7 Hari Terakhir", "30 Hari Terakhir" };

        [ObservableProperty]
        private string selectedTimeFilter = "Semua Waktu";

        partial void OnSelectedTimeFilterChanged(string value)
        {
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        // 3. Geographic Filter
        public IReadOnlyList<string> RegionFilterOptions { get; } = Region.Defaults.Select(r => r.Name).ToList();

        [ObservableProperty]
        private string selectedRegionFilter = Region.AllName;

        partial void OnSelectedRegionFilterChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Region.AllName)
            {
                var region = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name == value);
                if (region != null)
                {
                    FocusLocationRequested?.Invoke(
                        (region.MinLat + region.MaxLat) / 2.0,
                        (region.MinLon + region.MaxLon) / 2.0,
                        8);
                }
            }
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        [ObservableProperty]
        private bool isLast24HoursActive = false;

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
        private int filterBadgeCount = 2;

        // Sidebar: Saved, Newest Hotspot & Recent Items
        [ObservableProperty]
        private int savedLocationsCount = 0;

        [ObservableProperty]
        private bool isNewestHotspotsExpanded;

        public ObservableCollection<RecentLocationItem> NewestHotspots { get; } = new();
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
            _workspace.SettingsChanged += OnSettingsChanged;

            _simulationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };
            _simulationTimer.Tick += OnSimulationTick;

            _autoRefreshTimer = new DispatcherTimer();
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                if (_api.IsLoggedIn) await LoadDashboardDataAsync();
            };

            InitNotifications();

            ApplySession();
        }

        private void OnSettingsChanged()
        {
            ConfigureAutoRefresh();
            FocusDefaultRegion();
            ApplyFiltersAndRefreshView();
        }

        private void ConfigureAutoRefresh()
        {
            _autoRefreshTimer.Stop();
            var mins = _workspace.Settings.AutoRefreshMinutes;
            if (mins > 0 && _api.IsLoggedIn)
            {
                _autoRefreshTimer.Interval = TimeSpan.FromMinutes(mins);
                _autoRefreshTimer.Start();
            }
        }

        private void FocusDefaultRegion()
        {
            var name = _workspace.Settings.DefaultRegionName;
            if (string.IsNullOrWhiteSpace(name) || name == Region.AllName) return;
            var region = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (region != null)
            {
                FocusLocationRequested?.Invoke((region.MinLat + region.MaxLat) / 2.0, (region.MinLon + region.MaxLon) / 2.0, 8);
            }
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
            _autoRefreshTimer.Stop();
            EmergencyStatus = "Klik Profil untuk Login";
            IsNewestHotspotsExpanded = false;
            NewestHotspots.Clear();
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

            if (_workspace.Settings.HideResolvedHotspots)
            {
                filtered = filtered.Where(h => h.Status != HotspotStatus.Resolved);
            }

            if (!IsViirsActive)
            {
                filtered = filtered.Where(h => h.Source != HotspotSource.Satellite);
            }

            if (!IsManualActive)
            {
                filtered = filtered.Where(h => h.Source != HotspotSource.Manual);
            }

            if (IsConfidenceActive)
            {
                filtered = filtered.Where(h => h.Confidence >= 70.0);
            }

            if (SelectedStatusFilter != "Semua Status" && Enum.TryParse<HotspotStatus>(SelectedStatusFilter, out var statusFilter))
            {
                filtered = filtered.Where(h => h.Status == statusFilter);
            }

            if (SelectedTimeFilter != "Semua Waktu")
            {
                var cutoff = SelectedTimeFilter switch
                {
                    "24 Jam Terakhir" => DateTime.Now.AddHours(-24),
                    "7 Hari Terakhir" => DateTime.Now.AddDays(-7),
                    "30 Hari Terakhir" => DateTime.Now.AddDays(-30),
                    _ => DateTime.MinValue
                };
                if (cutoff > DateTime.MinValue)
                {
                    filtered = filtered.Where(h => h.DetectedAt >= cutoff);
                }
            }

            if (!string.IsNullOrWhiteSpace(SelectedRegionFilter) && SelectedRegionFilter != Region.AllName)
            {
                var geoRegion = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name == SelectedRegionFilter);
                if (geoRegion != null)
                {
                    filtered = filtered.Where(h => geoRegion.Contains(h.Latitude, h.Longitude));
                }
            }

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

            if (IsNewestHotspotsExpanded)
            {
                PopulateNewestHotspots(list);
            }

            RaiseMapDataChanged();
        }

        private void PopulateNewestHotspots(IEnumerable<HotspotDto> source)
        {
            NewestHotspots.Clear();
            foreach (var h in source.Take(8))
            {
                NewestHotspots.Add(ToHotspotItem(h));
            }
        }

        private static RecentLocationItem ToHotspotItem(HotspotDto h)
        {
            var regionName = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Contains(h.Latitude, h.Longitude))?.Name ?? "Indonesia";
            var pinColor = h.Status switch
            {
                HotspotStatus.Active => "#EF4444",
                HotspotStatus.Monitoring => "#F59E0B",
                _ => "#10B981"
            };

            return new RecentLocationItem
            {
                Id = h.Id,
                Title = $"{h.Id} • {regionName}",
                Subtitle = string.Create(CultureInfo.InvariantCulture, $"{h.Details} • ({h.Latitude:0.00}, {h.Longitude:0.00})"),
                PinColorHex = pinColor,
                Latitude = h.Latitude,
                Longitude = h.Longitude
            };
        }

        [RelayCommand]
        private async Task ToggleNewestHotspotsAsync()
        {
            IsNewestHotspotsExpanded = !IsNewestHotspotsExpanded;
            if (!IsNewestHotspotsExpanded) return;

            if (_allHotspots.Count == 0 && _api.IsLoggedIn)
            {
                await LoadDashboardDataAsync();
            }
            else
            {
                PopulateNewestHotspots(VisibleHotspots.Count > 0 ? VisibleHotspots : _allHotspots);
            }
        }

        [RelayCommand]
        private void SubmitSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;
            var q = SearchText.Trim();

            RecentLocationItem? entry = null;
            var matchedHotspot = VisibleHotspots.FirstOrDefault();
            var matchedRegion = Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            if (matchedRegion != null)
            {
                double lat = (matchedRegion.MinLat + matchedRegion.MaxLat) / 2.0;
                double lon = (matchedRegion.MinLon + matchedRegion.MaxLon) / 2.0;
                FocusLocationRequested?.Invoke(lat, lon, 8);
                entry = new RecentLocationItem
                {
                    Id = $"search-{matchedRegion.Name}",
                    Title = matchedRegion.Name,
                    Subtitle = $"Pencarian wilayah • {VisibleHotspots.Count} hotspot",
                    PinColorHex = "#F28A1E",
                    Latitude = lat,
                    Longitude = lon
                };
            }
            else if (matchedHotspot != null)
            {
                FocusLocationRequested?.Invoke(matchedHotspot.Latitude, matchedHotspot.Longitude, 11);
                entry = ToHotspotItem(matchedHotspot);
            }
            else
            {
                entry = new RecentLocationItem
                {
                    Id = $"search-{q}",
                    Title = q,
                    Subtitle = "Pencarian lokasi / hotspot",
                    PinColorHex = "#64748B",
                    Latitude = 0.32,
                    Longitude = 101.85
                };
            }

            var existing = RecentLocations.FirstOrDefault(r => r.Title.Equals(entry.Title, StringComparison.OrdinalIgnoreCase));
            if (existing != null) RecentLocations.Remove(existing);
            RecentLocations.Insert(0, entry);
            while (RecentLocations.Count > 10) RecentLocations.RemoveAt(RecentLocations.Count - 1);
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
                ConfigureAutoRefresh();
                FocusDefaultRegion();
            }
            else
            {
                UserName = string.Empty;
                UserEmail = string.Empty;
                UserRole = string.Empty;
                ResetDashboardData();
                PromptLoginOrShutdown();
            }
        }

        private void PromptLoginOrShutdown()
        {
            if (_isRequiringLogin || _api.IsLoggedIn || System.Windows.Application.Current is not { } app) return;
            _isRequiringLogin = true;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_api.IsLoggedIn) return;
                    var mainWin = app.MainWindow;
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                    mainWin?.Hide();

                    var ok = _dialogService.ShowLoginDialog() == true && _api.IsLoggedIn;
                    if (!ok)
                    {
                        app.Shutdown();
                        return;
                    }

                    app.ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
                    mainWin?.Show();
                }
                finally
                {
                    _isRequiringLogin = false;
                }
            }));
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
        private void ToggleFilterMenu()
        {
            IsProfileMenuOpen = false;
            IsNotificationsOpen = false;
            IsFilterMenuOpen = !IsFilterMenuOpen;
        }

        [RelayCommand]
        private void ToggleViirs()
        {
            IsViirsActive = !IsViirsActive;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        [RelayCommand]
        private void ToggleManual()
        {
            IsManualActive = !IsManualActive;
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

        [RelayCommand]
        private void ResetFilters()
        {
            IsViirsActive = true;
            IsManualActive = true;
            IsConfidenceActive = false;
            IsLast24HoursActive = false;
            IsConservationActive = false;
            SelectedStatusFilter = "Semua Status";
            SelectedTimeFilter = "Semua Waktu";
            SelectedRegionFilter = Region.AllName;
            UpdateFilterCount();
            ApplyFiltersAndRefreshView();
        }

        private void UpdateFilterCount()
        {
            int count = 0;
            if (IsViirsActive) count++;
            if (IsManualActive) count++;
            if (IsConfidenceActive) count++;
            if (SelectedStatusFilter != "Semua Status") count++;
            if (SelectedTimeFilter != "Semua Waktu") count++;
            if (SelectedRegionFilter != Region.AllName) count++;
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
            var target = SavedLocations.FirstOrDefault();
            if (target != null)
            {
                FocusLocationRequested?.Invoke(target.Latitude, target.Longitude, 12);
                return;
            }
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
