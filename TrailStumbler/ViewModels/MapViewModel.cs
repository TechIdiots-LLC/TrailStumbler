using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Navigation;
using Microsoft.Extensions.DependencyInjection;
using MaplibreNative.Routing.Core.Mvt;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Services;
using TrailStumbler.Services;

namespace TrailStumbler.ViewModels;

/// <summary>
/// Singleton owner of the live map state. LayersViewModel calls into it for
/// visibility toggles / zoom-to-layer even while MapPage isn't instantiated;
/// the next OnMapControllerReady (fired on every style load) reconciles.
/// Lifecycle contract cloned from VistumblerMAUI's MapViewModel.
/// </summary>
public partial class MapViewModel : ObservableObject
{
    private readonly ILayerRepository _repo;
    private readonly ITrackRecorderService _recorder;
    private readonly RouteOverlay _overlay;
    private readonly NavigationSession _session;
    private readonly SqliteTileCacheProvider _tileCache;
    private readonly IRouteDataSource _dataSource;

    // Set on StyleLoaded (main thread); null while no live style.
    private IMapLibreMapController? _controller;
    // Layer ids currently committed to the style; reset on each style reload.
    private readonly HashSet<string> _addedLayerIds = new();
    // Layers currently shown, for tap hit-testing (points queried before lines).
    private readonly List<MapLayer> _shownLayers = new();

    // Zoom-to-layer request from LayersPage, consumed when the map is next ready.
    private MapLayer? _pendingFitLayer;

    // Live recording points snapshot, for re-adding the yellow line on style reload.
    private IReadOnlyList<RecordingPoint>? _liveRecordingPoints;

    private const string RecordingSourceId = "recording-src";
    private const string RecordingLayerId = "recording-line";

