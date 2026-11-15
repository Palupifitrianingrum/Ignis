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
using Mapsui.Tiling.Layers;
using Ignis.Frontend.Models.Admin;
using Ignis.Frontend.Views.Admin;
using Ignis.Frontend.ViewModels;
using Ignis.Shared;

namespace Ignis.Frontend.Views.Dashboard;

public partial class MapCanvasPlaceholder : UserControl
{
    private bool _isInitialized;
    private MemoryLayer? _simulationLayer;
    private MemoryLayer? _hotspotLayer;

    private static readonly Mapsui.Styles.Color SatelliteColor = new(232, 83, 31);     // #E8531F
    private static readonly Mapsui.Styles.Color ManualColor = new(46, 139, 74);        // #2E8B4A
    private static readonly Mapsui.Styles.Color SimulationColor = new(249, 115, 22, 110); // Translucent orange

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
            UpdateMapLayers(_currentVm.VisibleHotspots, Array.Empty<CoordinateDto>());
        }
    }

    private void InitializeMapsui()
    {
        var map = new Mapsui.Map();

        // 1. Add English-labeled Tile Layer (Esri World Street Map with worldwide English labels)
        var tileLayer = CreateEnglishTileLayer();
        map.Layers.Add(tileLayer);

        // 2. Add Simulation & Hotspot Vector Layers
        _simulationLayer = new MemoryLayer { Name = "Simulation", Features = new List<IFeature>(), Style = null };
        _hotspotLayer = new MemoryLayer { Name = "Hotspots", Features = new List<IFeature>(), Style = null };
        map.Layers.Add(_simulationLayer);
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
        if (mapInfo?.Feature?["id"] is string id)
        {
            var dto = _currentVm.VisibleHotspots.FirstOrDefault(h => h.Id == id);
            if (dto != null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var win = new HotspotDetailWindow(new HotspotRow(dto))
                    {
                        Owner = Window.GetWindow(this)
                    };
                    win.ShowDialog();
                }));
            }
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
            UpdateMapLayers(vm.VisibleHotspots, Array.Empty<CoordinateDto>());
        }
        else
        {
            _currentVm = null;
        }
    }

    private void UpdateMapLayers(IReadOnlyList<HotspotDto> hotspots, IReadOnlyList<CoordinateDto> burningCells)
    {
        if (_hotspotLayer == null || _simulationLayer == null) return;

        var simFeatures = new List<IFeature>(burningCells.Count);
        foreach (var c in burningCells)
        {
            var (x, y) = SphericalMercator.FromLonLat(c.Longitude, c.Latitude);
            var f = new PointFeature(new MPoint(x, y));
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.75,
                Fill = new Brush(SimulationColor),
                Outline = new Pen(new Color(220, 38, 38, 160), 1)
            });
            simFeatures.Add(f);
        }
        _simulationLayer.Features = simFeatures;
        _simulationLayer.DataHasChanged();

        var hsFeatures = new List<IFeature>(hotspots.Count);
        foreach (var h in hotspots)
        {
            var (x, y) = SphericalMercator.FromLonLat(h.Longitude, h.Latitude);
            var f = new PointFeature(new MPoint(x, y));
            f["id"] = h.Id;
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.65,
                Fill = new Brush(h.Source == HotspotSource.Manual ? ManualColor : SatelliteColor),
                Outline = new Pen(Color.White, 2)
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

    public static TileLayer CreateCartoVoyagerLayer(string apiKey)
    {
        // CARTO Voyager raster tiles with English labels worldwide.
        // Requires a free API key from https://carto.com/basemaps/apikey to avoid the "API KEY REQUIRED" watermark.
        var urlTemplate = $"https://{{s}}.basemaps.cartocdn.com/rastertiles/voyager/{{z}}/{{x}}/{{y}}.png?key={apiKey}";
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(0, 16),
            urlTemplate,
            serverNodes: new[] { "a", "b", "c", "d" },
            name: "CARTO Voyager",
            configureHttpRequestMessage: req => req.Headers.Add("User-Agent", "Ignis-WPF/1.0")
        );

        return new TileLayer(tileSource)
        {
            Name = "CARTO Voyager (English)"
        };
    }
}
