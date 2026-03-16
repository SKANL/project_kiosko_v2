using System.Reactive.Subjects;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using Shiny.BluetoothLE.Hosting;

namespace Nodus.Judge.Infrastructure.Ble;

/// <summary>
/// GATT Server hosted by a Judge in LINK state.
/// Exposes ONLY 2 characteristics (Decision #25/#50):
///   NODUS_DATA_WRITE  — receives forwarded votes from downstream peers
///   NODUS_ACK_NOTIFY  — pushes ACKs downstream
///
/// Does NOT expose NODUS_BOOTSTRAP_READ (Admin-only characteristic).
/// </summary>
public sealed class BleGattServerService : IBleGattServerService, IDisposable
{
    private readonly IBleHostingManager _hosting;
    private readonly Subject<byte[]>    _incoming = new();

    private IGattCharacteristic? _ackChar;

    public BleGattServerService(IBleHostingManager hosting)
        => _hosting = hosting;

    // ── IBleGattServerService ─────────────────────────────────────────────
    public bool IsRunning => _hosting.IsAdvertising;
    public IObservable<byte[]> IncomingData => _incoming;

    public async Task<Result> StartAsync()
    {
        try
        {
            var access = await _hosting.RequestAccess();
            if (access != Shiny.AccessState.Available)
                return Result.Fail($"BLE hosting access denied: {access}");

            await _hosting.AddService(NodusGatt.MainServiceUuid, true, sb =>
            {
                // Forward-write only — no bootstrap read
                sb.AddCharacteristic(NodusGatt.DataWriteCharUuid, cb =>
                    cb.SetWrite(request =>
                    {
                        _incoming.OnNext(request.Data);
                        return Task.CompletedTask;
                    }, WriteOptions.WriteWithoutResponse));

                _ackChar = sb.AddCharacteristic(NodusGatt.AckNotifyCharUuid,
                    cb => cb.SetNotification());
            });

            await _hosting.StartAdvertising(new AdvertisementOptions
            {
                LocalName    = "Nodus-Link",
                ServiceUuids = new[] { NodusGatt.MainServiceUuid }
            });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"LINK GATT start failed: {ex.Message}");
        }
    }

    public Task<Result> StopAsync()
    {
        try
        {
            _hosting.StopAdvertising();
            _hosting.ClearServices();
            _ackChar = null;
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail(ex.Message));
        }
    }

    public async Task<Result> NotifyAckAsync(byte[] payload)
    {
        if (_ackChar is null)
            return Result.Fail("LINK GATT server not started");
        try
        {
            await _ackChar.Notify(payload);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"NotifyAck failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _incoming.Dispose();
        if (IsRunning)
        {
            _hosting.StopAdvertising();
            _hosting.ClearServices();
        }
    }
}
