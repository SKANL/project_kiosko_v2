using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Presentation.Views;
using Nodus.Admin.Application.UseCases.Events;
using Microsoft.Maui.Storage;

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
    private readonly IExcelExportService _excel;

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
    private string _newProjectSpeechVideo = string.Empty;

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

    private string _editSpeechVideo = string.Empty;
    public string EditSpeechVideo
    {
        get => _editSpeechVideo;
        set => SetProperty(ref _editSpeechVideo, value);
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

    public ManageProjectsViewModel(
        IProjectRepository projects,
        IEventRepository events,
        IAppSettingsService settings,
        BuildBootstrapPayloadUseCase bootstrap,
        IExcelExportService excel)
    {
        _projects = projects;
        _events = events;
        _settings = settings;
        _bootstrap = bootstrap;
        _excel = excel;
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
        NewProjectSpeechVideo = string.Empty;
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
        EditSpeechVideo = project.SpeechVideoLink;
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
        project.SpeechVideoLink = EditSpeechVideo.Trim();
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
    private async Task ExportProjectsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var eventId = _settings.ActiveEventId;
            if (!eventId.HasValue || eventId.Value <= 0)
            {
                await Shell.Current.DisplayAlertAsync("Exportar", "No hay un evento activo seleccionado.", "OK");
                return;
            }

            var outputDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var exportResult = await _excel.ExportProjectsAsync(eventId.Value, outputDir);
            if (exportResult.IsFail)
            {
                await Shell.Current.DisplayAlertAsync("Exportar", exportResult.Error ?? "No se pudo exportar.", "OK");
                return;
            }

            var filePath = exportResult.Value!;
            await Shell.Current.DisplayAlertAsync("Exportación completada", $"Archivo generado:\n{filePath}", "OK");
            if (File.Exists(filePath))
                await Launcher.TryOpenAsync(new Uri($"file:///{filePath.Replace('\\', '/')}"));
        });
    }

    [RelayCommand]
    private async Task ImportProjectsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var targetEventId = await SelectTargetEventAsync();
            if (!targetEventId.HasValue) return;

            var mode = await Shell.Current.DisplayActionSheetAsync(
                "Modo de importación",
                "Cancelar",
                null,
                "Reemplazar proyectos del evento",
                "Agregar sin borrar");

            if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "Cancelar", StringComparison.OrdinalIgnoreCase))
                return;

            bool replaceExisting = string.Equals(mode, "Reemplazar proyectos del evento", StringComparison.OrdinalIgnoreCase);

            var pickOptions = new PickOptions
            {
                PickerTitle = "Selecciona archivo de proyectos (.xlsx)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".xlsx" } },
                    { DevicePlatform.Android, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
                    { DevicePlatform.iOS, new[] { "org.openxmlformats.spreadsheetml.sheet" } },
                    { DevicePlatform.MacCatalyst, new[] { "org.openxmlformats.spreadsheetml.sheet" } }
                })
            };

            var file = await FilePicker.Default.PickAsync(pickOptions);
            if (file is null) return;

            var localPath = await EnsureLocalPathAsync(file);
            try
            {
                var importResult = await _excel.ImportProjectsAsync(localPath, targetEventId.Value, replaceExisting);
                if (importResult.IsFail)
                {
                    await Shell.Current.DisplayAlertAsync("Importar", importResult.Error ?? "No se pudo importar.", "OK");
                    return;
                }

                var data = importResult.Value!;
                await Shell.Current.DisplayAlertAsync(
                    "Importación completada",
                    $"Evento destino: {data.TargetEventId}\nImportados: {data.ImportedCount}\nOmitidos: {data.SkippedCount}",
                    "OK");

                if (_settings.ActiveEventId == targetEventId.Value)
                    await LoadProjectsAsync();

                await _bootstrap.ExecuteAsync(targetEventId.Value);
            }
            finally
            {
                if (!string.Equals(localPath, file.FullPath, StringComparison.OrdinalIgnoreCase) && File.Exists(localPath))
                {
                    try { File.Delete(localPath); } catch { }
                }
            }
        });
    }

    private async Task<int?> SelectTargetEventAsync()
    {
        var eventsResult = await _events.GetAllAsync();
        if (eventsResult.IsFail)
        {
            await Shell.Current.DisplayAlertAsync("Importar", eventsResult.Error ?? "No se pudieron cargar los eventos.", "OK");
            return null;
        }

        var allEvents = eventsResult.Value?
            .OrderByDescending(e => e.Id)
            .ToList() ?? new List<NodusEvent>();

        if (allEvents.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Importar", "No hay eventos disponibles.", "OK");
            return null;
        }

        var labels = allEvents
            .Select(e => $"#{e.Id} - {e.Name} ({e.Status})")
            .ToArray();

        var selected = await Shell.Current.DisplayActionSheetAsync(
            "Selecciona el evento destino",
            "Cancelar",
            null,
            labels);

        if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "Cancelar", StringComparison.OrdinalIgnoreCase))
            return null;

        var target = allEvents.FirstOrDefault(e => $"#{e.Id} - {e.Name} ({e.Status})" == selected);
        return target?.Id;
    }

    private static async Task<string> EnsureLocalPathAsync(FileResult file)
    {
        if (!string.IsNullOrWhiteSpace(file.FullPath) && File.Exists(file.FullPath))
            return file.FullPath;

        var tempPath = Path.Combine(FileSystem.CacheDirectory, $"import_projects_{Guid.NewGuid():N}.xlsx");
        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(tempPath);
        await source.CopyToAsync(target);
        return tempPath;
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
            SpeechVideoLink = NewProjectSpeechVideo?.Trim() ?? string.Empty,
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
        NewProjectSpeechVideo = string.Empty;
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
