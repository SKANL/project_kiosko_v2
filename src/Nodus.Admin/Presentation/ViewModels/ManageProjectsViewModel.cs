using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Presentation.Views;
using Nodus.Admin.Application.UseCases.Events;

namespace Nodus.Admin.Presentation.ViewModels;

public sealed class ManageProjectItem
{
    public required Project Project { get; init; }
    public required ICommand DeleteCommand { get; init; }
}

public sealed partial class ManageProjectsViewModel : BaseViewModel
{
    private readonly IProjectRepository _projects;
    private readonly IEventRepository _events;
    private readonly IAppSettingsService _settings;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;

    public ObservableCollection<ManageProjectItem> Projects { get; } = new();
    public ObservableCollection<string> AvailableCategories { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddProjectCommand))]
    private string _newProjectCategory = string.Empty;

    [ObservableProperty]
    private string _newProjectDescription = string.Empty;

    [ObservableProperty]
    private string _newProjectTechStack = string.Empty;

    [ObservableProperty]
    private string _newMemberName = string.Empty;

    public ObservableCollection<string> TeamMemberList { get; } = new();

    [ObservableProperty]
    private string _newProjectStand = string.Empty;

    [ObservableProperty]
    private string _newProjectGithub = string.Empty;

    [ObservableProperty]
    private string _newProjectVideo = string.Empty;

    [ObservableProperty]
    private ManageProjectItem? _selectedProject;

    private string _editName = string.Empty;
    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    private string _editCategory = string.Empty;
    public string EditCategory
    {
        get => _editCategory;
        set => SetProperty(ref _editCategory, value);
    }

    private string _editDescription = string.Empty;
    public string EditDescription
    {
        get => _editDescription;
        set => SetProperty(ref _editDescription, value);
    }

    private string _editMembers = string.Empty;
    public string EditMembers
    {
        get => _editMembers;
        set => SetProperty(ref _editMembers, value);
    }

    private string _editStand = string.Empty;
    public string EditStand
    {
        get => _editStand;
        set => SetProperty(ref _editStand, value);
    }

    private string _editGithub = string.Empty;
    public string EditGithub
    {
        get => _editGithub;
        set => SetProperty(ref _editGithub, value);
    }

    private string _editVideo = string.Empty;
    public string EditVideo
    {
        get => _editVideo;
        set => SetProperty(ref _editVideo, value);
    }

    private string _editTechStack = string.Empty;
    public string EditTechStack
    {
        get => _editTechStack;
        set => SetProperty(ref _editTechStack, value);
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    [ObservableProperty]
    private bool _hasProjects;

    [ObservableProperty]
    private bool _isCreatingProject;

    [RelayCommand]
    private void ToggleCreateProject()
    {
        IsCreatingProject = !IsCreatingProject;
    }

    public void CheckProjectsState()
    {
        HasProjects = Projects.Count > 0;
    }

    public ManageProjectsViewModel(IProjectRepository projects, IEventRepository events, IAppSettingsService settings, BuildBootstrapPayloadUseCase bootstrap)
    {
        _projects = projects;
        _events = events;
        _settings = settings;
        _bootstrap = bootstrap;
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

        // Load Categories
        AvailableCategories.Clear();
        var evtResult = await _events.GetByIdAsync(eventId.Value);
        if (evtResult.IsOk && evtResult.Value != null && !string.IsNullOrEmpty(evtResult.Value.Categories))
        {
            foreach (var c in evtResult.Value.Categories.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                AvailableCategories.Add(c.Trim());
            }
        }
        else
        {
            // Default categories if none defined
            AvailableCategories.Add("Software");
            AvailableCategories.Add("Hardware");
            AvailableCategories.Add("Social");
        }

        // Members
        TeamMemberList.Clear();
        NewMemberName = string.Empty;

        // Form
        NewProjectName = string.Empty;
        NewProjectDescription = string.Empty;
        NewProjectCategory = string.Empty;
        NewProjectStand = string.Empty;
        NewProjectGithub = string.Empty;
        NewProjectVideo = string.Empty;
        NewProjectTechStack = string.Empty;

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
    private void EditProject(ManageProjectItem item)
    {
        SelectedProject = item;
        var project = item.Project;
        EditName = project.Name;
        EditCategory = project.Category;
        EditDescription = project.Description;
        EditMembers = project.TeamMembers;
        EditStand = project.StandNumber;
        EditGithub = project.GithubLink;
        EditVideo = project.VideoLink;
        EditTechStack = project.TechStack;
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

        var project = SelectedProject.Project;
        project.Name = EditName.Trim();
        project.Category = EditCategory.Trim();
        project.Description = EditDescription.Trim();
        project.TeamMembers = EditMembers.Trim();
        project.StandNumber = EditStand.Trim();
        project.GithubLink = EditGithub.Trim();
        project.VideoLink = EditVideo.Trim();
        project.TechStack = EditTechStack.Trim();

        var result = await _projects.UpdateAsync(project);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        IsEditing = false;
        SelectedProject = null;

        if (_settings.ActiveEventId.HasValue)
            await _bootstrap.ExecuteAsync(_settings.ActiveEventId.Value);

        await LoadProjectsAsync();
    }

    [RelayCommand]
    private void AddMember()
    {
        if (string.IsNullOrWhiteSpace(NewMemberName)) return;
        var name = NewMemberName.Trim();
        if (!TeamMemberList.Contains(name))
            TeamMemberList.Add(name);
        NewMemberName = string.Empty;
        AddProjectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SelectProjectAsync(ManageProjectItem item)
    {
        if (item?.Project == null) return;
        
        try 
        {
            await Shell.Current.GoToAsync(nameof(ProjectDetailsPage), new Dictionary<string, object>
            {
                { "Project", item.Project }
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error de Navegación", ex.Message, "OK");
        }

        SelectedProject = null;
    }

    [RelayCommand]
    private async Task GoToScannerAsync()
    {
        await Shell.Current.GoToAsync(nameof(ProjectScannerPage));
    }

    [RelayCommand]
    private void RemoveMember(string name)
    {
        if (TeamMemberList.Contains(name))
            TeamMemberList.Remove(name);
        AddProjectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddProject))]
    private async Task AddProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName)) return;

        var members = string.Join(", ", TeamMemberList);
        
        var project = new Project
        {
            EventId = _settings.ActiveEventId ?? 0,
            Name = NewProjectName.Trim(),
            Category = NewProjectCategory ?? "General",
            Description = NewProjectDescription?.Trim() ?? string.Empty,
            TeamMembers = members,
            StandNumber = NewProjectStand?.Trim() ?? string.Empty,
            GithubLink = NewProjectGithub?.Trim() ?? string.Empty,
            VideoLink = NewProjectVideo?.Trim() ?? string.Empty,
            TechStack = NewProjectTechStack?.Trim() ?? string.Empty,
            ProjectCode = await _projects.GenerateUniqueCodeAsync(_settings.ActiveEventId ?? 0),
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        var result = await _projects.CreateAsync(project);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        // Reset form
        NewProjectName = string.Empty;
        NewProjectDescription = string.Empty;
        NewProjectCategory = string.Empty;
        NewProjectStand = string.Empty;
        NewProjectGithub = string.Empty;
        NewProjectVideo = string.Empty;
        NewProjectTechStack = string.Empty;
        TeamMemberList.Clear();

        IsCreatingProject = false;

        if (_settings.ActiveEventId.HasValue)
            await _bootstrap.ExecuteAsync(_settings.ActiveEventId.Value);

        await LoadProjectsAsync();
    }

    private bool CanAddProject() => !string.IsNullOrWhiteSpace(NewProjectName) && !string.IsNullOrWhiteSpace(NewProjectCategory);

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

        if (_settings.ActiveEventId.HasValue)
            await _bootstrap.ExecuteAsync(_settings.ActiveEventId.Value);

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
