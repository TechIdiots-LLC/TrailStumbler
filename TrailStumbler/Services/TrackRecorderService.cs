using System.Diagnostics;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Services;

namespace TrailStumbler.Services;

/// <summary>
/// GPS track recorder. Uses MAUI's Geolocation foreground listener (same pattern as
/// VistumblerMAUI's MauiGeolocationGpsService). On Android, a dedicated foreground service
/// keeps the process alive and shows a persistent notification while recording.
/// </summary>
public partial class TrackRecorderService : ITrackRecorderService
{
    private readonly ILayerRepository _repo;

    private int _sessionId = -1;
    private RecordingPoint? _lastPoint;
    private DateTime _lastPointTime = DateTime.MinValue;
    private DateTime _lastUpdateFired = DateTime.MinValue;
    private readonly List<RecordingPoint> _currentPoints = new();

    private const double AccuracyLimitM = 50;
    private const double MinDistanceM = 5;
    private const double MinIntervalS = 10;
    private const double ThrottleS = 2;

    public bool IsRecording { get; private set; }
    public int PointCount => _currentPoints.Count;

    public event EventHandler<IReadOnlyList<RecordingPoint>>? TrackUpdated;

    public TrackRecorderService(ILayerRepository repo) => _repo = repo;

    public async Task StartAsync(string sessionName)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            throw new PermissionException("Location permission denied");

        _currentPoints.Clear();
        _lastPoint = null;
        _lastPointTime = DateTime.MinValue;
        _lastUpdateFired = DateTime.MinValue;

        _sessionId = await _repo.StartRecordingSessionAsync(sessionName);
        IsRecording = true;

        StartPlatformService();

        Geolocation.LocationChanged += OnLocationChanged;
        if (!Geolocation.IsListeningForeground)
        {
            var req = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
            bool started = await Geolocation.StartListeningForegroundAsync(req);
            if (!started)
                Debug.WriteLine("[TrackRecorderService] StartListeningForegroundAsync returned false");
        }
    }

    public async Task<MapLayer?> StopAndSaveAsync()
    {
        StopListening();
        if (_sessionId < 0) return null;
        var layer = await _repo.SaveRecordingAsLayerAsync(_sessionId);
        _sessionId = -1;
        return layer;
    }

    public async Task DiscardAsync()
    {
        StopListening();
        if (_sessionId >= 0)
        {
            await _repo.DeleteRecordingSessionAsync(_sessionId);
            _sessionId = -1;
        }
    }

    private void StopListening()
    {
        Geolocation.LocationChanged -= OnLocationChanged;
        if (Geolocation.IsListeningForeground)
            Geolocation.StopListeningForeground();
        IsRecording = false;
        StopPlatformService();
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
    {
        var loc = e.Location;

        // Filter: skip fixes with poor accuracy.
        if (loc.Accuracy.HasValue && loc.Accuracy.Value > AccuracyLimitM) return;

        var now = DateTime.UtcNow;
        if (_lastPoint is not null)
        {
            var distM = Haversine(_lastPoint.Lat, _lastPoint.Lon, loc.Latitude, loc.Longitude);
            var elapsed = (now - _lastPointTime).TotalSeconds;
            if (distM < MinDistanceM && elapsed < MinIntervalS) return;
        }

        var point = new RecordingPoint
        {
            SessionId = _sessionId,
            Lat = loc.Latitude,
            Lon = loc.Longitude,
            Ele = loc.Altitude,
            TimestampTicks = loc.Timestamp.UtcTicks,
            AccuracyMeters = loc.Accuracy,
            SpeedMps = loc.Speed,
            Course = loc.Course,
        };

        _ = _repo.AddRecordingPointAsync(point);
        _currentPoints.Add(point);
        _lastPoint = point;
        _lastPointTime = now;

        if ((now - _lastUpdateFired).TotalSeconds >= ThrottleS)
        {
            _lastUpdateFired = now;
            var snapshot = _currentPoints.ToList();
            MainThread.BeginInvokeOnMainThread(() => TrackUpdated?.Invoke(this, snapshot));
        }
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // Platform-specific service start/stop are in partial methods below.
    partial void StartPlatformService();
    partial void StopPlatformService();
}
