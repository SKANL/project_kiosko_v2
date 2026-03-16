using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Presentation.ViewModels;

namespace Nodus.Judge.Presentation.Views;

[QueryProperty(nameof(ViewModel), "ViewModel")]
public partial class ProjectSelectionPage : ContentPage
{
    private readonly IProjectScanBuffer _scanBuffer;

    public VotingViewModel ViewModel
    {
        get => BindingContext as VotingViewModel;
        set => BindingContext = value;
    }

    public ProjectSelectionPage(IProjectScanBuffer scanBuffer)
    {
        InitializeComponent();
        _scanBuffer = scanBuffer;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is VotingViewModel viewModel && !string.IsNullOrWhiteSpace(_scanBuffer.PendingQr))
        {
            var qr = _scanBuffer.PendingQr;
            _scanBuffer.PendingQr = null;

            if (!string.IsNullOrWhiteSpace(qr))
            {
                await viewModel.HandleScannedProjectCommand.ExecuteAsync(qr);

                if (!viewModel.HasError)
                    await viewModel.CloseProjectSelectionCommand.ExecuteAsync(null);
            }
        }
    }
}
