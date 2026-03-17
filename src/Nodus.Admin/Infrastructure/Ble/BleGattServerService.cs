using System.Reactive.Subjects;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Domain.Common;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Nodus.Admin.Infrastructure.Ble;

/// <summary>
/// Windows GATT Server — hosts NODUS characteristics via WinRT GattServiceProvider.
/// Decision #25: 3 characteristics (DataWrite, AckNotify, BootstrapRead).
/// </summary>
public sealed class BleGattServerService : IBleGattServerService, IDisposable
{
    private readonly Subject<byte[]>        _incoming = new();
    private GattServiceProvider?            _serviceProvider;
    private GattLocalCharacteristic?        _ackChar;

    private byte[]                 _bootstrapPayload = [];
    private readonly object        _bootstrapLock    = new();
    private byte[]?                _bootstrapReadSnapshot;
    private DateTime               _bootstrapReadSnapshotExpiresUtc;
    private byte[]?                _pendingBootstrapPayload;
    private byte[]?                _bootstrapStreamPayload;
    private int                    _bootstrapStreamOffset;
    private DateTime               _bootstrapStreamExpiresUtc;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte[][]> _chunkBuffers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, DateTime> _chunkExpirations = new();

    // ── IBleGattServerService ─────────────────────────────────────────────
    public bool IsRunning
        => _serviceProvider?.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Started;

    public IObservable<byte[]> IncomingData => _incoming;

