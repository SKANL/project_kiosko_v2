using Nodus.Judge.Domain.Common;

namespace Nodus.Judge.Application.Interfaces.Services;

/// <summary>
/// GATT Server hosted by a Judge in LINK state.
/// Exposes ONLY 2 characteristics (Decision #25/#50):
///   NODUS_DATA_WRITE:  6E6F6400-0000-0000-0000-000000000002  (WriteWithoutResponse)
///   NODUS_ACK_NOTIFY:  6E6F6400-0000-0000-0000-000000000003  (Notify)
/// Does NOT expose NODUS_BOOTSTRAP_READ — LINK relays only forward.
/// </summary>
public interface IBleGattServerService
{
    bool IsRunning { get; }

    /// <summary>Raw data written to NODUS_DATA_WRITE by downstream peers.</summary>
    IObservable<byte[]> IncomingData { get; }

    Task<Result> StartAsync();
    Task<Result> StopAsync();

    /// <summary>Forward an ACK downstream to all subscribed LINK peers.</summary>
    Task<Result> NotifyAckAsync(byte[] payload);
}
