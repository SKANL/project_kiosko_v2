using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Services;

public sealed class CloudSyncService : ICloudSyncService
{
    private readonly IEventRepository _events;
    private readonly IProjectRepository _projects;
    private readonly IJudgeRepository _judges;
    private readonly IVoteRepository _votes;
    private readonly IAppSettingsService _settings;
    private readonly HttpClient _http;
    // URL read from settings — change it in AppSettingsService, applies everywhere
    public string CloudApiUrl => _settings.CloudApiUrl;
    
    // ── DTOs for Cloud Sync ───────────────────────────────────────────────
    private sealed class CloudEventDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("institution")] public string Institution { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("rubricJson")] public string RubricJson { get; set; } = string.Empty;
        [JsonPropertyName("rubricVersion")] public int RubricVersion { get; set; } = 1;
        [JsonPropertyName("maxProjects")] public int MaxProjects { get; set; } = 100;
        [JsonPropertyName("categories")] public string[] Categories { get; set; } = [];
        [JsonPropertyName("finishedAt")] public DateTime? FinishedAt { get; set; }
        [JsonPropertyName("syncedAtUtc")] public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class CloudProjectDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("githubLink")] public string? GithubLink { get; set; }
        [JsonPropertyName("videoLink")] public string? VideoLink { get; set; }
        [JsonPropertyName("speechVideoLink")] public string? SpeechVideoLink { get; set; }
        [JsonPropertyName("teamMembers")] public string? TeamMembers { get; set; }
        [JsonPropertyName("standNumber")] public string? StandNumber { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("editToken")] public string EditToken { get; set; } = string.Empty;
        [JsonPropertyName("syncedAtUtc")] public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class CloudJudgeDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("publicKeyBase64")] public string PublicKeyBase64 { get; set; } = string.Empty;
        [JsonPropertyName("isBlocked")] public bool IsBlocked { get; set; }
        [JsonPropertyName("registeredAtUtc")] public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
    }

    private sealed class CloudVoteDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("projectId")] public string ProjectId { get; set; } = string.Empty;
        [JsonPropertyName("judgeId")] public string JudgeId { get; set; } = string.Empty;
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("scoresJson")] public string ScoresJson { get; set; } = "{}";
        [JsonPropertyName("weightsJson")] public string WeightsJson { get; set; } = "{}";
        [JsonPropertyName("comment")] public string? Comment { get; set; }
        [JsonPropertyName("signature")] public string Signature { get; set; } = string.Empty;
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
        [JsonPropertyName("timestampUtc")] public long TimestampUtc { get; set; }
        [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; }
        [JsonPropertyName("syncedAtUtc")] public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("remotePhotoUrl")] public string? RemotePhotoUrl { get; set; }
        [JsonPropertyName("remoteAudioUrl")] public string? RemoteAudioUrl { get; set; }
    }

    private sealed class SyncEventRequestDto
    {
        [JsonPropertyName("event")] public CloudEventDto Event { get; set; } = new();
        [JsonPropertyName("projects")] public List<CloudProjectDto> Projects { get; set; } = new();
        [JsonPropertyName("judges")] public List<CloudJudgeDto> Judges { get; set; } = new();
    }

    private sealed class SyncVotesRequestDto
    {
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("votes")] public List<CloudVoteDto> Votes { get; set; } = new();
    }
    
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public CloudSyncService(
        IEventRepository events, 
        IProjectRepository projects, 
        IJudgeRepository judges,
        IVoteRepository votes,
        IAppSettingsService settings)
    {
        _events = events;
        _projects = projects;
        _judges = judges;
        _votes = votes;
        _settings = settings;
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
                    await PushNewVotesAsync();
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

            var eventPayload = new CloudEventDto
            {
                Id = cloudEventId,
                Name = evt.Name,
                Institution = evt.Institution,
                Description = evt.Description,
                Categories = evt.Categories.Split(';', StringSplitOptions.RemoveEmptyEntries),
                MaxProjects = evt.MaxProjects,
                RubricJson = evt.RubricJson,
                RubricVersion = evt.RubricVersion,
                FinishedAt = string.IsNullOrEmpty(evt.FinishedAt) ? (DateTime?)null : DateTime.Parse(evt.FinishedAt),
                SyncedAtUtc = DateTime.UtcNow
            };

            // Fetch local projects
            var projectsResult = await _projects.GetByEventAsync(eventId);
            var projectPayloads = (projectsResult.Value ?? new List<Project>())
                .Select(p => new CloudProjectDto
                {
                    Id = p.ProjectCode,
                    EventId = cloudEventId,
                    Name = p.Name,
                    Category = p.Category,
                    Description = p.Description,
                    GithubLink = p.GithubLink,
                    VideoLink = p.VideoLink,
                    SpeechVideoLink = p.SpeechVideoLink,
                    TeamMembers = p.TeamMembers,
                    StandNumber = p.StandNumber,
                    SortOrder = p.SortOrder,
                    EditToken = p.EditToken,
                    SyncedAtUtc = DateTime.UtcNow
                }).ToList();

            // Fetch local judges
            var judgesResult = await _judges.GetByEventAsync(eventId);
            var judgePayloads = (judgesResult.Value ?? new List<Judge>())
                .Select(j => new CloudJudgeDto
                {
                    Id = j.PublicKeyBase64, // Use public key as ID in cloud (identifiable)
                    EventId = cloudEventId,
                    Name = j.Name,
                    PublicKeyBase64 = j.PublicKeyBase64,
                    IsBlocked = j.IsBlocked,
                    RegisteredAtUtc = DateTime.Parse(j.CreatedAt)
                }).ToList();

            var syncRequest = new SyncEventRequestDto
            {
                Event = eventPayload,
                Projects = projectPayloads,
                Judges = judgePayloads
            };

            var syncUrl = $"{CloudApiUrl}/api/sync/event";
            var response = await _http.PostAsJsonAsync(syncUrl, syncRequest);
            
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
    private async Task PushNewVotesAsync()
    {
        var activeEventId = _settings.ActiveEventId ?? 0;
        if (activeEventId <= 0) return;

        var cloudEventId = $"EVT-{activeEventId:D3}";
        
        // Fetch all votes for active event
        var votesResult = await _votes.GetByEventAsync(activeEventId);
        if (votesResult.IsFail || votesResult.Value == null) return;

        // Filter for those not yet in cloud (or we could just push all, but filtering is better)
        var pendingVotes = votesResult.Value
            .Where(v => v.SyncStatus != Nodus.Admin.Domain.Enums.SyncStatus.ReachedCloud)
            .ToList();

        if (!pendingVotes.Any()) return;

        // Group into batches of 50
        foreach (var batch in pendingVotes.Chunk(50))
        {
            var votePayloads = new List<CloudVoteDto>();
            foreach (var v in batch)
            {
                var project = await _projects.GetByIdAsync(v.ProjectId);
                var judge = await _judges.GetByIdAsync(v.JudgeId);
                
                if (project.Value == null || judge.Value == null) continue;

                votePayloads.Add(new CloudVoteDto
                {
                    Id = v.PacketId, // Use packet id as unique vote id
                    EventId = cloudEventId,
                    ProjectId = project.Value.ProjectCode,
                    JudgeId = judge.Value.PublicKeyBase64,
                    Version = v.Version,
                    ScoresJson = v.ScoresJson,
                    WeightsJson = "{}", // To be populated if needed, rubric is stable anyway
                    Comment = null, // Admin doesn't handle comments yet?
                    Signature = v.SignatureBase64,
                    Nonce = v.PacketId, // Using packetId as nonce if not explicit
                    TimestampUtc = DateTimeOffset.Parse(v.ReceivedAt).ToUnixTimeSeconds(),
                    CreatedAtUtc = DateTime.Parse(v.CreatedAt),
                    SyncedAtUtc = DateTime.UtcNow
                });
            }

            if (!votePayloads.Any()) continue;

            var syncRequest = new SyncVotesRequestDto
            {
                EventId = cloudEventId,
                Votes = votePayloads
            };

            var syncUrl = $"{CloudApiUrl}/api/sync/votes";
            var response = await _http.PostAsJsonAsync(syncUrl, syncRequest);

            if (response.IsSuccessStatusCode)
            {
                // Mark locally as synched
                foreach (var v in batch)
                {
                    v.SyncStatus = Nodus.Admin.Domain.Enums.SyncStatus.ReachedCloud;
                    v.ScoreStatus = Nodus.Admin.Domain.Enums.SyncStatus.ReachedCloud;
                    await _votes.UpsertAsync(v);
                }
            }
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
            var url = $"{CloudApiUrl}/api/public/projects?eventId={cloudEventId}";
            
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
                    VideoLink = cp.VideoLink ?? string.Empty,
                    SpeechVideoLink = cp.SpeechVideoLink ?? string.Empty,
                    CreatedAt = cp.SyncedAtUtc.ToString("O")
                };

                await _projects.CreateAsync(newProject);
            }
        }
    }
}
