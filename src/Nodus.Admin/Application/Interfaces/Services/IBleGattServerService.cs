using Nodus.Admin.Domain.Common;

namespace Nodus.Admin.Application.Interfaces.Services;

/// <summary>
/// GATT Server hosted on Windows Admin — 4 characteristics.
/// UUIDs (Decision #25):
///   Service:          6E6F6400-0000-0000-0000-000000000001
///   NODUS_DATA_WRITE: 6E6F6400-0000-0000-0000-000000000002  (WriteWithoutResponse)
///   NODUS_ACK_NOTIFY: 6E6F6400-0000-0000-0000-000000000003  (Notify)
///   NODUS_BOOTSTRAP:  6E6F6400-0000-0000-0000-000000000004  (Read)
/// </summary>
public interface IBleGattServerService
{
    /// <summary>True while the GATT server is actively advertising.</summary>
    bool IsRunning { get; }

    /// <summary>Observable stream of raw byte payloads received on NODUS_DATA_WRITE.</summary>
    IObservable<byte[]> IncomingData { get; }

    Task<Result> StartAsync();
    Task<Result> StopAsync();

    /// <summary>
    /// Push ACK or blocklist notification to all subscribed LINK nodes.
    /// Byte prefix 0xA1 = ACK, 0x05 = blocklist.
    /// </summary>
    Task<Result> NotifyAsync(byte[] payload);

    /// <summary>
    /// Update the value returned by NODUS_BOOTSTRAP_READ.
    /// Called whenever event/project/judge data changes.
    /// </summary>
    void UpdateBootstrapPayload(byte[] payload);
}
