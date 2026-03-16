using SQLite;

namespace Nodus.Judge.Domain.Entities;

/// <summary>
/// Judge-side copy of an event — named LocalEvent to avoid collision with System.EventHandler.
/// Received via GATT bootstrap from Admin (byte prefix 0x03).
/// </summary>
[Table("local_events")]
public class LocalEvent
{
    [PrimaryKey]
    public int    RemoteId    { get; set; }   // Admin NodusEvent.Id

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    public string Institution { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Date        { get; set; } = string.Empty;

    /// <summary>Admin public key (Base64) for signature verification.</summary>
    public string AdminPublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of scoring rubric criteria received from Admin via bootstrap payload.
    /// Empty string means use the 5-criterion default rubric.
    /// </summary>
    public string RubricJson           { get; set; } = string.Empty;

    public int RubricVersion           { get; set; } = 1;

    /// <summary>
    /// Set by Admin when the event is closed (Finished state). ISO-8601 UTC timestamp.
    /// Empty when the event is still active.
    /// </summary>
    public string FinishedAt           { get; set; } = string.Empty;

    /// <summary>UTC deadline until which the Admin still accepts late-arriving votes.</summary>
    public string GraceEndsAt          { get; set; } = string.Empty;

    public string SyncedAt    { get; set; } = DateTime.UtcNow.ToString("O");
}
