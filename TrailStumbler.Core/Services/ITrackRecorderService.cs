using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Services;

public interface ITrackRecorderService
{
    bool IsRecording { get; }
    int PointCount { get; }

    // Fires on the main thread, throttled ≥2 s, with the current accepted-point list.
    event EventHandler<IReadOnlyList<RecordingPoint>>? TrackUpdated;

    Task StartAsync(string sessionName);

    /// <summary>Stops recording. Saves as a layer if ≥2 points; returns null otherwise.</summary>
    Task<MapLayer?> StopAndSaveAsync();

    Task DiscardAsync();

    /// <summary>
    /// The app has one GPS listener, which navigation uses too: while this says it's still needed, recording's end
    /// leaves it running.
    /// </summary>
    Func<bool>? GpsStillNeeded { get; set; }

    /// <summary>While recording, starts the GPS listener again if something else (navigation) stopped it.</summary>
    Task ResumeGpsAsync();
}
