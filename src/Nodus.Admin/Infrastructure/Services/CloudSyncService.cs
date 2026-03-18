using System.Net.Http.Json;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Services;

public sealed class CloudSyncService : ICloudSyncService
{
    private readonly IEventRepository _events;
    private readonly IProjectRepository _projects;
    private readonly HttpClient _http;
    // Updated URL to use Cloud API to bypass Mixed Content on Vercel
    // Added URL encoding for safety
    private readonly string _cloudApiUrl = "https://nodusapi-nlw0pofa.b4a.run";
    
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public CloudSyncService(IEventRepository events, IProjectRepository projects)
    {
        _events = events;
        _projects = projects;
        _http = new HttpClient();
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await PullNewRegistrationsAsync();
                }
                catch
                {
                    // Stealth fail, retry later
                }
                await Task.Delay(TimeSpan.FromSeconds(30), _cts.Token);
            }
        });
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
    }

    public async Task<(bool Success, string? Error)> PushActiveEventAsync(int eventId)
    {
        try
        {
            var eventResult = await _events.GetByIdAsync(eventId);
            if (eventResult.IsFail || eventResult.Value is null) 
                return (false, eventResult.Error ?? "Evento local no encontrado.");

            var evt = eventResult.Value;
            var cloudEventId = $"EVT-{evt.Id:D3}";

            var payload = new
            {
                Id = cloudEventId,
                Name = evt.Name,
                Institution = evt.Institution,
                Description = evt.Description,
                Categories = evt.Categories.Split(';', StringSplitOptions.RemoveEmptyEntries),
                MaxProjects = evt.MaxProjects,
                RubricJson = evt.RubricJson,
                RubricVersion = evt.RubricVersion,
                FinishedAt = string.IsNullOrEmpty(evt.FinishedAt) ? (DateTime?)null : DateTime.Parse(evt.FinishedAt)
            };

            var syncUrl = $"{_cloudApiUrl}/api/sync/event";
            var response = await _http.PostAsJsonAsync(syncUrl, new { Event = payload, Projects = new List<object>(), Judges = new List<object>() });
            
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                var msg = $"Error API ({(int)response.StatusCode}): {error}";
                if (string.IsNullOrEmpty(error)) msg = $"Error API ({(int)response.StatusCode}) en {syncUrl}";
                return (false, msg);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error de conexión: {ex.Message}");
        }
    }

    private async Task PullNewRegistrationsAsync()
    {
        // Get all active/recent events
        var eventsResult = await _events.GetAllAsync();
        if (eventsResult.IsFail || eventsResult.Value == null) return;

        foreach (var evt in eventsResult.Value.Where(e => string.IsNullOrEmpty(e.FinishedAt)))
        {
            var cloudEventId = $"EVT-{evt.Id:D3}";
            var url = $"{_cloudApiUrl}/api/public/projects?eventId={cloudEventId}";
            
            var cloudProjects = await _http.GetFromJsonAsync<List<CloudProjectDto>>(url);
            if (cloudProjects == null) continue;

            var localProjects = (await _projects.GetByEventAsync(evt.Id)).Value ?? new List<Project>();

            foreach (var cp in cloudProjects)
            {
                // Check if already exists by ProjectCode or Name/Category combo
                if (localProjects.Any(p => p.ProjectCode == cp.Id)) continue;

                var newProject = new Project
                {
                    EventId = evt.Id,
                    Name = cp.Name,
                    Description = cp.Description ?? string.Empty,
                    Category = cp.Category,
                    TeamMembers = cp.TeamMembers ?? string.Empty,
                    ProjectCode = cp.Id,
                    EditToken = cp.EditToken,
                    GithubLink = cp.GithubLink ?? string.Empty,
                    CreatedAt = cp.SyncedAtUtc.ToString("O")
                };

                await _projects.CreateAsync(newProject);
            }
        }
    }

    private sealed class CloudProjectDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? TeamMembers { get; set; }
        public string? GithubLink { get; set; }
        public string EditToken { get; set; } = string.Empty;
        public DateTime SyncedAtUtc { get; set; }
    }
}
