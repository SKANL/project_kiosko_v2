namespace Nodus.Web.DTOs;

// ── Request / Response DTOs ───────────────────────────────────────────────

/// <summary>
/// Returned by GET /api/event/current for the Admin's active event.
/// Used to populate the category dropdown and event name header.
/// </summary>
public sealed record EventInfoDto(
    string EventId,
    string Name,
    string Institution,
    string[] Categories,
    int MaxProjects,
    bool IsOpen);

/// <summary>
/// Sent to POST /api/projects when a student submits the registration form.
/// </summary>
public sealed class RegisterProjectRequest
{
    public string EventId    { get; set; } = string.Empty;
    public string Name       { get; set; } = string.Empty;
    public string Category   { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GithubLink  { get; set; }
    public string? TeamMembers { get; set; }
}

/// <summary>
/// Returned by POST /api/projects on successful registration.
/// Contains the "boarding pass" data shown to the student.
/// </summary>
public sealed record RegisterProjectResponse(
    string ProjectId,       // e.g. "PROJ-A3X"
    string ProjectName,
    string QrPayload,       // "nodus://vote?pid=PROJ-A3X"
    string? StandNumber,    // null → "Unassigned"
    string EditToken,       // UUID for edit link
    string EditUrl);        // full edit URL with token

/// <summary>
/// Returned by GET /api/projects/edit/{token} — pre-fills the edit form.
/// </summary>
public sealed record ProjectEditDto(
    string EventId,
    string ProjectId,
    string Name,
    string Category,
    string? Description,
    string? GithubLink,
    string? TeamMembers,
    bool   IsEventOpen);

/// <summary>
/// Sent to PUT /api/projects/edit/{token} to update an existing registration.
/// </summary>
public sealed class UpdateProjectRequest
{
    public string  Name        { get; set; } = string.Empty;
    public string  Category    { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GithubLink  { get; set; }
    public string? TeamMembers { get; set; }
}
