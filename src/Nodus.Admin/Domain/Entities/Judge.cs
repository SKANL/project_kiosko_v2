using SQLite;

namespace Nodus.Admin.Domain.Entities;

[Table("judges")]
public class Judge
{
    [PrimaryKey, AutoIncrement]
    public int    Id          { get; set; }

    public int    EventId     { get; set; }   // FK → NodusEvent.Id

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    public string Institution { get; set; } = string.Empty;

    /// <summary>Ed25519 public key (Base64) assigned to this judge.</summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>Short PIN used during onboarding bootstrap (hashed, never stored plain).</summary>
    public string PinHash     { get; set; } = string.Empty;

    public bool   IsActive    { get; set; } = true;

    /// <summary>When true, votes submitted by this judge are silently rejected.</summary>
    public bool   IsBlocked   { get; set; } = false;

    public string CreatedAt   { get; set; } = DateTime.UtcNow.ToString("O");
}
