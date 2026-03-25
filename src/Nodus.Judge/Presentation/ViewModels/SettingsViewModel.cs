using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Application.UseCases.Onboarding;
using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Exposes security, synchronisation and application settings.
/// Sections: 🔐 Seguridad · 📡 Sincronización · ⚙️ Aplicación
/// </summary>
public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly ICryptoService        _crypto;
    private readonly IAppSettingsService   _settings;
    private readonly ILocalJudgeRepository _judges;
    private readonly ILocalVoteRepository  _votes;
    private readonly ILocalEventRepository _events;
    private readonly ILocalProjectRepository _projects;
    private readonly SyncFromAdminUseCase _sync;

    // ── Constructor ────────────────────────────────────────────────────────────

    public SettingsViewModel(
        ICryptoService        crypto,
        IAppSettingsService   settings,
        ILocalJudgeRepository judges,
        ILocalVoteRepository  votes,
        ILocalEventRepository events,
        ILocalProjectRepository projects,
        SyncFromAdminUseCase sync)
    {
        _crypto        = crypto;
        _settings      = settings;
        _judges        = judges;
        _votes         = votes;
        _events        = events;
        _projects      = projects;
        _sync          = sync;
    }

    // ── 🔐 SEGURIDAD ─────────────────────────────────────────────────────────

    /// <summary>True when a PIN hash exists already.</summary>
    public bool   IsPinConfigured  => !string.IsNullOrEmpty(_settings.PinHash);

    /// <summary>Human-readable label: "✓ Configurado" / "⚠ Sin configurar".</summary>
    public string PinStatusLabel   => IsPinConfigured ? "✓ Configurado" : "⚠ Sin configurar";

    /// <summary>Date of last PIN change or "Nunca" if not tracked.</summary>
    public string PinLastChanged   => string.IsNullOrEmpty(_settings.PinLastChangedAt)
                                        ? "Nunca"
                                        : TryFormatDate(_settings.PinLastChangedAt);

    // PIN change form ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePin))]
    private bool _showPinForm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePin))]
    private string _currentPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePin))]
    private string _newPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePin))]
    private string _confirmPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinError))]
    private string _pinErrorMessage = string.Empty;

    public bool HasPinError => !string.IsNullOrEmpty(PinErrorMessage);

    [ObservableProperty]
    private bool _pinChangeSucceeded;

    public bool CanSavePin =>
        !string.IsNullOrWhiteSpace(CurrentPin) &&
        !string.IsNullOrWhiteSpace(NewPin)     &&
        NewPin == ConfirmPin;

    // ── 📡 SINCRONIZACIÓN ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string _activeEventName = "—";

    [ObservableProperty]
    private string _judgeId = "—";

    [ObservableProperty]
    private bool _isResyncing;

    [ObservableProperty]
    private string _resyncStatusMessage = "";

    // ── ⚙️ APLICACIÓN ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private int _votesSentCount;
    [ObservableProperty]
    private string _groqApiKey = string.Empty;

    public string AppVersion =>
        AppInfo.VersionString is { Length: > 0 } v ? v : "2.0";

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AppearingAsync()
        => await SafeExecuteAsync(async () =>
        {
            await LoadSummaryAsync();
            // Load saved Groq API key from secure storage if present
            try
            {
                var key = await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync("GROQ_API_KEY");
                GroqApiKey = key ?? string.Empty;
            }
            catch { GroqApiKey = string.Empty; }
        });

    private async Task LoadSummaryAsync()
    {
        // Juez ID
        JudgeId = _settings.SelfJudgeId?.ToString() ?? "—";

        // Evento activo
        var evtResult = await _events.GetActiveAsync().ConfigureAwait(false);
        ActiveEventName = (evtResult.IsOk && evtResult.Value is not null)
            ? evtResult.Value.Name
            : "Sin evento";

        // Votos enviados
        if (_settings.ActiveEventId.HasValue)
        {
            var voteResult = await _votes.GetByEventAsync(_settings.ActiveEventId.Value).ConfigureAwait(false);
            VotesSentCount = voteResult.IsOk ? voteResult.Value.Count : 0;
        }
        else
        {
            VotesSentCount = 0;
        }

        // Notify computed properties that depend on _settings
        OnPropertyChanged(nameof(IsPinConfigured));
        OnPropertyChanged(nameof(PinStatusLabel));
        OnPropertyChanged(nameof(PinLastChanged));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePinForm()
    {
        ShowPinForm      = !ShowPinForm;
        PinErrorMessage  = string.Empty;
        PinChangeSucceeded = false;
        CurrentPin       = string.Empty;
        NewPin           = string.Empty;
        ConfirmPin       = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanSavePin))]
    private async Task ChangePinAsync()
    {
        PinErrorMessage    = string.Empty;
        PinChangeSucceeded = false;
        // 1. Validate new password format
        if (NewPin.Trim().Length < 6)
        {
            PinErrorMessage = "La clave debe tener al menos 6 caracteres.";
            return;
        }

        // 2. Verify current password
        if (!_crypto.VerifyPin(CurrentPin, _settings.PinHash))
        {
            PinErrorMessage = "Clave del evento actual incorrecta.";
            return;
        }

        // 3. Confirm match
        if (NewPin != ConfirmPin)
        {
            PinErrorMessage = "Las claves nuevas no coinciden.";
            return;
        }

        // 4. Load judge identity
        var judgeResult = await _judges.GetSelfAsync().ConfigureAwait(false);
        if (!judgeResult.IsOk || judgeResult.Value is null)
        {
            PinErrorMessage = "No se encontró la identidad del juez.";
            return;
        }
        var judge = judgeResult.Value;

        // 5-6. Decrypt, re-encrypt and hash inside Task.Run to prevent UI freeze
        var cryptoResult = await Task.Run(() =>
        {
            var decResult = _crypto.DecryptPrivateKey(judge.EncryptedPrivateKeyBase64, CurrentPin);
            if (!decResult.IsOk)
                return Result<(string EncKey, string Hash)>.Fail("No se pudo descifrar la llave con la clave actual.");

            var encResult = _crypto.EncryptPrivateKey(decResult.Value!, NewPin);
            if (!encResult.IsOk)
                return Result<(string EncKey, string Hash)>.Fail("Error al cifrar con la clave nueva.");

            var hash = _crypto.HashPin(NewPin);
            return Result<(string EncKey, string Hash)>.Ok((encResult.Value!, hash));
        });

        if (!cryptoResult.IsOk)
        {
            PinErrorMessage = cryptoResult.Error!;
            return;
        }

        // 7. Persist updated judge + settings
        judge.EncryptedPrivateKeyBase64 = cryptoResult.Value!.EncKey;
        var saveResult = await _judges.UpsertAsync(judge).ConfigureAwait(false);
        if (!saveResult.IsOk)
        {
            PinErrorMessage = "Error al guardar el juez actualizado.";
            return;
        }

        _settings.PinHash          = cryptoResult.Value.Hash;
        _settings.SessionPin       = NewPin;
        _settings.PinLastChangedAt = DateTime.UtcNow.ToString("o");
        _settings.Save();

        // 8. Success
        PinChangeSucceeded = true;
        ShowPinForm        = false;
        OnPropertyChanged(nameof(PinStatusLabel));
        OnPropertyChanged(nameof(PinLastChanged));
    }

    [RelayCommand]
    private async Task ResyncAsync()
        => await SafeExecuteAsync(async () =>
        {
            IsResyncing = true;
            ResyncStatusMessage = "Buscando cambios del evento...";

            try
            {
                var result = await _sync.ExecuteAsync().ConfigureAwait(false);
                if (result.IsFail)
                {
                    PinErrorMessage = string.Empty;
                    ErrorMessage = result.Error!;
                    HasError = true;
                    ResyncStatusMessage = "No se pudo sincronizar. Revisa distancia o estado de la app Admin.";
                    return;
                }

                await LoadSummaryAsync().ConfigureAwait(false);
                ResyncStatusMessage = "Sincronización completada.";
                await MainThread.InvokeOnMainThreadAsync(() =>
                    Shell.Current.DisplayAlertAsync("Listo", "La app actualizó los proyectos y jueces disponibles.", "Continuar"));
            }
            finally
            {
                IsResyncing = false;
            }
        });

    [RelayCommand]
    private async Task OpenConnectionHelpAsync()
        => await Shell.Current.GoToAsync(nameof(Presentation.Views.SyncPage)).ConfigureAwait(false);

    [RelayCommand]
    private async Task ClearLocalDataAsync()
    {
        bool confirm = await Shell.Current
            .DisplayAlertAsync(
                "Borrar datos",
                "¿Eliminar todos los datos locales? Esta acción no se puede deshacer.",
                "Borrar",
                "Cancelar")
            .ConfigureAwait(false);

        if (!confirm) return;

        await _events.DeleteAllAsync().ConfigureAwait(false);
        await _projects.DeleteAllAsync().ConfigureAwait(false);
        await _judges.DeleteAllAsync().ConfigureAwait(false);
        await _votes.DeleteAllAsync().ConfigureAwait(false);
        _sync.StopAutoPolling();

        _settings.ActiveEventId = null;
        _settings.SelfJudgeId   = null;
        _settings.PinHash        = string.Empty;
        _settings.SessionPin     = string.Empty;
        _settings.PinLastChangedAt = string.Empty;
        _settings.Save();

        // Return to onboarding
        await Shell.Current.GoToAsync("OnboardingPage").ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task SaveGroqApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(GroqApiKey))
        {
            await Shell.Current.DisplayAlertAsync("Clave API", "Introduce una clave válida.", "OK");
            return;
        }
        try
        {
            await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync("GROQ_API_KEY", GroqApiKey);
            await Shell.Current.DisplayAlertAsync("Clave API", "Clave guardada en almacenamiento seguro.", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Error", $"No se pudo guardar la clave: {ex.Message}", "OK");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TryFormatDate(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToLocalTime().ToString("dd MMM yyyy, HH:mm");
        return iso;
    }
}
