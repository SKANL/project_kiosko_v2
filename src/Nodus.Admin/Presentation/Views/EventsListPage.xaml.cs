using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class EventsListPage : ContentPage
{
    private readonly EventsListViewModel _vm;

    public EventsListPage(EventsListViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.AppearingCommand.ExecuteAsync(null);
    }
}
