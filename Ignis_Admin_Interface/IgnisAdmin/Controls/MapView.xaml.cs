using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IgnisAdmin.Models;
using IgnisAdmin.Services;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
namespace IgnisAdmin.Controls;

/// <summary>
/// Peta berbasis Mapsui (native WPF + SkiaSharp), tile Esri World Street Map (default) atau CARTO Voyager bila API key diisi.
/// API publik sama seperti versi Leaflet sebelumnya, jadi halaman lain tidak perlu diubah:
/// SetHotspots, FocusOn, SetPick, MapClicked (lat, lon), HotspotClicked (id).
/// </summary>
public partial class MapView : UserControl
{
    public event Action<double, double>? MapClicked;   // (latitude, longitude)
    public event Action<string>? HotspotClicked;       // id hotspot

    readonly Mapsui.UI.Wpf.MapControl _map = new();
    readonly MemoryLayer _hotspotLayer;
    readonly MemoryLayer _pickLayer;

    // Navigator Mapsui baru bisa dipakai setelah kontrol punya ukuran; perintah tampilan ditunda sampai saat itu.
    Action? _pendingView;

    static readonly Color RedFill = new(232, 83, 31);     // satelit  #E8531F
    static readonly Color GreenFill = new(46, 139, 74);   // manual   #2E8B4A
    static readonly Color DarkFill = new(15, 59, 51);     // pick     #0F3B33

    public MapView()
    {
        InitializeComponent();

        var map = new Mapsui.Map();
        var (tiles, attribution) = CreateTileLayer();
        map.Layers.Add(tiles);
        Attrib.Text = attribution;

        _hotspotLayer = new MemoryLayer { Name = "Hotspots", IsMapInfoLayer = true, Features = new List<IFeature>(), Style = null };
        _pickLayer = new MemoryLayer { Name = "Pick", IsMapInfoLayer = false, Features = new List<IFeature>(), Style = null };
        map.Layers.Add(_hotspotLayer);
        map.Layers.Add(_pickLayer);

        _map.Map = map;
        _map.Info += (_, e) =>
        {
            var info = e.MapInfo;
            if (info == null) return;

            // klik pada titik hotspot
            if (info.Feature?["id"] is string id)
            {
                Dispatcher.BeginInvoke(new Action(() => HotspotClicked?.Invoke(id)));
                return;
            }

            // klik pada area kosong peta -> koordinat (untuk form tambah/edit)
            if (info.WorldPosition is { } p)
            {
                var (lon, lat) = SphericalMercator.ToLonLat(p.X, p.Y);
                MapClicked?.Invoke(lat, lon);
            }
        };

        // Terapkan tampilan awal / tertunda setelah ukuran kontrol diketahui
        _map.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && _pendingView is { } view)
            {
                _pendingView = null;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, view);
            }
        };

        Root.Children.Insert(0, _map);
        _pendingView = ShowIndonesia;
    }

    // Basemap disamakan dengan Ignis.Frontend (versi publik):
    //  - default: Esri World Street Map (label Inggris, tanpa API key)
    //  - jika API key CARTO diisi (Pengaturan / env CARTO_API_KEY): CARTO Voyager
    static (TileLayer Layer, string Attribution) CreateTileLayer()
    {
        var key = DataStore.Instance.Settings.CartoKey;
        if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("CARTO_API_KEY") ?? "";
        return string.IsNullOrWhiteSpace(key)
            ? (CreateEnglishTileLayer(), "Tiles © Esri")
            : (CreateCartoVoyagerLayer(key.Trim()), "© OpenStreetMap contributors © CARTO");
    }

    static TileLayer CreateEnglishTileLayer()
    {
        var urlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}";
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(yAxis: YAxis.OSM, minZoomLevel: 0, maxZoomLevel: 16, name: "Mercator"),
            urlTemplate,
            name: "Esri World Street Map",
            userAgent: "Ignis-WPF/1.0"
            // Mapsui.Wpf 5.x / BruTile 6: ganti baris userAgent di atas dengan
            // configureHttpRequestMessage: req => req.Headers.Add("User-Agent", "Ignis-WPF/1.0")
        );
        return new TileLayer(tileSource) { Name = "World Street Map (English)" };
    }

    static TileLayer CreateCartoVoyagerLayer(string apiKey)
    {
        var urlTemplate = $"https://{{s}}.basemaps.cartocdn.com/rastertiles/voyager/{{z}}/{{x}}/{{y}}.png?key={Uri.EscapeDataString(apiKey)}";
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(yAxis: YAxis.OSM, minZoomLevel: 0, maxZoomLevel: 16, name: "Mercator"),
            urlTemplate,
            serverNodes: new[] { "a", "b", "c", "d" },
            name: "CARTO Voyager",
            userAgent: "Ignis-WPF/1.0"
        );
        return new TileLayer(tileSource) { Name = "CARTO Voyager (English)" };
    }

    void RunView(Action view)
    {
        if (_map.ActualWidth > 0 && _map.ActualHeight > 0 && _pendingView == null) view();
        else _pendingView = view;   // menimpa tampilan tertunda sebelumnya (yang terakhir menang)
    }

    void ShowIndonesia()
    {
        var (x1, y1) = SphericalMercator.FromLonLat(94, -11);
        var (x2, y2) = SphericalMercator.FromLonLat(141, 6);
        _map.Map?.Navigator.ZoomToBox(new MRect(x1, y1, x2, y2));
    }

    void Redraw() { if (_map.IsLoaded) _map.Refresh(); }   // sebelum Loaded, data akan tergambar otomatis saat kontrol tampil

    static double ResolutionForZoom(int zoom) => 156543.03392804097 / Math.Pow(2, zoom);

    public void SetHotspots(IEnumerable<Hotspot> list)
    {
        var features = new List<IFeature>();
        foreach (var h in list)
        {
            var (x, y) = SphericalMercator.FromLonLat(h.Longitude, h.Latitude);
            var f = new PointFeature(new MPoint(x, y));
            f["id"] = h.Id;
            f["src"] = h.SourceText;
            f["conf"] = h.ConfidenceText;
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.55,
                Fill = new Mapsui.Styles.Brush(h.IsManual ? GreenFill : RedFill),
                Outline = new Pen(Color.White, 2)
            });
            features.Add(f);
        }
        _hotspotLayer.Features = features;
        _hotspotLayer.DataHasChanged();
        Redraw();
    }

    public void FocusOn(double lat, double lon, int zoom = 11)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        RunView(() => _map.Map?.Navigator.CenterOnAndZoomTo(new MPoint(x, y), ResolutionForZoom(zoom)));
    }

    public void SetPick(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var f = new PointFeature(new MPoint(x, y));
        f.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.8,
            Fill = new Mapsui.Styles.Brush(DarkFill),
            Outline = new Pen(Color.White, 3)
        });
        _pickLayer.Features = new List<IFeature> { f };
        _pickLayer.DataHasChanged();
        Redraw();
    }
}
