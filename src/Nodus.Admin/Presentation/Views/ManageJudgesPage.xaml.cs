using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class ManageJudgesPage : ContentPage
{
    public ManageJudgesPage(ManageJudgesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ManageJudgesViewModel vm)
            vm.AppearingCommand.Execute(null);
    }
}
