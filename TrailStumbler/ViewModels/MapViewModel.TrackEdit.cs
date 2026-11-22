using System.Diagnostics;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailStumbler.Core.Models;
using TrailStumbler.Core.Recording;

namespace TrailStumbler.ViewModels;

/// <summary>
/// Editing a recorded track: its points drawn on the map, a stretch selected by tapping its first and last point,
/// and left out of the track (a road driven by mistake) or put back. Points left out are kept, but the track is drawn,
/// measured and exported without them, a gap where they were (<see cref="RecordedTrack"/>).
/// </summary>
public partial class MapViewModel
{
    private const string EditLineSourceId = "track-edit-lines-src";
    private const string EditPointSourceId = "track-edit-points-src";
    private const string EditLineLayerId = "track-edit-lines";
    private const string EditPointLayerId = "track-edit-points";
    private const double EditTapPixels = 24;

    private MapLayer? _editingTrack;
    private List<RecordingPoint> _editPoints = [];
    private int? _selectionStart, _selectionEnd;
    private bool _selectionDone;   // both ends tapped: the next tap starts a new selection

    [ObservableProperty] private bool _isEditingTrack;
    [ObservableProperty] private string _trackEditTitle = "";
    [ObservableProperty] private string _trackEditStatus = "";
    [ObservableProperty] private bool _hasTrackSelection;

    /// <summary>A recorded track's points were left out or put back: its distance and bounds have changed.</summary>
    public event EventHandler<MapLayer>? TrackEdited;

    /// <summary>Opens a recorded track for editing on the map.</summary>
    public async Task BeginTrackEditAsync(MapLayer layer)
    {
        if (IsEditingTrack) await FinishTrackEditAsync();
        _editingTrack = layer;
        _editPoints = await _repo.GetRecordingPointsByLayerAsync(layer.Id);
        ClearSelection();
        TrackEditTitle = $"Editing {layer.Name}";
        IsEditingTrack = true;
        RemoveLayerFromMap(layer);   // the edit drawing stands in for it
        DrawTrackEdit();
        UpdateTrackEditStatus();
        RequestFitBounds(layer);
    }

    /// <summary>A tap on the map while editing: the nearest point within reach ends or starts the selection.</summary>
    public void TapTrackPoint(double lat, double lon, double screenX, double screenY)
    {
        if (_controller is null || _editPoints.Count == 0) return;
        int nearest = 0;
        double nearestM = double.MaxValue;
        for (int i = 0; i < _editPoints.Count; i++)
        {
            double m = HaversineM(lat, lon, _editPoints[i].Lat, _editPoints[i].Lon);
            if (m < nearestM) { nearestM = m; nearest = i; }
        }
        try
        {
            var (x, y) = _controller.LatLngToScreenPoint(_editPoints[nearest].Lat, _editPoints[nearest].Lon);
            if (Math.Sqrt((x - screenX) * (x - screenX) + (y - screenY) * (y - screenY)) > EditTapPixels) return;
        }
        catch (Exception ex) { Debug.WriteLine($"[MapViewModel] LatLngToScreenPoint threw: {ex.Message}"); }

        if (_selectionStart is null || _selectionDone)
        {
            _selectionStart = _selectionEnd = nearest;
            _selectionDone = false;
        }
        else
        {
            _selectionEnd = nearest;
            _selectionDone = true;
        }
        HasTrackSelection = true;
        DrawTrackEdit();
        UpdateTrackEditStatus();
    }

    [RelayCommand]
    private Task ExcludeSelectionAsync() => SetSelectionExcludedAsync(true);

    [RelayCommand]
    private Task IncludeSelectionAsync() => SetSelectionExcludedAsync(false);

    private async Task SetSelectionExcludedAsync(bool excluded)
    {
        if (_editingTrack is null || _selectionStart is not int a || _selectionEnd is not int b) return;
        var (lo, hi) = a <= b ? (a, b) : (b, a);
        var ids = _editPoints.Skip(lo).Take(hi - lo + 1).Select(p => p.Id).ToList();
        try
        {
            if (!await _repo.SetRecordingPointsExcludedAsync(_editingTrack, ids, excluded))
            {
                TrackEditStatus = "A track has to keep at least two points in a row";
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] SetRecordingPointsExcluded threw: {ex}");
            TrackEditStatus = $"Not saved: {ex.Message}";
            return;
        }
        for (int i = lo; i <= hi; i++) _editPoints[i].Excluded = excluded;
        ClearSelection();
        DrawTrackEdit();
        UpdateTrackEditStatus();
        TrackEdited?.Invoke(this, _editingTrack);
    }

    [RelayCommand]
    private void ClearTrackSelection()
    {
        ClearSelection();
        DrawTrackEdit();
        UpdateTrackEditStatus();
    }

    /// <summary>Done: the edit drawing goes and the track is shown as saved.</summary>
    [RelayCommand]
    private async Task FinishTrackEditAsync()
    {
        RemoveTrackEditFromMap();
        var layer = _editingTrack;
        _editingTrack = null;
        _editPoints = [];
        ClearSelection();
        IsEditingTrack = false;
        if (layer is { IsVisible: true }) await AddLayerToMapAsync(layer);
    }