    public async Task<Result> StartAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null || !adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported)
                return Result.Fail("BLE peripheral role not supported on this device");

            // ── Create service ─────────────────────────────────────────────
            var svcResult = await GattServiceProvider.CreateAsync(Guid.Parse(NodusGatt.MainServiceUuid));
            if (svcResult.Error != BluetoothError.Success)
                return Result.Fail($"GATT service create failed: {svcResult.Error}");
            _serviceProvider = svcResult.ServiceProvider;

            // ── NODUS_DATA_WRITE (WriteWithoutResponse) ────────────────────
            var wrResult = await _serviceProvider.Service.CreateCharacteristicAsync(
                Guid.Parse(NodusGatt.DataWriteCharUuid),
                new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
                    WriteProtectionLevel     = GattProtectionLevel.Plain,
                    UserDescription          = "Nodus Data Write"
                });
            if (wrResult.Error != BluetoothError.Success)
                return Result.Fail($"DataWrite characteristic failed: {wrResult.Error}");
            wrResult.Characteristic.WriteRequested += OnDataWriteRequested;

            // ── NODUS_ACK_NOTIFY (Notify) ──────────────────────────────────
            var ntResult = await _serviceProvider.Service.CreateCharacteristicAsync(
                Guid.Parse(NodusGatt.AckNotifyCharUuid),
                new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.Notify,
                    WriteProtectionLevel     = GattProtectionLevel.Plain,
                    UserDescription          = "Nodus ACK Notify"
                });
            if (ntResult.Error != BluetoothError.Success)
                return Result.Fail($"AckNotify characteristic failed: {ntResult.Error}");
            _ackChar = ntResult.Characteristic;

            // ── NODUS_BOOTSTRAP_READ (Read) ────────────────────────────────
            var rdResult = await _serviceProvider.Service.CreateCharacteristicAsync(
                Guid.Parse(NodusGatt.BootstrapReadCharUuid),
                new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read,
                    WriteProtectionLevel     = GattProtectionLevel.Plain,
                    UserDescription          = "Nodus Bootstrap Read"
                });
            if (rdResult.Error != BluetoothError.Success)
                return Result.Fail($"BootstrapRead characteristic failed: {rdResult.Error}");
            rdResult.Characteristic.ReadRequested += OnBootstrapReadRequested;

            // ── Start advertising ──────────────────────────────────────────
            _serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
            {
                IsConnectable  = true,
                IsDiscoverable = true
            });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"GATT start failed: {ex.Message}");
        }
    }

    public Task<Result> StopAsync()
    {
        try
        {
            _serviceProvider?.StopAdvertising();
            _serviceProvider = null;
            _ackChar         = null;
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Fail($"GATT stop failed: {ex.Message}"));
        }
    }

    public async Task<Result> NotifyAsync(byte[] payload)
    {
        if (_ackChar is null)
            return Result.Fail("GATT server not started");
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(payload);
            var buffer = writer.DetachBuffer();
            foreach (var client in _ackChar.SubscribedClients)
                await _ackChar.NotifyValueAsync(buffer, client);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Notify failed: {ex.Message}");
        }
    }

    public void UpdateBootstrapPayload(byte[] payload)
    {
        lock (_bootstrapLock)
        {
            // Keep reads consistent: if a long-read is in progress, defer updates until it ends.
            if (IsBootstrapReadSnapshotActive())
            {
                _pendingBootstrapPayload = payload;
                return;
            }

            _bootstrapPayload = payload;
        }
    }

    // ── WinRT event handlers ──────────────────────────────────────────────

    private async void OnDataWriteRequested(
        GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();
        if (request is null) return;

        var reader = DataReader.FromBuffer(request.Value);
        var data   = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(data);

        // -- CHUNK PROCESSING START --
        if (data.Length > 7 && data[0] == NodusPrefix.ChunkedPayload)
        {
            uint txId = BitConverter.ToUInt32(data, 1);
            byte index = data[5];
            byte total = data[6];
            byte[] chunkData = data[7..];

            var buffer = _chunkBuffers.GetOrAdd(txId, _ => new byte[total][]);
            buffer[index] = chunkData;
            _chunkExpirations[txId] = DateTime.UtcNow.AddSeconds(30);

            bool complete = true;
            foreach (var b in buffer)
            {
                if (b == null)
                {
                    complete = false;
                    break;
                }
            }

            if (complete)
            {
                _chunkBuffers.TryRemove(txId, out _);
                _chunkExpirations.TryRemove(txId, out _);
                
                int totalLen = 0;
                foreach (var b in buffer) totalLen += b.Length;
                var assembled = new byte[totalLen];
                int offset = 0;
                foreach (var b in buffer)
                {
                    System.Buffer.BlockCopy(b, 0, assembled, offset, b.Length);
                    offset += b.Length;
                }
                
                data = assembled; // Replaced with original assembled payload (e.g. Vote packet)
            }
            else
            {
                if (request.Option == GattWriteOption.WriteWithResponse)
                    request.Respond();
                return; // Wait for more chunks
            }
        }
        // -- CHUNK PROCESSING END --

        // A new pre-read command starts a new bootstrap transfer session.
        if (data.Length > 0 && (data[0] == NodusPrefix.JudgeRegister || data[0] == NodusPrefix.SyncRequest))
        {
            lock (_bootstrapLock)
                ResetBootstrapStream();
        }

        // WinRT Bluetooth limits backpressure. Acknowledge Mac-level right away.
        if (request.Option == GattWriteOption.WriteWithResponse)
            request.Respond();

        // Push to the observer pipeline via ThreadPool to avoid blocking WinRT GATT thread
        // with heavy Crypto/JSON tasks in ProcessVoteUseCase.
        _ = Task.Run(() => _incoming.OnNext(data));
    }

    private async void OnBootstrapReadRequested(
        GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();
        if (request is null) return;

        byte[] payload;
        int offset = (int)request.Offset;
        lock (_bootstrapLock)
        {
            // Start (or refresh) a stable snapshot at offset 0 so all ATT Read Blob calls
            // during this transaction see the same payload bytes.
            if (offset == 0 || !IsBootstrapReadSnapshotActive())
            {
                _bootstrapReadSnapshot = _bootstrapPayload.ToArray();
                _bootstrapReadSnapshotExpiresUtc = DateTime.UtcNow.AddSeconds(5);
            }

            payload = _bootstrapReadSnapshot ?? _bootstrapPayload;
        }

        if (payload.Length == 0)
        {
            request.RespondWithValue(new DataWriter().DetachBuffer());
            return;
        }

        // BLE Long Read: WinRT fires this event once per ATT Read/Read Blob Request,
        // each with a different Offset.  Slice the payload so each response is correct.
        if (offset >= payload.Length)
        {
            // Signal end-of-value with an empty buffer
            request.RespondWithValue(new DataWriter().DetachBuffer());
            CompleteBootstrapReadSnapshot();
            return;
        }

        // Compatibility path: some Android stacks cap a single read to ~512 bytes and do not
        // continue with Read Blob offsets. Serve progressive windows on offset=0 requests.
        if (offset == 0 && payload.Length > 500)
        {
            byte[] window;
            lock (_bootstrapLock)
            {
                if (_bootstrapStreamPayload is null
                    || DateTime.UtcNow > _bootstrapStreamExpiresUtc
                    || _bootstrapStreamPayload.Length != payload.Length)
                {
                    _bootstrapStreamPayload = payload;
                    _bootstrapStreamOffset = 0;
                }

                _bootstrapStreamExpiresUtc = DateTime.UtcNow.AddSeconds(10);

                var remaining = _bootstrapStreamPayload.Length - _bootstrapStreamOffset;
                var chunkSize = Math.Min(480, remaining);
                window = _bootstrapStreamPayload
                    .AsSpan(_bootstrapStreamOffset, chunkSize)
                    .ToArray();

                _bootstrapStreamOffset += chunkSize;
                if (_bootstrapStreamOffset >= _bootstrapStreamPayload.Length)
                    ResetBootstrapStream();
            }

            var chunkWriter = new DataWriter();
            chunkWriter.WriteBytes(window);
            request.RespondWithValue(chunkWriter.DetachBuffer());
            return;
        }

        var writer = new DataWriter();
        writer.WriteBytes(payload[offset..]);
        request.RespondWithValue(writer.DetachBuffer());
    }

    private bool IsBootstrapReadSnapshotActive()
    {
        if (_bootstrapReadSnapshot is null)
            return false;

        if (DateTime.UtcNow <= _bootstrapReadSnapshotExpiresUtc)
            return true;

        _bootstrapReadSnapshot = null;
        _bootstrapReadSnapshotExpiresUtc = DateTime.MinValue;

        if (_pendingBootstrapPayload is not null)
        {
            _bootstrapPayload = _pendingBootstrapPayload;
            _pendingBootstrapPayload = null;
        }

        return false;
    }

    private void CompleteBootstrapReadSnapshot()
    {
        lock (_bootstrapLock)
        {
            _bootstrapReadSnapshot = null;
            _bootstrapReadSnapshotExpiresUtc = DateTime.MinValue;

            if (_pendingBootstrapPayload is not null)
            {
                _bootstrapPayload = _pendingBootstrapPayload;
                _pendingBootstrapPayload = null;
            }

            ResetBootstrapStream();
        }
    }

    private void ResetBootstrapStream()
    {
        _bootstrapStreamPayload = null;
        _bootstrapStreamOffset = 0;
        _bootstrapStreamExpiresUtc = DateTime.MinValue;
    }

    public void Dispose()
    {
        _incoming.Dispose();
        _serviceProvider?.StopAdvertising();
        _serviceProvider = null;
    }
}


