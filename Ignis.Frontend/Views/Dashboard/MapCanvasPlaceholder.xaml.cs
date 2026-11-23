using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Limiting;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using Ignis.Frontend.Models;
using Mapsui.Tiling.Layers;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Views.Admin;
using Ignis.Frontend.ViewModels;
using Ignis.Shared;

namespace Ignis.Frontend.Views.Dashboard;

public partial class MapCanvasPlaceholder : UserControl
{
    private bool _isInitialized;
    private MemoryLayer? _smokeLayer;
    private MemoryLayer? _simulationLayer;
    private MemoryLayer? _simulationOverlayLayer;
    private MemoryLayer? _locationsLayer;
    private MemoryLayer? _hotspotLayer;

    private static readonly Mapsui.Styles.Color SatelliteColor = new(232, 83, 31);     // #E8531F
    private static readonly Mapsui.Styles.Color ManualColor = new(46, 139, 74);        // #2E8B4A

    public MapCanvasPlaceholder()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        _isInitialized = true;

        InitializeMapsui();

        if (_currentVm != null)
        {
            UpdateMapLayers(_currentVm.VisibleHotspots, _currentVm.CurrentSimulationState, _currentVm.SimulationHotspotId, _currentVm.SavedLocations.ToList());
        }
    }

    private void InitializeMapsui()
    {
        var map = new Mapsui.Map();

        // 1. Add English-labeled Tile Layer (Esri World Street Map with worldwide English labels)
        var tileLayer = CreateEnglishTileLayer();
        map.Layers.Add(tileLayer);

        // 2. Add Simulation & Hotspot Vector Layers
        _smokeLayer = new MemoryLayer { Name = "Smoke", Features = new List<IFeature>(), Style = null };
        _simulationLayer = new MemoryLayer { Name = "Simulation", Features = new List<IFeature>(), Style = null };
        _simulationOverlayLayer = new MemoryLayer { Name = "SimulationOverlay", Features = new List<IFeature>(), Style = null };
        _locationsLayer = new MemoryLayer { Name = "SavedLocations", Features = new List<IFeature>(), Style = null };
        _hotspotLayer = new MemoryLayer { Name = "Hotspots", Features = new List<IFeature>(), Style = null };
        map.Layers.Add(_smokeLayer);                 // asap di paling bawah, api di atasnya
        map.Layers.Add(_simulationLayer);
        map.Layers.Add(_simulationOverlayLayer);
        map.Layers.Add(_locationsLayer);             // lokasi tersimpan pengguna (rumah) dengan cincin radius
        map.Layers.Add(_hotspotLayer);

        // 3. Set Limiter so user cannot pan or zoom outside map extent into empty white space
        map.Navigator.Limiter = new ViewportLimiterKeepWithinExtent();

        // 4. Limit zoom bounds:
        if (map.Navigator.Resolutions.Count > 16)
        {
            var maxZoomInRes = map.Navigator.Resolutions[16];  // Zoom level 16
            var maxZoomOutRes = map.Navigator.Resolutions[3];   // Zoom level 3
            map.Navigator.OverrideZoomBounds = new MMinMax(
                Math.Min(maxZoomInRes, maxZoomOutRes),
                Math.Max(maxZoomInRes, maxZoomOutRes)
            );
        }
        else if (map.Navigator.Resolutions.Count > 4)
        {
            var minRes = map.Navigator.Resolutions.Min();
            var maxZoomOutRes = map.Navigator.Resolutions[3];
            map.Navigator.OverrideZoomBounds = new MMinMax(
                Math.Min(minRes, maxZoomOutRes),
                Math.Max(minRes, maxZoomOutRes)
            );
        }

        if (tileLayer.Extent != null)
        {
            map.Navigator.OverridePanBounds = tileLayer.Extent;
        }

        // 5. Initial Center: Pelalawan / Riau, Indonesia (0.32° N, 101.85° E)
        var centerLonLat = SphericalMercator.FromLonLat(101.85, 0.32);
        var centerPoint = new MPoint(centerLonLat.x, centerLonLat.y);

        MapControl.Map = map;
        MapControl.Info += OnMapInfo;

        if (map.Navigator.Resolutions.Count > 10)
        {
            map.Navigator.CenterOnAndZoomTo(centerPoint, map.Navigator.Resolutions[10]);
        }
        else
        {
            map.Navigator.CenterOn(centerPoint);
        }
    }

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        if (MapControl.Map == null || _currentVm == null) return;

        var mapInfo = e.GetMapInfo(MapControl.Map.Layers);

        // Mode memilih lokasi (dari dialog lokasi tersimpan): klik di mana saja = titik yang dipilih.
        if (_currentVm.IsPickingLocation)
        {
            if (mapInfo?.WorldPosition is { } world)
            {
                var lonLat = SphericalMercator.ToLonLat(world.X, world.Y);
                double lon = lonLat.lon, lat = lonLat.lat;
                Dispatcher.BeginInvoke(new Action(() => _currentVm.CompletePick(lat, lon)));
            }
            return;
        }

        if (mapInfo?.Feature?["id"] is string id)
        {
            var hotspot = _currentVm.VisibleHotspots.FirstOrDefault(h => h.Id == id);
            // Klik pin = zoom maksimal ke titik hotspot dan simulasikan hotspot itu.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (hotspot != null)
                {
                    OnFocusLocationRequested(hotspot.Latitude, hotspot.Longitude, 16);
                }
                _ = _currentVm.SelectSimulationTargetAsync(id);
            }));
        }
    }

    private MainWindowViewModel? _currentVm;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.ZoomInRequested -= OnZoomInRequested;
            _currentVm.ZoomOutRequested -= OnZoomOutRequested;
            _currentVm.ResetNorthRequested -= OnResetNorthRequested;
            _currentVm.LocateMeRequested -= OnLocateMeRequested;
            _currentVm.MapDataChanged -= UpdateMapLayers;
            _currentVm.FocusLocationRequested -= OnFocusLocationRequested;
        }

        if (e.NewValue is MainWindowViewModel vm)
        {
            _currentVm = vm;
            _currentVm.ZoomInRequested += OnZoomInRequested;
            _currentVm.ZoomOutRequested += OnZoomOutRequested;
            _currentVm.ResetNorthRequested += OnResetNorthRequested;
            _currentVm.LocateMeRequested += OnLocateMeRequested;
            _currentVm.MapDataChanged += UpdateMapLayers;
            _currentVm.FocusLocationRequested += OnFocusLocationRequested;
            UpdateMapLayers(vm.VisibleHotspots, vm.CurrentSimulationState, vm.SimulationHotspotId, vm.SavedLocations.ToList());
        }
        else
        {
            _currentVm = null;
        }
    }

    // Warna sel menurut umur api: baru menyala (kuning) -> menengah (oranye) -> lama terbakar (merah tua).
    private static readonly (double R, double G, double B)[] AgeStops = { (253, 224, 71), (249, 115, 22), (153, 27, 27) };

    private static Mapsui.Styles.Color AgeColor(int age, int currentHour, byte alpha)
    {
        double t = currentHour <= 0 ? 0 : Math.Clamp(age / (double)currentHour, 0, 1);
        double scaled = t * (AgeStops.Length - 1);
        int i = Math.Min((int)scaled, AgeStops.Length - 2);
        double f = scaled - i;
        byte Mix(double a, double b) => (byte)Math.Round(a + (b - a) * f);
        return new Mapsui.Styles.Color(
            Mix(AgeStops[i].R, AgeStops[i + 1].R), Mix(AgeStops[i].G, AgeStops[i + 1].G), Mix(AgeStops[i].B, AgeStops[i + 1].B), alpha);
    }

    private static Polygon CellPolygon(double lat, double lon, double stepDeg)
    {
        double h = stepDeg / 2.0;
        var (x1, y1) = SphericalMercator.FromLonLat(lon - h, lat - h);
        var (x2, y2) = SphericalMercator.FromLonLat(lon + h, lat + h);
        return new Polygon(new LinearRing(new[]
        {
            new NetTopologySuite.Geometries.Coordinate(x1, y1), new NetTopologySuite.Geometries.Coordinate(x2, y1),
            new NetTopologySuite.Geometries.Coordinate(x2, y2), new NetTopologySuite.Geometries.Coordinate(x1, y2),
            new NetTopologySuite.Geometries.Coordinate(x1, y1)
        }));
    }

    private static LineString Line(double lat1, double lon1, double lat2, double lon2)
    {
        var (x1, y1) = SphericalMercator.FromLonLat(lon1, lat1);
        var (x2, y2) = SphericalMercator.FromLonLat(lon2, lat2);
        return new LineString(new[] { new NetTopologySuite.Geometries.Coordinate(x1, y1), new NetTopologySuite.Geometries.Coordinate(x2, y2) });
    }

    private static GeometryFeature LineFeature(LineString line, Mapsui.Styles.Color color, double width)
    {
        var f = new GeometryFeature(line);
        f.Styles.Add(new VectorStyle { Line = new Pen(color, width) });
        return f;
    }

    // Warna asap menurut tingkat: abu-abu muda (ringan) sampai hampir hitam (pekat); makin pekat makin buram.
    private static Mapsui.Styles.Color SmokeColor(int level) => level switch
    {
        3 => new Mapsui.Styles.Color(45, 45, 55, 165),
        2 => new Mapsui.Styles.Color(95, 95, 108, 120),
        _ => new Mapsui.Styles.Color(150, 150, 160, 75)
    };

    /// <summary>Lingkaran radius (km) di sekitar sebuah titik, sebagai poligon di koordinat peta.</summary>
    private static Polygon CirclePolygon(double lat, double lon, double radiusKm, int segments = 48)
    {
        double cosLat = Math.Max(0.01, Math.Cos(lat * Math.PI / 180.0));
        var ring = new NetTopologySuite.Geometries.Coordinate[segments + 1];
        for (int i = 0; i < segments; i++)
        {
            double a = 2 * Math.PI * i / segments;
            var (x, y) = SphericalMercator.FromLonLat(lon + radiusKm * Math.Sin(a) / (111.32 * cosLat), lat + radiusKm * Math.Cos(a) / 111.32);
            ring[i] = new NetTopologySuite.Geometries.Coordinate(x, y);
        }
        ring[segments] = ring[0];
        return new Polygon(new LinearRing(ring));
    }

    private void UpdateMapLayers(IReadOnlyList<HotspotDto> hotspots, SimulationMapState? sim, string? selectedId, IReadOnlyList<SavedLocationItem> locations)
    {
        if (_hotspotLayer == null || _simulationLayer == null || _simulationOverlayLayer == null || _smokeLayer == null || _locationsLayer == null) return;

        // 0) Asap: kotak grid kasar, makin pekat makin gelap. Digambar paling bawah supaya api tetap terlihat di atasnya.
        var smokeFeatures = new List<IFeature>(sim?.SmokeCells.Count ?? 0);
        if (sim != null)
        {
            foreach (var c in sim.SmokeCells)
            {
                var f = new GeometryFeature(CellPolygon(c.Latitude, c.Longitude, sim.SmokeCellSizeDegrees));
                f.Styles.Add(new VectorStyle { Fill = new Brush(SmokeColor(c.Level)), Outline = new Pen(Mapsui.Styles.Color.Transparent, 0) });
                smokeFeatures.Add(f);
            }
        }
        _smokeLayer.Features = smokeFeatures;
        _smokeLayer.DataHasChanged();

        // 0b) Lokasi tersimpan pengguna: cincin radius peringatan + penanda; merah bila ada notifikasi belum dibaca.
        var locationFeatures = new List<IFeature>(locations.Count * 2);
        foreach (var loc in locations)
        {
            bool alert = loc.UnreadCount > 0;
            var ringColor = alert ? new Mapsui.Styles.Color(220, 38, 38) : new Mapsui.Styles.Color(22, 163, 74);

            var ring = new GeometryFeature(CirclePolygon(loc.Latitude, loc.Longitude, loc.RadiusKm));
            ring.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Mapsui.Styles.Color(ringColor.R, ringColor.G, ringColor.B, 12)),   // tipis: peta di bawahnya tetap terbaca
                Outline = new Pen(new Mapsui.Styles.Color(ringColor.R, ringColor.G, ringColor.B, 210), 2) { PenStyle = PenStyle.Dash }
            });
            locationFeatures.Add(ring);

            var (px, py) = SphericalMercator.FromLonLat(loc.Longitude, loc.Latitude);
            var marker = new PointFeature(new MPoint(px, py));
            marker.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.6,
                Fill = new Brush(Mapsui.Styles.Color.White),
                Outline = new Pen(ringColor, 4)
            });
            marker.Styles.Add(new LabelStyle
            {
                Text = loc.Name,
                ForeColor = new Mapsui.Styles.Color(15, 23, 42),
                BackColor = new Brush(new Mapsui.Styles.Color(255, 255, 255, 215)),
                Offset = new Offset(0, 24),
                Font = new Mapsui.Styles.Font { Size = 12 }
            });
            locationFeatures.Add(marker);
        }
        _locationsLayer.Features = locationFeatures;
        _locationsLayer.DataHasChanged();

        // 1) Sel api: kotak sesuai grid simulasi, diwarnai menurut umur.
        var cellFeatures = new List<IFeature>(sim?.Cells.Count ?? 0);
        if (sim != null)
        {
            foreach (var c in sim.Cells)
            {
                var f = new GeometryFeature(CellPolygon(c.Latitude, c.Longitude, sim.CellSizeDegrees));
                f.Styles.Add(new VectorStyle
                {
                    Fill = new Brush(AgeColor(c.Age, sim.CurrentHour, 175)),
                    Outline = new Pen(AgeColor(c.Age, sim.CurrentHour, 235), 0.8)
                });
                cellFeatures.Add(f);
            }
        }
        _simulationLayer.Features = cellFeatures;
        _simulationLayer.DataHasChanged();

        // 2) Penanda titik asal dan panah arah rambatan (panjang tetap dalam jumlah sel, jadi ikut skala peta).
        var overlay = new List<IFeature>();
        if (sim != null)
        {
            double rad = sim.SpreadDirection * Math.PI / 180.0;
            double dLat = Math.Cos(rad), dLon = Math.Sin(rad);   // 0 derajat = utara (+lintang), 90 = timur (+bujur)
            double len = sim.CellSizeDegrees * 5;
            double tipLat = sim.OriginLatitude + dLat * len, tipLon = sim.OriginLongitude + dLon * len;
            var arrowColor = new Mapsui.Styles.Color(30, 64, 175);   // biru tua, kontras terhadap sel api

            overlay.Add(LineFeature(Line(sim.OriginLatitude, sim.OriginLongitude, tipLat, tipLon), arrowColor, 3.5));
            foreach (var spread in new[] { 150.0, -150.0 })   // dua sirip kepala panah
            {
                double a = (sim.SpreadDirection + spread) * Math.PI / 180.0;
                double headLen = sim.CellSizeDegrees * 1.6;
                overlay.Add(LineFeature(
                    Line(tipLat, tipLon, tipLat + Math.Cos(a) * headLen, tipLon + Math.Sin(a) * headLen), arrowColor, 3.5));
            }

            var (ox, oy) = SphericalMercator.FromLonLat(sim.OriginLongitude, sim.OriginLatitude);
            var origin = new PointFeature(new MPoint(ox, oy));
            origin.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.5,
                Fill = new Brush(Mapsui.Styles.Color.White),
                Outline = new Pen(new Mapsui.Styles.Color(220, 38, 38), 3)
            });
            overlay.Add(origin);
        }
        _simulationOverlayLayer.Features = overlay;
        _simulationOverlayLayer.DataHasChanged();

        // 3) Pin hotspot; yang sedang disimulasikan diberi cincin biru dan ukuran lebih besar.
        var hsFeatures = new List<IFeature>(hotspots.Count);
        foreach (var h in hotspots)
        {
            var (x, y) = SphericalMercator.FromLonLat(h.Longitude, h.Latitude);
            var f = new PointFeature(new MPoint(x, y));
            f["id"] = h.Id;
            bool selected = h.Id == selectedId;
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = selected ? 0.95 : 0.65,
                Fill = new Brush(h.Source == HotspotSource.Manual ? ManualColor : SatelliteColor),
                Outline = selected ? new Pen(new Mapsui.Styles.Color(30, 64, 175), 4) : new Pen(Mapsui.Styles.Color.White, 2)
            });
            hsFeatures.Add(f);
        }
        _hotspotLayer.Features = hsFeatures;
        _hotspotLayer.DataHasChanged();

        if (MapControl.IsLoaded) MapControl.Refresh();
    }

    private void OnFocusLocationRequested(double lat, double lon, int zoom)
    {
        if (MapControl.Map == null) return;
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        int z = Math.Clamp(zoom, 3, 16);
        if (MapControl.Map.Navigator.Resolutions.Count > z)
        {
            MapControl.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), MapControl.Map.Navigator.Resolutions[z], 400);
        }
        else
        {
            MapControl.Map.Navigator.CenterOn(new MPoint(x, y), 400);
        }
    }

    private void OnZoomInRequested() => MapControl.Map?.Navigator.ZoomIn(300);
    private void OnZoomOutRequested() => MapControl.Map?.Navigator.ZoomOut(300);
    private void OnResetNorthRequested() => MapControl.Map?.Navigator.RotateTo(0, 300);
    private void OnLocateMeRequested()
    {
        var centerLonLat = SphericalMercator.FromLonLat(101.85, 0.32);
        MapControl.Map?.Navigator.CenterOn(new MPoint(centerLonLat.x, centerLonLat.y), 500);
    }

    private static TileLayer CreateEnglishTileLayer()
    {
        // Default: Esri World Street Map provides full worldwide labels in English (China, Japan, etc.) without requiring an API key.
        var urlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}";
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(0, 16),
            urlTemplate,
            name: "Esri World Street Map",
            configureHttpRequestMessage: req => req.Headers.Add("User-Agent", "Ignis-WPF/1.0")
        );

        return new TileLayer(tileSource)
        {
            Name = "World Street Map (English)"
        };
    }
}
