using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

[QueryProperty(nameof(ViewEventId), "viewEventId")]
public partial class ResultsPage : ContentPage
{
    private readonly ResultsViewModel _vm;

    public ResultsPage(ResultsViewModel vm)
    {
        InitializeComponent();
        _vm            = vm;
        BindingContext = vm;
    }

    /// <summary>
    /// Populated by Shell navigation via ?viewEventId=N query param.
    /// Routes results display to the specified event instead of the active event.
    /// </summary>
    public int ViewEventId
    {
        set => _vm.SetViewEventId(value);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.AppearingCommand.ExecuteAsync(null);
    }
}