    [ObservableProperty] private string _styleUrl = MapStyles.StyleUrl;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordingStatus = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelsButtonText))]
    private bool _isLabelsVisible = true;

    public string LabelsButtonText => _isLabelsVisible ? "Labels ✓" : "Labels";

    partial void OnIsLabelsVisibleChanged(bool value)
    {
        if (_controller is not null)
            _ = RefreshMapLayersAsync();
    }

    [RelayCommand]
    private void ToggleLabels() => IsLabelsVisible = !_isLabelsVisible;

    // ── Tap-to-inspect popup ──────────────────────────────────────────────────
    [ObservableProperty] private MapFeatureInfo? _selectedFeature;
    [ObservableProperty] private bool _isPopupVisible;

    // ── Route planner ─────────────────────────────────────────────────────────

    private const string OriginSourceId  = "wp-origin-src";
    private const string OriginLayerId   = "wp-origin";
    private const string DestSourceId    = "wp-dest-src";
    private const string DestLayerId     = "wp-dest";
    private const double SnapThresholdM  = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginLabel))]
    private (double Lat, double Lon)? _origin;
    partial void OnOriginChanged((double Lat, double Lon)? value) => UpdateWaypointPinsOnMap();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationLabel))]
    private (double Lat, double Lon)? _destination;
    partial void OnDestinationChanged((double Lat, double Lon)? value) => UpdateWaypointPinsOnMap();

    // true while the user is actively placing that pin (next map tap drops it)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacingPin))]
    private bool _isPlacingOrigin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlacingPin))]
    private bool _isPlacingDestination;

    [ObservableProperty] private bool _originSnapped;
    [ObservableProperty] private bool _destinationSnapped;
    [ObservableProperty] private bool _isNavigating;
    [ObservableProperty] private RouteProgress? _currentProgress;
    [ObservableProperty] private string _routeStatusMessage = "";
    [ObservableProperty] private bool _hasHighwayWarning;
    [ObservableProperty] private int _selectedProfileIndex;

    // Route alternatives — populated after a successful Plan Route.
    public ObservableCollection<RouteOptionViewModel> RouteOptions { get; } = [];
    public bool HasRouteOptions => RouteOptions.Count > 0;

    // Number of alternative routes the user wants the engine to attempt (1–3).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteCountLabel))]
    private int _routeCount = 1;
    public string RouteCountLabel => $"Routes: {_routeCount}";

    [RelayCommand]
    private void IncreaseRouteCount() { if (_routeCount < 3) RouteCount++; }

    [RelayCommand]
    private void DecreaseRouteCount() { if (_routeCount > 1) RouteCount--; }

    public bool IsPlacingPin => _isPlacingOrigin || _isPlacingDestination;

    public string OriginLabel => _origin is null
        ? "Tap map to place A"
        : $"{_origin.Value.Lat:F5}, {_origin.Value.Lon:F5}" + (_originSnapped ? " ●" : "");

    public string DestinationLabel => _destination is null
        ? "Tap map to place B"
        : $"{_destination.Value.Lat:F5}, {_destination.Value.Lon:F5}" + (_destinationSnapped ? " ●" : "");

    // Profile list shown in the route planner picker.
    // HybridOffline variants appear first; they use MVT tiles from the basemap's
    // "openmaptiles" source so no external server is required at route-plan time.
    public static readonly string[] ProfileNames =
    [
        "Hybrid moto (offline)",   // HybridOfflineMotorcycle — default
        "Hybrid bike (offline)",   // HybridOfflineBicycle
        "Trails only",             // TrackOnly
        "Hybrid moto (online)",    // HybridMotorcycle — Valhalla
        "Hybrid bike (online)",    // HybridBicycle
        "Auto (road)",
        "Motorcycle (road)",
        "Bicycle (road)",
        "Walking",
    ];

    private static readonly RouteProfile[] ProfileValues =
    [
        RouteProfile.HybridOfflineMotorcycle,
        RouteProfile.HybridOfflineBicycle,
        RouteProfile.TrackOnly,
        RouteProfile.HybridMotorcycle,
        RouteProfile.HybridBicycle,
        RouteProfile.Auto,
        RouteProfile.Motorcycle,
        RouteProfile.Bicycle,
        RouteProfile.Pedestrian,
    ];

    private RouteProfile SelectedProfile
        => _selectedProfileIndex >= 0 && _selectedProfileIndex < ProfileValues.Length
            ? ProfileValues[_selectedProfileIndex]
            : RouteProfile.HybridOfflineMotorcycle;

    // Cached MVT TileJSON URL derived from the "openmaptiles" source in the loaded style.
    // Populated once by FetchMvtTileUrlAsync; required for HybridOffline/Offline profiles.
    private string? _mvtTileJsonUrl;

    // Raised on the main thread when a track is saved; LayersViewModel subscribes.
    public event EventHandler<MapLayer>? RecordingSaved;

    /// <summary>Called from App crash-recovery to propagate a recovered track to LayersViewModel.</summary>
    public void NotifyRecordingSaved(MapLayer layer) => RecordingSaved?.Invoke(this, layer);

    public MapViewModel(ILayerRepository repo, ITrackRecorderService recorder,
        RouteOverlay overlay, IServiceProvider services,
        SqliteTileCacheProvider tileCache, IRouteDataSource dataSource)
    {
        _repo = repo;
        _recorder = recorder;
        _recorder.TrackUpdated += OnTrackUpdated;

        _overlay = overlay;
        _tileCache = tileCache;
        _dataSource = dataSource;
        _session = services.GetRequiredService<NavigationSession>();
        _session.ProgressUpdated += OnProgressUpdated;
        _session.AnnouncementNeeded += OnAnnouncementNeeded;
    }

    private static string SourceId(int layerId) => $"trail-src-{layerId}";

    private static string MapLayerId(MapLayer layer) => layer.Family switch
    {
        GeometryFamily.Line => $"trail-{layer.Id}-line",
        GeometryFamily.Polygon => $"trail-{layer.Id}-fill",
        _ => $"trail-{layer.Id}-symbol",
    };

    // ── Controller wiring ─────────────────────────────────────────────────────

    /// <summary>Called from MapPage.xaml.cs on every StyleLoaded. Re-adds all
    /// visible layers (runtime sources/layers are wiped on style reload) and
    /// applies any pending zoom-to-layer request.</summary>
    public async void OnMapControllerReady(IMapLibreMapController controller)
    {
        _controller = controller;
        _overlay.SetController(controller);
        _addedLayerIds.Clear();
        _shownLayers.Clear();

        try
        {
            var layers = await _repo.GetLayersAsync();
            // Lines/areas first, points on top; within a family, SortOrder.
            foreach (var layer in layers.Where(l => l.IsVisible)
                         .OrderBy(l => l.Family == GeometryFamily.Point ? 1 : 0)
                         .ThenBy(l => l.SortOrder))
                await AddLayerToMapAsync(layer);

            // Re-add the live recording line if a style reload happened mid-recording.
            if (_isRecording && _liveRecordingPoints?.Count >= 2)
                UpdateRecordingLineOnMap(_liveRecordingPoints);

            // Re-draw active route(s) if navigation was running when style reloaded.
            if (_session.RouteAlternatives.Count > 0)
                _overlay.ShowRoutes(_session.RouteAlternatives, _session.SelectedRouteIndex);
            else if (_session.ActiveRoute is { } singleRoute)
                _overlay.ShowRoute(singleRoute);

            StatusMessage = $"{_addedLayerIds.Count} layer(s) shown";
            UpdateWaypointPinsOnMap();
            TryApplyPendingFit();
            if (_mvtTileJsonUrl is null)
                _ = FetchMvtTileUrlAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] OnMapControllerReady threw: {ex}");
            StatusMessage = $"Map layer load failed: {ex.Message}";
        }
    }

    // ── Layer add/remove ──────────────────────────────────────────────────────

    private async Task AddLayerToMapAsync(MapLayer layer)
    {
        if (_controller is null) return;
        var mapLayerId = MapLayerId(layer);
        if (_addedLayerIds.Contains(mapLayerId)) return;

        var geoJson = await _repo.BuildLayerGeoJsonAsync(layer.Id);
        _controller.AddGeoJsonSource(SourceId(layer.Id), geoJson);

        switch (layer.Family)
        {
            case GeometryFamily.Line:
                _controller.AddLineLayer(mapLayerId, SourceId(layer.Id),
                    belowLayerId: null, sourceLayer: null, properties: LinePaint(layer.ColorHex),
                    enableInteraction: true);
                break;

            case GeometryFamily.Polygon:
                _controller.AddFillLayer(mapLayerId, SourceId(layer.Id),
                    belowLayerId: null, sourceLayer: null, properties: FillPaint(layer.ColorHex));
                _controller.AddLineLayer($"trail-{layer.Id}-outline", SourceId(layer.Id),
                    belowLayerId: null, sourceLayer: null, properties: LinePaint(layer.ColorHex, width: 1.5));
                _addedLayerIds.Add($"trail-{layer.Id}-outline");
                break;

            default:
                _controller.AddSymbolLayer(mapLayerId, SourceId(layer.Id),
                    belowLayerId: null, sourceLayer: null, properties: SymbolLayout(),
                    enableInteraction: true);
                break;
        }
        _addedLayerIds.Add(mapLayerId);
        _shownLayers.RemoveAll(l => l.Id == layer.Id);
        _shownLayers.Add(layer);
    }

    private void RemoveLayerFromMap(MapLayer layer)
    {
        if (_controller is null) return;
        foreach (var id in new[] { MapLayerId(layer), $"trail-{layer.Id}-outline" })
        {
            if (!_addedLayerIds.Remove(id)) continue;
            try { _controller.RemoveLayer(id); }
            catch (Exception ex) { Debug.WriteLine($"[MapViewModel] RemoveLayer({id}) threw: {ex}"); }
        }
        try { _controller.RemoveSource(SourceId(layer.Id)); }
        catch (Exception ex) { Debug.WriteLine($"[MapViewModel] RemoveSource threw: {ex}"); }
        _shownLayers.RemoveAll(l => l.Id == layer.Id);
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (_isRecording)
            await StopRecordingAsync();
        else
            await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            var name = $"Track {DateTime.Now:yyyy-MM-dd HH:mm}";
            await _recorder.StartAsync(name);
            IsRecording = true;
            RecordingStatus = "Recording… 0 pts";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] StartRecording threw: {ex}");
            RecordingStatus = $"Could not start: {ex.Message}";
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            var layer = await _recorder.StopAndSaveAsync();
            IsRecording = false;
            _liveRecordingPoints = null;
            RemoveRecordingLineFromMap();

            if (layer is not null)
            {
                RecordingStatus = $"Saved: {layer.Name}";
                RecordingSaved?.Invoke(this, layer);
            }
            else
            {
                RecordingStatus = "Discarded (too few points)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] StopRecording threw: {ex}");
            RecordingStatus = $"Save failed: {ex.Message}";
        }
    }

    // Called by ITrackRecorderService.TrackUpdated on the main thread, throttled ≥2 s.
    private void OnTrackUpdated(object? sender, IReadOnlyList<RecordingPoint> points)
    {
        _liveRecordingPoints = points;
        RecordingStatus = $"Recording… {points.Count} pts";
        if (points.Count >= 2)
            UpdateRecordingLineOnMap(points);
    }

    private void UpdateRecordingLineOnMap(IReadOnlyList<RecordingPoint> points)
    {
        if (_controller is null) return;
        try
        {
            var geoJson = BuildRecordingGeoJson(points);
            if (_addedLayerIds.Contains(RecordingLayerId))
            {
                _controller.SetGeoJsonSource(RecordingSourceId, geoJson);
            }
            else
            {
                _controller.AddGeoJsonSource(RecordingSourceId, geoJson);
                _controller.AddLineLayer(RecordingLayerId, RecordingSourceId,
                    belowLayerId: null, sourceLayer: null,
                    properties: new Dictionary<string, object?>
                    {
                        ["line-color"] = "#FFFF66",
                        ["line-width"] = 4.0,
                        ["line-opacity"] = 1.0,
                    });
                _addedLayerIds.Add(RecordingLayerId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] UpdateRecordingLine threw: {ex}");
        }
    }

    private void RemoveRecordingLineFromMap()
    {
        if (_controller is null || !_addedLayerIds.Contains(RecordingLayerId)) return;
        try { _controller.RemoveLayer(RecordingLayerId); } catch { }
        try { _controller.RemoveSource(RecordingSourceId); } catch { }
        _addedLayerIds.Remove(RecordingLayerId);
    }

    private static string BuildRecordingGeoJson(IReadOnlyList<RecordingPoint> points)
    {
        var sb = new StringBuilder(points.Count * 28);
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = points[i];
            sb.Append('[');
            sb.Append(p.Lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(p.Lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(']');
        }
        sb.Append("]}}]}");
        return sb.ToString();
    }

    // ── Paint ─────────────────────────────────────────────────────────────────
    // Trail lines honor a per-feature simplestyle "stroke" (KML style colors carried
    // through import) and fall back to the layer color — same expression the user's
    // atv-trail-map web page uses.

    private Dictionary<string, object?> LinePaint(string colorHex, double width = 3.0)
    {
        var paint = new Dictionary<string, object?>
        {
            ["line-color"] = new object[]
            {
                "case",
                new object[] { "has", "stroke" },
                new object[] { "get", "stroke" },
                colorHex,
            },
            ["line-width"] = width,
            ["line-opacity"] = 0.85,
        };
        if (_isLabelsVisible)
        {
            paint["text-field"] = new object[] { "coalesce", new object[] { "get", "name" }, "" };
            paint["text-size"] = 11.0;
            paint["text-color"] = "#333333";
            paint["text-halo-color"] = "#FFFFFF";
            paint["text-halo-width"] = 1.0;
            paint["text-max-angle"] = 30.0;
            paint["symbol-placement"] = "line";
        }
        return paint;
    }

    // Maps the "category" property (from KML icon href filename) to colored sprite names
    // present in the WDB OSM basemap style. OnStyleImageMissingReceived is the future hook
    // for bundling sprites in-app when using styles that lack them.
    private Dictionary<string, object?> SymbolLayout()
    {
        var layout = new Dictionary<string, object?>
        {
            ["icon-image"] = new object[]
            {
                "case",
                new object[] { "==", new object[] { "get", "category" }, "parking" },     "colored:parking_15",
                new object[] { "==", new object[] { "get", "category" }, "fuel" },         "colored:fuel_15",
                new object[] { "==", new object[] { "get", "category" }, "food" },         "colored:restaurant_15",
                new object[] { "==", new object[] { "get", "category" }, "lodging" },      "colored:hotel_15",
                new object[] { "==", new object[] { "get", "category" }, "camping" },      "colored:campsite_15",
                new object[] { "==", new object[] { "get", "category" }, "star" },         "colored:star_15",
                new object[] { "==", new object[] { "get", "category" }, "scenic_view" },  "colored:attraction_15",
                new object[] { "==", new object[] { "get", "category" }, "restroom" },     "colored:toilets_15",
                new object[] { "==", new object[] { "get", "category" }, "atv_club" },     "colored:warehouse_15",
                "colored:circle_15",
            },
            ["icon-size"] = 1.2,
            ["icon-allow-overlap"] = true,
            ["icon-ignore-placement"] = true,
        };
        if (_isLabelsVisible)
        {
            layout["text-field"] = new object[] { "coalesce", new object[] { "get", "name" }, "" };
            layout["text-size"] = 11.0;
            layout["text-anchor"] = "top";
            layout["text-offset"] = new object[] { 0.0, 0.5 };
            layout["text-color"] = "#333333";
            layout["text-halo-color"] = "#FFFFFF";
            layout["text-halo-width"] = 1.0;
            layout["text-optional"] = true;
        }
        return layout;
    }

    private static Dictionary<string, object?> FillPaint(string colorHex) => new()
    {
        ["fill-color"] = colorHex,
        ["fill-opacity"] = 0.25,
    };

    // ── Called from LayersViewModel ───────────────────────────────────────────

    private static bool IsPersistentLayerId(string id)
        => id == RecordingLayerId || id == OriginLayerId || id == DestLayerId;

    /// <summary>Re-adds all shown layers with the current paint settings (e.g. after label toggle).
    /// Operates on the already-loaded layer list — no DB round-trip needed.</summary>
    private async Task RefreshMapLayersAsync()
    {
        if (_controller is null) return;
        // Remove all current layers so they can be re-added with fresh paint.
        foreach (var id in _addedLayerIds.ToList())
        {
            if (IsPersistentLayerId(id)) continue;
            try { _controller.RemoveLayer(id); } catch { }
        }
        // Remove sources for non-persistent layers.
        _addedLayerIds.RemoveWhere(id => !IsPersistentLayerId(id));
        _shownLayers.Clear();

        // Extract integer layer ids from the layer id strings.
        var layers = await _repo.GetLayersAsync();
        foreach (var layer in layers.Where(l => l.IsVisible)
                     .OrderBy(l => l.Family == GeometryFamily.Point ? 1 : 0)
                     .ThenBy(l => l.SortOrder))
        {
            try { _controller.RemoveSource(SourceId(layer.Id)); } catch { }
            await AddLayerToMapAsync(layer);
        }
        // Re-draw waypoint pins on top after all trail layers.
        UpdateWaypointPinsOnMap();
    }

    /// <summary>Apply a visibility toggle to the live map (persistence is the
    /// caller's job). No-op without a live controller — the next style load
    /// reconciles from the DB.</summary>
    public async Task SetLayerVisibilityAsync(MapLayer layer, bool visible)
    {
        if (_controller is null) return;
        if (visible) await AddLayerToMapAsync(layer);
        else RemoveLayerFromMap(layer);
    }

    /// <summary>Remove a deleted layer from the live map immediately.</summary>
    public void OnLayerDeleted(MapLayer layer) => RemoveLayerFromMap(layer);

    /// <summary>Newly imported layers: show them if the map is live.</summary>
    public async Task OnLayersImportedAsync(IEnumerable<MapLayer> layers)
    {
        foreach (var layer in layers.Where(l => l.IsVisible))
            await AddLayerToMapAsync(layer);
    }

    /// <summary>Queue a zoom-to-layer; applied now if the map is live, otherwise on
    /// the next controller-ready. Caller navigates to MapPage.</summary>
    public void RequestFitBounds(MapLayer layer)
    {
        _pendingFitLayer = layer;
        TryApplyPendingFit();
    }

    /// <summary>Also called from MapPage.OnAppearing — a queued fit must apply when
    /// the page comes back on screen with an already-loaded style.</summary>
    public void TryApplyPendingFit()
    {
        if (_controller is null || _pendingFitLayer is null) return;
        var layer = _pendingFitLayer;
        _pendingFitLayer = null;

        try
        {
            var cam = _controller.CameraForLatLngs(
                new[] { (layer.MinLat, layer.MinLon), (layer.MaxLat, layer.MaxLon) },
                padTop: 40, padLeft: 40, padBottom: 40, padRight: 40);
            // Clamp for single-point layers, where the bbox has zero extent.
            _controller.EaseTo(cam.Lat, cam.Lon, Math.Min(cam.Zoom, 16));
            StatusMessage = $"Zoomed to {layer.Name}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] TryApplyPendingFit threw: {ex}");
        }
    }

    /// <summary>Re-style a layer after a color change: paint is fixed when a layer
    /// is committed to the style, so remove and re-add.</summary>
    public async Task ApplyLayerColorAsync(MapLayer layer)
    {
        if (_controller is null || !layer.IsVisible) return;
        RemoveLayerFromMap(layer);
        await AddLayerToMapAsync(layer);
    }

    // ── Tap-to-inspect ────────────────────────────────────────────────────────

    /// <summary>Called from MapPage on MapClick. Hit-tests shown layers (points
    /// before lines/areas, then newest first), resolves the hit's "_fid" back to
    /// its DB row, and opens the popup.</summary>
    public async Task OnMapTappedAsync(double screenX, double screenY)
    {
        if (_controller is null) return;

        var candidates = _shownLayers
            .OrderByDescending(l => l.Family == GeometryFamily.Point ? 1 : 0)
            .ThenByDescending(l => l.SortOrder)
            .ToList();

        foreach (var layer in candidates)
        {
            string? json;
            try { json = _controller.QueryRenderedFeaturesAtPoint(screenX, screenY, MapLayerId(layer)); }
            catch { continue; }

            long? fid = ParseFirstFid(json);
            if (fid is null) continue;

            var feature = await _repo.GetFeatureAsync(fid.Value);
            if (feature is null) continue;

            SelectedFeature = new MapFeatureInfo
            {
                Title = feature.Name.Length > 0 ? feature.Name : layer.Name,
                LayerName = layer.Name,
                Description = feature.Description,
                Category = ExtractCategory(feature.PropertiesJson),
            };
            IsPopupVisible = true;
            return;
        }

        // Nothing under the tap — dismiss any open popup.
        IsPopupVisible = false;
    }

    [RelayCommand]
    private void ClosePopup() => IsPopupVisible = false;

    // ── Route planner commands ────────────────────────────────────────────────

    /// <summary>Enter origin-placement mode; the next map tap will drop the A pin.</summary>
    [RelayCommand]
    private void ActivateOriginPlacement()
    {
        IsPlacingOrigin = true;
        IsPlacingDestination = false;
    }

    /// <summary>Enter destination-placement mode; the next map tap will drop the B pin.</summary>
    [RelayCommand]
    private void ActivateDestinationPlacement()
    {
        IsPlacingDestination = true;
        IsPlacingOrigin = false;
    }

    /// <summary>Called from MapPage when IsPlacingPin is true and the user taps/long-presses.
    /// Snaps to nearest routable trail coordinate within 50 m, then sets the appropriate pin.</summary>
    public async Task PlacePinAsync(double lat, double lon)
    {
        var snapped = await SnapToTrailAsync(lat, lon);
        bool wasSnapped = snapped.HasValue;
        var coord = snapped ?? (lat, lon);

        if (_isPlacingOrigin)
        {
            OriginSnapped = wasSnapped;
            Origin = coord;
            IsPlacingOrigin = false;
        }
        else if (_isPlacingDestination)
        {
            DestinationSnapped = wasSnapped;
            Destination = coord;
            IsPlacingDestination = false;
        }
    }

    /// <summary>Find the nearest TrackFeature coordinate within <see cref="SnapThresholdM"/> metres.
    /// Returns null when no routable track is within range.</summary>
    private async Task<(double Lat, double Lon)?> SnapToTrailAsync(double lat, double lon)
    {
        try
        {
            var features = await _dataSource.GetRoutableTrackFeaturesAsync();
            (double Lat, double Lon)? best = null;
            double bestDist = SnapThresholdM;

            foreach (var feature in features)
            {
                foreach (var (fLon, fLat) in feature.Coordinates)
                {
                    double d = HaversineM(lat, lon, fLat, fLon);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = (fLat, fLon);
                    }
                }
            }
            return best;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] SnapToTrail threw: {ex}");
            return null;
        }
    }

    /// <summary>Add or update the A/B waypoint circle layers on the live map.</summary>
    private void UpdateWaypointPinsOnMap()
    {
        if (_controller is null) return;

        UpdatePin(OriginSourceId, OriginLayerId, _origin, "#22AA22");
        UpdatePin(DestSourceId, DestLayerId, _destination, "#CC2222");
    }

    private void UpdatePin(string srcId, string layId, (double Lat, double Lon)? coord, string color)
    {
        if (_controller is null) return;
        try
        {
            if (coord is null)
            {
                // Remove if present.
                if (_addedLayerIds.Remove(layId))
                {
                    try { _controller.RemoveLayer(layId); } catch { }
                    try { _controller.RemoveSource(srcId); } catch { }
                }
                return;
            }

            var geoJson = $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\"," +
                          $"\"properties\":{{}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":" +
                          $"[{coord.Value.Lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}," +
                          $"{coord.Value.Lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}]}}}}]}}";

            if (_addedLayerIds.Contains(layId))
            {
                _controller.SetGeoJsonSource(srcId, geoJson);
            }
            else
            {
                _controller.AddGeoJsonSource(srcId, geoJson);
                _controller.AddCircleLayer(layId, srcId,
                    belowLayerId: null, sourceLayer: null,
                    properties: new Dictionary<string, object?>
                    {
                        ["circle-radius"] = 10.0,
                        ["circle-color"] = color,
                        ["circle-stroke-color"] = "#FFFFFF",
                        ["circle-stroke-width"] = 2.0,
                        ["circle-opacity"] = 0.9,
                    });
                _addedLayerIds.Add(layId);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] UpdatePin({layId}) threw: {ex}");
        }
    }

    [RelayCommand]
    private async Task ClearWaypointsAsync()
    {
        // Always stop any active session first so the overlay is cleared and the
        // Geolocation listener is removed before we accept new pins.
        await _session.StopAsync();
        IsNavigating = false;
        CurrentProgress = null;
        HasHighwayWarning = false;
        RouteOptions.Clear();
        OnPropertyChanged(nameof(HasRouteOptions));

        Origin = null;
        Destination = null;
        OriginSnapped = false;
        DestinationSnapped = false;
        IsPlacingOrigin = false;
        IsPlacingDestination = false;
        RouteStatusMessage = "";
    }

    [RelayCommand]
    private async Task StartNavigationAsync()
    {
        if (_origin is null || _destination is null)
        {
            RouteStatusMessage = "Set both A and B pins first";
            return;
        }

        var profile = SelectedProfile;
        bool needsMvt = profile is RouteProfile.HybridOfflineMotorcycle
                                 or RouteProfile.HybridOfflineBicycle
                                 or RouteProfile.OfflineAuto
                                 or RouteProfile.OfflineBicycle
                                 or RouteProfile.OfflinePedestrian;

        if (needsMvt && _mvtTileJsonUrl is null)
        {
            RouteStatusMessage = "Waiting for map tile source…";
            await FetchMvtTileUrlAsync();
            if (_mvtTileJsonUrl is null)
            {
                RouteStatusMessage = "Offline routing unavailable — no vector tile source found";
                return;
            }
        }

        RouteStatusMessage = "Calculating route…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var routeLog = new System.Text.StringBuilder();
            var routeProgress = new Progress<string>(msg =>
            {
                Debug.WriteLine($"[Route] {msg}");
                routeLog.AppendLine(msg);
                RouteStatusMessage = routeLog.ToString().TrimEnd();
            });
            var tracks = await _dataSource.GetRoutableTrackFeaturesAsync();
            var opts = new RouteOptions
            {
                Origin = _origin.Value,
                Destination = _destination.Value,
                Profile = profile,
                TileCacheProvider = _tileCache,
                MvtTileJsonUrl = _mvtTileJsonUrl,
                TrackFeatures = tracks,
                Progress = routeProgress,
                CancellationToken = cts.Token,
            };
            var route = await _session.StartAsync(opts, maxRoutes: _routeCount);
            if (route is null)
            {
                routeLog.AppendLine("No route found");
                RouteStatusMessage = routeLog.ToString().TrimEnd();
                return;
            }

            // Populate route-option cards (shortest is index 0, already selected by session).
            RouteOptions.Clear();
            var alts = _session.RouteAlternatives;
            for (int i = 0; i < alts.Count; i++)
                RouteOptions.Add(new RouteOptionViewModel(i, alts[i], SelectRoute));
            if (RouteOptions.Count > 0)
                RouteOptions[0].IsSelected = true;
            OnPropertyChanged(nameof(HasRouteOptions));

            IsNavigating = true;
            HasHighwayWarning = route.HighwayWarning is { HighwayStepIndices.Count: > 0 };
            RouteStatusMessage = "";
        }
        catch (OperationCanceledException)
        {
            RouteStatusMessage = "Route timed out — try a shorter distance or different profile";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] StartNavigation threw: {ex}");
            RouteStatusMessage = $"Route error: {ex.Message}";
        }
    }

    /// <summary>Fetches the style JSON once and extracts the tile URL for the
    /// "openmaptiles" source, which the HybridOffline and Offline profiles use
    /// for road-graph routing without a Valhalla server.</summary>
    private async Task FetchMvtTileUrlAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            var styleJson = await http.GetStringAsync(_styleUrl);
            using var doc = JsonDocument.Parse(styleJson);
            if (!doc.RootElement.TryGetProperty("sources", out var sources)) return;
            if (!sources.TryGetProperty("openmaptiles", out var src)) return;

            // Prefer the TileJSON "url" field; fall back to the first entry in "tiles".
            if (src.TryGetProperty("url", out var urlEl))
            {
                _mvtTileJsonUrl = urlEl.GetString();
            }
            else if (src.TryGetProperty("tiles", out var tilesEl) &&
                     tilesEl.ValueKind == JsonValueKind.Array &&
                     tilesEl.GetArrayLength() > 0)
            {
                _mvtTileJsonUrl = tilesEl[0].GetString();
            }

            if (_mvtTileJsonUrl is not null)
                Debug.WriteLine($"[MapViewModel] MVT tile URL: {_mvtTileJsonUrl}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] FetchMvtTileUrl threw: {ex}");
        }
    }

    /// <summary>Called by RouteOptionViewModel.SelectCommand when the user taps an
    /// alternative route card. Updates selection highlight and switches the overlay.</summary>
    public void SelectRoute(int index)
    {
        _session.SelectAlternative(index);
        for (int i = 0; i < RouteOptions.Count; i++)
            RouteOptions[i].IsSelected = (i == index);
        HasHighwayWarning = _session.ActiveRoute?.HighwayWarning is { HighwayStepIndices.Count: > 0 };
    }

    [RelayCommand]
    private async Task StopNavigationAsync()
    {
        await _session.StopAsync();
        IsNavigating = false;
        CurrentProgress = null;
        HasHighwayWarning = false;
        RouteOptions.Clear();
        OnPropertyChanged(nameof(HasRouteOptions));
        RouteStatusMessage = "";
    }

    private void OnProgressUpdated(object? sender, RouteProgress progress)
    {
        CurrentProgress = progress;
    }

    private void OnAnnouncementNeeded(object? sender, ManeuverAnnouncementEventArgs e)
    {
        // TTS phase 2 — log for now
        Debug.WriteLine($"[Navigation] {e.Step.Instruction} in {e.DistanceMeters:F0} m");
    }

    private static long? ParseFirstFid(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            if (!doc.RootElement.TryGetProperty("features", out var features) ||
                features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
                return null;
            if (features[0].TryGetProperty("properties", out var props) &&
                props.TryGetProperty("_fid", out var fid) &&
                fid.TryGetInt64(out var value))
                return value;
        }
        catch (JsonException) { }
        return null;
    }

    private static string ExtractCategory(string propertiesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(propertiesJson);
            return doc.RootElement.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    private static double HaversineM(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
