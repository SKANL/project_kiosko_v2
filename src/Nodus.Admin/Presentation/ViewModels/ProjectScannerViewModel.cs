using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Controls;

namespace Nodus.Admin.Presentation.ViewModels;

public partial class ProjectScannerViewModel : BaseViewModel
{
    private readonly IProjectRepository _projects;
    private readonly IAppSettingsService _settings;
    private readonly HttpClient _http;

    [ObservableProperty] private string _statusMessage = "Esperando QR...";
    [ObservableProperty] private Color _statusColor = Colors.Gray;
    private bool _isProcessing = false;

    public ProjectScannerViewModel(IProjectRepository projects, IAppSettingsService settings)
    {
        _projects = projects;
        _settings = settings;
        _http = new HttpClient();
        Title = "Escanear Registro";
    }

    public async void ProcessQrResult(string qrValue)
    {
        if (_isProcessing) return;
        _isProcessing = true;
        IsBusy = true;

        try
        {
            // Expected format: nodus://vote?pid=PROJ-XYZ
            string? pid = null;
            if (qrValue.StartsWith("nodus://vote?pid="))
            {
                pid = qrValue.Replace("nodus://vote?pid=", "");
            }
            else if (qrValue.Contains("pid="))
            {
                // Fallback for messy scans
                var uri = new Uri(qrValue.Replace("nodus://", "http://"));
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                pid = query["pid"];
            }

            if (string.IsNullOrEmpty(pid))
            {
                StatusMessage = "QR no reconocido como pase de Nodus.";
                StatusColor = Colors.Red;
                _isProcessing = false;
                IsBusy = false;
                return;
            }

            StatusMessage = $"Procesando {pid}...";
            StatusColor = (Color)Microsoft.Maui.Controls.Application.Current!.Resources["NodusAccent"];

            var activeEventId = _settings.ActiveEventId ?? 0;
            if (activeEventId == 0)
            {
                StatusMessage = "No hay un evento activo seleccionado.";
                StatusColor = Colors.Red;
                _isProcessing = false;
                IsBusy = false;
                return;
            }

            // Check if exists locally
            var projects = await _projects.GetByEventAsync(activeEventId);
            if (projects.IsOk && projects.Value!.Any(p => p.ProjectCode == pid))
            {
                StatusMessage = "Este proyecto ya está registrado localmente.";
                StatusColor = Colors.Orange;
                await Task.Delay(2000);
                await Shell.Current.GoToAsync("..");
                return;
            }

            // Fetch from cloud
            var cloudUrl = $"{_settings.CloudApiUrl}/api/public/projects/{pid}";
            var response = await _http.GetAsync(cloudUrl);

            if (response.IsSuccessStatusCode)
            {
                var cloudProject = await response.Content.ReadFromJsonAsync<JsonElement>();
                
                var newProject = new Project
                {
                    EventId = activeEventId,
                    Name = cloudProject.GetProperty("name").GetString() ?? "Sin nombre",
                    Category = cloudProject.GetProperty("category").GetString() ?? "General",
                    Description = cloudProject.GetProperty("description").GetString() ?? "",
                    GithubLink = cloudProject.GetProperty("githubLink").GetString() ?? "",
                    VideoLink = cloudProject.GetProperty("videoLink").GetString() ?? "", // New field!
                    TeamMembers = cloudProject.GetProperty("teamMembers").GetString() ?? "",
                    ProjectCode = pid,
                    EditToken = cloudProject.TryGetProperty("editToken", out var et) ? et.GetString() ?? "" : "",
                    CreatedAt = DateTime.UtcNow.ToString("O")
                };

                await _projects.CreateAsync(newProject);
                
                StatusMessage = $"¡Proyecto '{newProject.Name}' registrado con éxito!";
                StatusColor = Colors.Green;
                
                await Task.Delay(2000);
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                StatusMessage = "No se encontró el proyecto en la nube.";
                StatusColor = Colors.Red;
                _isProcessing = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusColor = Colors.Red;
            _isProcessing = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
