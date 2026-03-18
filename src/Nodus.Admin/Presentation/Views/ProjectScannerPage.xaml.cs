using ZXing.Net.Maui;
using Nodus.Admin.Presentation.ViewModels;

namespace Nodus.Admin.Presentation.Views;

public partial class ProjectScannerPage : ContentPage
{
    private readonly ProjectScannerViewModel _viewModel;

    public ProjectScannerPage(ProjectScannerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        ProjectQrScanner.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false
        };
    }

    private void OnBarcodeDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var first = e.Results.FirstOrDefault();
        if (first != null)
        {
            Dispatcher.Dispatch(() => _viewModel.ProcessQrResult(first.Value));
        }
    }
}
