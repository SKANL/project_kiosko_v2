using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;
using Nodus.Admin.Infrastructure.Ble;

namespace Nodus.Admin.Application.UseCases.Events;

/// <summary>
/// Closes a running event:
///   1. Sets Status = Finished and records FinishedAt / GraceEndsAt timestamps.
///   2. Rebuilds the GATT bootstrap payload (so newly-syncing judges see Finished status).
///   3. Sends BLE push notification 0x07 (EventChanged) to all connected judges.
///
/// Judges can continue syncing final votes until GraceEndsAt.
/// </summary>
public sealed class CloseEventUseCase
{
    public static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(10);

    private readonly IEventRepository            _events;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;
    private readonly IBleGattServerService        _ble;

    public CloseEventUseCase(
        IEventRepository            events,
        BuildBootstrapPayloadUseCase bootstrap,
        IBleGattServerService        ble)
    {
        _events    = events;
        _bootstrap = bootstrap;
        _ble       = ble;
    }

    public async Task<Result> ExecuteAsync(int eventId)
    {
        var evtResult = await _events.GetByIdAsync(eventId);
        if (evtResult.IsFail)  return Result.Fail(evtResult.Error!);
        if (evtResult.Value is null) return Result.Fail($"Event {eventId} not found");

        var evt = evtResult.Value;
        if (evt.Status == EventStatus.Finished)
            return Result.Ok();   // idempotent

        evt.Status     = EventStatus.Finished;
        evt.FinishedAt = DateTime.UtcNow.ToString("O");
        evt.GraceEndsAt = DateTime.UtcNow.Add(GraceWindow).ToString("O");
        evt.UpdatedAt  = DateTime.UtcNow.ToString("O");

        var updateResult = await _events.UpdateAsync(evt);
        if (updateResult.IsFail)
            return Result.Fail($"Could not close event: {updateResult.Error}");

        // Rebuild bootstrap so judges learn the event is Finished on next sync.
        await _bootstrap.ExecuteAsync(eventId);

        // Notify connected judges to refresh their local event state.
        await _ble.NotifyAsync([NodusPrefix.EventChanged]);

        return Result.Ok();
    }
}
