using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaplibreNative.Routing.Core.Models;

namespace TrailStumbler.ViewModels;

/// <summary>Represents one computed route alternative in the route-plan pull-up sheet.</summary>
public partial class RouteOptionViewModel : ObservableObject
{
    public int Index { get; }
    public DirectionsRoute Route { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;

    public IRelayCommand SelectCommand { get; }

    public RouteOptionViewModel(int index, DirectionsRoute route, Action<int> onSelect)
    {
        Index = index;
        Route = route;
        Label = $"Route {index + 1}  ·  {route.Distance / 1000.0:F1} km  ·  {FormatDuration(route.Duration)}";
        SelectCommand = new RelayCommand(() => onSelect(index));
    }

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
            : $"{ts.Minutes}m";
    }
}
