using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Enums;

namespace Nodus.Judge.Application.Interfaces.Services;

/// <summary>
/// Firefly swarm protocol (Doc 12 / Decision #3).
/// Manages the FSM: SEEKER → CANDIDATE → LINK → COOLDOWN
/// and handles peer discovery via BLE scanning.
/// </summary>
public interface IBleSwarmService
{
    /// <summary>Current Firefly state — UI observes this to show connectivity badge.</summary>
    FireflyState CurrentState { get; }

    /// <summary>Observable stream of state transitions.</summary>
    IObservable<FireflyState> StateChanges { get; }

    /// <summary>
    /// Observable of discovered Admin/LINK peer device IDs (MAC or UUID).
    /// Filtered by RSSI > -75 dBm (Decision #3).
    /// </summary>
    IObservable<string> PeerDiscovered { get; }

    /// <summary>Device ID of the current LINK peer, null when not connected.</summary>
    string? LinkedPeerId { get; }

    /// <summary>
    /// Start the Firefly FSM.
    /// In SEEKER state: starts BLE scan for NODUS_MAIN_SERVICE.
    /// </summary>
    Task<Result> StartAsync();

    Task<Result> StopAsync();
}
