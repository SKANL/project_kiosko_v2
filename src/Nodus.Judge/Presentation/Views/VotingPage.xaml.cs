using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Presentation.ViewModels;

namespace Nodus.Judge.Presentation.Views;

public partial class VotingPage : ContentPage
{
    private readonly VotingViewModel _viewModel;
    private readonly IProjectScanBuffer _scanBuffer;

    public VotingPage(VotingViewModel viewModel, IProjectScanBuffer scanBuffer)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _scanBuffer = scanBuffer;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var pendingQr = _scanBuffer.PendingQr;
        if (!string.IsNullOrWhiteSpace(pendingQr))
        {
            _scanBuffer.PendingQr = null;
            await MainThread.InvokeOnMainThreadAsync(() => _viewModel.HandleScannedProjectCommand.Execute(pendingQr));
        }
    }
}
