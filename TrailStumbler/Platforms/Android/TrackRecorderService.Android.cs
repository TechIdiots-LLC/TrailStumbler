using Android.Content;
using TrailStumbler; // for TrackRecordingForegroundService

namespace TrailStumbler.Services;

/// <summary>Android-specific foreground service start/stop for TrackRecorderService.</summary>
public partial class TrackRecorderService
{
    partial void StartPlatformService()
    {
        var intent = new Intent(Platform.AppContext, typeof(TrackRecordingForegroundService));
        Platform.AppContext.StartForegroundService(intent);
    }

    partial void StopPlatformService()
    {
        var intent = new Intent(Platform.AppContext, typeof(TrackRecordingForegroundService));
        Platform.AppContext.StopService(intent);
    }
}
