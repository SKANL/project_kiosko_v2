using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class ProjectDetailsPage : ContentPage
{
	public ProjectDetailsPage(ProjectDetailsViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
