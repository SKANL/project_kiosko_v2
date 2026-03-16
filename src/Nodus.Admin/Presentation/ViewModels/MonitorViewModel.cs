using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Events;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Presentation.ViewModels;

/// <summary>
/// Real-time vote feed. Subscribes to the BLE incoming-data observable and
/// refreshes the vote list whenever a new vote arrives.
/// </summary>
public sealed partial class MonitorViewModel : BaseViewModel, IDisposable
{
    private readonly IBleGattServerService _ble;
    private readonly GetVoteSummaryUseCase _summary;
    private readonly IVoteRepository       _votes;
    private readonly IJudgeRepository      _judges;
    private readonly IProjectRepository    _projects;
    private readonly IAppSettingsService   _settings;
    private readonly CloseEventUseCase     _closeEvent;
    private IDisposable? _subscription;

    public MonitorViewModel(
        IBleGattServerService ble,
        GetVoteSummaryUseCase summary,
        IVoteRepository votes,
        IJudgeRepository judges,
        IProjectRepository projects,
        IAppSettingsService settings,
        CloseEventUseCase closeEvent)
    {
        _ble        = ble;
        _summary    = summary;
        _votes      = votes;
        _judges     = judges;
        _projects   = projects;
        _settings   = settings;
        _closeEvent = closeEvent;
        Title       = "Centro de control";
    }

    // ── State ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isBleRunning;
    [ObservableProperty] private int    _totalVotes;
    [ObservableProperty] private int    _activeJudges;
    [ObservableProperty] private int    _registeredJudges;
    [ObservableProperty] private int    _activeRelaysEstimated;
    [ObservableProperty] private int    _unsyncedToCloud;
    [ObservableProperty] private string _lastReceivedAt = "—";

    public ObservableCollection<Vote> RecentVotes { get; } = new();
    public ObservableCollection<ProjectMonitorItem> ProjectCoverage { get; } = new();
    public ObservableCollection<NodeInspectorItem> NodeInspector { get; } = new();

