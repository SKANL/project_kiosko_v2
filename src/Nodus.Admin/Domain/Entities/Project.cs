using SQLite;

namespace Nodus.Admin.Domain.Entities;

[Table("projects")]
public class Project
{
    [PrimaryKey, AutoIncrement]
    public int    Id          { get; set; }

    public int    EventId     { get; set; }   // FK → NodusEvent.Id

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Objetivos   { get; set; } = string.Empty;
    public string Category    { get; set; } = string.Empty;
    public string TeamMembers { get; set; } = string.Empty;  // Comma-separated

    /// <summary>Display order on the judging form.</summary>
    public int    SortOrder   { get; set; }

    /// <summary>
    /// Monotonic event-local cursor used by judges to request only newly registered projects.
    /// </summary>
    public int    SequenceNumber { get; set; }

    /// <summary>Short mnemonic code (e.g. PROJ-001) displayed at the project stand QR code.</summary>
    public string ProjectCode { get; set; } = string.Empty;
    
    /// <summary>Physical stand identifier (e.g. A-12, Stand 5).</summary>
    public string StandNumber { get; set; } = string.Empty;

    /// <summary>Optional link to project source code or repository.</summary>
    public string GithubLink { get; set; } = string.Empty;

    /// <summary>Technologies used in the project (comma-separated, e.g. "C#, .NET, SQLite").</summary>
    public string TechStack  { get; set; } = string.Empty;

    /// <summary>Optional link to a pitch video.</summary>
    public string VideoLink  { get; set; } = string.Empty;

    /// <summary>Optional link to a speech video.</summary>
    public string SpeechVideoLink { get; set; } = string.Empty;

    /// <summary>
    /// System-generated UUID allowing a student to reopen the registration form to correct typos.
    /// Valid until the Admin closes the event (Decision #26, Doc 14 §1, Doc 15 §2).
    /// Generated automatically on project creation.
    /// </summary>
    public string EditToken { get; set; } = Guid.NewGuid().ToString("N");

    public string CreatedAt   { get; set; } = DateTime.UtcNow.ToString("O");
}
