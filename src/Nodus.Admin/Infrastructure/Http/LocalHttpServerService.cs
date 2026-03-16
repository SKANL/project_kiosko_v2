using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Entities;

namespace Nodus.Admin.Infrastructure.Http;

// ── Request / Response DTOs (matches Nodus.Web DTOs exactly) ──────────────

public sealed class EventInfoResponse
{
    public string   EventId     { get; set; } = string.Empty;
    public string   Name        { get; set; } = string.Empty;
    public string   Institution { get; set; } = string.Empty;
    public string[] Categories  { get; set; } = [];
    public int      MaxProjects { get; set; } = 100;
    public bool     IsOpen      { get; set; }
}

public sealed class RegisterProjectRequest
{
    public string  EventId     { get; set; } = string.Empty;
    public string  Name        { get; set; } = string.Empty;
    public string  Category    { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GithubLink  { get; set; }
    public string? TeamMembers { get; set; }
}

public sealed class RegisterProjectResponse
{
    public string  ProjectId   { get; set; } = string.Empty;
    public string  ProjectName { get; set; } = string.Empty;
    public string  QrPayload   { get; set; } = string.Empty;
    public string? StandNumber { get; set; }
    public string  EditToken   { get; set; } = string.Empty;
    public string  EditUrl     { get; set; } = string.Empty;
}

public sealed class ProjectEditResponse
{
    public string  EventId     { get; set; } = string.Empty;
    public string  ProjectId   { get; set; } = string.Empty;
    public string  Name        { get; set; } = string.Empty;
    public string  Category    { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GithubLink  { get; set; }
    public string? TeamMembers { get; set; }
    public bool    IsEventOpen { get; set; }
}

public sealed class UpdateProjectRequest
{
    public string  Name        { get; set; } = string.Empty;
    public string  Category    { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GithubLink  { get; set; }
    public string? TeamMembers { get; set; }
}

// ── Service Interface ─────────────────────────────────────────────────────

/// <summary>
/// Embedded Kestrel HTTP server running on port 5000.
/// Serves the local Admin API consumed by Nodus.Web (hosted on Vercel).
/// Decision #52 variant: Nodus.Web is hosted externally; only APIs are local.
/// </summary>
public interface ILocalHttpServerService
{
    bool IsRunning { get; }
    string LocalUrl { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}

// ── Service Implementation ────────────────────────────────────────────────

public sealed class LocalHttpServerService : ILocalHttpServerService, IAsyncDisposable
{
    private readonly IEventRepository    _events;
    private readonly IProjectRepository  _projects;
    private readonly IAppSettingsService _settings;

    private WebApplication? _app;
    private bool _running;

    private const int Port = 5000;
    // Vercel deployment domain for CORS
    private const string VercelOrigin = "https://nodus-web.vercel.app";

    public bool   IsRunning { get; private set; }
    public string LocalUrl  { get; private set; } = string.Empty;

    public LocalHttpServerService(
        IEventRepository    events,
        IProjectRepository  projects,
        IAppSettingsService settings)
    {
        _events   = events;
        _projects = projects;
        _settings = settings;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_running) return;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        builder.Services.AddCors(opts =>
            opts.AddDefaultPolicy(p =>
                p.WithOrigins(VercelOrigin, "http://localhost:3000")
                 .AllowAnyHeader()
                 .AllowAnyMethod()));

        _app = builder.Build();
        _app.UseCors();

        MapEndpoints(_app);

        // Fire-and-forget so it doesn't block the MAUI UI thread
        _ = _app.StartAsync(ct);

        // Compute local URL for QR generation
        var localIp = GetLocalIpAddress();
        LocalUrl = $"{localIp}:{Port}";
        IsRunning = _running = true;
    }

