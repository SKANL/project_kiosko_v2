using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;
using QRCoder;
using System.Collections.ObjectModel;
using Nodus.Admin.Application.UseCases.Events;

namespace Nodus.Admin.Presentation.ViewModels;

[QueryProperty(nameof(Project), "Project")]
public partial class ProjectDetailsViewModel : BaseViewModel
{
    private readonly IProjectRepository _projects;
    private readonly IEventRepository _events;
    private readonly IAppSettingsService _settings;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;

    public ProjectDetailsViewModel(
        IProjectRepository projects, 
        IEventRepository events, 
        IAppSettingsService settings,
        BuildBootstrapPayloadUseCase bootstrap)
    {
        _projects = projects;
        _events = events;
        _settings = settings;
        _bootstrap = bootstrap;
        Title = "Detalles del Proyecto";
    }

    [ObservableProperty]
    private Project? _project;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editCategory = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _editObjetivos = string.Empty;

    [ObservableProperty]
    private string _editStand = string.Empty;

    [ObservableProperty]
    private string _editGithub = string.Empty;

    [ObservableProperty]
    private string _editVideo = string.Empty;

    [ObservableProperty]
    private string _editMembers = string.Empty;

    [ObservableProperty]
    private string _editTechStack = string.Empty;

    public ObservableCollection<string> AvailableCategories { get; } = new();

    [ObservableProperty]
    private bool _isQrDialogVisible;

    [ObservableProperty]
    private ImageSource? _offlineQrSource;

    [ObservableProperty]
    private ImageSource? _onlineQrSource;

    [ObservableProperty]
    private string _offlineUrl = string.Empty;

    [ObservableProperty]
    private string _onlineUrl = string.Empty;

    [ObservableProperty]
    private bool _showOfflineQr = true;

    partial void OnProjectChanged(Project? value)
    {
        if (value != null)
        {
            LoadProjectData(value);
            _ = LoadCategoriesAsync();
        }
    }

    private void LoadProjectData(Project project)
    {
        EditName = project.Name;
        EditCategory = project.Category;
        EditDescription = project.Description;
        EditObjetivos = project.Objetivos;
        EditStand = project.StandNumber;
        EditGithub = project.GithubLink;
        EditVideo = project.VideoLink;
        EditMembers = project.TeamMembers;
        EditTechStack = project.TechStack;
    }

    private async Task LoadCategoriesAsync()
    {
        var eventId = _settings.ActiveEventId;
        if (!eventId.HasValue) return;

        var result = await _events.GetByIdAsync(eventId.Value);
        if (result.IsOk && result.Value != null && !string.IsNullOrEmpty(result.Value.Categories))
        {
            AvailableCategories.Clear();
            foreach (var c in result.Value.Categories.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                AvailableCategories.Add(c.Trim());
            }
        }
    }

    [RelayCommand]
    private void ToggleEdit()
    {
        if (!IsEditing)
        {
            if (Project != null) LoadProjectData(Project);
        }
        IsEditing = !IsEditing;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Project == null || string.IsNullOrWhiteSpace(EditName)) return;

        Project.Name = EditName.Trim();
        Project.Category = EditCategory.Trim();
        Project.Description = EditDescription.Trim();
        Project.Objetivos = EditObjetivos.Trim();
        Project.StandNumber = EditStand.Trim();
        Project.GithubLink = EditGithub.Trim();
        Project.VideoLink = EditVideo.Trim();
        Project.TeamMembers = EditMembers.Trim();
        Project.TechStack = EditTechStack.Trim();

        var result = await _projects.UpdateAsync(Project);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        if (_settings.ActiveEventId.HasValue)
            await _bootstrap.ExecuteAsync(_settings.ActiveEventId.Value);

        IsEditing = false;
        OnPropertyChanged(nameof(Project)); // Refresh labels
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        if (Project != null) LoadProjectData(Project);
    }

    [RelayCommand]
    private async Task ShowQrDialogAsync()
    {
        if (Project == null) return;

        IsBusy = true;
        try
        {
            // Offline URL: nodus://vote?pid={Code}
            OfflineUrl = $"nodus://vote?pid={Project.ProjectCode}";
            
            // Online URL: https://project-kiosko-v2.vercel.app/vote?pid={Code}&event=EVT-{Id}
            var eventIdStr = $"EVT-{Project.EventId:D3}";
            OnlineUrl = $"https://project-kiosko-v2.vercel.app/vote?pid={Project.ProjectCode}&event={eventIdStr}";

            await Task.Run(() =>
            {
                using var generator = new QRCodeGenerator();
                
                // Offline QR
                var offlineData = generator.CreateQrCode(OfflineUrl, QRCodeGenerator.ECCLevel.Q);
                var offlinePng = new PngByteQRCode(offlineData).GetGraphic(6);
                
                // Online QR
                var onlineData = generator.CreateQrCode(OnlineUrl, QRCodeGenerator.ECCLevel.M);
                var onlinePng = new PngByteQRCode(onlineData).GetGraphic(6);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OfflineQrSource = ImageSource.FromStream(() => new MemoryStream(offlinePng));
                    OnlineQrSource = ImageSource.FromStream(() => new MemoryStream(onlinePng));
                });
            });

            ShowOfflineQr = true;
            IsQrDialogVisible = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al generar QRs: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CloseQrDialog()
    {
        IsQrDialogVisible = false;
    }

    [RelayCommand]
    private void ToggleQrMode()
    {
        ShowOfflineQr = !ShowOfflineQr;
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Project == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Eliminar Proyecto",
            $"¿Estás seguro de que deseas eliminar \"{Project.Name}\"? Esta acción no se puede deshacer.",
            "Eliminar",
            "Cancelar");

        if (!confirm) return;

        var result = await _projects.DeleteAsync(Project.Id);
        if (result.IsFail)
        {
            ErrorMessage = result.Error!;
            HasError = true;
            return;
        }

        if (_settings.ActiveEventId.HasValue)
            await _bootstrap.ExecuteAsync(_settings.ActiveEventId.Value);

        await Shell.Current.GoToAsync("..");
    }
}
