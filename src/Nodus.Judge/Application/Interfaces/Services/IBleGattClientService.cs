using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Application.Interfaces.Services;

/// <summary>
/// GATT Client — Judge connects to Admin (or a LINK relay node).
/// Handles vote submission and bootstrap/sync flow.
/// </summary>
public interface IBleGattClientService
{
    bool IsConnected { get; }


    /// <summary>
    /// Ensures there is an active GATT connection. If disconnected, performs a scan/connect attempt.
    /// </summary>
    Task<Result> EnsureConnectedAsync(int timeoutSeconds = 15);
    /// <summary>Observable stream of ACK notifications from NODUS_ACK_NOTIFY (0xA1 prefix).</summary>
    IObservable<byte[]> AckReceived { get; }

    /// <summary>Observable stream of EventChanged notifications from NODUS_ACK_NOTIFY (0x07 prefix).</summary>
    IObservable<byte[]> EventChangeReceived { get; }

    /// <summary>Observable stream of blocklist updates from NODUS_ACK_NOTIFY (0x05 prefix).</summary>
    IObservable<byte[]> BlocklistReceived { get; }

    /// <summary>Connect to the GATT server of the given peer.</summary>
    Task<Result> ConnectAsync(string peerId);

    /// <summary>
    /// Scan for the first device advertising the Nodus GATT service and connect to it.
    /// Times out after <paramref name="timeoutSeconds"/> seconds.
    /// </summary>
    Task<Result> ScanAndConnectAsync(int timeoutSeconds = 15);

    Task<Result> DisconnectAsync();

    /// <summary>
    /// Write a vote payload to NODUS_DATA_WRITE (byte prefix 0x01).
    /// Uses WriteWithoutResponse.
    /// </summary>
    Task<Result> WriteVoteAsync(byte[] payload);

    /// <summary>
    /// Bootstrap / delta sync flow (Decision #56):
    ///   1. Write 0x06 to NODUS_DATA_WRITE (sync request)
    ///   2. Read NODUS_BOOTSTRAP_READ for the delta payload
    /// Returns raw payload bytes (caller deserializes).
    /// </summary>
    Task<Result<byte[]>> SyncFromAdminAsync(byte[]? preReadPayload = null);
}
