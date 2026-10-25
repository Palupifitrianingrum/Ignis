using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Models;
using Ignis.Frontend.Navigation;

namespace Ignis.Frontend.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IDialogService _dialogService;
        private readonly DispatcherTimer _simulationTimer;

        [ObservableProperty]
        private bool isLoggedIn;

        [ObservableProperty]
        private string searchText = string.Empty;

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
        private string userName = "Captain R. Miller";

        [ObservableProperty]
        private string userRole = "Incident Commander";

        [ObservableProperty]
        private string emergencyStatus = "Siaga Darurat Riau";

        [ObservableProperty]
        private int filterBadgeCount = 3;

        // Sidebar: Saved & Recent Items
        [ObservableProperty]
        private int savedLocationsCount = 4;

        // Standard ObservableCollection (idiomatic MVVM: no need for source generator)
        public ObservableCollection<RecentLocationItem> RecentLocations { get; } = new();

        // Fire Risk Index
        [ObservableProperty]
        private int totalMonitoredNodes = 214;

        [ObservableProperty]
        private int lowRiskCount = 48;

        [ObservableProperty]
        private int mediumRiskCount = 24;

        [ObservableProperty]
        private int highRiskCount = 67;

        [ObservableProperty]
        private int criticalRiskCount = 75;

        // Wind & Smoke Simulation
        [ObservableProperty]
        private bool isSimulationPlaying = false;

        [ObservableProperty]
        private double simulationProgress = 14.5; // Represents 14:30 (14.5 hours from 0 to 18)

        partial void OnSimulationProgressChanged(double value)
        {
            UpdateTimeTextFromProgress(value);
        }

        [ObservableProperty]
        private string simulationTimeText = "14:30 WIB";

        [ObservableProperty]
        private string simulationRangeText = "00:00 - 18:00 WIB";

        // Map View status
        [ObservableProperty]
        private double currentZoomLevel = 12.0;

        [ObservableProperty]
        private bool areHotspotsVisible = true;

        public MainWindowViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            _simulationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _simulationTimer.Tick += OnSimulationTick;

            // Initialize Recent Locations matching the design
            RecentLocations.Add(new RecentLocationItem
            {
                Title = "Muaro Jambi, Jambi",
                Subtitle = "Koordinat: -1.6131, 103.8",
                PinColorHex = "#F97316"
            });
            RecentLocations.Add(new RecentLocationItem
            {
                Title = "Kecamatan Pelalawan, Riau",
                Subtitle = "Status: 3 Hotspot Aktif • Kemarin",
                PinColorHex = "#F59E0B"
            });
            RecentLocations.Add(new RecentLocationItem
            {
                Title = "Kabupaten Ketapang, Kali...",
                Subtitle = "Status: Risiko Tinggi • 3 hari lalu",
                PinColorHex = "#EF4444"
            });
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
        private void OpenSettings()
        {
        }

        [RelayCommand]
        private void OpenSaved()
        {
        }

        [RelayCommand]
        private void OpenLogin()
        {
            bool? result = _dialogService.ShowLoginDialog();
            if (result == true)
            {
                IsLoggedIn = true;
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
        }

        [RelayCommand]
        private void ToggleConfidence()
        {
            IsConfidenceActive = !IsConfidenceActive;
            UpdateFilterCount();
        }

        [RelayCommand]
        private void ToggleLast24Hours()
        {
            IsLast24HoursActive = !IsLast24HoursActive;
            UpdateFilterCount();
        }

        [RelayCommand]
        private void ToggleConservation()
        {
            IsConservationActive = !IsConservationActive;
            UpdateFilterCount();
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

        [RelayCommand]
        private void ToggleSimulation()
        {
            IsSimulationPlaying = !IsSimulationPlaying;
            if (IsSimulationPlaying)
            {
                _simulationTimer.Start();
            }
            else
            {
                _simulationTimer.Stop();
            }
        }

        private void OnSimulationTick(object? sender, EventArgs e)
        {
            if (SimulationProgress >= 18.0)
            {
                SimulationProgress = 0.0;
            }
            else
            {
                SimulationProgress = Math.Min(18.0, SimulationProgress + 0.5);
            }
        }

        private void UpdateTimeTextFromProgress(double hours)
        {
            int hour = (int)Math.Floor(hours);
            int minute = (int)Math.Round((hours - hour) * 60.0);
            if (minute >= 60)
            {
                hour++;
                minute = 0;
            }
            SimulationTimeText = $"{hour:D2}:{minute:D2} WIB";
        }

        [RelayCommand]
        private void ZoomIn()
        {
            CurrentZoomLevel = Math.Min(18.0, CurrentZoomLevel + 1.0);
        }

        [RelayCommand]
        private void ZoomOut()
        {
            CurrentZoomLevel = Math.Max(4.0, CurrentZoomLevel - 1.0);
        }

        [RelayCommand]
        private void ResetNorth()
        {
        }

        [RelayCommand]
        private void LocateMe()
        {
        }

        [RelayCommand]
        private void ToggleLayers()
        {
            AreHotspotsVisible = !AreHotspotsVisible;
        }
    }
}
