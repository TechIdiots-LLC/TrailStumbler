using System.Diagnostics;
using CommunityToolkit.Maui;
using MapLibreNative.Maui.Handlers;
using MaplibreNative.Routing;
using Microsoft.Extensions.DependencyInjection;
using TrailStumbler.Core.Services;
using TrailStumbler.Services;
using TrailStumbler.ViewModels;
using TrailStumbler.Views;

namespace TrailStumbler;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Debug.WriteLine("[MauiProgram] CreateMauiApp ENTER");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureMauiHandlers(handlers =>
            {
                Debug.WriteLine("[MauiProgram] Registering MapLibreMap -> MapLibreMapHandler");
                handlers.AddHandler(typeof(MapLibreMap), typeof(MapLibreMapHandler));
            });

        var services = builder.Services;

        // ── Services ─────────────────────────────────────────────────────────
        // SqliteLayerRepository implements both ILayerRepository and IRouteDataSource;
        // register the concrete type so both interfaces share the same singleton.
        services.AddSingleton<SqliteLayerRepository>();
        services.AddSingleton<ILayerRepository>(sp => sp.GetRequiredService<SqliteLayerRepository>());
        services.AddSingleton<IRouteDataSource>(sp => sp.GetRequiredService<SqliteLayerRepository>());

        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<ITrackRecorderService, TrackRecorderService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<SqliteTileCacheProvider>();

        // ── Routing plugin (RouteOverlay singleton + NavigationSession transient) ──
        builder.UseMapLibreRouting(); // IRouteDataSource already registered above

        // ── ViewModels ───────────────────────────────────────────────────────
        // MapViewModel is a singleton so LayersViewModel can drive the live map
        // (toggle/zoom) even while MapPage isn't instantiated.
        services.AddSingleton<MapViewModel>();
        services.AddSingleton<LayersViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ── Shell + Pages ────────────────────────────────────────────────────
        services.AddTransient<AppShell>();
        services.AddTransient<LayersPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<SettingsPage>();

        Debug.WriteLine("[MauiProgram] CreateMauiApp EXIT (build)");
        return builder.Build();
    }
}
