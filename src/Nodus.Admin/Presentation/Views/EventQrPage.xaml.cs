using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class EventQrPage : ContentPage
{
	private readonly EventQrViewModel _vm;

	public EventQrPage(EventQrViewModel vm)
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
