using Nodus.Judge.Domain.Enums;
using SQLite;

namespace Nodus.Judge.Domain.Entities;

[Table("local_votes")]
public class LocalVote
{
    [PrimaryKey, AutoIncrement]
    public int    Id           { get; set; }

    public int    EventId      { get; set; }
    public int    ProjectId    { get; set; }
    public int    JudgeId      { get; set; }

    /// <summary>Criteria scores as JSON — same schema as Admin.</summary>
    public string ScoresJson   { get; set; } = "{}";

    public double WeightedScore { get; set; }

    /// <summary>Ed25519 signature (Base64) — signed with this judge's private key.</summary>
    public string SignatureBase64 { get; set; } = string.Empty;

    /// <summary>Transport packet id used for relay deduplication/inspection.</summary>
    public string PacketId        { get; set; } = string.Empty;

    /// <summary>JSON array of relay judge ids traversed by this packet.</summary>
    public string HopPathJson     { get; set; } = "[]";

    public int    RemainingTtl    { get; set; } = 0;

    /// <summary>Score sync status — kept for SubmitVoteUseCase and VotingViewModel callers.</summary>
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;

    /// <summary>Re-evaluation version. Starts at 1, incremented on each re-score of the same project.</summary>
    public int    Version      { get; set; } = 1;

    // ── Multimedia fields (Decision #20, #47) ───────────────────────────────────

    /// <summary>Device-local file path of an attached photo, or null if none.</summary>
    public string? LocalPhotoPath { get; set; }

    /// <summary>Device-local file path of a recorded voice note, or null if none.</summary>
    public string? LocalAudioPath { get; set; }

    /// <summary>
    /// Independent sync axis for the score payload.
    /// Mirrors SyncStatus for clarity; both are updated together by SubmitVoteUseCase.
    /// </summary>
    public SyncStatus ScoreStatus { get; set; } = SyncStatus.Pending;

    /// <summary>
    /// Independent sync axis for media attachments.
    /// Initialised to ReachedCloud when no local media exists (Decision #47).
    /// </summary>
    public SyncStatus MediaStatus { get; set; } = SyncStatus.ReachedCloud;

    public string CreatedAt    { get; set; } = DateTime.UtcNow.ToString("O");
}
