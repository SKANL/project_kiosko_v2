namespace Nodus.Judge.Application.DTOs;

/// <summary>
/// Access event advertised by Admin over BLE for onboarding without camera scanning.
/// </summary>
public sealed record DiscoveredAccessEventDto(
    int EventId,
    string EventName,
    string Institution,
    string AccessQrRaw)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Institution)
        ? EventName
        : $"{EventName} ({Institution})";
}
