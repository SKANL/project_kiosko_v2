using System.Reactive.Linq;
using System.Reactive.Subjects;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Enums;
using Shiny.BluetoothLE;

namespace Nodus.Judge.Infrastructure.Ble;

/// <summary>
/// Implements the Firefly BLE Swarm Protocol (Doc 12 / Decision #3).
///
/// State machine:
///   SEEKER    — scanning for NODUS_MAIN_SERVICE, RSSI > -75 dBm
///   CANDIDATE — peer found, random(3s–15s) backoff before attempting LINK
///   LINK      — connected; max 60 s before forced disconnect
///   COOLDOWN  — post-LINK cooldown, 5 min before resuming SEEKER
/// </summary>
public sealed class BleSwarmService : IBleSwarmService, IDisposable
{
    private readonly IBleManager             _ble;
    private readonly BleGattClientService    _client;   // We reuse the client for LINK connection
    private readonly IBleGattServerService   _relayServer;

    private readonly BehaviorSubject<FireflyState> _stateSubj  = new(FireflyState.Seeker);
    private readonly Subject<string>               _peerSubj   = new();

    private IDisposable? _scanSub;
    private CancellationTokenSource? _cts;

    // Discovered peers: id → IPeripheral
    private readonly Dictionary<string, IPeripheral> _peers = new();
    private readonly Dictionary<string, int> _peerRssi = new();

    // Timers
    private static readonly TimeSpan LinkMaxDuration   = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CooldownDuration  = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RandomMinBackoff  = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RandomMaxBackoff  = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CandidateSampleWindow = TimeSpan.FromSeconds(2);
    private const int RedundancyRssiDbm = -80;

    private readonly Random _rng = new();

    public BleSwarmService(IBleManager ble, BleGattClientService client, IBleGattServerService relayServer)
    {
        _ble    = ble;
        _client = client;
        _relayServer = relayServer;
    }

    // ── IBleSwarmService ──────────────────────────────────────────────────
    public FireflyState CurrentState     => _stateSubj.Value;
    public IObservable<FireflyState> StateChanges => _stateSubj.AsObservable();
    public IObservable<string> PeerDiscovered    => _peerSubj.AsObservable();
    public string? LinkedPeerId                  => _client.ConnectedPeripheral?.Uuid.ToString();

    public Task<Result> StartAsync()
    {
        if (_stateSubj.Value != FireflyState.Seeker)
            return Task.FromResult(Result.Fail("Already running"));

        _cts = new CancellationTokenSource();
        _ = RunSeekerAsync(_cts.Token);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> StopAsync()
    {
        _cts?.Cancel();
        _scanSub?.Dispose();
        _scanSub = null;
        _peers.Clear();
        _peerRssi.Clear();
        _ = _relayServer.StopAsync();
        _stateSubj.OnNext(FireflyState.Seeker);
        return Task.FromResult(Result.Ok());
    }

    // ── FSM ───────────────────────────────────────────────────────────────
    private async Task RunSeekerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TransitionTo(FireflyState.Seeker);
            string? peerId = await ScanForPeerAsync(ct);
            if (ct.IsCancellationRequested || peerId is null) break;

            TransitionTo(FireflyState.Candidate);
            bool linked = await CandidateBackoffAsync(peerId, ct);
            if (ct.IsCancellationRequested) break;

            if (linked)
            {
                TransitionTo(FireflyState.Link);
                await LinkSessionAsync(peerId, ct);
                if (ct.IsCancellationRequested) break;

                TransitionTo(FireflyState.Cooldown);
                await Task.Delay(CooldownDuration, ct).ConfigureAwait(false);
            }
        }
    }

    private Task<string?> ScanForPeerAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>();
        _peers.Clear();
        _peerRssi.Clear();

        _scanSub?.Dispose();
        _scanSub = _ble.Scan(new ScanConfig
        {
            ServiceUuids  = [NodusGatt.MainServiceUuid]
        })
        .Where(sr => sr.Rssi >= NodusGatt.MinRssiDbm)
        .Subscribe(
            sr =>
            {
                string id = sr.Peripheral.Uuid.ToString();
                _peers[id] = sr.Peripheral;
                _peerRssi[id] = sr.Rssi;
                _peerSubj.OnNext(id);
            },
            ex => tcs.TrySetResult(null)
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CandidateSampleWindow, ct).ConfigureAwait(false);
                var bestPeerId = _peerRssi.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                tcs.TrySetResult(bestPeerId);
            }
            catch (TaskCanceledException)
            {
                tcs.TrySetResult(null);
            }
            finally
            {
                _scanSub?.Dispose();
                _scanSub = null;
            }
        }, ct);

        ct.Register(() =>
        {
            _scanSub?.Dispose();
            tcs.TrySetResult(null);
        });

        return tcs.Task;
    }

    private async Task<bool> CandidateBackoffAsync(string peerId, CancellationToken ct)
    {
        if (Microsoft.Maui.Devices.DeviceInfo.Current.Platform == Microsoft.Maui.Devices.DevicePlatform.iOS && !App.IsInForeground)
            return false;

        var batteryLevel = Microsoft.Maui.Devices.Battery.Default.ChargeLevel;
        if (batteryLevel > 0 && batteryLevel < 0.20)
            return false;

        double ms = RandomMinBackoff.TotalMilliseconds
                  + _rng.NextDouble() * (RandomMaxBackoff - RandomMinBackoff).TotalMilliseconds;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(ms), ct);
        }
        catch (TaskCanceledException) { return false; }

        if (!_peers.TryGetValue(peerId, out var peripheral)) return false;

        // Trickle-like redundancy guard: if enough strong peers are already visible,
        // skip LINK promotion to reduce relay crowding.
        var strongPeers = _peerRssi.Count(kv => kv.Value >= RedundancyRssiDbm);
        if (strongPeers >= 2)
            return false;

        var connectResult = await _client.ConnectAsync(peerId);
        return connectResult.IsOk;
    }

    private async Task LinkSessionAsync(string peerId, CancellationToken ct)
    {
        var relayStart = await _relayServer.StartAsync();
        if (relayStart.IsFail)
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
            return;
        }

        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkCts.CancelAfter(LinkMaxDuration);

        try
        {
            // Wait until the underlying connection drops or max LINK duration
            while (!linkCts.IsCancellationRequested && _client.IsConnected)
                await Task.Delay(500, linkCts.Token);
        }
        catch (TaskCanceledException) { }
        finally
        {
            await _relayServer.StopAsync();
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }
    }

    private void TransitionTo(FireflyState state)
        => _stateSubj.OnNext(state);

    public void Dispose()
    {
        _scanSub?.Dispose();
        _cts?.Cancel();
        _stateSubj.Dispose();
        _peerSubj.Dispose();
    }
}
