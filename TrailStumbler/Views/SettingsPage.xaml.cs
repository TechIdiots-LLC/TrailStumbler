using TrailStumbler.ViewModels;

namespace TrailStumbler.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    // A category's name ticks its box too
    private void OnCategoryLabelTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is PointCategoryOption option)
            option.IsShown = !option.IsShown;
    }
}
