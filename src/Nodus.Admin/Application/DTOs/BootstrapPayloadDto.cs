namespace Nodus.Admin.Application.DTOs;

/// <summary>
/// Payload serialized into NODUS_BOOTSTRAP_READ.
/// Judges write 0x06 first, then read this (Decision #56).
/// Serialized as MessagePack or compressed JSON — byte prefix 0x03.
/// </summary>
public sealed record BootstrapPayloadDto(
    string PayloadType,
    int    EventId,
    string EventName,
    string Institution,
    string EventDate,
    string AdminPublicKeyBase64,
    string EventStatus,
    string FinishedAt,
    string GraceEndsAt,
    int    SinceSeq,
    IReadOnlyList<ProjectInfoDto>  Projects,
    IReadOnlyList<JudgeInfoDto>    Judges,
    int    RubricVersion,
    string RubricJson
);

public sealed record ProjectInfoDto(int Id, string Name, string Description, string Category, string TeamMembers, int SortOrder, string ProjectCode, int SequenceNumber, string StandNumber = "", string GithubLink = "", string VideoLink = "", string SpeechVideoLink = "", string TechStack = "", string Objetivos = "");
public sealed record JudgeInfoDto  (int Id, string Name, string PublicKeyBase64);
