namespace Nodus.Judge.Application.DTOs;

/// <summary>
/// Mirrors Admin's BootstrapPayloadDto — deserialized from NODUS_BOOTSTRAP_READ.
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
    List<ProjectInfoDto>  Projects,
    List<JudgeInfoDto>    Judges,
    int    RubricVersion = 1,
    string RubricJson = ""
);

public sealed record ProjectInfoDto(int Id, string Name, string Description, string Category, string TeamMembers, int SortOrder, string ProjectCode = "", int SequenceNumber = 0, string StandNumber = "", string GithubLink = "", string VideoLink = "", string SpeechVideoLink = "", string TechStack = "", string Objetivos = "");
public sealed record JudgeInfoDto  (int Id, string Name, string PublicKeyBase64);
