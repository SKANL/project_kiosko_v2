using Nodus.Judge.Application.Interfaces.Services;
using ZXing.Net.Maui;

namespace Nodus.Judge.Presentation.Views;

public partial class ProjectScannerPage : ContentPage
{
    private readonly IProjectScanBuffer _scanBuffer;
    private bool _scanLocked;

    public ProjectScannerPage(IProjectScanBuffer scanBuffer)
    {
        InitializeComponent();
        _scanBuffer = scanBuffer;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _scanLocked = false;
        ProjectQrScanner.IsDetecting = true;
        ProjectQrScanner.IsEnabled = true;
    }

    protected override void OnDisappearing()
    {
        ProjectQrScanner.IsDetecting = false;
        ProjectQrScanner.IsEnabled = false;
        base.OnDisappearing();
    }

    private async void OnProjectQrDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_scanLocked)
            return;

        var value = e.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return;

        _scanLocked = true;
        ProjectQrScanner.IsDetecting = false;
        _scanBuffer.PendingQr = value;

        HapticFeedback.Default.Perform(HapticFeedbackType.Click);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current.Navigation.NavigationStack.Count > 1)
                await Shell.Current.GoToAsync("..");
            else
                await Shell.Current.GoToAsync("//VotingPage");
        });
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
            await Shell.Current.GoToAsync("..");
        else
            await Shell.Current.GoToAsync("//VotingPage");
    }
}
