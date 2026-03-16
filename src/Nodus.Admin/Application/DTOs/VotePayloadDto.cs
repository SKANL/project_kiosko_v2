namespace Nodus.Admin.Application.DTOs;

/// <summary>
/// JSON-deserialized BLE vote payload received from a Judge (prefix 0x01).
/// Must match the structure serialized by Nodus.Judge.
/// </summary>
public sealed class VotePayloadDto
{
    public string PacketId             { get; set; } = string.Empty;
    public int    Ttl                  { get; set; } = 2;
    public List<string> Hops           { get; set; } = new();
    public int    EventId              { get; set; }
    public int    ProjectId            { get; set; }
    public int    JudgeId              { get; set; }
    public string JudgePublicKeyBase64 { get; set; } = string.Empty;

    /// <summary>Criterion name → score (0–10).</summary>
    public Dictionary<string, double> Scores { get; set; } = new();

    public double WeightedScore     { get; set; }
    public string SignatureBase64   { get; set; } = string.Empty;

    /// <summary>Re-evaluation version. 1 = first submission, N+1 = re-score.</summary>
    public int    Version           { get; set; } = 1;
}
