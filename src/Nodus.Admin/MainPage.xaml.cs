using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm          = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.AppearingCommand.ExecuteAsync(null);
    }

    private async void OnEventSetupClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//EventSetupPage");

    private async void OnMonitorClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//MonitorPage");

    private async void OnResultsClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//ResultsPage");
}
