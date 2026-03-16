using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Events;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;
using Nodus.Admin.Presentation.Views;

namespace Nodus.Admin.Presentation.ViewModels;

/// <summary>
/// Wraps a NodusEvent for display in the list.
/// Commands are embedded per-item (closure pattern) so that CollectionView DataTemplates
/// never need RelativeSource AncestorType lookups, which are unreliable in MAUI on Windows.
/// </summary>
public sealed class EventListItem
{
    public required NodusEvent Event      { get; init; }
    public required int        VoteCount  { get; init; }

    // Commands — set during construction in EventsListViewModel.LoadEventsAsync
    public required ICommand ActivateCommand    { get; init; }
    public required ICommand DeleteCommand      { get; init; }
    public required ICommand ViewQrsCommand     { get; init; }
    public required ICommand ViewResultsCommand { get; init; }

    public string StatusLabel => Event.Status switch
    {
        EventStatus.Active   => "Activo",
        EventStatus.Paused   => "Pausado",
        EventStatus.Finished => "Finalizado",
        _                    => "Borrador"
    };

    public string StatusColor => Event.Status switch
    {
        EventStatus.Active   => "#34C759",   // NodusSuccess
        EventStatus.Paused   => "#FF9500",   // NodusWarning
        EventStatus.Finished => "#5AC8FA",   // NodusInfo
        _                    => "#6C6C70"    // NodusSecondaryLabel
    };

    public bool IsActive    => Event.Status == EventStatus.Active;
    public bool CanActivate => Event.Status != EventStatus.Active && Event.Status != EventStatus.Finished;
}

/// <summary>
/// Shows all saved events. Supports creating, activating, and deleting events.
/// Activating an event pauses the previous one and pushes a BLE 0x07 notification
/// to all connected judges.
/// </summary>
public sealed partial class EventsListViewModel : BaseViewModel
{
    private readonly IEventRepository    _events;
    private readonly IVoteRepository     _votes;
    private readonly IJudgeRepository    _judges;
    private readonly IProjectRepository  _projects;
    private readonly IAppSettingsService _settings;
    private readonly ActivateEventUseCase _activate;

    public EventsListViewModel(
        IEventRepository    events,
        IVoteRepository     votes,
        IJudgeRepository    judges,
        IProjectRepository  projects,
        IAppSettingsService settings,
        ActivateEventUseCase activate)
    {
        _events   = events;
        _votes    = votes;
        _judges   = judges;
        _projects = projects;
        _settings = settings;
        _activate = activate;
        Title     = "Eventos";
    }

    // ── State ─────────────────────────────────────────────────────────

    public ObservableCollection<EventListItem> Events { get; } = new();

    [ObservableProperty] private bool   _hasEvents;
    [ObservableProperty] private string _activatedMessage = string.Empty;
    [ObservableProperty] private bool   _activationSucceeded;

    /// <summary>True when there are no events — used by XAML instead of InvertedBoolConverter.</summary>
    public bool HasNoEvents => !HasEvents;

    partial void OnHasEventsChanged(bool value) => OnPropertyChanged(nameof(HasNoEvents));

    // ── Lifecycle ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AppearingAsync()
        => await SafeExecuteAsync(async () =>
        {
            ActivationSucceeded = false;
            ActivatedMessage    = string.Empty;
            await LoadEventsAsync();
        });

    private async Task LoadEventsAsync()
    {
        var result = await _events.GetAllAsync();
        if (result.IsFail) { ErrorMessage = result.Error!; HasError = true; return; }

        Events.Clear();

        var allEvents = result.Value!
            .OrderByDescending(e => e.Status == EventStatus.Active)
            .ThenByDescending(e => e.Id)
            .ToList();

        foreach (var evt in allEvents)
        {
            int voteCount = 0;
            var voteResult = await _votes.GetByEventAsync(evt.Id);
            if (voteResult.IsOk)
                voteCount = voteResult.Value!.Count;

            // Capture evt in a local so the closures reference the correct loop variable.
            var capturedEvt       = evt;
            var capturedVoteCount = voteCount;

            // Use a two-step init so the commands can close over the 'item' variable.
            EventListItem? item = null;
            item = new EventListItem
            {
                Event      = capturedEvt,
                VoteCount  = capturedVoteCount,
                ActivateCommand    = new AsyncRelayCommand(async () => await ActivateItemAsync(item!)),
                DeleteCommand      = new AsyncRelayCommand(async () => await DeleteItemAsync(item!)),
                ViewQrsCommand     = new AsyncRelayCommand(async () => await ViewQrsItemAsync(item!)),
                ViewResultsCommand = new AsyncRelayCommand(async () => await ViewResultsItemAsync(item!)),
            };
            Events.Add(item);
        }

        HasEvents = Events.Count > 0;
    }

    // ── Commands ─────────────────────────────────────────────────────

    /// <summary>Called from each item's ActivateCommand closure.</summary>
    private async Task ActivateItemAsync(EventListItem item)
        => await SafeExecuteAsync(async () =>
        {
            ActivationSucceeded = false;
            var result = await _activate.ExecuteAsync(item.Event.Id);
            if (result.IsFail) { ErrorMessage = result.Error!; HasError = true; return; }

            ActivatedMessage    = $"✓ \"{item.Event.Name}\" ya quedó activo. Los jueces verán el cambio automáticamente al reconectarse o sincronizar.";
            ActivationSucceeded = true;
            await LoadEventsAsync();
        });

    /// <summary>Called from each item's DeleteCommand closure.</summary>
    private async Task DeleteItemAsync(EventListItem item)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Eliminar evento",
            $"¿Eliminar \"{item.Event.Name}\"? Esta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (!confirm) return;

        await SafeExecuteAsync(async () =>
        {
            if (_settings.ActiveEventId == item.Event.Id)
            {
                _settings.ActiveEventId = null;
                _settings.Save();
            }

            await _votes.DeleteByEventAsync(item.Event.Id);
            await _judges.DeleteByEventAsync(item.Event.Id);
            await _projects.DeleteByEventAsync(item.Event.Id);

            var result = await _events.DeleteAsync(item.Event.Id);
            if (result.IsFail) { ErrorMessage = result.Error!; HasError = true; return; }

            await LoadEventsAsync();
        });
    }

    [RelayCommand]
    private async Task NavigateToNewEventAsync()
        => await Shell.Current.GoToAsync(nameof(EventSetupPage));

    /// <summary>Called from each item's ViewQrsCommand closure.</summary>
    private async Task ViewQrsItemAsync(EventListItem item)
        => await Shell.Current.GoToAsync(
               $"{nameof(EventSetupPage)}?sourceEventId={item.Event.Id}");

    /// <summary>Called from each item's ViewResultsCommand closure.</summary>
    private async Task ViewResultsItemAsync(EventListItem item)
        => await Shell.Current.GoToAsync(
               $"//ResultsPage?viewEventId={item.Event.Id}");
}
