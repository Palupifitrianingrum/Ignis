using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ignis.Frontend.Models;
using Ignis.Frontend.Services.Backend;
using Ignis.Shared;

namespace Ignis.Frontend.ViewModels
{
    /// <summary>
    /// Simulasi penyebaran api di dashboard: memilih hotspot, memuat hasil dari API (<c>GET /api/hotspots/{id}/simulation</c>),
    /// memutar per jam, dan menyiapkan data untuk peta (sel berwarna menurut umur) dan panel visualisasi (kompas, luas, grafik).
    /// Semua perhitungan penyebaran ada di server; di sini hanya presentasi.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Ukuran kanvas grafik luas di panel (harus sama dengan ukuran di SimulationPanel.xaml).</summary>
        public const double ChartWidth = 264;
        public const double ChartHeight = 52;

        private const int SimulationHours = 18;
        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        private readonly Dictionary<string, SimulationDto> _simulationCache = new();
        private SimulationDto? _simulation;
        private Dictionary<(double Lat, double Lon), int> _ignitionHour = new();
        private DateTime _simulationStart = DateTime.Now;
        private int _simulationLoadVersion;
        private int _renderedHour = -1;

        // ---- Pemutar ----
        [ObservableProperty] private bool isSimulationPlaying;
        [ObservableProperty] private double simulationProgress;
        [ObservableProperty] private double simulationMaxHours = SimulationHours;
        [ObservableProperty] private string simulationTimeText = "--:-- WIB";
        [ObservableProperty] private string simulationRangeText = string.Empty;

