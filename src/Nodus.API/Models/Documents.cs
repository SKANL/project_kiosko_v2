using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Nodus.API.Models;

// ── MongoDB Document Models ───────────────────────────────────────────────
// Each model maps to a MongoDB collection in the "nodus" database.
// Synced FROM Admin (BSON over REST); used for cloud dashboards, results.

[BsonIgnoreExtraElements]
public sealed class EventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;     // "EVT-XYZ"

    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("institution")]
    [JsonPropertyName("institution")]
    public string Institution { get; set; } = string.Empty;

    [BsonElement("description")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("rubricJson")]
    [JsonPropertyName("rubricJson")]
    public string RubricJson { get; set; } = string.Empty;

    [BsonElement("rubricVersion")]
    [JsonPropertyName("rubricVersion")]
    public int RubricVersion { get; set; } = 1;

    [BsonElement("maxProjects")]
    [JsonPropertyName("maxProjects")]
    public int MaxProjects { get; set; } = 100;

    [BsonElement("categories")]
    [JsonPropertyName("categories")]
    public string[] Categories { get; set; } = [];

    [BsonElement("finishedAt")]
    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    [BsonElement("syncedAtUtc")]
    [JsonPropertyName("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class ProjectDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;     // "PROJ-A3X"

    [BsonElement("eventId")]
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("category")]
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [BsonElement("description")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [BsonElement("githubLink")]
    [JsonPropertyName("githubLink")]
    public string? GithubLink { get; set; }

    [BsonElement("videoLink")]
    [JsonPropertyName("videoLink")]
    public string? VideoLink { get; set; }

    [BsonElement("teamMembers")]
    [JsonPropertyName("teamMembers")]
    public string? TeamMembers { get; set; }

    [BsonElement("standNumber")]
    [JsonPropertyName("standNumber")]
    public string? StandNumber { get; set; }

    [BsonElement("sortOrder")]
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    [BsonElement("editToken")]
    [JsonPropertyName("editToken")]
    public string EditToken { get; set; } = string.Empty;

    [BsonElement("syncedAtUtc")]
    [JsonPropertyName("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class VoteDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;     // GUID

    [BsonElement("eventId")]
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("projectId")]
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("judgeId")]
    [JsonPropertyName("judgeId")]
    public string JudgeId { get; set; } = string.Empty;

    [BsonElement("version")]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [BsonElement("scoresJson")]
    [JsonPropertyName("scoresJson")]
    public string ScoresJson { get; set; } = "{}";

    [BsonElement("weightsJson")]
    [JsonPropertyName("weightsJson")]
    public string WeightsJson { get; set; } = "{}";

    [BsonElement("comment")]
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [BsonElement("signature")]
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [BsonElement("nonce")]
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [BsonElement("timestampUtc")]
    [JsonPropertyName("timestampUtc")]
    public long TimestampUtc { get; set; }

    [BsonElement("createdAtUtc")]
    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; }

    [BsonElement("syncedAtUtc")]
    [JsonPropertyName("syncedAtUtc")]
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;

    // Multimedia references (GridFS object IDs or URLs)
    [BsonElement("remotePhotoUrl")]
    [JsonPropertyName("remotePhotoUrl")]
    public string? RemotePhotoUrl { get; set; }

    [BsonElement("remoteAudioUrl")]
    [JsonPropertyName("remoteAudioUrl")]
    public string? RemoteAudioUrl { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class JudgeDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;     // UUID

    [BsonElement("eventId")]
    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("publicKeyBase64")]
    [JsonPropertyName("publicKeyBase64")]
    public string PublicKeyBase64 { get; set; } = string.Empty;

    [BsonElement("isBlocked")]
    [JsonPropertyName("isBlocked")]
    public bool IsBlocked { get; set; }

    [BsonElement("registeredAtUtc")]
    [JsonPropertyName("registeredAtUtc")]
    public DateTime RegisteredAtUtc { get; set; }
}
