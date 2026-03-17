using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nodus.API.Models;

// ── MongoDB Document Models ───────────────────────────────────────────────
// Each model maps to a MongoDB collection in the "nodus" database.
// Synced FROM Admin (BSON over REST); used for cloud dashboards, results.

[BsonIgnoreExtraElements]
public sealed class EventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;     // "EVT-XYZ"

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("institution")]
    public string Institution { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("rubricJson")]
    public string RubricJson { get; set; } = string.Empty;

    [BsonElement("rubricVersion")]
    public int RubricVersion { get; set; } = 1;

    [BsonElement("maxProjects")]
    public int MaxProjects { get; set; } = 100;

    [BsonElement("categories")]
    public string[] Categories { get; set; } = [];

    [BsonElement("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    [BsonElement("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class ProjectDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;     // "PROJ-A3X"

    [BsonElement("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("githubLink")]
    public string? GithubLink { get; set; }

    [BsonElement("teamMembers")]
    public string? TeamMembers { get; set; }

    [BsonElement("standNumber")]
    public string? StandNumber { get; set; }

    [BsonElement("sortOrder")]
    public int SortOrder { get; set; }

    [BsonElement("editToken")]
    public string EditToken { get; set; } = string.Empty;

    [BsonElement("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class VoteDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;     // GUID

    [BsonElement("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("judgeId")]
    public string JudgeId { get; set; } = string.Empty;

    [BsonElement("version")]
    public int Version { get; set; } = 1;

    [BsonElement("scoresJson")]
    public string ScoresJson { get; set; } = "{}";

    [BsonElement("weightsJson")]
    public string WeightsJson { get; set; } = "{}";

    [BsonElement("comment")]
    public string? Comment { get; set; }

    [BsonElement("signature")]
    public string Signature { get; set; } = string.Empty;

    [BsonElement("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [BsonElement("timestampUtc")]
    public long TimestampUtc { get; set; }

    [BsonElement("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;

    // Multimedia references (GridFS object IDs or URLs)
    [BsonElement("remotePhotoUrl")]
    public string? RemotePhotoUrl { get; set; }

    [BsonElement("remoteAudioUrl")]
    public string? RemoteAudioUrl { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class JudgeDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty;     // UUID

    [BsonElement("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("publicKeyBase64")]
    public string PublicKeyBase64 { get; set; } = string.Empty;

    [BsonElement("isBlocked")]
    public bool IsBlocked { get; set; }

    [BsonElement("registeredAtUtc")]
    public DateTime RegisteredAtUtc { get; set; }
}