        // ---- Target dan status muat ----
        [ObservableProperty] private string? simulationHotspotId;
        [ObservableProperty] private string simulationTargetText = "Klik pin hotspot di peta untuk menjalankan simulasi";

        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSimulationPanelVisible))]
        private bool isSimulationLoading;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSimulationPanelVisible)), NotifyPropertyChangedFor(nameof(HasSimulationError))]
        private string? simulationError;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSimulationPanelVisible))]
        private bool hasSimulation;

        public bool HasSimulationError => !string.IsNullOrEmpty(SimulationError);
        public bool IsSimulationPanelVisible => HasSimulation || IsSimulationLoading || HasSimulationError;

        // ---- Isi panel visualisasi ----
        [ObservableProperty] private string simulationHourText = string.Empty;
        [ObservableProperty] private string simulationAreaText = "0 ha";
        [ObservableProperty] private string simulationCellsText = string.Empty;
        [ObservableProperty] private string simulationWindText = string.Empty;
        [ObservableProperty] private string simulationSpreadText = string.Empty;
        [ObservableProperty] private string simulationConditionsText = string.Empty;
        [ObservableProperty] private string simulationLimitText = string.Empty;
        [ObservableProperty] private bool showSimulationLimit;

        /// <summary>Sudut putar panah kompas (0 = menghadap utara, searah jarum jam) = arah rambatan api.</summary>
        [ObservableProperty] private double windArrowAngle;

        [ObservableProperty] private PointCollection areaChartLinePoints = new();
        [ObservableProperty] private PointCollection areaChartFillPoints = new();
        [ObservableProperty] private double areaChartMarkerX;

        /// <summary>Hotspot yang sedang disimulasikan (untuk tombol "Lihat detail").</summary>
        public HotspotDto? SelectedSimulationHotspot => _allHotspots.FirstOrDefault(h => h.Id == SimulationHotspotId);

        /// <summary>Keadaan peta untuk jam sekarang; null bila tidak ada simulasi.</summary>
        public SimulationMapState? CurrentSimulationState => BuildMapState();

        partial void OnSimulationProgressChanged(double value)
        {
            if (_simulation == null) return;
            var hour = Math.Clamp((int)Math.Round(value), 0, _simulation.Hours);
            if (hour == _renderedHour) return;   // geseran dalam jam yang sama tidak perlu menggambar ulang
            RenderHour(hour);
        }

        // =====================================================================
        // Memilih dan memuat
        // =====================================================================

        /// <summary>
        /// Jadikan hotspot ini target simulasi: ambil hasilnya dari server lalu (opsional) langsung diputar.
        /// Tidak pernah melempar; kegagalan ditampilkan lewat <see cref="SimulationError"/>.
        /// </summary>
        public async Task SelectSimulationTargetAsync(string hotspotId, bool autoPlay = true)
        {
            if (!_api.IsLoggedIn) return;

            int version = ++_simulationLoadVersion;   // pilihan yang lebih baru membatalkan hasil yang terlambat datang
            StopPlayback();
            ClearSimulationData();   // jangan tampilkan hasil hotspot sebelumnya di bawah nama hotspot yang baru

            var hotspot = _allHotspots.FirstOrDefault(h => h.Id == hotspotId);
            SimulationHotspotId = hotspotId;
            SimulationError = null;
            SimulationTargetText = hotspot != null ? $"{hotspotId} • {RegionNameOf(hotspot.Latitude, hotspot.Longitude)}" : hotspotId;
            IsSimulationLoading = true;
            RaiseMapDataChanged();   // sorot pin yang dipilih segera, sebelum hasil datang

            try
            {
                if (!_simulationCache.TryGetValue(hotspotId, out var sim))
                {
                    sim = await _api.GetSimulationAsync(hotspotId, SimulationHours);
                    _simulationCache[hotspotId] = sim;
                }

                if (version != _simulationLoadVersion) return;
                ApplySimulation(sim);
                if (autoPlay) StartPlayback();
            }
            catch (Exception ex)
            {
                if (version != _simulationLoadVersion) return;
                ClearSimulationData();
                SimulationError = ex is BackendException ? ex.Message : "Gagal memuat simulasi. Coba lagi.";
                RaiseMapDataChanged();
            }
            finally
            {
                if (version == _simulationLoadVersion) IsSimulationLoading = false;
            }
        }

        private void ApplySimulation(SimulationDto sim)
        {
            _simulation = sim;

            // Jam kapan tiap sel pertama kali menyala: dasar pewarnaan menurut umur api.
            var ignition = new Dictionary<(double, double), int>();
            foreach (var frame in sim.Frames.OrderBy(f => f.HourOffset))
                foreach (var c in frame.BurningCells)
                    ignition.TryAdd((c.Latitude, c.Longitude), frame.HourOffset);
            _ignitionHour = ignition;

            _simulationStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0);
            SimulationMaxHours = sim.Hours;
            SimulationRangeText = $"{_simulationStart:HH:mm} - {_simulationStart.AddHours(sim.Hours):HH:mm} WIB";

            SimulationSpreadText = $"Api merambat ke {Compass.Name(sim.SpreadDirection)} ({sim.SpreadDirection:0}°)";
            SimulationWindText = $"Angin dari {Compass.Name(sim.WindFromDirection)} ({sim.WindFromDirection:0}°) • {sim.WindSpeed.ToString("0.0", Id)} m/s";
            SimulationConditionsText =
                $"{sim.Temperature.ToString("0.#", Id)} °C • RH {sim.Humidity:0}% • hujan {sim.Precipitation.ToString("0.#", Id)} mm"
                + (string.IsNullOrWhiteSpace(sim.Condition) ? string.Empty
                   : string.Equals(sim.Condition, "Simulasi", StringComparison.OrdinalIgnoreCase) ? " • data cuaca simulasi"
                   : $" • {sim.Condition}");
            WindArrowAngle = sim.SpreadDirection;

            ShowSimulationLimit = sim.Truncated;
            SimulationLimitText = $"Model mencapai batas {sim.MaxCells} sel, sehingga luas tidak bertambah lagi setelah itu.";

            BuildAreaChart(sim);
            HasSimulation = true;

            SimulationProgress = 0;
            RenderHour(0);   // paksa gambar jam 0, juga bila progress sudah 0 sehingga handler perubahan tidak terpanggil
        }

        private void RenderHour(int hour)
        {
            if (_simulation == null) return;
            _renderedHour = hour;

            var frame = _simulation.Frames.FirstOrDefault(f => f.HourOffset == hour) ?? _simulation.Frames[^1];
            var when = _simulationStart.AddHours(hour);
            SimulationTimeText = $"{when:HH:mm} WIB";
            SimulationHourText = $"Jam ke-{hour} dari {_simulation.Hours}";
            SimulationAreaText = $"{frame.AreaHa.ToString("N0", Id)} ha";
            SimulationCellsText = hour == 0
                ? "1 sel (titik asal)"
                : $"{frame.BurningCells.Count} sel terbakar • +{frame.NewCells} baru";
            AreaChartMarkerX = _simulation.Hours == 0 ? 0 : hour * ChartWidth / _simulation.Hours;

            RaiseMapDataChanged();
        }

        private void BuildAreaChart(SimulationDto sim)
        {
            var frames = sim.Frames.OrderBy(f => f.HourOffset).ToList();
            double maxArea = Math.Max(frames.Max(f => f.AreaHa), 1.0);
            const double pad = 3;

            var line = new PointCollection();
            foreach (var f in frames)
            {
                double x = sim.Hours == 0 ? 0 : f.HourOffset * ChartWidth / sim.Hours;
                double y = ChartHeight - pad - f.AreaHa / maxArea * (ChartHeight - 2 * pad);
                line.Add(new Point(x, y));
            }

            var fill = new PointCollection(line) { new Point(ChartWidth, ChartHeight), new Point(0, ChartHeight) };
            line.Freeze();
            fill.Freeze();
            AreaChartLinePoints = line;
            AreaChartFillPoints = fill;
        }

        /// <summary>Bangun keadaan peta untuk jam sekarang, termasuk umur tiap sel.</summary>
        private SimulationMapState? BuildMapState()
        {
            if (_simulation == null) return null;

            int hour = Math.Clamp((int)Math.Round(SimulationProgress), 0, _simulation.Hours);
            var frame = _simulation.Frames.FirstOrDefault(f => f.HourOffset == hour) ?? _simulation.Frames[^1];

            var cells = frame.BurningCells
                .Select(c => new SimulationCellView(
                    c.Latitude, c.Longitude,
                    Math.Max(0, hour - _ignitionHour.GetValueOrDefault((c.Latitude, c.Longitude), hour))))
                .ToList();

            return new SimulationMapState(
                _simulation.HotspotId, _simulation.OriginLatitude, _simulation.OriginLongitude,
                _simulation.CellSizeDegrees, _simulation.SpreadDirection, hour, _simulation.Hours, cells);
        }

        // =====================================================================
        // Pemutar dan penutupan
        // =====================================================================

        [RelayCommand]
        private void ToggleSimulation()
        {
            if (_simulation == null) return;
            if (IsSimulationPlaying)
            {
                StopPlayback();
                return;
            }
            if (SimulationProgress >= SimulationMaxHours) SimulationProgress = 0;   // putar ulang dari awal
            StartPlayback();
        }

        private void StartPlayback()
        {
            IsSimulationPlaying = true;
            _simulationTimer.Start();
        }

        private void StopPlayback()
        {
            IsSimulationPlaying = false;
            _simulationTimer.Stop();
        }

        /// <summary>Maju satu jam per tik; berhenti di jam terakhir (tombol play memulai ulang).</summary>
        private void OnSimulationTick(object? sender, EventArgs e)
        {
            if (_simulation == null || SimulationProgress >= SimulationMaxHours)
            {
                StopPlayback();
                return;
            }

            SimulationProgress = Math.Min(SimulationMaxHours, Math.Floor(SimulationProgress) + 1);
            if (SimulationProgress >= SimulationMaxHours) StopPlayback();
        }

        /// <summary>Tutup panel dan hapus gambar simulasi dari peta (cache hasil tetap, jadi memilih lagi cepat).</summary>
        [RelayCommand]
        private void CloseSimulation()
        {
            _simulationLoadVersion++;   // batalkan muat yang sedang berjalan
            StopPlayback();
            ClearSimulationData();
            SimulationHotspotId = null;
            SimulationError = null;
            IsSimulationLoading = false;
            SimulationTargetText = DefaultSimulationHint();
            RaiseMapDataChanged();
        }

        /// <summary>Reset penuh (logout atau data dashboard dikosongkan), termasuk cache.</summary>
        private void ClearSimulation()
        {
            _simulationLoadVersion++;
            StopPlayback();
            _simulationCache.Clear();
            ClearSimulationData();
            SimulationHotspotId = null;
            SimulationError = null;
            IsSimulationLoading = false;
            SimulationTargetText = DefaultSimulationHint();
        }

        private string DefaultSimulationHint() =>
            _api.IsLoggedIn ? "Klik pin hotspot di peta untuk menjalankan simulasi" : "Login untuk menjalankan simulasi";

        private void ClearSimulationData()
        {
            _simulation = null;
            _ignitionHour = new();
            _renderedHour = -1;
            HasSimulation = false;
            SimulationProgress = 0;
            SimulationMaxHours = SimulationHours;
            SimulationTimeText = "--:-- WIB";
            SimulationRangeText = string.Empty;
            SimulationHourText = string.Empty;
            SimulationAreaText = "0 ha";
            SimulationCellsText = string.Empty;
            ShowSimulationLimit = false;
            AreaChartLinePoints = new PointCollection();
            AreaChartFillPoints = new PointCollection();
            AreaChartMarkerX = 0;
        }

        private static string RegionNameOf(double latitude, double longitude) =>
            Region.Defaults.FirstOrDefault(r => !r.IsAll && r.Contains(latitude, longitude))?.Name ?? "Indonesia";
    }
}
