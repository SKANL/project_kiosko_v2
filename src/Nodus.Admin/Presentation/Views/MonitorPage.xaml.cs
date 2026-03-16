using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class MonitorPage : ContentPage
{
    private readonly MonitorViewModel _vm;

    public MonitorPage(MonitorViewModel vm)
    {
        InitializeComponent();
        _vm            = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.AppearingCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.DisappearingCommand.Execute(null);
    }
}
