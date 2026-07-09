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
    private const double SheetCollapsedH    = 56;
    private const double SheetBaseExpandedH = 260;   // base with stepper row
    private const double SheetRouteCardH    = 52;    // height per route option card

    private readonly MapViewModel _vm;
    private bool _mapReadyFired;
    private bool _styleLoadedFired;
    private bool _firstAppearLogged;
    private bool _sheetExpanded;

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

                // Auto-expand the sheet so pins are visible.
                if (!_sheetExpanded)
                    ExpandSheet();
            }
            catch (Exception ex) { Log($"OnMapLongClick threw: {ex}"); }
        };

        _vm.PropertyChanged += OnVmPropertyChanged;
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
        else if (e.PropertyName == nameof(MapViewModel.HasRouteOptions) && _sheetExpanded)
        {
            AnimateSheet(ComputeExpandedH());
        }
    }

    private double ComputeExpandedH()
        => SheetBaseExpandedH + _vm.RouteOptions.Count * SheetRouteCardH;

    // ── Sheet expand / collapse ───────────────────────────────────────────────

    private void OnSheetHandleClicked(object? sender, EventArgs e)
    {
        if (_sheetExpanded) CollapseSheet(); else ExpandSheet();
    }

    private void ExpandSheet()
    {
        _sheetExpanded = true;
        SheetHandle.Text = "⌄";
        SheetContent.IsVisible = true;
        AnimateSheet(ComputeExpandedH());
    }

    private void CollapseSheet()
    {
        _sheetExpanded = false;
        SheetHandle.Text = "⌃";
        SheetContent.IsVisible = false;
        AnimateSheet(SheetCollapsedH);
    }

    private void AnimateSheet(double targetH)
    {
        var from = RouteSheet.HeightRequest;
        new Animation(v => RouteSheet.HeightRequest = v, from, targetH)
            .Commit(this, "SheetResize", length: 200, easing: Easing.CubicOut);
    }

    private static void Log(string msg)
        => Debug.WriteLine($"{LogTag} {DateTime.Now:HH:mm:ss.fff} {msg}");
}
