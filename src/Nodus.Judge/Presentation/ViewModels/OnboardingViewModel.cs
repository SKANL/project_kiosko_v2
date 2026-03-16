using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Application.UseCases.Onboarding;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Simplified two-step onboarding:
///   Step 1 (JoinEvent): scan access QR + name + event password → register and sync.
///   Step 2 (Done): confirmation.
///
/// No separate PIN is created.  The event access password serves as the key that
/// protects the judge's private signing key, eliminating the "create a PIN" step.
/// The judge's name is persisted in settings so it never has to be re-entered.
/// </summary>
public sealed partial class OnboardingViewModel : BaseViewModel
{
    private readonly SyncFromAdminUseCase  _sync;
    private readonly ICryptoService        _crypto;
    private readonly IAppSettingsService   _settings;
    private readonly ILocalJudgeRepository _judges;

    public OnboardingViewModel(
        SyncFromAdminUseCase  sync,
        ICryptoService        crypto,
        IAppSettingsService   settings,
        ILocalJudgeRepository judges)
    {
        _sync     = sync;
        _crypto   = crypto;
        _settings = settings;
        _judges   = judges;
        Title = "Bienvenida";
    }

    public enum OnboardingStep { JoinEvent, Done }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnStepJoinEvent))]
    [NotifyPropertyChangedFor(nameof(IsOnStepDone))]
    private OnboardingStep _currentStep = OnboardingStep.JoinEvent;

    public bool IsOnStepJoinEvent => CurrentStep == OnboardingStep.JoinEvent;
    public bool IsOnStepDone      => CurrentStep == OnboardingStep.Done;

    [ObservableProperty] private string _judgeName       = string.Empty;
    [ObservableProperty] private string _eventPassword   = string.Empty;
    [ObservableProperty] private string _accessQrRaw     = string.Empty;
    [ObservableProperty] private string _accessStatusMessage = "Escanea el QR de acceso que muestra la organización.";
    [ObservableProperty] private string _connectionStatusMessage = "Aún no hay conexión confirmada con la app Admin.";
    [ObservableProperty] private bool _isConnectionConfirmed;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isNavigatingToVoting;
    [ObservableProperty] private bool   _syncSucceeded;
    [ObservableProperty] private string _syncEventName   = string.Empty;
    [ObservableProperty] private int    _syncProjectCount;
    [ObservableProperty] private int    _syncJudgeCount;

    [RelayCommand]
    private void AcceptAccessQr(string qrText)
    {
        if (string.IsNullOrWhiteSpace(qrText)) return;
        AccessQrRaw = qrText.Trim();
        AccessStatusMessage = "QR detectado. Confirma conexión con la app Admin para continuar.";
        ConnectionStatusMessage = "Pulsa 'Confirmar conexión' para validar el enlace BLE.";
        IsConnectionConfirmed = false;
        HasError = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmConnectionAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(AccessQrRaw))
            {
                ErrorMessage = "Primero escanea el QR de acceso del evento.";
                HasError = true;
                return;
            }

            var accessQr = ParseAccessQr(AccessQrRaw);
            if (accessQr is null)
            {
                ErrorMessage = "El QR escaneado no corresponde a un acceso válido.";
                HasError = true;
                return;
            }

            IsConnecting = true;
            ConnectionStatusMessage = "Conectando con la app Admin...";

            var connectResult = await _sync.EnsureAdminConnectionAsync(timeoutSeconds: 12);
            IsConnecting = false;

            if (connectResult.IsFail)
            {
                IsConnectionConfirmed = false;
                ConnectionStatusMessage = "No se pudo confirmar la conexión. Acércate al equipo Admin e inténtalo otra vez.";
                ErrorMessage = connectResult.Error!;
                HasError = true;
                return;
            }

            IsConnectionConfirmed = true;
            ConnectionStatusMessage = "Conexión confirmada. Ahora escribe tu nombre y la clave del evento.";
            AccessStatusMessage = "Enlace listo. Completa tus datos para entrar.";
            HasError = false;
            ErrorMessage = string.Empty;
        });

    [RelayCommand]
    private async Task JoinEventAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(AccessQrRaw))
            {
                ErrorMessage = "Primero escanea el QR de acceso del evento.";
                HasError = true;
                return;
            }
            if (!IsConnectionConfirmed)
            {
                ErrorMessage = "Confirma la conexión con la app Admin antes de continuar.";
                HasError = true;
                return;
            }
            if (string.IsNullOrWhiteSpace(JudgeName))
            {
                ErrorMessage = "Escribe tu nombre para identificar tus evaluaciones.";
                HasError = true;
                return;
            }
            if (string.IsNullOrWhiteSpace(EventPassword))
            {
                ErrorMessage = "Escribe la clave del evento.";
                HasError = true;
                return;
            }

            var accessQr = ParseAccessQr(AccessQrRaw);
            if (accessQr is null)
            {
                ErrorMessage = "El QR escaneado no corresponde a un acceso válido.";
                HasError = true;
                return;
            }

            var decrypted = _crypto.DecryptPayloadWithPassword(
                accessQr.Token,
                EventPassword.Trim(),
                $"event:{accessQr.EventId}:access");
            if (decrypted.IsFail)
            {
                ErrorMessage = "La clave del evento no es correcta.";
                HasError = true;
                return;
            }

            JudgeAccessPayload? accessPayload;
            try
            {
                accessPayload = JsonSerializer.Deserialize<JudgeAccessPayload>(decrypted.Value!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"No se pudo leer el contenido del QR: {ex.Message}";
                HasError = true;
                return;
            }

            if (accessPayload is null || accessPayload.EventId != accessQr.EventId)
            {
                ErrorMessage = "Los datos del QR están incompletos.";
                HasError = true;
                return;
            }

            // Run CPU-heavy crypto off the UI thread to avoid ANR on slower devices.
            var trimmedPassword = EventPassword.Trim();
            var cryptoPrep = await Task.Run(() =>
            {
                var (pubKey, privKey) = _crypto.GenerateKeyPair();
                var encResult = _crypto.EncryptPrivateKey(privKey, trimmedPassword);
                return (pubKey, encResult);
            });

            var pubKey = cryptoPrep.pubKey;
            var encResult = cryptoPrep.encResult;
            if (encResult.IsFail)
            {
                ErrorMessage = "No se pudo preparar tu identidad de firmado. Inténtalo otra vez.";
                HasError = true;
                return;
            }

            // Store placeholder judge row so SyncFromAdminUseCase can find self by public key
            await _judges.UpsertAsync(new LocalJudge
            {
                RemoteId                  = 0,
                Name                      = JudgeName.Trim(),
                PublicKeyBase64           = pubKey,
                EncryptedPrivateKeyBase64 = encResult.Value!,
                IsSelf                    = true
            });

            // Persist name and session key — name survives app restarts, key lives in memory only
            _settings.SessionPin = trimmedPassword;

            AccessStatusMessage = "Uniéndote al evento y descargando proyectos...";
            var result = await _sync.ExecuteAsync(new SyncFromAdminUseCase.RegistrationContext(
                accessPayload.EventId,
                JudgeName.Trim(),
                accessPayload.SharedKeyBase64));
            if (result.IsFail)
            {
                // Roll back placeholder identity if sync could not complete.
                var self = await _judges.GetSelfAsync();
                if (self.IsOk && self.Value is not null)
                    await _judges.DeleteAsync(self.Value.RemoteId);

                _settings.SessionPin = string.Empty;
                ErrorMessage = result.Error!;
                HasError = true;
                AccessStatusMessage = $"No se pudo completar el ingreso: {result.Error}";
                return;
            }

            _settings.JudgeName  = JudgeName.Trim();
            _settings.PinHash    = await Task.Run(() => _crypto.HashPin(trimmedPassword));
            _settings.SessionPin = trimmedPassword;
            _settings.Save();

            var payload = result.Value!;
            SyncEventName    = payload.EventName;
            SyncProjectCount = payload.Projects.Count;
            SyncJudgeCount   = payload.Judges.Count;
            SyncSucceeded    = true;
            AccessStatusMessage = "Todo quedó listo. Ya puedes comenzar a evaluar.";
            CurrentStep = OnboardingStep.Done;
        });

    [RelayCommand(CanExecute = nameof(CanNavigateToVoting))]
    private async Task NavigateToVotingAsync()
    {
        if (IsNavigatingToVoting)
            return;

        try
        {
            IsNavigatingToVoting = true;
            await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//VotingPage"));
        }
        finally
        {
            IsNavigatingToVoting = false;
            NavigateToVotingCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanNavigateToVoting() => !IsNavigatingToVoting;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        // Pre-fill name from settings so returning judges don't re-type it
        if (string.IsNullOrEmpty(JudgeName) && !string.IsNullOrEmpty(_settings.JudgeName))
            JudgeName = _settings.JudgeName;

        // Skip JoinEvent if already fully onboarded
        if (_settings.IsOnboarded)
            CurrentStep = OnboardingStep.Done;

        if (CurrentStep == OnboardingStep.JoinEvent)
        {
            IsConnectionConfirmed = false;
            ConnectionStatusMessage = "Escanea el QR y confirma conexión para continuar.";
        }

        await Task.CompletedTask;
    }

    private static AccessQrInfo? ParseAccessQr(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))         return null;
        if (!string.Equals(uri.Scheme, "nodus", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(uri.Host,   "join",  StringComparison.OrdinalIgnoreCase)) return null;

        var values = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);

        if (!values.TryGetValue("eid", out var eventText) || !int.TryParse(eventText, out var eventId) || eventId <= 0)
            return null;

        values.TryGetValue("token", out var token);
        return string.IsNullOrWhiteSpace(token) ? null : new AccessQrInfo(eventId, token);
    }

    private sealed record AccessQrInfo(int EventId, string Token);
    private sealed record JudgeAccessPayload(int EventId, string EventName, string SharedKeyBase64);
}
