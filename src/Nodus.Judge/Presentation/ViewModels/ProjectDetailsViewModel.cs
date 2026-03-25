using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Domain.Common;
using System.Text;
using System.Text.Json;
using Nodus.Judge.Infrastructure.Services;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Presentation.Views;

namespace Nodus.Judge.Presentation.ViewModels;

[QueryProperty(nameof(Project), "Project")]
public partial class ProjectDetailsViewModel : BaseViewModel
{
    private readonly ILocalVoteRepository _votes;
    private readonly ILocalJudgeRepository _judges;
    private readonly ILocalProjectRepository _projects;
    private readonly IGroqService _groq;
    private const int SectionPreviewLimit = 240;

    public ProjectDetailsViewModel(ILocalVoteRepository votes, ILocalJudgeRepository judges, ILocalProjectRepository projects, IGroqService groq)
    {
        _votes = votes;
        _judges = judges;
        _projects = projects;
        _groq = groq;
        Title = "Detalles del Proyecto";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string TryFormatDate(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToLocalTime().ToString("dd MMM yyyy, HH:mm");
        return iso;
    }

    [ObservableProperty]
    private string _aiSummary = string.Empty;

    [ObservableProperty]
    private string _aiSummaryFull = string.Empty;

    [ObservableProperty]
    private LocalProject? _project;

    [ObservableProperty]
    private bool _isAlreadyVoted;

    [ObservableProperty]
    private string _previousScore = "—";

    public string AiActionText => string.IsNullOrWhiteSpace(AiSummaryFull) ? "AI: Generar resumen" : "AI: Regenerar resumen";
    public string EvaluateButtonText => IsAlreadyVoted ? "Actualizar evaluación" : "Evaluar proyecto";
    public string DescriptionText => BuildPreview(Project?.Description, IsDescriptionExpanded);
    public string ObjectivesText => BuildPreview(Project?.Objetivos, IsObjectivesExpanded);
    public string TeamText => BuildPreview(Project?.TeamMembers, IsTeamExpanded);
    public string TechText => BuildPreview(Project?.TechStack, IsTechExpanded);
    public bool CanExpandDescription => CanExpand(Project?.Description);
    public bool CanExpandObjectives => CanExpand(Project?.Objetivos);
    public bool CanExpandTeam => CanExpand(Project?.TeamMembers);
    public bool CanExpandTech => CanExpand(Project?.TechStack);
    public string DescriptionToggleText => IsDescriptionExpanded ? "Ver menos" : "Ver más";
    public string ObjectivesToggleText => IsObjectivesExpanded ? "Ver menos" : "Ver más";
    public string TeamToggleText => IsTeamExpanded ? "Ver menos" : "Ver más";
    public string TechToggleText => IsTechExpanded ? "Ver menos" : "Ver más";

    /// <summary>True when at least one link is set — drives RECURSOS section visibility.</summary>
    public bool HasLinks => !string.IsNullOrWhiteSpace(Project?.GithubLink)
        || !string.IsNullOrWhiteSpace(Project?.VideoLink)
        || !string.IsNullOrWhiteSpace(Project?.SpeechVideoLink);

    partial void OnProjectChanged(LocalProject? value)
    {
        if (value != null)
        {
            IsDescriptionExpanded = false;
            IsObjectivesExpanded = false;
            IsTeamExpanded = false;
            IsTechExpanded = false;

            OnPropertyChanged(nameof(HasLinks));
            RefreshExpandableTexts();
            UpdateAiState(value.AiSummaryJson);
            AiGeneratedAt = string.IsNullOrWhiteSpace(value.AiGeneratedAt) ? string.Empty : TryFormatDate(value.AiGeneratedAt);
            Task.Run(async () =>
            {
                await CheckVoteStatusAsync();

                
            });
        }
    }

    [ObservableProperty]
    private bool _isRawAvailable;

    [ObservableProperty]
    private bool _isAiTruncated;

    [ObservableProperty]
    private string _aiGeneratedAt = string.Empty;

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    [ObservableProperty]
    private bool _isObjectivesExpanded;

    [ObservableProperty]
    private bool _isTeamExpanded;

    [ObservableProperty]
    private bool _isTechExpanded;

    partial void OnIsAlreadyVotedChanged(bool value)
    {
        OnPropertyChanged(nameof(EvaluateButtonText));
    }

    partial void OnIsDescriptionExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(DescriptionToggleText));
    }

    partial void OnIsObjectivesExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ObjectivesText));
        OnPropertyChanged(nameof(ObjectivesToggleText));
    }

    partial void OnIsTeamExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(TeamText));
        OnPropertyChanged(nameof(TeamToggleText));
    }

    partial void OnIsTechExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(TechText));
        OnPropertyChanged(nameof(TechToggleText));
    }

    [RelayCommand]
    private void ToggleDescription()
        => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private void ToggleObjectives()
        => IsObjectivesExpanded = !IsObjectivesExpanded;

    [RelayCommand]
    private void ToggleTeam()
        => IsTeamExpanded = !IsTeamExpanded;

    [RelayCommand]
    private void ToggleTech()
        => IsTechExpanded = !IsTechExpanded;

    [RelayCommand]
    private async Task ShowRawAiAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(Project.AiSummaryJson))
        {
            await Shell.Current.DisplayAlertAsync("AI", "No hay resultado AI disponible.", "OK");
            return;
        }

        var page = new RawAiJsonPage(Project.AiSummaryJson, "Resultado AI (JSON crudo)", "JSON copiado al portapapeles.");
        await Shell.Current.Navigation.PushModalAsync(page);
    }

    [RelayCommand]
    private async Task ShowFullAiAsync()
    {
        if (string.IsNullOrWhiteSpace(AiSummaryFull))
        {
            await Shell.Current.DisplayAlertAsync("AI", "No hay resumen completo disponible.", "OK");
            return;
        }

        var page = new RawAiJsonPage(AiSummaryFull, "Resumen AI (completo)", "Resumen copiado al portapapeles.");
        await Shell.Current.Navigation.PushModalAsync(page);
    }

    [RelayCommand]
    private async Task CopyAiSummaryAsync()
    {
        var value = string.IsNullOrWhiteSpace(AiSummaryFull) ? AiSummary : AiSummaryFull;
        if (string.IsNullOrWhiteSpace(value))
        {
            await Shell.Current.DisplayAlertAsync("AI", "No hay resumen para copiar.", "OK");
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(value);
            await Shell.Current.DisplayAlertAsync("Copiado", "Resumen copiado al portapapeles.", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Error", $"No se pudo copiar el resumen: {ex.Message}", "OK");
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

    [RelayCommand]
    private async Task OpenSpeechVideoAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(Project.SpeechVideoLink)) return;
        await Launcher.Default.OpenAsync(Project.SpeechVideoLink);
    }

    [RelayCommand]
    private async Task GenerateAiSummaryAsync()
    {
        if (Project == null) return;

        await SafeExecuteAsync(async () =>
        {
            var payload = new
            {
                name = Project.Name,
                description = Project.Description,
                objectives = Project.Objetivos,
                team = Project.TeamMembers,
                tech = Project.TechStack,
                category = Project.Category,
                stand = Project.StandNumber
            };
            var projectJson = JsonSerializer.Serialize(payload);

            var res = await _groq.GenerateStructuredSummaryAsync(projectJson);
            if (res.IsFail)
            {
                var err = res.Error ?? "Unknown error";
                // If API key missing, offer to navigate to Settings
                if (err.Contains("GROQ API key", System.StringComparison.OrdinalIgnoreCase) || err.Contains("not configured", System.StringComparison.OrdinalIgnoreCase))
                {
                    var go = await Shell.Current.DisplayAlertAsync("Clave API faltante", "La clave GROQ no está configurada. ¿Quieres ir a Ajustes para configurarla?", "Ir a Ajustes", "Cancelar");
                    if (go)
                    {
                        await Shell.Current.GoToAsync(nameof(Presentation.Views.SettingsPage));
                        return;
                    }
                    return;
                }

                await Shell.Current.DisplayAlertAsync("AI Error", err, "OK");
                return;
            }

            Project.AiSummaryJson = res.Value ?? string.Empty;
            Project.AiGeneratedAt = DateTime.UtcNow.ToString("o");
            var save = await _projects.BulkUpsertAsync(new[] { Project });
            if (save.IsFail)
            {
                await Shell.Current.DisplayAlertAsync("AI Error", save.Error ?? "Failed to save AI summary", "OK");
                return;
            }

            UpdateAiState(Project.AiSummaryJson);
            AiGeneratedAt = TryFormatDate(Project.AiGeneratedAt);
        });
    }

    private void UpdateAiState(string? raw)
    {
        AiSummaryFull = AiResultParser.ParseSummaryFull(raw);
        AiSummary = AiResultParser.ParseSummaryToDisplay(raw);
        IsAiTruncated = AiResultParser.IsDisplayTruncated(raw);
        IsRawAvailable = !string.IsNullOrWhiteSpace(raw);
        OnPropertyChanged(nameof(AiActionText));
    }

    private void RefreshExpandableTexts()
    {
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(ObjectivesText));
        OnPropertyChanged(nameof(TeamText));
        OnPropertyChanged(nameof(TechText));
        OnPropertyChanged(nameof(CanExpandDescription));
        OnPropertyChanged(nameof(CanExpandObjectives));
        OnPropertyChanged(nameof(CanExpandTeam));
        OnPropertyChanged(nameof(CanExpandTech));
        OnPropertyChanged(nameof(DescriptionToggleText));
        OnPropertyChanged(nameof(ObjectivesToggleText));
        OnPropertyChanged(nameof(TeamToggleText));
        OnPropertyChanged(nameof(TechToggleText));
    }

    private static bool CanExpand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Trim().Length > SectionPreviewLimit;
    }

    private static string BuildPreview(string? text, bool expanded)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Trim();
        if (expanded || normalized.Length <= SectionPreviewLimit)
            return normalized;

        return normalized.Substring(0, SectionPreviewLimit).TrimEnd() + "...";
    }

    
}
