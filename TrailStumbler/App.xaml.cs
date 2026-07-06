using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TrailStumbler.Core.Services;
using TrailStumbler.Services;
using TrailStumbler.ViewModels;

namespace TrailStumbler;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_services.GetRequiredService<AppShell>());

    protected override async void OnStart()
    {
        base.OnStart();
        await CheckForCrashedRecordingAsync();
    }

    /// <summary>If the app crashed mid-recording there will be an unclosed RecordingSession.
    /// Prompt the user to save it as a layer or discard it.</summary>
    private async Task CheckForCrashedRecordingAsync()
    {
        try
        {
            var repo = _services.GetRequiredService<ILayerRepository>();
            var session = await repo.GetOpenRecordingSessionAsync();
            if (session is null) return;

            // Give the UI a moment to settle before showing the dialog.
            await Task.Delay(800);

            var page = Windows.FirstOrDefault()?.Page;
            if (page is null) return;

            bool save = await page.DisplayAlertAsync(
                "Unsaved recording",
                $"The app closed while recording '{session.Name}'. Save the track?",
                "Save", "Discard");

            var recorder = _services.GetRequiredService<ITrackRecorderService>();
            var mapVm = _services.GetRequiredService<MapViewModel>();
            var layersVm = _services.GetRequiredService<LayersViewModel>();

            if (save)
            {
                var layer = await repo.SaveRecordingAsLayerAsync(session.Id);
                if (layer is not null)
                {
                    mapVm.NotifyRecordingSaved(layer);
                    await mapVm.OnLayersImportedAsync(new[] { layer });
                }
            }
            else
            {
                await repo.DeleteRecordingSessionAsync(session.Id);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] CheckForCrashedRecording threw: {ex}");
        }
    }
}
