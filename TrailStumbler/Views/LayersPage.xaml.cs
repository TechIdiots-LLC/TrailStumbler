using TrailStumbler.ViewModels;

namespace TrailStumbler.Views;

public partial class LayersPage : ContentPage
{
    private readonly LayersViewModel _vm;

    public LayersPage(LayersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.EnsureLoadedAsync();
    }
}
