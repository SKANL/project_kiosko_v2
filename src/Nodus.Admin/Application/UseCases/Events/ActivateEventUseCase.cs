using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;

namespace Nodus.Admin.Application.UseCases.Events;

/// <summary>
/// Activates an event:
///   1. Pauses the currently active event (if any).
///   2. Sets the target event to Active.
///   3. Persists settings (ActiveEventId).
///   4. Rebuilds the GATT bootstrap payload for the new event.
///   5. Sends BLE push notification 0x07 (EventChanged) to all connected judges.
/// </summary>
public sealed class ActivateEventUseCase
{
    private readonly IEventRepository            _events;
    private readonly IAppSettingsService         _settings;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;
    private readonly IBleGattServerService        _ble;

    public ActivateEventUseCase(
        IEventRepository            events,
        IAppSettingsService         settings,
        BuildBootstrapPayloadUseCase bootstrap,
        IBleGattServerService        ble)
    {
        _events    = events;
        _settings  = settings;
        _bootstrap = bootstrap;
        _ble       = ble;
    }

    public async Task<Result> ExecuteAsync(int eventId)
    {
        // 1. Load target event
        var evtResult = await _events.GetByIdAsync(eventId);
        if (evtResult.IsFail)
            return Result.Fail(evtResult.Error!);

        // 2. Resolve currently active event (for potential rollback)
        var currentResult = await _events.GetCurrentAsync();
        NodusEvent? previousActive = (currentResult.IsOk
                                       && currentResult.Value is not null
                                       && currentResult.Value.Id != eventId)
                                      ? currentResult.Value
                                      : null;

        // 3. Pause the previous event
        if (previousActive is not null)
        {
            previousActive.Status = EventStatus.Paused;
            var pauseResult = await _events.UpdateAsync(previousActive);
            if (pauseResult.IsFail)
                return Result.Fail($"Could not pause current event: {pauseResult.Error}");
        }

        // 4. Activate target event
        var evt = evtResult.Value!;
        evt.Status = EventStatus.Active;
        var activateResult = await _events.UpdateAsync(evt);
        if (activateResult.IsFail)
        {
            // Rollback: restore previous event to Active
            if (previousActive is not null)
            {
                previousActive.Status = EventStatus.Active;
                await _events.UpdateAsync(previousActive);
            }
            return Result.Fail($"Could not activate event: {activateResult.Error}");
        }

        // 5. Rebuild GATT bootstrap payload BEFORE updating settings
        //    so that if this fails, settings still reflect the previous state.
        var buildResult = await _bootstrap.ExecuteAsync(eventId);
        if (buildResult.IsFail)
        {
            // Rollback DB changes
            evt.Status = EventStatus.Draft;
            await _events.UpdateAsync(evt);
            if (previousActive is not null)
            {
                previousActive.Status = EventStatus.Active;
                await _events.UpdateAsync(previousActive);
            }
            return Result.Fail($"Bootstrap rebuild failed: {buildResult.Error}");
        }

        // 6. Persist new active ID (only after bootstrap succeeded)
        _settings.ActiveEventId = eventId;
        _settings.Save();

        // 7. Notify all connected judges (BLE push 0x07 = EventChanged) — best effort
        if (_ble.IsRunning)
        {
            try
            {
                await _ble.NotifyAsync([0x07]);   // 0x07 = NodusPrefix.EventChanged
            }
            catch (Exception ex)
            {
                // Non-fatal: event is active, judges will sync on next connection
                System.Diagnostics.Debug.WriteLine(
                    $"ActivateEvent: notify judges failed (non-fatal): {ex.Message}");
            }
        }

        return Result.Ok();
    }
}
