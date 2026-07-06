using CommunityToolkit.Mvvm.ComponentModel;
using TrailStumbler.Services;

namespace TrailStumbler.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public List<string> StyleNames { get; } =
        MapStyles.Presets.Select(p => p.Name).Append(MapStyles.CustomName).ToList();

    [ObservableProperty] private string _selectedStyleName;
    [ObservableProperty] private string _customStyleUrl = "";
    [ObservableProperty] private bool _isCustom;

    public SettingsViewModel()
    {
        var current = MapStyles.StyleUrl;
        var preset = MapStyles.Presets.FirstOrDefault(p => p.Url == current);
        if (preset.Name is not null)
        {
            _selectedStyleName = preset.Name;
        }
        else
        {
            _selectedStyleName = MapStyles.CustomName;
            _customStyleUrl = current;
            _isCustom = true;
        }
    }

    partial void OnSelectedStyleNameChanged(string value)
    {
        IsCustom = value == MapStyles.CustomName;
        if (!IsCustom)
        {
            var preset = MapStyles.Presets.First(p => p.Name == value);
            MapStyles.StyleUrl = preset.Url;
        }
        else if (!string.IsNullOrWhiteSpace(CustomStyleUrl))
        {
            MapStyles.StyleUrl = CustomStyleUrl;
        }
    }

    partial void OnCustomStyleUrlChanged(string value)
    {
        if (IsCustom && !string.IsNullOrWhiteSpace(value))
            MapStyles.StyleUrl = value;
    }
}
