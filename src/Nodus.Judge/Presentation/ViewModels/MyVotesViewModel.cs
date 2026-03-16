using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Entities;

namespace Nodus.Judge.Presentation.ViewModels;

public sealed class MyVoteItem
{
    public required LocalVote Vote { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectCategory { get; init; }
}

public sealed partial class MyVotesViewModel : BaseViewModel
{
    private readonly ILocalVoteRepository _votes;
    private readonly ILocalProjectRepository _projects;
    private readonly IAppSettingsService _settings;

    public ObservableCollection<MyVoteItem> MyVotes { get; } = new();

    [ObservableProperty] private bool _hasVotes;

    public MyVotesViewModel(
        ILocalVoteRepository votes,
        ILocalProjectRepository projects,
        IAppSettingsService settings)
    {
        _votes = votes;
        _projects = projects;
        _settings = settings;
        Title = "Mis Votos";
    }

    [RelayCommand]
    public async Task AppearingAsync() => await LoadVotesAsync();

    private async Task LoadVotesAsync()
    {
        var eventId = _settings.ActiveEventId;
        var judgeId = _settings.SelfJudgeId;

        if (!eventId.HasValue || !judgeId.HasValue)
        {
            HasVotes = false;
            return;
        }

        var votesResult = await _votes.GetByEventAsync(eventId.Value);
        var projectsResult = await _projects.GetByEventAsync(eventId.Value);

        if (votesResult.IsFail || projectsResult.IsFail)
        {
            ErrorMessage = "No se pudieron cargar los votos.";
            HasError = true;
            return;
        }

        var allVotes = votesResult.Value!
            .Where(v => v.JudgeId == judgeId.Value)
            .GroupBy(v => v.ProjectId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .OrderByDescending(v => v.CreatedAt)
            .ToList();

        var projects = projectsResult.Value!.ToDictionary(p => p.RemoteId);

        MyVotes.Clear();
        foreach (var vote in allVotes)
        {
            projects.TryGetValue(vote.ProjectId, out var proj);
            MyVotes.Add(new MyVoteItem
            {
                Vote = vote,
                ProjectName = proj?.Name ?? $"Proyecto #{vote.ProjectId}",
                ProjectCategory = proj?.Category ?? ""
            });
        }

        HasVotes = MyVotes.Count > 0;
    }
}
