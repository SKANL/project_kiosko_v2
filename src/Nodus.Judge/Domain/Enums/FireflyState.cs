namespace Nodus.Judge.Domain.Enums;

/// <summary>
/// Firefly FSM states (Decision #3 / Doc 12).
/// SEEKER → CANDIDATE → LINK → COOLDOWN
/// </summary>
public enum FireflyState
{
    Seeker,
    Candidate,
    Link,
    Cooldown
}
