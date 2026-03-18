using System.Linq;
using System.ComponentModel;
using Nodus.Judge.Presentation.ViewModels;
using ZXing.Net.Maui;

namespace Nodus.Judge.Presentation.Views;

public partial class OnboardingPage : ContentPage
{
    private readonly OnboardingViewModel _vm;
    private bool _accessQrLocked;
    private bool _isPageActive;

    public OnboardingPage(OnboardingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageActive = true;
        _accessQrLocked = false;
        UpdateScannerState();
        await _vm.AppearingCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        _isPageActive = false;
        _accessQrLocked = true;
        AccessQrScanner.IsDetecting = false;
        base.OnDisappearing();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnHandlerChanging(args);
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (!_isPageActive)
            return;

        var barcode = e.Results.FirstOrDefault();
        if (_accessQrLocked || barcode is null)
            return;

        var value = barcode.Value;
        if (string.IsNullOrWhiteSpace(value))
            return;

        _accessQrLocked = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_isPageActive)
                return;

            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
            _vm.AcceptAccessQrCommand.Execute(value);
            
            // Automatically trigger connection confirmation for a smoother flow
            if (_vm.ConfirmConnectionCommand.CanExecute(null))
            {
                await _vm.ConfirmConnectionCommand.ExecuteAsync(null);
            }

            // Keep scanner paused after a successful read to avoid camera pressure
            // while BLE onboarding requests are executed.
            AccessQrScanner.IsDetecting = false;
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isPageActive)
            return;

        if (e.PropertyName == nameof(OnboardingViewModel.IsBusy)
            || e.PropertyName == nameof(OnboardingViewModel.IsConnecting)
            || e.PropertyName == nameof(OnboardingViewModel.IsConnectionConfirmed)
            || e.PropertyName == nameof(OnboardingViewModel.CurrentStep))
        {
            UpdateScannerState();
        }
    }

    private void UpdateScannerState()
    {
        var shouldDetect = _isPageActive
                           && !_vm.IsBusy
                           && !_vm.IsConnecting
                           && !_vm.IsConnectionConfirmed
                           && _vm.CurrentStep == OnboardingViewModel.OnboardingStep.JoinEvent;

        // Some Android vendors keep camera resources active unless the view is hidden.
        AccessQrScanner.IsDetecting = shouldDetect;
        AccessQrScanner.IsVisible = shouldDetect;
        AccessQrScanner.IsEnabled = shouldDetect;
        _accessQrLocked = !shouldDetect;
    }
}
