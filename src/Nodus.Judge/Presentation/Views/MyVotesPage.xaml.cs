using Nodus.Judge.Presentation.ViewModels;

namespace Nodus.Judge.Presentation.Views;

public partial class MyVotesPage : ContentPage
{
    public MyVotesPage(MyVotesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MyVotesViewModel vm)
            vm.AppearingCommand.Execute(null);
    }
}
