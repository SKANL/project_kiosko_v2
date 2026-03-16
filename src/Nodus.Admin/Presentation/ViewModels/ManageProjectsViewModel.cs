using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Presentation.ViewModels;

public sealed class ManageProjectItem
{
    public required Project Project { get; init; }
    public required ICommand DeleteCommand { get; init; }
}

public sealed partial class ManageProjectsViewModel : BaseViewModel
{
    private readonly IProjectRepository _projects;
    private readonly IAppSettingsService _settings;

    public ObservableCollection<ManageProjectItem> Projects { get; } = new();

    [ObservableProperty] private string _newProjectName = string.Empty;
    [ObservableProperty] private string _newProjectCategory = string.Empty;
    [ObservableProperty] private string _newProjectDescription = string.Empty;
    [ObservableProperty] private string _newProjectMembers = string.Empty;
    [ObservableProperty] private string _newProjectStand = string.Empty;
    [ObservableProperty] private string _newProjectGithub = string.Empty;

    [ObservableProperty] private Project? _selectedProject;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editCategory = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private string _editMembers = string.Empty;
    [ObservableProperty] private string _editStand = string.Empty;
    [ObservableProperty] private string _editGithub = string.Empty;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _hasProjects;

    public ManageProjectsViewModel(IProjectRepository projects, IAppSettingsService settings)
    {
        _projects = projects;
        _settings = settings;
        Title = "Proyectos";
    }

    [RelayCommand]
    public async Task AppearingAsync() => await LoadProjectsAsync();

    private async Task LoadProjectsAsync()
    {
        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue)
        {
            HasProjects = false;
            return;
        }

        var result = await _projects.GetByEventAsync(eventId.Value);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        Projects.Clear();
        foreach (var project in result.Value!)
        {
            var item = new ManageProjectItem
            {
                Project = project,
                DeleteCommand = new AsyncRelayCommand(async () => await DeleteProjectAsync(project))
            };
            Projects.Add(item);
        }

        HasProjects = Projects.Count > 0;
    }

    [RelayCommand]
    private void EditProject(Project project)
    {
        SelectedProject = project;
        EditName = project.Name;
        EditCategory = project.Category;
        EditDescription = project.Description;
        EditMembers = project.TeamMembers;
        EditStand = project.StandNumber;
        EditGithub = project.GithubLink;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        SelectedProject = null;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (SelectedProject == null || string.IsNullOrWhiteSpace(EditName)) return;

        SelectedProject.Name = EditName.Trim();
        SelectedProject.Category = EditCategory.Trim();
        SelectedProject.Description = EditDescription.Trim();
        SelectedProject.TeamMembers = EditMembers.Trim();
        SelectedProject.StandNumber = EditStand.Trim();
        SelectedProject.GithubLink = EditGithub.Trim();

        var result = await _projects.UpdateAsync(SelectedProject);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        IsEditing = false;
        SelectedProject = null;
        await LoadProjectsAsync();
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName)) return;

        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue) return;

        var newProject = new Project
        {
            EventId = eventId.Value,
            Name = NewProjectName.Trim(),
            Category = NewProjectCategory.Trim(),
            Description = NewProjectDescription.Trim(),
            TeamMembers = NewProjectMembers.Trim(),
            StandNumber = NewProjectStand.Trim(),
            GithubLink = NewProjectGithub.Trim(),
            ProjectCode = GenerateProjectCode()
        };

        var result = await _projects.CreateAsync(newProject);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        NewProjectName = string.Empty;
        NewProjectCategory = string.Empty;
        NewProjectDescription = string.Empty;
        NewProjectMembers = string.Empty;
        NewProjectStand = string.Empty;
        NewProjectGithub = string.Empty;
        
        await LoadProjectsAsync();
    }

    private async Task DeleteProjectAsync(Project project)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Eliminar Proyecto",
            $"¿Eliminar \"{project.Name}\"? Esta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (!confirm) return;

        var result = await _projects.DeleteAsync(project.Id);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        await LoadProjectsAsync();
    }

    private string GenerateProjectCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var code = new string(Enumerable.Range(0, 3)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
                .ToArray());
            var projectCode = $"PROJ-{code}";
            if (!Projects.Any(p => string.Equals(p.Project.ProjectCode, projectCode, StringComparison.OrdinalIgnoreCase)))
                return projectCode;
        }
    }
}
