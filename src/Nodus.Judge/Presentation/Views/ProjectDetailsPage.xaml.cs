using Nodus.Judge.Presentation.ViewModels;

namespace Nodus.Judge.Presentation.Views;

public partial class ProjectDetailsPage : ContentPage
{
	public ProjectDetailsPage(ProjectDetailsViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}