    public async Task StopAsync()
    {
        if (_app is not null) await _app.StopAsync();
        IsRunning = _running = false;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ── Endpoint Mapping ─────────────────────────────────────────────────

    private void MapEndpoints(WebApplication app)
    {
        var json = new JsonSerializerOptions
        {
            PropertyNamingPolicy      = JsonNamingPolicy.CamelCase,
            WriteIndented             = false,
            DefaultIgnoreCondition    = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // GET /api/event/current — returns active event info for category dropdown (Decision #19)
        app.MapGet("/api/event/current", async () =>
        {
            var eventId = _settings.ActiveEventId;
            if (eventId is null) return Results.NotFound(new { error = "No active event." });

            var result = await _events.GetByIdAsync(eventId.Value);
            if (result.IsFail || result.Value is null)
                return Results.NotFound(new { error = result.Error });

            var evt = result.Value;
            var isClosed = !string.IsNullOrEmpty(evt.FinishedAt);

            // Parse categories from RubricJson or fallback
            string[] categories = ["Software", "Hardware", "Social"];
            if (!string.IsNullOrEmpty(evt.RubricJson))
            {
                try
                {
                    // Categories stored separately in settings — use a sensible fallback
                    // TODO: add a dedicated Categories field to NodusEvent in a future pass
                }
                catch { /* use fallback */ }
            }

            var projectsResult = await _projects.GetByEventAsync(eventId.Value);
            var projectCount   = projectsResult.IsOk ? projectsResult.Value!.Count : 0;

            return Results.Json(new EventInfoResponse
            {
                EventId     = evt.Id.ToString(),
                Name        = evt.Name,
                Institution = evt.Institution,
                Categories  = categories,
                MaxProjects = 100,
                IsOpen      = !isClosed
            }, json);
        });

        // POST /api/projects — student registers a new project
        app.MapPost("/api/projects", async (RegisterProjectRequest req) =>
        {
            var eventId = _settings.ActiveEventId;
            if (eventId is null) return Results.BadRequest(new { error = "No active event." });

            var evtResult = await _events.GetByIdAsync(eventId.Value);
            if (evtResult.IsFail || evtResult.Value is null) return Results.NotFound();
            if (!string.IsNullOrEmpty(evtResult.Value.FinishedAt))
                return Results.StatusCode(410); // Gone — event closed

            // Validate
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length < 3)
                return Results.BadRequest(new { error = "Project name too short." });

            // Check maxProjects (Decision #49) — hardcoded 100 for now
            var existingResult = await _projects.GetByEventAsync(eventId.Value);
            if (existingResult.IsOk && existingResult.Value!.Count >= 100)
                return Results.Conflict(new { error = "Maximum projects reached." }); // 409

            // Assign next PROJ-{3} code
            var projectCode = await GenerateUniqueCodeAsync(eventId.Value, existingResult.IsOk ? existingResult.Value! : []);

            var editToken = Guid.NewGuid().ToString("N");
            var project   = new Project
            {
                EventId     = eventId.Value,
                Name        = req.Name.Trim(),
                Category    = req.Category.Trim(),
                Description = req.Description?.Trim(),
                GithubLink  = req.GithubLink?.Trim(),
                TeamMembers = req.TeamMembers?.Trim(),
                ProjectCode = projectCode,
                EditToken   = editToken
            };

            var createResult = await _projects.CreateAsync(project);
            if (createResult.IsFail)
                return Results.Problem(createResult.Error, statusCode: 500);

            var localIp  = LocalUrl.Split(':')[0];
            var editUrl  = $"https://nodus-web.vercel.app/edit?token={editToken}&server={LocalUrl}";
            var qrPayload = $"nodus://vote?pid={projectCode}";

            return Results.Ok(new RegisterProjectResponse
            {
                ProjectId   = projectCode,
                ProjectName = project.Name,
                QrPayload   = qrPayload,
                StandNumber = project.StandNumber,
                EditToken   = editToken,
                EditUrl     = editUrl
            });
        });

        // GET /api/projects/edit/{token} — pre-fills edit form
        app.MapGet("/api/projects/edit/{token}", async (string token) =>
        {
            var eventId = _settings.ActiveEventId;
            if (eventId is null) return Results.NotFound();

            var evtResult = await _events.GetByIdAsync(eventId.Value);
            if (evtResult.IsFail || evtResult.Value is null) return Results.NotFound();

            var projectsResult = await _projects.GetByEventAsync(eventId.Value);
            if (projectsResult.IsFail) return Results.NotFound();

            var project = projectsResult.Value!.FirstOrDefault(p => p.EditToken == token);
            if (project is null) return Results.NotFound();

            var isClosed = !string.IsNullOrEmpty(evtResult.Value.FinishedAt);

            return Results.Json(new ProjectEditResponse
            {
                EventId     = evtResult.Value.Id.ToString(),
                ProjectId   = project.ProjectCode,
                Name        = project.Name,
                Category    = project.Category,
                Description = project.Description,
                GithubLink  = project.GithubLink,
                TeamMembers = project.TeamMembers,
                IsEventOpen = !isClosed
            }, json);
        });

        // PUT /api/projects/edit/{token} — update registration
        app.MapPut("/api/projects/edit/{token}", async (string token, UpdateProjectRequest req) =>
        {
            var eventId = _settings.ActiveEventId;
            if (eventId is null) return Results.NotFound();

            var evtResult = await _events.GetByIdAsync(eventId.Value);
            if (evtResult.IsFail || evtResult.Value is null) return Results.NotFound();

            // Block edit if event is closed
            if (!string.IsNullOrEmpty(evtResult.Value.FinishedAt))
                return Results.StatusCode(410); // 410 Gone

            var projectsResult = await _projects.GetByEventAsync(eventId.Value);
            if (projectsResult.IsFail) return Results.NotFound();

            var project = projectsResult.Value!.FirstOrDefault(p => p.EditToken == token);
            if (project is null) return Results.NotFound();

            project.Name        = req.Name.Trim();
            project.Category    = req.Category.Trim();
            project.Description = req.Description?.Trim();
            project.GithubLink  = req.GithubLink?.Trim();
            project.TeamMembers = req.TeamMembers?.Trim();

            var updateResult = await _projects.UpdateAsync(project);
            return updateResult.IsOk ? Results.NoContent() : Results.Problem(updateResult.Error, statusCode: 500);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<string> GenerateUniqueCodeAsync(int eventId, List<Project> existing)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var used = existing.Select(p => p.ProjectCode).ToHashSet();
        while (true)
        {
            var code = new string(Enumerable.Range(0, 3)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
                .ToArray());
            var full = $"PROJ-{code}";
            if (!used.Contains(full)) return full;
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host    = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var address = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return address?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }
}
