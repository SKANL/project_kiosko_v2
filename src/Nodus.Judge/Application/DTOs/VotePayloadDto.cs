namespace Nodus.Judge.Application.DTOs;

/// <summary>
/// Vote payload built on-device before signing and BLE transmission.
/// Byte prefix 0x01 marks this as a Vote message.
/// </summary>
public sealed record VotePayloadDto(
    string PacketId,
    int    Ttl,
    List<string> Hops,
    int    EventId,
    int    ProjectId,
    int    JudgeId,
    Dictionary<string, double> Scores,  // criterion → value
    double WeightedScore,
    string SignatureBase64,
    string JudgePublicKeyBase64,
    int    Version
);