    // ── Lifecycle ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AppearingAsync()
    {
        // Ensure any previous subscription is cleaned up before re-subscribing
        Disappearing();

        IsBleRunning = _ble.IsRunning;

        // Subscribe to incoming BLE data → refresh recent votes on each new packet
        _subscription = _ble.IncomingData
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(_ =>
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await RefreshRecentVotesAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"MonitorViewModel refresh error: {ex.Message}");
                    }
                }));

        await RefreshRecentVotesAsync();
    }

    [RelayCommand]
    public void Disappearing()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    // ── Data ──────────────────────────────────────────────────────────

    private async Task RefreshRecentVotesAsync()
    {
        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue || eventId.Value <= 0) return;

        var result = await _votes.GetByEventAsync(eventId.Value);
        var judgesResult = await _judges.GetByEventAsync(eventId.Value);
        var projectsResult = await _projects.GetByEventAsync(eventId.Value);
        if (result.IsFail || judgesResult.IsFail || projectsResult.IsFail) return;

        var ordered = result.Value!
            .OrderByDescending(v => v.ReceivedAt)
            .Take(50)
            .ToList();

        var judges = judgesResult.Value!.ToList();
        var projects = projectsResult.Value!.OrderBy(p => p.SortOrder).ToList();
        var latestVotes = result.Value!
            .GroupBy(v => new { v.ProjectId, v.JudgeId })
            .Select(group => group
                .OrderByDescending(v => v.Version)
                .ThenByDescending(v => ParseUtc(v.ReceivedAt) ?? DateTime.MinValue)
                .First())
            .ToList();

        var relayIds = ordered
            .SelectMany(v => ParseHopIds(v.HopPathJson))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var recentThreshold = DateTime.UtcNow.AddMinutes(-5);
        var activeJudgeIds = ordered
            .Where(v => (ParseUtc(v.ReceivedAt) ?? DateTime.MinValue) >= recentThreshold)
            .Select(v => v.JudgeId)
            .ToHashSet();

        RegisteredJudges = judges.Count;
        ActiveJudges = judges.Count(judge => activeJudgeIds.Contains(judge.Id) || relayIds.Contains(judge.Id.ToString()));
        ActiveRelaysEstimated = relayIds.Count;
        UnsyncedToCloud = result.Value!.Count(v =>
            v.SyncStatus        != Domain.Enums.SyncStatus.Synced       &&
            v.ScoreStatus       != Domain.Enums.SyncStatus.ReachedCloud &&
            v.ScoreStatus       != Domain.Enums.SyncStatus.ReachedAdmin);

        TotalVotes    = result.Value!.Count;
        var latestVote = ordered.FirstOrDefault();
        LastReceivedAt = latestVote is null
            ? "—"
            : (ParseUtc(latestVote.ReceivedAt)?.ToLocalTime().ToString("dd/MM HH:mm:ss") ?? latestVote.ReceivedAt);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            RecentVotes.Clear();
            foreach (var vote in ordered)
                RecentVotes.Add(vote);

            ProjectCoverage.Clear();
            foreach (var project in projects)
            {
                var votesForProject = latestVotes.Where(v => v.ProjectId == project.Id).ToList();
                var lastActivity = votesForProject
                    .Select(v => ParseUtc(v.ReceivedAt))
                    .Where(v => v.HasValue)
                    .Max();

                var projectStatus = BuildProjectStatus(votesForProject.Count, judges.Count, lastActivity);
                ProjectCoverage.Add(new ProjectMonitorItem(
                    project.Id,
                    string.IsNullOrWhiteSpace(project.ProjectCode) ? $"Proyecto {project.Id}" : project.ProjectCode,
                    project.Name,
                    $"{votesForProject.Count}/{Math.Max(judges.Count, 1)}",
                    projectStatus.Label,
                    projectStatus.Color,
                    lastActivity.HasValue ? ToRelativeTime(lastActivity.Value) : "Sin actividad"
                ));
            }

            NodeInspector.Clear();
            foreach (var judge in judges
                .OrderBy(j => {
                    var jVotes  = ordered.Where(v => v.JudgeId == j.Id).Count();
                    var relayed = ordered.Where(v => ParseHopIds(v.HopPathJson).Contains(j.Id.ToString(), StringComparer.Ordinal)).Count();
                    if (relayed > 0) return 0;   // relay first
                    if (jVotes  > 0) return 1;   // active seeker second
                    return 2;                     // no activity last
                })
                .ThenBy(j => j.Name))
            {
                var judgeVotes = ordered.Where(v => v.JudgeId == judge.Id).ToList();
                var relayedVotes = ordered.Where(v => ParseHopIds(v.HopPathJson).Contains(judge.Id.ToString(), StringComparer.Ordinal)).ToList();
                var lastSeen = judgeVotes
                    .Concat(relayedVotes)
                    .Select(v => ParseUtc(v.ReceivedAt))
                    .Where(v => v.HasValue)
                    .Max();

                var isRelay = relayedVotes.Count > 0;
                NodeInspector.Add(new NodeInspectorItem(
                    judge.Id,
                    judge.Name,
                    isRelay ? "LINK (estimado)" : judgeVotes.Count > 0 ? "SEEKER (estimado)" : "Sin actividad",
                    isRelay ? "#34C759" : judgeVotes.Count > 0 ? "#5AC8FA" : "#8E8E93",
                    lastSeen.HasValue ? ToRelativeTime(lastSeen.Value) : "Aún sin señal",
                    judge.IsBlocked ? "Bloqueado" : "Permitido"
                ));
            }
        });
    }

    // ── Commands ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloseEvent))]
    private bool _isEventClosed;

    public bool CanCloseEvent => !IsEventClosed;

    [RelayCommand]
    private async Task CloseEventAsync()
        => await SafeExecuteAsync(async () =>
        {
            var eventId = _settings.ActiveEventId;
            if (!eventId.HasValue || eventId.Value <= 0) return;
            var result = await _closeEvent.ExecuteAsync(eventId.Value);
            if (result.IsOk) IsEventClosed = true;
            else { ErrorMessage = result.Error!; HasError = true; }
        });

    [RelayCommand]
    private async Task ToggleBleAsync()
        => await SafeExecuteAsync(async () =>
        {
            if (_ble.IsRunning) { await _ble.StopAsync(); IsBleRunning = false; }
            else                { await _ble.StartAsync(); IsBleRunning = true; }
        });

    [RelayCommand]
    private async Task RefreshAsync()
        => await RefreshRecentVotesAsync();

    public void Dispose()
    {
        _subscription?.Dispose();
    }

    private static (string Label, string Color) BuildProjectStatus(int voteCount, int registeredJudges, DateTime? lastActivity)
    {
        if (voteCount <= 0)
            return ("Sin datos", "#FF3B30");

        if (registeredJudges > 0 && voteCount >= registeredJudges)
            return ("Completo", "#34C759");

        if (!lastActivity.HasValue || lastActivity.Value < DateTime.UtcNow.AddHours(-1))
            return ("Bajo", "#FF9500");

        return ("En curso", "#5AC8FA");
    }

    private static IEnumerable<string> ParseHopIds(string hopPathJson)
    {
        if (string.IsNullOrWhiteSpace(hopPathJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(hopPathJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static DateTime? ParseUtc(string value)
        => DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static string ToRelativeTime(DateTime timestampUtc)
    {
        var delta = DateTime.UtcNow - timestampUtc;
        if (delta.TotalMinutes < 1) return "Ahora mismo";
        if (delta.TotalMinutes < 60) return $"Hace {(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"Hace {(int)delta.TotalHours} h";
        return timestampUtc.ToLocalTime().ToString("dd/MM HH:mm");
    }
}

public sealed record ProjectMonitorItem(
    int Id,
    string ProjectCode,
    string ProjectName,
    string VotesLabel,
    string StatusLabel,
    string StatusColor,
    string LastActivityLabel);

public sealed record NodeInspectorItem(
    int JudgeId,
    string JudgeName,
    string EstimatedRole,
    string RoleColor,
    string LastSeenLabel,
    string AccessLabel);
