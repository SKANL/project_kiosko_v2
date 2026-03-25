using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Application.Services;
using Nodus.Judge.Application.UseCases.Voting;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Enums;
using Nodus.Judge.Presentation.Views;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Manages the voting flow: browse projects, score each one, submit signed vote.
/// Each project gets one vote with a dictionary of scores per criterion.
/// </summary>
[QueryProperty(nameof(ProjectId), "ProjectId")]
public sealed partial class VotingViewModel : BaseViewModel
{
    private string _projectIdStr = string.Empty;
    private int? _targetProjectId;

    public string ProjectId
    {
        get => _projectIdStr;
        set
        {
            _projectIdStr = value;
            if (int.TryParse(value, out var id))
            {
                _targetProjectId = id;
                ApplyTargetProject();
            }
        }
    }

    private void ApplyTargetProject()
    {
        if (_targetProjectId.HasValue && Projects.Count > 0)
        {
            var idx = Projects.ToList().FindIndex(p => p.RemoteId == _targetProjectId.Value);
            if (idx >= 0)
            {
                CurrentProjectIndex = idx;
                Task.Run(LoadExistingScoresAsync);
                VoteSubmitted = false;
            }
            _targetProjectId = null;
        }
    }
    private readonly SubmitVoteUseCase        _submit;
    private readonly ILocalProjectRepository  _projects;
    private readonly ILocalEventRepository    _localEvents;
    private readonly ILocalVoteRepository     _votes;
    private readonly IGroqService _groq;
    private readonly ILocalJudgeRepository    _judges;
    private readonly ICryptoService           _crypto;
    private readonly IAppSettingsService      _settings;
    private readonly PinValidationService     _pinValidation;
    private readonly IBleSwarmService         _swarm;
    private IDisposable? _swarmStateSubscription;
    public VotingViewModel(
        SubmitVoteUseCase submit,
        ILocalProjectRepository projects,
        ILocalEventRepository localEvents,
        ILocalVoteRepository votes,
        IGroqService groq,
        ILocalJudgeRepository judges,
        ICryptoService crypto,
        IAppSettingsService settings,
        PinValidationService pinValidation,
        IBleSwarmService swarm)
    {
        _submit = submit;
        _projects = projects;
        _localEvents = localEvents;
        _votes = votes;
        _groq = groq;
        _judges = judges;
        _crypto = crypto;
        _settings = settings;
        _pinValidation = pinValidation;
        _swarm = swarm;
        Title = "Evaluar";

        UpdateSwarmAssist(_swarm.CurrentState);
        _swarmStateSubscription = _swarm.StateChanges.Subscribe(state =>
            MainThread.BeginInvokeOnMainThread(() => UpdateSwarmAssist(state)));

        // When the Projects collection changes, force-notify all computed properties that
        // depend on Projects.Count / Projects[index]. Without this, setting
        // CurrentProjectIndex = 0 after a Clear+Add cycle fires no PropertyChanged
        // (SetProperty sees no delta), leaving HasCurrentProject stale in the UI.
        Projects.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FilteredProjects));
            OnPropertyChanged(nameof(HasCurrentProject));
            OnPropertyChanged(nameof(HasCurrentProjectDescription));
            OnPropertyChanged(nameof(HasNoProjects));
            OnPropertyChanged(nameof(CurrentProjectName));
            OnPropertyChanged(nameof(CurrentProjectCategory));
            OnPropertyChanged(nameof(CurrentProjectDescription));
            OnPropertyChanged(nameof(CurrentProjectNumber));
            SubmitVoteCommand.NotifyCanExecuteChanged();
            NextProjectCommand.NotifyCanExecuteChanged();
            PreviousProjectCommand.NotifyCanExecuteChanged();
        };
    }

    // ── Projects ──────────────────────────────────────────────────────

    public ObservableCollection<LocalProject> Projects { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public IEnumerable<LocalProject> FilteredProjects => 
        string.IsNullOrWhiteSpace(SearchQuery) 
            ? Projects 
            : Projects.Where(p => 
                p.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || 
                p.StandNumber.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredProjects));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentProjectName))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectCategory))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectDescription))]
    [NotifyPropertyChangedFor(nameof(HasCurrentProject))]
    [NotifyPropertyChangedFor(nameof(HasCurrentProjectDescription))]
    [NotifyPropertyChangedFor(nameof(HasNoProjects))]
    [NotifyPropertyChangedFor(nameof(CurrentProjectNumber))]
    [NotifyCanExecuteChangedFor(nameof(SubmitVoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousProjectCommand))]
    private int _currentProjectIndex;

    public string CurrentProjectName        => CurrentProject?.Name        ?? "—";
    public string CurrentProjectCategory    => CurrentProject?.Category    ?? "—";
    public string CurrentProjectDetails     => CurrentProject is null
        ? "—"
        : $"{(string.IsNullOrEmpty(CurrentProject.StandNumber) ? "" : $"Stand {CurrentProject.StandNumber} · ")}{CurrentProject.Category}";
    public string CurrentProjectDescription => CurrentProject?.Description ?? "—";
    public bool   HasCurrentProject         => CurrentProject is not null;
    public bool   HasCurrentProjectDescription => HasCurrentProject && !string.IsNullOrWhiteSpace(CurrentProjectDescription) && CurrentProjectDescription != "—";
    public bool   HasNoProjects             => !HasCurrentProject;
    public int    CurrentProjectNumber      => HasCurrentProject ? CurrentProjectIndex + 1 : 0;

    private LocalProject? CurrentProject
        => Projects.Count > 0 && CurrentProjectIndex >= 0 && CurrentProjectIndex < Projects.Count
            ? Projects[CurrentProjectIndex]
            : null;

    // ── Scores ────────────────────────────────────────────────────────

    /// <summary>
    /// Dynamic criteria loaded from <see cref="LocalEvent.RubricJson"/>.
    /// Falls back to 5 equal-weight standard criteria when rubric is absent.
    /// </summary>
    public ObservableCollection<CriterionScoreItem> Criteria { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitVoteCommand))]
    private bool _isEventClosed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitVoteCommand))]
    private bool _isGracePeriodActive;
    [ObservableProperty] private bool _isScannerVisible = false;
    [ObservableProperty] private string _eventStateMessage = string.Empty;
    [ObservableProperty] private string _projectSelectionMessage = "Toca 'Escanear QR' cuando estés listo para leer el proyecto.";
    [ObservableProperty] private string _syncStateLabel = "Listo para comenzar";
    [ObservableProperty] private string _syncStateHint = "La app guardará y moverá tus evaluaciones automáticamente.";
    [ObservableProperty] private string _syncStateIcon = "☁";
    [ObservableProperty] private bool _hasPendingVotes;
    [ObservableProperty] private bool _hasSyncRisk;
    [ObservableProperty] private bool _hasSyncedVotes = true;
    [ObservableProperty] private bool _isMuleMode;
    [ObservableProperty] private bool _isRelayAssistVisible;
    [ObservableProperty] private string _relayAssistMessage = string.Empty;

    // PIN for decrypting private key at submission time
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinCaptured))]
    [NotifyPropertyChangedFor(nameof(IsPinMissing))]
    [NotifyCanExecuteChangedFor(nameof(SubmitVoteCommand))]
    private string _pin = string.Empty;

    public bool IsPinCaptured => !string.IsNullOrWhiteSpace(Pin);
    public bool IsPinMissing => string.IsNullOrWhiteSpace(Pin);

    // PIN management properties
    [ObservableProperty] private bool _showPinMenu;
    [ObservableProperty] private string _newPin = string.Empty;
    [ObservableProperty] private string _confirmPin = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinError))]
    private string _pinErrorMessage = string.Empty;
    [ObservableProperty] private bool _pinChangeSucceeded;

    public bool HasPinError => !string.IsNullOrEmpty(PinErrorMessage);

    // ── Status ────────────────────────────────────────────────────────

    [ObservableProperty] private int    _totalProjects;
    [ObservableProperty] private int    _submittedCount;
    [ObservableProperty] private bool   _voteSubmitted;
    [ObservableProperty] private string _submittedMessage = string.Empty;

    // ── Lifecycle ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AppearingAsync()
        => await SafeExecuteAsync(async () =>
        {
            // Restore PIN from session if available (user entered it during onboarding)
            if (string.IsNullOrEmpty(Pin) && !string.IsNullOrEmpty(_settings.SessionPin))
                Pin = _settings.SessionPin;

            var eventId = _settings.ActiveEventId;
            if (eventId is null)
            {
                ErrorMessage = "No hay evento activo. Sincroniza con el Admin primero.";
                HasError     = true;
                return;
            }

            var result = await _projects.GetByEventAsync(eventId.Value);
            if (result.IsFail)
            {
                ErrorMessage = $"Error al cargar proyectos: {result.Error}";
                HasError     = true;
                return;
            }

            Projects.Clear();
            foreach (var p in result.Value!.OrderBy(p => p.SortOrder))
                Projects.Add(p);

            TotalProjects = Projects.Count;

            if (TotalProjects == 0)
            {
                ErrorMessage = $"No se encontraron proyectos para el evento #{eventId.Value}. Vuelve a sincronizar con el Admin.";
                HasError     = true;
                return;
            }

            HasError = false;
            ErrorMessage = string.Empty;

            // Load rubric criteria from event definition
            var evtResult = await _localEvents.GetByIdAsync(eventId.Value);
            if (evtResult.IsOk)
            {
                LoadCriteria(evtResult.Value!.RubricJson);
                UpdateEventState(evtResult.Value!);
            }
            else
            {
                LoadCriteria(string.Empty);
            }

            var votes = await _votes.GetByEventAsync(eventId.Value);
            SubmittedCount = votes.IsOk ? votes.Value!.Count : 0;
            await RefreshSyncStateAsync();

            if (_targetProjectId.HasValue)
            {
                ApplyTargetProject();
            }
            else 
            {
                if (CurrentProjectIndex < 0 || CurrentProjectIndex >= Projects.Count)
                {
                    CurrentProjectIndex = 0;
                }
                await LoadExistingScoresAsync();
            }
        });

    // ── Navigation ────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextProjectAsync()
    {
        if (CurrentProjectIndex < Projects.Count - 1)
        {
            CurrentProjectIndex++;
            await LoadExistingScoresAsync();
            VoteSubmitted = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousProjectAsync()
    {
        if (CurrentProjectIndex > 0)
        {
            CurrentProjectIndex--;
            await LoadExistingScoresAsync();
            VoteSubmitted = false;
        }
    }

    private bool CanGoNext()     => Projects.Count > 0 && CurrentProjectIndex < Projects.Count - 1;
    private bool CanGoPrevious() => CurrentProjectIndex > 0;

    [RelayCommand]
    private void ToggleScanner()
    {
        IsScannerVisible = !IsScannerVisible;
        ProjectSelectionMessage = IsScannerVisible
            ? "Apunta la cámara al QR que está en la mesa del proyecto."
            : "La cámara está oculta. También puedes moverte manualmente entre proyectos.";
    }

    [RelayCommand]
    private async Task OpenProjectScannerAsync()
        => await Shell.Current.GoToAsync(nameof(ProjectScannerPage));

    [RelayCommand]
    private async Task OpenProjectSelectionAsync()
        => await Shell.Current.GoToAsync(nameof(ProjectSelectionPage), new Dictionary<string, object>
        {
            { "ViewModel", this }
        });

    [RelayCommand]
    private async Task SelectProjectAsync(LocalProject project)
    {
        if (project == null) return;
        
        await Shell.Current.GoToAsync(nameof(ProjectDetailsPage), new Dictionary<string, object>
        {
            { "Project", project }
        });
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

    [RelayCommand]
    private async Task OpenSyncAsync()
        => await Shell.Current.GoToAsync(nameof(SyncPage));

    [RelayCommand]
    private async Task OpenMyVotesAsync()
        => await Shell.Current.GoToAsync(nameof(MyVotesPage));

    

    [RelayCommand]
    private async Task CloseProjectSelectionAsync()
    {
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
            await Shell.Current.GoToAsync("..");
        else
            await Shell.Current.GoToAsync("//VotingPage");
    }

    [RelayCommand]
    private void ClearPin()
    {
        Pin = string.Empty;
        _settings.SessionPin = string.Empty;
        _settings.Save();
    }

    [RelayCommand]
    private async Task HandleScannedProjectAsync(string qrText)
        => await SafeExecuteAsync(async () =>
        {
            var eventId = _settings.ActiveEventId;
            if (!eventId.HasValue || eventId.Value <= 0)
            {
                ErrorMessage = "Todavía no hay un evento cargado en este equipo.";
                HasError = true;
                return;
            }

            var projectCode = ParseProjectCode(qrText);
            if (string.IsNullOrWhiteSpace(projectCode))
            {
                ErrorMessage = "Ese QR no corresponde a un proyecto válido de este evento.";
                HasError = true;
                return;
            }

            var result = await _projects.GetByCodeAsync(eventId.Value, projectCode);
            if (result.IsFail || result.Value is null)
            {
                ErrorMessage = $"El proyecto {projectCode} todavía no está disponible en este evento.";
                HasError = true;
                return;
            }

            var index = Projects.IndexOf(Projects.FirstOrDefault(project => project.RemoteId == result.Value.RemoteId)!);
            if (index >= 0)
            {
                CurrentProjectIndex = index;
                await LoadExistingScoresAsync();
                VoteSubmitted = false;
                IsScannerVisible = false;
                ProjectSelectionMessage = $"Se abrió {result.Value.Name}. Ya puedes evaluarlo.";
                HasError = false;
                ErrorMessage = string.Empty;
            }
        });

    // ── Submit ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSubmitVote))]
    private async Task SubmitVoteAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (CurrentProject is null) return;

            var scores  = Criteria.ToDictionary(c => c.CriterionId, c => c.Value);
            var weights = Criteria.ToDictionary(c => c.CriterionId, c => c.Weight);

            var request = new SubmitVoteUseCase.Request(
                CurrentProject.RemoteId,
                scores,
                weights,
                Pin);

            var result = await _submit.ExecuteAsync(request);
            if (result.IsFail || result.Value is null)
            {
                ErrorMessage = result.Error!;
                HasError     = true;
                return;
            }

            VoteSubmitted    = true;
            SubmittedCount++;
            SubmittedMessage = result.Value.SyncStatus == SyncStatus.Synced
                ? $"La evaluación de \"{CurrentProjectName}\" ya llegó al equipo del organizador."
                : $"La evaluación de \"{CurrentProjectName}\" quedó guardada en este equipo y se enviará sola cuando encuentre una ruta disponible.";

            await RefreshSyncStateAsync();
        });

    private bool CanSubmitVote() => HasCurrentProject && !string.IsNullOrWhiteSpace(Pin) && (!IsEventClosed || IsGracePeriodActive);

    // ── PIN Management ────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePinMenu()
    {
        ShowPinMenu = !ShowPinMenu;
        if (!ShowPinMenu)
        {
            NewPin = string.Empty;
            ConfirmPin = string.Empty;
            PinErrorMessage = string.Empty;
            PinChangeSucceeded = false;
        }
    }

    [RelayCommand]
    private void ChangePinInitiate()
    {
        PinChangeSucceeded = false;
        PinErrorMessage = string.Empty;
        NewPin = string.Empty;
        ConfirmPin = string.Empty;
    }

    [RelayCommand]
    private async Task ChangePinAsync()
        => await SafeExecuteAsync(async () =>
        {
            PinErrorMessage = string.Empty;
            PinChangeSucceeded = false;

            // Validate new PIN format
            var validateResult = _pinValidation.ValidatePin(NewPin);
            if (validateResult.IsFail)
            {
                PinErrorMessage = validateResult.Error!;
                return;
            }

            // Verify confirmation matches
            if (NewPin != ConfirmPin)
            {
                PinErrorMessage = "La confirmación del PIN no coincide";
                return;
            }

            // Current PIN is required to decrypt the stored private key
            if (string.IsNullOrEmpty(Pin))
            {
                PinErrorMessage = "Necesitas el PIN actual para cambiarlo";
                return;
            }

            // Verify old PIN against stored hash
            if (!string.IsNullOrEmpty(_settings.PinHash) && !_crypto.VerifyPin(Pin, _settings.PinHash))
            {
                PinErrorMessage = "El PIN actual no es correcto";
                return;
            }

            // Load judge identity
            var judgeResult = await _judges.GetSelfAsync();
            if (!judgeResult.IsOk || judgeResult.Value is null)
            {
                PinErrorMessage = "No se encontró la identidad local del juez";
                return;
            }
            var judge = judgeResult.Value;

            // Decrypt private key with old PIN
            var decResult = _crypto.DecryptPrivateKey(judge.EncryptedPrivateKeyBase64, Pin);
            if (!decResult.IsOk)
            {
                PinErrorMessage = "No se pudo abrir la llave con el PIN actual";
                return;
            }

            // Re-encrypt with new PIN
            var encResult = _crypto.EncryptPrivateKey(decResult.Value!, NewPin);
            if (!encResult.IsOk)
            {
                PinErrorMessage = "No se pudo proteger la llave con el nuevo PIN";
                return;
            }

            // Persist updated judge
            judge.EncryptedPrivateKeyBase64 = encResult.Value!;
            var saveResult = await _judges.UpsertAsync(judge);
            if (!saveResult.IsOk)
            {
                PinErrorMessage = "No se pudo guardar el nuevo PIN";
                return;
            }

            // Update settings
            _settings.PinHash    = _crypto.HashPin(NewPin);
            _settings.SessionPin = NewPin;
            _settings.Save();

            // Update in-session PIN so the next vote submission uses the new PIN
            Pin = NewPin;

            PinChangeSucceeded = true;
            PinErrorMessage = "PIN actualizado correctamente";

            await Task.Delay(2000);
            ShowPinMenu = false;
            NewPin = string.Empty;
            ConfirmPin = string.Empty;
        });

    [RelayCommand]
    private async Task ResetPinAsync()
        => await SafeExecuteAsync(async () =>
        {
            // A PIN reset without knowing the old PIN is impossible without losing the
            // encrypted private key.  The only safe recovery path is to clear settings
            // and force the judge to re-onboard with a fresh key-pair.
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "Restablecer identidad",
                "Al restablecer tu PIN se borrará la identidad del jurado y las evaluaciones pendientes. Luego tendrás que volver a sincronizar con la organización. ¿Deseas continuar?",
                "Sí, restablecer",
                "No");

            if (!confirmed) return;

            _settings.PinHash       = string.Empty;
            _settings.SessionPin    = string.Empty;
            _settings.SelfJudgeId   = null;
            _settings.ActiveEventId = null;
            _settings.Save();

            // Navigate back to onboarding
            await Shell.Current.GoToAsync("//OnboardingPage");
        });

    [RelayCommand]
    private void ValidatePinFormat()
    {
        PinErrorMessage = string.Empty;

        if (string.IsNullOrEmpty(NewPin))
            return;

        var validateResult = _pinValidation.ValidatePin(NewPin);
        if (validateResult.IsFail)
            PinErrorMessage = validateResult.Error!;
    }

    private void ResetScores()
    {
        foreach (var c in Criteria) c.Value = (c.Min + c.Max) / 2.0;
    }

    private void UpdateEventState(LocalEvent evt)
    {
        IsEventClosed = !string.IsNullOrWhiteSpace(evt.FinishedAt);
        IsGracePeriodActive = false;

        if (!IsEventClosed)
        {
            EventStateMessage = "La evaluación está abierta. Escanea un QR y califica el proyecto.";
            return;
        }

        if (DateTime.TryParse(evt.GraceEndsAt, out var graceEndsAt)
            && DateTime.UtcNow <= graceEndsAt.ToUniversalTime())
        {
            IsGracePeriodActive = true;
            EventStateMessage = $"La evaluación cerró, pero aún puedes enviar pendientes hasta las {graceEndsAt.ToLocalTime():HH:mm}.";
            return;
        }

        EventStateMessage = "La evaluación terminó. Ya no se aceptan nuevas calificaciones.";
    }

    private void UpdateSwarmAssist(FireflyState state)
    {
        if (state == FireflyState.Link)
        {
            IsRelayAssistVisible = true;
            RelayAssistMessage = "Ahora estás ayudando a conectar evaluaciones cercanas. Mantén la app abierta unos minutos.";
            return;
        }

        IsRelayAssistVisible = false;
        RelayAssistMessage = string.Empty;
    }

    private async Task RefreshSyncStateAsync()
    {
        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue || eventId.Value <= 0)
        {
            HasPendingVotes = false;
            HasSyncRisk = false;
            HasSyncedVotes = true;
            SyncStateIcon = "☁";
            SyncStateLabel = "Listo para comenzar";
            SyncStateHint = "La app guardará y moverá tus evaluaciones automáticamente.";
            return;
        }

        var votesResult = await _votes.GetByEventAsync(eventId.Value);
        var votes = votesResult.IsOk ? votesResult.Value! : [];
        var pendingVotes = votes.Where(v => v.SyncStatus == SyncStatus.Pending).ToList();
        var nowUtc = DateTime.UtcNow;
        IsMuleMode = pendingVotes.Any(v => DateTime.TryParse(v.CreatedAt, out var createdAt) && (nowUtc - createdAt.ToUniversalTime()) >= TimeSpan.FromMinutes(10));

        HasPendingVotes = pendingVotes.Count > 0;
        HasSyncRisk = IsEventClosed && !IsGracePeriodActive && pendingVotes.Count > 0;
        HasSyncedVotes = !HasPendingVotes && !HasSyncRisk;

        if (HasSyncRisk)
        {
            SyncStateIcon = "⚠";
            SyncStateLabel = "Requiere atención";
            SyncStateHint = "Hay evaluaciones guardadas fuera del tiempo aceptado por el organizador.";
            return;
        }

        if (HasPendingVotes)
        {
            if (IsMuleMode)
            {
                SyncStateIcon = "⬣";
                SyncStateLabel = "Modo traslado";
                SyncStateHint = "Tus evaluaciones están seguras en este equipo. Acércate a la mesa de organización para enviarlas más rápido.";
                return;
            }

            SyncStateIcon = "💾";
            SyncStateLabel = "Guardado en este equipo";
            SyncStateHint = pendingVotes.Count == 1
                ? "Tienes 1 evaluación esperando una ruta cercana para enviarse."
                : $"Tienes {pendingVotes.Count} evaluaciones esperando una ruta cercana para enviarse.";
            return;
        }

        SyncStateIcon = "☁";
        SyncStateLabel = votes.Count == 0 ? "Listo para comenzar" : "Todo al día";
        SyncStateHint = votes.Count == 0
            ? "Cuando envíes la primera evaluación, la app la guardará incluso si la red tarda."
            : "Las últimas evaluaciones ya llegaron al organizador.";
    }

    private void LoadCriteria(string rubricJson)
    {
        Criteria.Clear();
        if (!string.IsNullOrWhiteSpace(rubricJson))
        {
            try
            {
                var defs = JsonSerializer.Deserialize<List<RubricDef>>(rubricJson);
                if (defs is { Count: > 0 })
                {
                    foreach (var d in defs)
                        Criteria.Add(new CriterionScoreItem
                        {
                            CriterionId = d.id,
                            Label       = d.label,
                            Min         = d.min,
                            Max         = d.max,
                            Step        = d.step,
                            Weight      = d.weight,
                            Value       = (d.min + d.max) / 2.0
                        });
                    return;
                }
            }
            catch { /* fall through to defaults */ }
        }

        // Fallback: 5 equal-weight criteria
        foreach (var (id, label) in new[]
        {
            ("innovation",   "Innovación"),
            ("impact",       "Impacto"),
            ("feasibility",  "Viabilidad"),
            ("presentation", "Presentación"),
            ("technical",    "Técnica")
        })
        {
            Criteria.Add(new CriterionScoreItem
            {
                CriterionId = id,
                Label       = label,
                Min         = 0, Max = 10, Step = 0.5, Weight = 1.0, Value = 7.0
            });
        }
    }

    private async Task LoadExistingScoresAsync()
    {
        if (CurrentProject is null) return;
        var eventId = _settings.ActiveEventId;
        if (eventId is null) return;

        var judgeResult = await _judges.GetSelfAsync();
        if (!judgeResult.IsOk || judgeResult.Value is null) return;
        var judgeRemoteId = judgeResult.Value.RemoteId;

        var voteResult = await _votes.GetLatestByJudgeProjectAsync(
            eventId.Value, CurrentProject.RemoteId, judgeRemoteId);
        if (!voteResult.IsOk || voteResult.Value is null)
        {
            ResetScores();
            return;
        }

        var existingScores = JsonSerializer.Deserialize<Dictionary<string, double>>(
            voteResult.Value.ScoresJson) ?? new();
        foreach (var c in Criteria)
        {
            if (existingScores.TryGetValue(c.CriterionId, out var v))
                c.Value = v;
        }
    }

    private static string ParseProjectCode(string qrText)
    {
        if (!Uri.TryCreate(qrText, UriKind.Absolute, out var uri))
            return string.Empty;
        if (!string.Equals(uri.Scheme, "nodus", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (!string.Equals(uri.Host, "vote", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var values = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);

        return values.TryGetValue("pid", out var pid) ? pid.Trim().ToUpperInvariant() : string.Empty;
    }

    // DTO for rubric JSON deserialization
    private sealed record RubricDef(string id, string label, double weight, double min, double max, double step);
}
