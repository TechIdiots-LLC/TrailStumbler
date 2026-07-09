using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrailStumbler.Core.Models;

namespace TrailStumbler.ViewModels;

/// <summary>One row in the layer list. Visibility changes persist immediately and
/// drive the live map through the parent's callbacks.</summary>
public partial class LayerItemViewModel : ObservableObject
{
    private readonly LayersViewModel _parent;

    public MapLayer Layer { get; }

    public LayerItemViewModel(MapLayer layer, LayersViewModel parent)
    {
        Layer = layer;
        _parent = parent;
        _isVisible = layer.IsVisible;
        _isUsedForRouting = layer.IsUsedForRouting;
    }

    public string Name => Layer.Name;
    public string ColorHex => Layer.ColorHex;
    public void NotifyColorChanged() => OnPropertyChanged(nameof(ColorHex));
    public string Subtitle
    {
        get
        {
            var kind = Layer.Family switch
            {
                GeometryFamily.Line => "trails",
                GeometryFamily.Polygon => "areas",
                _ => "points",
            };
            return $"{Layer.FeatureCount} {kind} • {Layer.SourceFileName}";
        }
    }

    [ObservableProperty] private bool _isVisible;

    // Two-way CheckBox binding lands here; persist + update the live map.
    partial void OnIsVisibleChanged(bool value) => _ = _parent.OnItemVisibilityChangedAsync(this, value);

    [ObservableProperty] private bool _isUsedForRouting;

    partial void OnIsUsedForRoutingChanged(bool value) => _ = _parent.OnItemRoutingChangedAsync(this, value);

    // Only Line layers can be used for routing (points/areas have no traversal topology).
    public bool IsLineLayer => Layer.Family == GeometryFamily.Line;

    [RelayCommand]
    private Task ZoomAsync() => _parent.ZoomToLayerAsync(this);

    [RelayCommand]
    private Task ChangeColorAsync() => _parent.ChangeLayerColorAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _parent.DeleteLayerAsync(this);

    [RelayCommand]
    private Task ExportAsync() => _parent.ExportLayerAsync(this);
}
