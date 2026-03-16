using System.Reactive.Linq;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Votes;
using Nodus.Admin.Domain.Common;

namespace Nodus.Admin.Application.Services;

/// <summary>
/// Background service that listens to incoming BLE data, processes votes,
/// persists them to DB, and sends ACK notifications back to judges.
/// </summary>
public sealed class VoteProcessingService : IDisposable
{
    private readonly IBleGattServerService _ble;
    private readonly ProcessVoteUseCase _processVote;
    private IDisposable? _subscription;

    public VoteProcessingService(
        IBleGattServerService ble,
        ProcessVoteUseCase processVote)
    {
        _ble = ble;
        _processVote = processVote;
    }

    /// <summary>Starts listening to incoming BLE data and processing votes.</summary>
    public void Start()
    {
        Stop(); // Clear any existing subscription

        // SelectMany fully awaits the async lambda before emitting — this ensures votes are
        // processed sequentially and ACKs are sent before the next data item is handled.
        _subscription = _ble.IncomingData
            .SelectMany(async data =>
            {
                try
                {
                    var ackResult = await _processVote.ExecuteAsync(data);
                    if (ackResult.IsOk)
                    {
                        var notifyResult = await _ble.NotifyAsync(ackResult.Value!);
                        if (notifyResult.IsFail)
                            System.Diagnostics.Debug.WriteLine(
                                $"VoteProcessingService ACK notify failed: {notifyResult.Error}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"VoteProcessingService vote processing failed: {ackResult.Error}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"VoteProcessingService unhandled error: {ex.GetType().Name}: {ex.Message}");
                }
                return System.Reactive.Unit.Default;
            })
            .Subscribe(
                _ => { },
                ex => System.Diagnostics.Debug.WriteLine(
                    $"VoteProcessingService stream error: {ex.Message}"));
    }

    /// <summary>Stops listening to incoming BLE data.</summary>
    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
