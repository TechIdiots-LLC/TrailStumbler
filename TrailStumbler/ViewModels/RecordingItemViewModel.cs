using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailStumbler.Core.Models;

namespace TrailStumbler.ViewModels;

/// <summary>One row in the Recordings subsection of the Layers page.</summary>
public partial class RecordingItemViewModel : ObservableObject
{
    private readonly LayersViewModel _parent;

    public MapLayer Layer { get; }

    public RecordingItemViewModel(MapLayer layer, LayersViewModel parent)
    {
        Layer = layer;
        _parent = parent;
        _isVisible = layer.IsVisible;
    }

    public string Name => Layer.Name;

    public string DistanceLabel
    {
        get
        {
            if (Layer.DistanceMeters is null or 0) return "";
            var km = Layer.DistanceMeters.Value / 1000.0;
            return km >= 1 ? $"{km:F1} km" : $"{Layer.DistanceMeters.Value:F0} m";
        }
    }

    [ObservableProperty] private bool _isVisible;

    partial void OnIsVisibleChanged(bool value) => _ = _parent.OnItemVisibilityChangedAsync(this, value);

    [RelayCommand]
    private Task ZoomAsync() => _parent.ZoomToLayerAsync(this);

    [RelayCommand]
    private async Task ExportGpxAsync()
    {
        try { await _parent.ExportTrackGpxAsync(this); }
        catch (Exception ex) { Debug.WriteLine($"[RecordingItemViewModel] ExportGpx failed: {ex}"); }
    }

    [RelayCommand]
    private Task DeleteAsync() => _parent.DeleteRecordedTrackAsync(this);
}
