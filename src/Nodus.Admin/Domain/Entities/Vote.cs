using Nodus.Admin.Domain.Enums;
using SQLite;

namespace Nodus.Admin.Domain.Entities;

[Table("votes")]
public class Vote
{
    [PrimaryKey, AutoIncrement]
    public int    Id           { get; set; }

    public int    EventId      { get; set; }   // FK → NodusEvent.Id
    public int    ProjectId    { get; set; }   // FK → Project.Id
    public int    JudgeId      { get; set; }   // FK → Judge.Id

    /// <summary>
    /// Criteria scores as JSON — e.g. {"innovation":8.5,"presentation":9.0,"technical":7.5}
    /// Weighted average is computed at read time.
    /// </summary>
    public string ScoresJson   { get; set; } = "{}";

    /// <summary>Computed weighted score (0–10). Stored for fast aggregation.</summary>
    public double WeightedScore { get; set; }

    /// <summary>Ed25519 signature (Base64) over SHA-256(EventId|ProjectId|JudgeId|ScoresJson).</summary>
    public string SignatureBase64 { get; set; } = string.Empty;

    /// <summary>Transport packet id used to identify relayed traffic.</summary>
    public string PacketId        { get; set; } = string.Empty;

    /// <summary>JSON array of relay judge ids seen while the packet travelled through the swarm.</summary>
    public string HopPathJson     { get; set; } = "[]";

    /// <summary>Remaining relay TTL observed when the Admin accepted the packet.</summary>
    public int    RemainingTtl    { get; set; } = 0;

    /// <summary>Score sync status (BLE path). Kept for backward compat with MonitorViewModel and ProcessVoteUseCase.</summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;

    /// <summary>Raw payload bytes (Base64) as received over BLE — kept for audit.</summary>
    public string RawPayloadBase64 { get; set; } = string.Empty;

    /// <summary>Re-evaluation version. Starts at 1, increments each time a judge re-scores this project.</summary>
    public int    Version      { get; set; } = 1;

    // ── Multimedia fields (Decision #20, #47) ───────────────────────────────────

    /// <summary>Device-local path of attached photo, or null.</summary>
    public string? LocalPhotoPath { get; set; }

    /// <summary>Device-local path of attached voice note, or null.</summary>
    public string? LocalAudioPath { get; set; }

    /// <summary>MongoDB GridFS URL once Admin uploads the photo to the cloud. Null until MediaStatus = ReachedCloud.</summary>
    public string? RemotePhotoUrl { get; set; }

    /// <summary>MongoDB GridFS URL once Admin uploads the audio to the cloud. Null until MediaStatus = ReachedCloud.</summary>
    public string? RemoteAudioUrl { get; set; }

    /// <summary>
    /// Independent sync axis for the score payload (BLE swarm path).
    /// Mirrors SyncStatus for clarity; both fields are updated in sync.
    /// </summary>
    public SyncStatus ScoreStatus { get; set; } = SyncStatus.Pending;

    /// <summary>
    /// Independent sync axis for attached media (Wi-Fi or BLE Mule path).
    /// Set to ReachedCloud on creation when no local media exists (Decision #47).
    /// </summary>
    public SyncStatus MediaStatus { get; set; } = SyncStatus.ReachedCloud;

    public string ReceivedAt   { get; set; } = DateTime.UtcNow.ToString("O");
    public string CreatedAt    { get; set; } = DateTime.UtcNow.ToString("O");
}
