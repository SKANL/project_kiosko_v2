using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Infrastructure.Http;
using QRCoder;

namespace Nodus.Admin.Presentation.ViewModels;

[QueryProperty(nameof(EventId), "eventId")]
public sealed partial class EventQrViewModel : BaseViewModel
{
    private readonly IEventRepository _events;
    private readonly ILocalHttpServerService _server;

    public EventQrViewModel(IEventRepository events, ILocalHttpServerService server)
    {
        _events = events;
        _server = server;
        Title = "Códigos QR";
    }

    [ObservableProperty]
    private int _eventId;

    partial void OnEventIdChanged(int value)
    {
        if (value > 0)
        {
            _ = LoadDataAsync();
        }
    }

    private bool _hasStudentRegistrationQr;
    public bool HasStudentRegistrationQr
    {
        get => _hasStudentRegistrationQr;
        set => SetProperty(ref _hasStudentRegistrationQr, value);
    }

    private ImageSource? _studentRegistrationQrSource;
    public ImageSource? StudentRegistrationQrSource
    {
        get => _studentRegistrationQrSource;
        set => SetProperty(ref _studentRegistrationQrSource, value);
    }

    private string _studentRegistrationUrl = string.Empty;
    public string StudentRegistrationUrl
    {
        get => _studentRegistrationUrl;
        set => SetProperty(ref _studentRegistrationUrl, value);
    }

    private bool _hasJudgeAccessQr;
    public bool HasJudgeAccessQr
    {
        get => _hasJudgeAccessQr;
        set => SetProperty(ref _hasJudgeAccessQr, value);
    }

    private ImageSource? _judgeAccessQrSource;
    public ImageSource? JudgeAccessQrSource
    {
        get => _judgeAccessQrSource;
        set => SetProperty(ref _judgeAccessQrSource, value);
    }

    private string _eventName = string.Empty;
    public string EventName
    {
        get => _eventName;
        set => SetProperty(ref _eventName, value);
    }

    [RelayCommand]
    public async Task AppearingAsync()
    {
        if (EventId <= 0) return;
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var eventResult = await _events.GetByIdAsync(EventId);
            if (eventResult.IsFail)
            {
                ErrorMessage = eventResult.Error!;
                HasError = true;
                return;
            }

            var evt = eventResult.Value!;
            EventName = evt.Name;

            await Task.Run(() => GenerateQrCodes(evt.AccessQrPayload, _server.LocalUrl));
        });
    }

    private async Task GenerateQrCodes(string judgeAccessPayload, string serverLocalUrl)
    {
        try
        {
            byte[]? accessPng = null;
            byte[]? studentPng = null;

            // Updated URL to use Cloud API to bypass Mixed Content on Vercel
            var cloudApi = "https://nodusapi-nhsm2zm5.b4a.run";
            var cloudEventId = $"EVT-{EventId:D3}"; // e.g. EVT-001
            var registerUrl = $"https://project-kiosko-v2.vercel.app/register?event={cloudEventId}&cloudApi={cloudApi}";

            await Task.Run(() =>
            {
                using var generator = new QRCodeGenerator();

                // access QR
                var accessQrData = generator.CreateQrCode(judgeAccessPayload, QRCodeGenerator.ECCLevel.Q);
                accessPng = new PngByteQRCode(accessQrData).GetGraphic(6);

                // student registration QR
                var studentQrData = generator.CreateQrCode(registerUrl, QRCodeGenerator.ECCLevel.M);
                studentPng = new PngByteQRCode(studentQrData).GetGraphic(6);
            });

            // Update UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (accessPng != null)
                {
                    JudgeAccessQrSource = ImageSource.FromStream(() => new MemoryStream(accessPng));
                    HasJudgeAccessQr = true;
                }

                if (studentPng != null)
                {
                    StudentRegistrationQrSource = ImageSource.FromStream(() => new MemoryStream(studentPng));
                    StudentRegistrationUrl = registerUrl;
                    HasStudentRegistrationQr = true;
                }
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ErrorMessage = $"Error al generar QRs: {ex.Message}";
                HasError = true;
            });
        }
    }
}
