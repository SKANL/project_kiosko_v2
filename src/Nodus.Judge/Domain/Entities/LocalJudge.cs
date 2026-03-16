using SQLite;

namespace Nodus.Judge.Domain.Entities;

[Table("local_judges")]
public class LocalJudge
{
    [PrimaryKey]
    public int    RemoteId    { get; set; }   // Admin Judge.Id

    public int    EventId     { get; set; }   // FK → LocalEvent.RemoteId

    [NotNull]
    public string Name        { get; set; } = string.Empty;

    public string Institution { get; set; } = string.Empty;

    /// <summary>This judge's Ed25519 public key (Base64).</summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Ed25519 private key (Base64, AES-256-GCM encrypted with PIN-derived key).
    /// Only present for the "self" judge record.
    /// </summary>
    public string EncryptedPrivateKeyBase64 { get; set; } = string.Empty;

    /// <summary>True when this record represents the app's own identity.</summary>
    public bool   IsSelf      { get; set; }

    public string SyncedAt    { get; set; } = DateTime.UtcNow.ToString("O");
}
