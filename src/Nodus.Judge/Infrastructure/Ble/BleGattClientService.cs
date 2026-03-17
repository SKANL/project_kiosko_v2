using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Collections.Generic;
using System.Threading;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using Shiny.BluetoothLE;

namespace Nodus.Judge.Infrastructure.Ble;

/// <summary>
/// GATT Client — Judge connects to Admin (or a LINK relay) and performs:
///   - Vote write    (0x01 prefix → NODUS_DATA_WRITE)
///   - Sync request  (0x06 → NODUS_DATA_WRITE, then read NODUS_BOOTSTRAP_READ)
///   - ACK listen    (subscribe to NODUS_ACK_NOTIFY)
/// </summary>
public sealed class BleGattClientService : IBleGattClientService, IDisposable
{
    private const int ConnectAttempts = 2;
    private static readonly TimeSpan PreReadAckTimeout = TimeSpan.FromSeconds(8);
    private readonly IBleManager         _ble;
    private readonly Subject<byte[]>     _ackSubj          = new();
    private readonly Subject<byte[]>     _eventChangedSubj = new();
    private readonly Subject<byte[]>     _blocklistSubj    = new();
    private readonly SemaphoreSlim       _gattOpLock       = new(1, 1);

    private IPeripheral? _peripheral;
    private IDisposable? _ackNotifySub;

    public BleGattClientService(IBleManager ble) => _ble = ble;

    // ── IBleGattClientService ─────────────────────────────────────────────
    public bool IsConnected => _peripheral?.Status == ConnectionState.Connected;

    /// <summary>Exposes the underlying peripheral — used by BleSwarmService.</summary>
    public IPeripheral? ConnectedPeripheral => _peripheral;

    public IObservable<byte[]> AckReceived         => _ackSubj.AsObservable();
    public IObservable<byte[]> EventChangeReceived => _eventChangedSubj.AsObservable();
    public IObservable<byte[]> BlocklistReceived   => _blocklistSubj.AsObservable();

    private void HandleNotify(byte[]? data)
    {
        if (data?.Length > 0)
        {
            if (data[0] == NodusPrefix.Ack)
                _ackSubj.OnNext(data);
            else if (data[0] == NodusPrefix.EventChanged)
                _eventChangedSubj.OnNext(data);
            else if (data[0] == NodusPrefix.Blocklist)
                _blocklistSubj.OnNext(data);
        }
    }

