using System.ComponentModel;
using System.Diagnostics;
using MapLibreNative.Maui.Handlers;
using TrailStumbler.Services;
using TrailStumbler.ViewModels;

namespace TrailStumbler.Views;

/// <summary>
/// Map lifecycle skeleton cloned from VistumblerMAUI's MapPage: MapReady re-applies
/// the style URL (Windows timing workaround), StyleLoaded hands the controller to the
/// view model, and a watchdog logs a diagnosis if the map never comes up.
/// </summary>
public partial class MapPage : ContentPage
{
    private const string LogTag = "[MapPage]";
    private const double SheetMaxShare = 0.6;   // the open sheet's content takes at most this much of the page

    private readonly MapViewModel _vm;
    private bool _mapReadyFired;
    private bool _styleLoadedFired;
    private bool _firstAppearLogged;
    private bool _sheetExpanded;
    private bool _reopenAfterPin;   // the sheet was tucked away to place a pin; open it again once it's placed
    private long _lastSheetToggle;  // one tap can arrive twice (on Windows a click was seen to); the second is ignored

    public MapPage(MapViewModel vm)
    {
        Log("ctor start");
        InitializeComponent();
        BindingContext = _vm = vm;

        Map.HandlerChanged += (_, _) =>
            Log($"Map.HandlerChanged handler={Map.Handler?.GetType().Name ?? "null"}");
        Map.MapReady += OnMapReady;
        Map.StyleLoaded += OnStyleLoaded;

        Map.MapClick += async (_, e) =>
        {
            try
            {
                if (_vm.IsPlacingPin)
                    await _vm.PlacePinAsync(e.LatLng.Latitude, e.LatLng.Longitude);
                else
                    await _vm.OnMapTappedAsync(e.ScreenX, e.ScreenY);
            }
            catch (Exception ex) { Log($"OnMapClick threw: {ex}"); }
        };

        // Long-press shortcut: activate the right placement mode and immediately drop the pin.
        Map.MapLongClick += async (_, e) =>
        {
            try
            {
                if (_vm.Origin is null)
                    _vm.ActivateOriginPlacementCommand.Execute(null);
                else
                    _vm.ActivateDestinationPlacementCommand.Execute(null);

                await _vm.PlacePinAsync(e.LatLng.Latitude, e.LatLng.Longitude);

                // Open the sheet, so the pins set are shown.
                if (!_sheetExpanded)
                    await ExpandSheetAsync();
            }
            catch (Exception ex) { Log($"OnMapLongClick threw: {ex}"); }
        };

        _vm.PropertyChanged += OnVmPropertyChanged;
        SizeChanged += (_, _) => SheetScroll.MaximumHeightRequest = Math.Max(160, Height * SheetMaxShare);
        Map.SizeChanged += (_, _) => UpdateCameraViewport();
        NavPanel.SizeChanged += (_, _) => UpdateCameraViewport();
        RouteSheet.SizeChanged += (_, _) => UpdateCameraViewport();
    }

    /// <summary>The navigation camera frames the road ahead in the map left between the instruction panel and the sheet.</summary>
    private void UpdateCameraViewport()
    {
        double top = NavPanel.IsVisible ? NavPanel.Height : 0;
        double bottom = _sheetExpanded ? RouteSheet.Height : SheetHeader.Height;
        _vm.SetMapViewport(Map.Width, Map.Height, Math.Max(0, top), Math.Max(0, bottom));
    }

    private void OnMapReady(object? sender, EventArgs e)
    {
        _mapReadyFired = true;
        var ctrl = (Map.Handler as MapLibreMapHandler)?.Controller;
        Log($"Map.MapReady controller={(ctrl is null ? "null" : "set")} reapplying StyleUrl='{_vm.StyleUrl}'");

        if (ctrl is not null)
            ctrl.OnDidFailLoadingMapReceived += msg => Log($"!! OnDidFailLoadingMap: {msg}");

        // Workaround: on Windows the property mapper fires UpdateStyleUrl before the
        // native map exists, so the initial style never loads. Re-apply here.
        try { ctrl?.SetStyleString(_vm.StyleUrl); }
        catch (Exception ex) { Log($"Map.MapReady SetStyleString threw: {ex}"); }
    }

    private void OnStyleLoaded(object? sender, EventArgs e)
    {
        _styleLoadedFired = true;
        var ctrl = (Map.Handler as MapLibreMapHandler)?.Controller;
        Log($"Map.StyleLoaded controller={(ctrl is null ? "null" : "set")}");

        if (ctrl is not null)
            _vm.OnMapControllerReady(ctrl);
        else
            Log("Map.StyleLoaded: controller was null — skipping OnMapControllerReady");

        RefreshTerrainControl();
    }

