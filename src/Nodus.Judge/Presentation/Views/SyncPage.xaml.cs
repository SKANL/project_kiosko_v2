using Nodus.Judge.Presentation.ViewModels;

namespace Nodus.Judge.Presentation.Views;

public partial class SyncPage : ContentPage
{
    private readonly SyncViewModel _vm;

    public SyncPage(SyncViewModel vm)
    {
        InitializeComponent();
        _vm            = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.AppearingCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.DisappearingCommand.Execute(null);
    }
}
