using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class ManageProjectsPage : ContentPage
{
    public ManageProjectsPage(ManageProjectsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ManageProjectsViewModel vm)
            vm.AppearingCommand.Execute(null);
    }
}
