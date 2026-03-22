using SQLite;

namespace Nodus.Judge.Domain.Entities;

[Table("local_projects")]
public class LocalProject
{
    [PrimaryKey]
    public int    RemoteId    { get; set; }   // Admin Project.Id

    public int    EventId     { get; set; }   // FK → LocalEvent.RemoteId

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Objetivos   { get; set; } = string.Empty;
    public string Category    { get; set; } = string.Empty;
    public string TeamMembers { get; set; } = string.Empty;
    public int    SortOrder   { get; set; }
    public int    SequenceNumber { get; set; }

    /// <summary>Short mnemonic code (e.g. PROJ-001) used for QR-based project navigation.</summary>
    public string ProjectCode { get; set; } = string.Empty;

    /// <summary>Physical stand identifier (e.g. A-12) shown to the judge.</summary>
    public string StandNumber { get; set; } = string.Empty;

    /// <summary>Optional link to source code.</summary>
    public string GithubLink { get; set; } = string.Empty;

    /// <summary>Optional link to a pitch video.</summary>
    public string VideoLink  { get; set; } = string.Empty;

    /// <summary>Technologies used in the project (comma-separated).</summary>
    public string TechStack  { get; set; } = string.Empty;

    public string SyncedAt    { get; set; } = DateTime.UtcNow.ToString("O");
}