    private void ClearSelection()
    {
        _selectionStart = _selectionEnd = null;
        _selectionDone = false;
        HasTrackSelection = false;
    }

    private void UpdateTrackEditStatus()
    {
        int kept = _editPoints.Count(p => !p.Excluded);
        double meters = RecordedTrack.Build(_editPoints)?.DistanceMeters ?? 0;
        string distance = meters >= 1000 ? $"{meters / 1000:F1} km" : $"{meters:F0} m";
        string selection = _selectionStart is int a && _selectionEnd is int b
            ? (_selectionDone
                ? $"Points {Math.Min(a, b) + 1:N0} to {Math.Max(a, b) + 1:N0} selected ({Math.Abs(b - a) + 1:N0})"
                : $"Point {a + 1:N0} selected: tap the stretch's other end")
            : "Tap a point, then another: the stretch between is selected";
        TrackEditStatus = $"{selection}\n{kept:N0} of {_editPoints.Count:N0} points kept · {distance}";
    }

    // ── The edit drawing: stretches kept (yellow) and left out (grey), and the points, the selection blue ──

    private void DrawTrackEdit()
    {
        if (_controller is null || !IsEditingTrack) return;
        try
        {
            var (lines, points) = BuildTrackEditGeoJson();
            if (_addedLayerIds.Contains(EditPointLayerId))
            {
                _controller.SetGeoJsonSource(EditLineSourceId, lines);
                _controller.SetGeoJsonSource(EditPointSourceId, points);
                return;
            }
            _controller.AddGeoJsonSource(EditLineSourceId, lines);
            _controller.AddGeoJsonSource(EditPointSourceId, points);
            _controller.AddLineLayer(EditLineLayerId, EditLineSourceId, belowLayerId: null, sourceLayer: null,
                properties: new Dictionary<string, object?>
                {
                    ["line-color"] = new object[] { "case", new object[] { "get", "excluded" }, "#9E9E9E", "#FFFF66" },
                    ["line-width"] = 4.0,
                    ["line-opacity"] = 0.9,
                });
            _controller.AddCircleLayer(EditPointLayerId, EditPointSourceId, belowLayerId: null, sourceLayer: null,
                properties: new Dictionary<string, object?>
                {
                    ["circle-radius"] = 5.0,
                    ["circle-color"] = new object[] { "match", new object[] { "get", "state" }, 2, "#1E88E5", 1, "#9E9E9E", "#FFFF66" },
                    ["circle-stroke-color"] = "#333333",
                    ["circle-stroke-width"] = 1.0,
                });
            _addedLayerIds.Add(EditLineLayerId);
            _addedLayerIds.Add(EditPointLayerId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapViewModel] DrawTrackEdit threw: {ex}");
        }
    }

    private void RemoveTrackEditFromMap()
    {
        if (_controller is null) return;
        foreach (var (layer, source) in new[] { (EditPointLayerId, EditPointSourceId), (EditLineLayerId, EditLineSourceId) })
        {
            if (!_addedLayerIds.Remove(layer)) continue;
            try { _controller.RemoveLayer(layer); } catch { }
            try { _controller.RemoveSource(source); } catch { }
        }
    }

    private (string Lines, string Points) BuildTrackEditGeoJson()
    {
        var inv = CultureInfo.InvariantCulture;
        string Pos(RecordingPoint p) => $"[{p.Lon.ToString("F6", inv)},{p.Lat.ToString("F6", inv)}]";
        var (lo, hi) = _selectionStart is int a && _selectionEnd is int b ? (Math.Min(a, b), Math.Max(a, b)) : (-1, -1);

        // A line a run of points alike, sharing its first point with the run before so they meet
        var lines = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        bool firstLine = true;
        for (int start = 0; start < _editPoints.Count - 1;)
        {
            bool excluded = _editPoints[start].Excluded || _editPoints[start + 1].Excluded;
            int end = start + 1;
            while (end + 1 < _editPoints.Count && (_editPoints[end].Excluded || _editPoints[end + 1].Excluded) == excluded) end++;
            if (!firstLine) lines.Append(',');
            firstLine = false;
            lines.Append("{\"type\":\"Feature\",\"properties\":{\"excluded\":").Append(excluded ? "true" : "false")
                 .Append("},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[")
                 .Append(string.Join(",", _editPoints.Skip(start).Take(end - start + 1).Select(Pos))).Append("]}}");
            start = end;
        }
        lines.Append("]}");

        var points = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
        for (int i = 0; i < _editPoints.Count; i++)
        {
            if (i > 0) points.Append(',');
            int state = i >= lo && i <= hi ? 2 : _editPoints[i].Excluded ? 1 : 0;
            points.Append("{\"type\":\"Feature\",\"properties\":{\"state\":").Append(state)
                  .Append("},\"geometry\":{\"type\":\"Point\",\"coordinates\":").Append(Pos(_editPoints[i])).Append("}}");
        }
        points.Append("]}");
        return (lines.ToString(), points.ToString());
    }
}
