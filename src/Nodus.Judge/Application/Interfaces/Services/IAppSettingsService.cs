namespace Nodus.Judge.Application.Interfaces.Services;

public interface IAppSettingsService
{
    int?   ActiveEventId    { get; set; }
    int?   SelfJudgeId      { get; set; }
    bool   IsOnboarded      { get; }
    string DatabasePath     { get; }

    /// <summary>Judge's display name — persisted so the judge doesn't re-enter it on each event.</summary>
    string JudgeName         { get; set; }

    /// <summary>In-memory session key (event access password) used to sign votes. Not persisted.</summary>
    string SessionPin        { get; set; }

    /// <summary>Hashed event password stored so we can verify the judge is re-using the right password.</summary>
    string PinHash           { get; set; }

    /// <summary>
    /// Per-event AES-256 shared key (Base64) received from the Admin via the Judge Access QR.
    /// Stored so that delta-poll bootstrap payloads can be decrypted across app restarts.
    /// </summary>
    string SharedEventKey    { get; set; }

    /// <summary>BLE connection timeout in seconds. Persisted. Default: 15.</summary>
    int    BleTimeoutSeconds { get; set; }

    /// <summary>ISO-8601 timestamp of last password change. Persisted.</summary>
    string PinLastChangedAt  { get; set; }

    void Save();
    void Load();
}
