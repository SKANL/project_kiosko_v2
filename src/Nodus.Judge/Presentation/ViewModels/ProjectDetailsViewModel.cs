using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Presentation.Views;

namespace Nodus.Judge.Presentation.ViewModels;

[QueryProperty(nameof(Project), "Project")]
public partial class ProjectDetailsViewModel : BaseViewModel
{
    private readonly ILocalVoteRepository _votes;
    private readonly ILocalJudgeRepository _judges;

    public ProjectDetailsViewModel(ILocalVoteRepository votes, ILocalJudgeRepository judges)
    {
        _votes = votes;
        _judges = judges;
        Title = "Detalles del Proyecto";
    }

    [ObservableProperty]
    private LocalProject? _project;

    [ObservableProperty]
    private bool _isAlreadyVoted;

    [ObservableProperty]
    private string _previousScore = "—";

    /// <summary>True when at least one of GithubLink or VideoLink is set — drives RECURSOS section visibility.</summary>
    public bool HasLinks => !string.IsNullOrWhiteSpace(Project?.GithubLink) || !string.IsNullOrWhiteSpace(Project?.VideoLink);

    partial void OnProjectChanged(LocalProject? value)
    {
        if (value != null)
        {
            OnPropertyChanged(nameof(HasLinks));
            Task.Run(CheckVoteStatusAsync);
        }
    }

    private async Task CheckVoteStatusAsync()
    {
        if (Project == null) return;

        var judgeResult = await _judges.GetSelfAsync();
        if (judgeResult.IsOk && judgeResult.Value != null)
        {
            var voteResult = await _votes.GetLatestByJudgeProjectAsync(
                Project.EventId, Project.RemoteId, judgeResult.Value.RemoteId);
            
            if (voteResult.IsOk && voteResult.Value != null)
            {
                IsAlreadyVoted = true;
                PreviousScore = voteResult.Value.WeightedScore.ToString("F2");
                return;
            }
        }
        
        IsAlreadyVoted = false;
        PreviousScore = "—";
    }

    [RelayCommand]
    private async Task EvaluateAsync()
    {
        if (Project == null) return;

        // Navigate to VotingPage and pass the project or set it globally
        // For simplicity in this app structure, we navigate to the tab and 
        // rely on the shared state or a message.
        // Actually, we can use Shell navigation to the route.
        await Shell.Current.GoToAsync($"//VotingPage?ProjectId={Project.RemoteId}");
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task OpenGithubAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(Project.GithubLink)) return;
        await Launcher.Default.OpenAsync(Project.GithubLink);
    }

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(Project.VideoLink)) return;
        await Launcher.Default.OpenAsync(Project.VideoLink);
    }
}
