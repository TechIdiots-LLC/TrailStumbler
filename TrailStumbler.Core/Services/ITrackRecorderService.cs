using TrailStumbler.Core.Models;

namespace TrailStumbler.Core.Services;

public interface ITrackRecorderService
{
    bool IsRecording { get; }
    int PointCount { get; }

    // Fires on the main thread, throttled â‰¥2 s, with the current accepted-point list.
    event EventHandler<IReadOnlyList<RecordingPoint>>? TrackUpdated;

    Task StartAsync(string sessionName);

    /// <summary>Stops recording. Saves as a layer if â‰¥2 points; returns null otherwise.</summary>
    Task<MapLayer?> StopAndSaveAsync();

    Task DiscardAsync();
}
