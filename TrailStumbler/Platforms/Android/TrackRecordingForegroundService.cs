using Android.App;
using Android.Content;
using Android.OS;

namespace TrailStumbler;

/// <summary>
/// Foreground service that shows the persistent "Recording track…" notification on Android.
/// The service itself doesn't handle GPS — that's done by MAUI's Geolocation listener.
/// Started/stopped by TrackRecorderService.Android.cs.
/// </summary>
[Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeLocation, Exported = false)]
public class TrackRecordingForegroundService : Service
{
    private const int NotificationId = 1001;
    internal const string ChannelId = "trail_recording";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureNotificationChannel();

        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("TrailStumbler")
            .SetContentText("Recording track…")
            .SetSmallIcon(Android.Resource.Drawable.IcMenuMapmode)
            .SetOngoing(true)
            .Build()!;

        StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeLocation);
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void EnsureNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channel = new NotificationChannel(ChannelId, "Track Recording", NotificationImportance.Low)
        {
            Description = "Shown while a GPS track is being recorded",
        };
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