    public async Task<Result> ConnectAsync(string peerId)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            try
            {
                await DisconnectAsync();

                // Scan briefly to find the peripheral by UUID
                _peripheral = await _ble
                    .Scan(new ScanConfig { ServiceUuids = [NodusGatt.MainServiceUuid] })
                    .Where(sr => sr.Peripheral.Uuid.ToString() == peerId)
                    .Select(sr => sr.Peripheral)
                    .Take(1)
                    .Timeout(TimeSpan.FromSeconds(10))
                    .FirstAsync();

                var connectResult = await ConnectAndSubscribeAsync(_peripheral).ConfigureAwait(false);
                if (connectResult.IsOk)
                    return connectResult;

                lastError = new InvalidOperationException(connectResult.Error);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < ConnectAttempts)
                await Task.Delay(350).ConfigureAwait(false);
        }

        return Result.Fail($"Connect failed after {ConnectAttempts} attempts: {lastError?.Message}");
    }

    /// <summary>
    /// Scans for any peripheral advertising the Nodus GATT service UUID and connects
    /// to the first one found.  This is the normal startup path for a Judge joining
    /// an event without knowing the Admin's device address in advance.
    /// </summary>
    public async Task<Result> ScanAndConnectAsync(int timeoutSeconds = 15)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            try
            {
                await DisconnectAsync();

                _peripheral = await _ble
                    .Scan(new ScanConfig { ServiceUuids = [NodusGatt.MainServiceUuid] })
                    .Select(sr => sr.Peripheral)
                    .Take(1)
                    .Timeout(TimeSpan.FromSeconds(timeoutSeconds))
                    .FirstAsync();

                var connectResult = await ConnectAndSubscribeAsync(_peripheral).ConfigureAwait(false);
                if (connectResult.IsOk)
                    return connectResult;

                lastError = new InvalidOperationException(connectResult.Error);
            }
            catch (TimeoutException)
            {
                lastError = new TimeoutException("No Nodus Admin device found nearby. Make sure the Admin BLE server is running.");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < ConnectAttempts)
                await Task.Delay(400).ConfigureAwait(false);
        }

        return Result.Fail($"Scan & connect failed after {ConnectAttempts} attempts: {lastError?.Message}");
    }

    public async Task<Result> EnsureConnectedAsync(int timeoutSeconds = 15)
    {
        if (IsConnected)
        {
            return Result.Ok();
        }

        return await ScanAndConnectAsync(timeoutSeconds);
    }

    public async Task<Result> DisconnectAsync()
    {
        try
        {
            _ackNotifySub?.Dispose();
            _ackNotifySub = null;
            _peripheral?.CancelConnection();
            _peripheral = null;
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Disconnect failed: {ex.Message}");
        }
    }

    public async Task<Result> WriteVoteAsync(byte[] payload)
    {
        await _gattOpLock.WaitAsync().ConfigureAwait(false);
        if (_peripheral is null || !IsConnected)
        {
            _gattOpLock.Release();
            return Result.Fail("Not connected");
        }
        try
        {
            int mtu = _peripheral.Mtu > 0 ? _peripheral.Mtu : 23;
            int maxPayloadSize = Math.Max(20, mtu - 3) - 7;
            if (maxPayloadSize < 10) maxPayloadSize = 128; // Safe fallback

            if (payload.Length <= maxPayloadSize + 7 && payload[0] != NodusPrefix.ChunkedPayload)
            {
                await _peripheral
                    .WriteCharacteristic(NodusGatt.MainServiceUuid, NodusGatt.DataWriteCharUuid, payload, withResponse: true)
                    .Timeout(TimeSpan.FromSeconds(10))
                    .DefaultIfEmpty()
                    .FirstAsync();
                return Result.Ok();
            }

            uint txId = (uint)new Random().Next(1, int.MaxValue);
            int totalChunks = (int)Math.Ceiling((double)payload.Length / maxPayloadSize);
            
            if (totalChunks > 255)
                 return Result.Fail("Payload too large for BLE chunking");

            for (byte i = 0; i < totalChunks; i++)
            {
                int offset = i * maxPayloadSize;
                int len = Math.Min(maxPayloadSize, payload.Length - offset);
                byte[] chunk = new byte[len + 7];
                chunk[0] = NodusPrefix.ChunkedPayload;
                BitConverter.TryWriteBytes(chunk.AsSpan(1, 4), txId);
                chunk[5] = i;
                chunk[6] = (byte)totalChunks;
                Buffer.BlockCopy(payload, offset, chunk, 7, len);

                await _peripheral
                    .WriteCharacteristic(NodusGatt.MainServiceUuid, NodusGatt.DataWriteCharUuid, chunk, withResponse: true)
                    .Timeout(TimeSpan.FromSeconds(5))
                    .DefaultIfEmpty()
                    .FirstAsync();
            }
            return Result.Ok();
        }
        catch (TimeoutException)
        {
            return Result.Fail("Vote write timed out after 10 s");
        }
        catch (Exception ex)
        {
            return Result.Fail($"WriteVote failed: {ex.Message}");
        }
        finally
        {
            _gattOpLock.Release();
        }
    }

    public async Task<Result<byte[]>> SyncFromAdminAsync(byte[]? preReadPayload = null)
    {
        await _gattOpLock.WaitAsync().ConfigureAwait(false);
        if (_peripheral is null || !IsConnected)
        {
            _gattOpLock.Release();
            return Result<byte[]>.Fail("Not connected");
        }
        try
        {
            var readyResult = await EnsureSyncChannelReadyAsync().ConfigureAwait(false);
            if (readyResult.IsFail)
                return Result<byte[]>.Fail(readyResult.Error!);

            Task<Result>? ackWaitTask = null;

            if (preReadPayload is not null && preReadPayload.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[BLE Sync] pre-read write prefix=0x{preReadPayload[0]:X2}, bytes={preReadPayload.Length}");
                ackWaitTask = WaitForPreReadAckAsync(preReadPayload);

                try
                {
                    await _peripheral
                        .WriteCharacteristic(NodusGatt.MainServiceUuid, NodusGatt.DataWriteCharUuid, preReadPayload, withResponse: false)
                        .Timeout(TimeSpan.FromSeconds(10))
                        .DefaultIfEmpty()
                        .FirstAsync();
                }
                catch (TimeoutException)
                {
                    if (preReadPayload[0] == NodusPrefix.SyncRequest)
                    {
                        // Some Android stacks time out the no-response write observable even when
                        // payload may have already reached the peripheral. Continue polling flow.
                        System.Diagnostics.Debug.WriteLine("[BLE Sync] pre-read write timeout for sync request, continuing");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[BLE Sync] pre-read write timeout");
                        return Result<byte[]>.Fail("Pre-read write timed out before bootstrap read");
                    }
                }

                if (preReadPayload[0] != NodusPrefix.SyncRequest)
                {
                    var ackResult = await ackWaitTask.ConfigureAwait(false);
                    if (ackResult.IsFail)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BLE Sync] pre-read ACK failed: {ackResult.Error}");
                        return Result<byte[]>.Fail(ackResult.Error!);
                    }

                    System.Diagnostics.Debug.WriteLine("[BLE Sync] pre-read ACK received");
                }
            }

            if (ackWaitTask is not null && preReadPayload is not null && preReadPayload[0] == NodusPrefix.SyncRequest)
            {
                // For sync requests, we MUST await the ACK to guarantee the Admin has 
                // finished building the compressed/encrypted payload.
                await ObserveOptionalSyncAckAsync(ackWaitTask).ConfigureAwait(false);
            }

            var readResult = await ReadBootstrapAggregateAsync().ConfigureAwait(false);
            if (readResult.IsFail)
                return Result<byte[]>.Fail(readResult.Error!);

            return Result<byte[]>.Ok(readResult.Value!);
        }
        catch (TimeoutException)
        {
            System.Diagnostics.Debug.WriteLine("[BLE Sync] bootstrap read timeout (read phase)");
            return Result<byte[]>.Fail("Bootstrap read timed out — is the Admin BLE server running?");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BLE Sync] exception: {ex.Message}");
            return Result<byte[]>.Fail($"SyncFromAdmin failed: {ex.Message}");
        }
        finally
        {
            _gattOpLock.Release();
        }
    }

    private static bool IsCompleteBootstrapEnvelope(List<byte> bytes)
    {
        if (bytes.Count < 2)
            return false;

        byte version = bytes[1];
        
        // v1 envelope (unencrypted, legacy):
        // [prefix:1][0x01][jsonLen:4][compLen:4][compressedBytes...]
        if (version == 0x01)
        {
            if (bytes.Count < 10) return false;
            int expectedCompLength = BitConverter.ToInt32(bytes.ToArray(), 6);
            if (expectedCompLength <= 0) return false;
            return (bytes.Count - 10) >= expectedCompLength;
        }
        
        // v2 envelope (encrypted):
        // [prefix:1][0x02][jsonLen:4][compLen:4][aesLen:4][nonce(12)|tag(16)|cipher...]
        if (version == 0x02)
        {
            if (bytes.Count < 14) return false;
            int expectedAesLength = BitConverter.ToInt32(bytes.ToArray(), 10);
            if (expectedAesLength <= 0) return false;
            return (bytes.Count - 14) >= expectedAesLength;
        }

        // Unknown version or legacy simple payload
        return bytes.Count > 100; // Heuristic for simple legacy data
    }

    private Task<Result> WaitForPreReadAckAsync(byte[] preReadPayload)
    {
        if (preReadPayload.Length == 0)
            return Task.FromResult(Result.Ok());

        byte expectedAckCode = preReadPayload[0] switch
        {
            NodusPrefix.JudgeRegister => NodusPrefix.JudgeRegister,
            NodusPrefix.SyncRequest   => NodusPrefix.SyncRequest,
            _                         => (byte)0x00
        };

        if (expectedAckCode == 0x00)
            return Task.FromResult(Result.Ok());

        var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? sub = null;

        sub = _ackSubj.Subscribe(ack =>
        {
            if (ack.Length >= 2 && ack[0] == NodusPrefix.Ack && ack[1] == expectedAckCode)
                tcs.TrySetResult(Result.Ok());
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PreReadAckTimeout).ConfigureAwait(false);
                tcs.TrySetResult(Result.Fail($"Timed out waiting ACK 0x{expectedAckCode:X2} before bootstrap read"));
            }
            catch
            {
            }
            finally
            {
                sub?.Dispose();
            }
        });

        return tcs.Task;
    }

    public void Dispose()
    {
        _ackNotifySub?.Dispose();
        _ackSubj.Dispose();
        _eventChangedSubj.Dispose();
        _blocklistSubj.Dispose();
        _peripheral?.CancelConnection();
    }

    private async Task<Result> ConnectAndSubscribeAsync(IPeripheral peripheral)
    {
        try
        {
            await peripheral.ConnectAsync(new ConnectionConfig { AutoConnect = false }).ConfigureAwait(false);

            // Give Android GATT a brief warm-up window to reduce first-read failures.
            await Task.Delay(300).ConfigureAwait(false);

            EnsureAckNotifySubscription();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Connect handshake failed: {ex.Message}");
        }
    }

    private void EnsureAckNotifySubscription()
    {
        if (_peripheral is null || !IsConnected)
            return;

        if (_ackNotifySub is not null)
            return;

        _ackNotifySub?.Dispose();
        _ackNotifySub = _peripheral
            .NotifyCharacteristic(NodusGatt.MainServiceUuid, NodusGatt.AckNotifyCharUuid)
            .Subscribe(
                result =>
                {
                    HandleNotify(result.Data);
                },
                _ =>
                {
                    _ackNotifySub?.Dispose();
                    _ackNotifySub = null;
                },
                () =>
                {
                    _ackNotifySub?.Dispose();
                    _ackNotifySub = null;
                });
    }

    private async Task<Result> EnsureSyncChannelReadyAsync()
    {
        EnsureAckNotifySubscription();

        if (_ackNotifySub is null)
            return Result.Fail("ACK notify subscription is not available");

        // Give Android GATT a brief settle window after notify setup to avoid
        // issuing write/read while service discovery and CCCD enabling are still in-flight.
        await Task.Delay(180).ConfigureAwait(false);
        return Result.Ok();
    }

    private async Task<Result<byte[]>> ReadBootstrapAggregateAsync()
    {
        var aggregate = new List<byte>(1024);
        byte[]? previousChunk = null;
        var consecutiveTimeouts = 0;

        for (var i = 0; i < 14; i++)
        {
            System.Diagnostics.Debug.WriteLine("[BLE Sync] reading bootstrap characteristic...");
            byte[] chunk;
            try
            {
                var read = await _peripheral!
                    .ReadCharacteristic(NodusGatt.MainServiceUuid, NodusGatt.BootstrapReadCharUuid)
                    .Timeout(TimeSpan.FromSeconds(5))
                    .FirstAsync();
                chunk = read.Data ?? Array.Empty<byte>();
                consecutiveTimeouts = 0;
            }
            catch (TimeoutException)
            {
                consecutiveTimeouts++;
                if (aggregate.Count > 0 && consecutiveTimeouts >= 2)
                    break;
                if (consecutiveTimeouts >= 4)
                    return Result<byte[]>.Fail("Bootstrap read timed out repeatedly");

                await Task.Delay(250).ConfigureAwait(false);
                continue;
            }

            if (chunk.Length == 0)
            {
                if (aggregate.Count == 0)
                    return Result<byte[]>.Fail("Bootstrap read returned empty payload");
                break;
            }

            if (previousChunk is not null && previousChunk.AsSpan().SequenceEqual(chunk))
                break;

            aggregate.AddRange(chunk);
            previousChunk = chunk;

            if (IsCompleteBootstrapEnvelope(aggregate))
                break;
        }

        System.Diagnostics.Debug.WriteLine($"[BLE Sync] bootstrap read completed bytes={aggregate.Count}");
        return aggregate.Count == 0
            ? Result<byte[]>.Fail("Bootstrap read returned null data")
            : Result<byte[]>.Ok(aggregate.ToArray());
    }

    private static async Task ObserveOptionalSyncAckAsync(Task<Result> ackWaitTask)
    {
        try
        {
            var ackResult = await ackWaitTask.ConfigureAwait(false);
            if (ackResult.IsOk)
                System.Diagnostics.Debug.WriteLine("[BLE Sync] pre-read ACK received (sync request)");
            else
                System.Diagnostics.Debug.WriteLine($"[BLE Sync] pre-read ACK missing for sync request, continuing to read: {ackResult.Error}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BLE Sync] optional sync ACK observer failed: {ex.Message}");
        }
    }
}
