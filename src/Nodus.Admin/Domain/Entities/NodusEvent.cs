using Nodus.Admin.Domain.Enums;
using SQLite;

namespace Nodus.Admin.Domain.Entities;

/// <summary>
/// Represents a judging event (e.g., "Innovation Fair 2025").
/// Named NodusEvent to avoid collision with System.EventHandler.
/// </summary>
[Table("events")]
public class NodusEvent
{
    [PrimaryKey, AutoIncrement]
    public int    Id          { get; set; }

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    [NotNull]
    public string Institution { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string Categories  { get; set; } = "Software;Hardware;Social";
    public int    MaxProjects { get; set; } = 100;

    /// <summary>ISO 8601 date string — SQLite stores as TEXT.</summary>
    public string Date        { get; set; } = string.Empty;

    public EventStatus Status { get; set; } = EventStatus.Draft;

    /// <summary>Ed25519 public key (Base64) used to sign all votes in this event.</summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>Ed25519 private key (Base64, encrypted at rest).</summary>
    public string EncryptedPrivateKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of scoring rubric criteria (Decision #46).
    /// Format: [{"id":"...","label":"...","weight":1.0,"min":0,"max":10,"step":0.5},...]
    /// Empty string or empty array means the 5-criterion default rubric is used.
    /// </summary>
    public string RubricJson  { get; set; } = string.Empty;

    /// <summary>Monotonic rubric version used by judges to detect rubric changes.</summary>
    public int RubricVersion { get; set; } = 1;

    /// <summary>UTC timestamp when event was closed (Status set to Finished). Null until closed.</summary>
    public string FinishedAt  { get; set; } = string.Empty;

    /// <summary>UTC timestamp until which judges can still sync final votes after closure.</summary>
    public string GraceEndsAt { get; set; } = string.Empty;

    /// <summary>Per-event shared AES key used for encrypted judge registration payloads.</summary>
    public string SharedKeyBase64 { get; set; } = string.Empty;

    /// <summary>Encrypted Judge Access QR payload shown to judges during onboarding.</summary>
    public string AccessQrPayload { get; set; } = string.Empty;

    public string CreatedAt   { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt   { get; set; } = DateTime.UtcNow.ToString("O");
}