    /// <summary>
    /// Shows the map's 3D terrain button and points it at the right DEM, as VistumblerMAUI
    /// does. The button toggles terrain against a raster-dem already in the style, so the
    /// style's own DEM is used when it has one (the WifiDB relief styles do); a style
    /// without one gets the app's from <see cref="TerrainSources.Fallbacks"/>.
    ///
    /// Runs on every style load, since the answer changes with the basemap and a style
    /// reload drops a source the app added. The button is hidden until the lookup lands;
    /// the result is cached per URL, so only the first load of each style waits.
    /// </summary>
    private void RefreshTerrainControl()
    {
        var styleUrl = _vm.StyleUrl;
        Map.ShowTerrainControl = false;

        _ = Task.Run(async () =>
        {
            var styleDem = await TerrainSources.FindAsync(styleUrl);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // A newer style load may have overtaken this lookup; leave its answer alone.
                if (_vm.StyleUrl != styleUrl) return;

                try
                {
                    var sourceId = styleDem;

                    if (sourceId is null)
                    {
                        var dem = TerrainSources.DefaultFallback;
                        Map.AddRasterDemSource(
                            TerrainSources.FallbackSourceId,
                            dem.TileJsonUrl,
                            dem.TileUrlTemplates,
                            dem.TileSize,
                            minZoom: 0,
                            maxZoom: dem.MaxZoom,
                            encoding: dem.Encoding,
                            attribution: dem.Attribution);

                        sourceId = TerrainSources.FallbackSourceId;
                        Log($"terrain: '{styleUrl}' declares no raster-dem — added '{dem.Name}'");
                    }
                    else
                    {
                        Log($"terrain: using the style's own DEM '{sourceId}'");
                    }

                    Map.TerrainControlSourceId = sourceId;
                    Map.ShowTerrainControl     = true;
                }
                catch (Exception ex)
                {
                    // A DEM that cannot be added is not worth breaking the map over:
                    // leave the button hidden and carry on with a flat map.
                    Log($"terrain: setup failed, button stays hidden: {ex}");
                    Map.ShowTerrainControl = false;
                }
            });
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log($"OnAppearing mapReady={_mapReadyFired} styleLoaded={_styleLoadedFired}");

        if (!_firstAppearLogged)
        {
            _firstAppearLogged = true;
            _ = StartWatchdogAsync();
        }

        if (_vm.StyleUrl != MapStyles.StyleUrl)
            _vm.StyleUrl = MapStyles.StyleUrl;

        _vm.TryApplyPendingFit();
    }

    private async Task StartWatchdogAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        Log($"WATCHDOG t=5s mapReady={_mapReadyFired} styleLoaded={_styleLoadedFired}");
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!_mapReadyFired)
            Log("WATCHDOG: MapReady never fired — handler/CreatePlatformView likely failed " +
                "(check MauiProgram registered MapLibreMapHandler).");
        else if (!_styleLoadedFired)
            Log($"WATCHDOG: MapReady fired but StyleLoaded did not — style URL load failed " +
                $"(URL='{_vm.StyleUrl}'). Check network/tile server.");
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.CurrentProgress))
        {
            var p = _vm.CurrentProgress;
            if (p is not null) NavPanel.Update(p);
            else NavPanel.Clear();
        }
        else if (e.PropertyName == nameof(MapViewModel.HasHighwayWarning))
        {
            NavPanel.SetHighwayWarning(
                _vm.HasHighwayWarning ? _vm.CurrentProgress?.Route.HighwayWarning : null);
        }
        else if (e.PropertyName == nameof(MapViewModel.IsNavigating))
        {
            // Under way: the sheet makes way for the map (its header shows what's going on)
            if (_vm.IsNavigating) _ = CollapseSheetAsync();
            UpdateCameraViewport();
        }
        else if (e.PropertyName == nameof(MapViewModel.IsPlacingPin) && !_vm.IsPlacingPin && _reopenAfterPin)
        {
            // The pin is placed: back to the planner
            _reopenAfterPin = false;
            _ = ExpandSheetAsync();
        }
    }

    // ── Route sheet: tucked away to its header, or open ──────────────────────────
    // It sizes to its content (capped, scrolling beyond), and slides rather than resizing, so nothing in it is
    // ever squeezed or cut off.

    private void OnSheetHeaderTapped(object? sender, EventArgs e)
    {
        if (!TakeToggle()) return;
        _ = _sheetExpanded ? CollapseSheetAsync() : ExpandSheetAsync();
    }

    private void OnSheetSwipedUp(object? sender, SwipedEventArgs e)
    {
        if (TakeToggle()) _ = ExpandSheetAsync();
    }

    private void OnSheetSwipedDown(object? sender, SwipedEventArgs e)
    {
        if (TakeToggle()) _ = CollapseSheetAsync();
    }

    /// <summary>Whether a tap or swipe may open or close the sheet: not within half a second of the last.</summary>
    private bool TakeToggle()
    {
        long now = Environment.TickCount64;
        if (now - _lastSheetToggle < 500) return false;
        _lastSheetToggle = now;
        return true;
    }

    /// <summary>Set A / Set B: the sheet makes way for the map until the pin is placed.</summary>
    private void OnPlacePinClicked(object? sender, EventArgs e)
    {
        if (!_sheetExpanded) return;
        _reopenAfterPin = true;
        _ = CollapseSheetAsync();
    }

    private async Task ExpandSheetAsync()
    {
        if (_sheetExpanded) return;
        _sheetExpanded = true;
        SheetChevron.Text = "▼";
        SheetScroll.IsVisible = true;
        // From where its header sat, up to its full height
        double rise = Math.Min(SheetScroll.Measure(RouteSheet.Width, double.PositiveInfinity).Height,
            SheetScroll.MaximumHeightRequest);
        RouteSheet.TranslationY = rise;
        await RouteSheet.TranslateToAsync(0, 0, 200, Easing.CubicOut);
        UpdateCameraViewport();
    }

    private async Task CollapseSheetAsync()
    {
        if (!_sheetExpanded) return;
        _sheetExpanded = false;
        SheetChevron.Text = "▲";
        await RouteSheet.TranslateToAsync(0, SheetScroll.Height, 180, Easing.CubicIn);
        if (_sheetExpanded) return;   // opened again meanwhile
        SheetScroll.IsVisible = false;
        RouteSheet.TranslationY = 0;
        UpdateCameraViewport();
    }

    private static void Log(string msg)
        => Debug.WriteLine($"{LogTag} {DateTime.Now:HH:mm:ss.fff} {msg}");
}
