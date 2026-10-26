using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Mapsui;
using Mapsui.Limiting;
using Mapsui.Projections;
using Mapsui.Tiling;
using Ignis.Frontend.ViewModels;

namespace Ignis.Frontend.Views.Dashboard;

public partial class MapCanvasPlaceholder : UserControl
{
    private bool _isInitialized;

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
    }

    private void InitializeMapsui()
    {
        var map = new Mapsui.Map();

        // 1. Add standard OpenStreetMap Tile Layer
        var tileLayer = OpenStreetMap.CreateTileLayer();
        map.Layers.Add(tileLayer);

        // 2. Set Limiter so user cannot pan or zoom outside map extent into empty white space
        map.Navigator.Limiter = new ViewportLimiterKeepWithinExtent();

        // 3. Limit zoom bounds: maximum zoom out resolution (level 3) so it cannot zoom out into empty white space
        if (map.Navigator.Resolutions.Count > 4)
        {
            var minRes = map.Navigator.Resolutions.Min();
            var maxZoomOutRes = map.Navigator.Resolutions[3]; // Zoom level 3: slightly smaller than full world, fits screen without white border
            map.Navigator.OverrideZoomBounds = new MMinMax(Math.Min(minRes, maxZoomOutRes), Math.Max(minRes, maxZoomOutRes));
        }

        if (tileLayer.Extent != null)
        {
            map.Navigator.OverridePanBounds = tileLayer.Extent;
        }

        // 4. Initial Center: Pelalawan / Riau, Indonesia (0.32° N, 101.85° E)
        var centerLonLat = SphericalMercator.FromLonLat(101.85, 0.32);
        var centerPoint = new MPoint(centerLonLat.x, centerLonLat.y);

        MapControl.Map = map;

        // 3. Center and Zoom to resolution level
        if (map.Navigator.Resolutions.Count > 10)
        {
            map.Navigator.CenterOnAndZoomTo(centerPoint, map.Navigator.Resolutions[10]);
        }
        else
        {
            map.Navigator.CenterOn(centerPoint);
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
        }

        if (e.NewValue is MainWindowViewModel vm)
        {
            _currentVm = vm;
            _currentVm.ZoomInRequested += OnZoomInRequested;
            _currentVm.ZoomOutRequested += OnZoomOutRequested;
            _currentVm.ResetNorthRequested += OnResetNorthRequested;
            _currentVm.LocateMeRequested += OnLocateMeRequested;
        }
        else
        {
            _currentVm = null;
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
}
