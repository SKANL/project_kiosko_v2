using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using Nodus.Judge.Application.DTOs;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Infrastructure.Ble;

namespace Nodus.Judge.Application.Services;

/// <summary>
/// Forwards downstream judge vote packets while this device is acting as a LINK relay.
/// Only vote packets (0x01) travel through relays. Bootstrap polling remains direct-to-admin.
/// </summary>
public sealed class RelayForwardingService : IDisposable
{
    private readonly IBleGattServerService _server;
    private readonly IBleGattClientService _client;
    private readonly IAppSettingsService _settings;
    private IDisposable? _incomingSubscription;
    private IDisposable? _ackSubscription;
    private IDisposable? _blocklistSubscription;

    public RelayForwardingService(
        IBleGattServerService server,
        IBleGattClientService client,
        IAppSettingsService settings)
    {
        _server = server;
        _client = client;
        _settings = settings;
    }

    public void Start()
    {
        Stop();

        _incomingSubscription = _server.IncomingData
            .SelectMany(ForwardIncomingAsync)
            .Subscribe(_ => { }, ex => System.Diagnostics.Debug.WriteLine($"RelayForwarding incoming error: {ex.Message}"));

        _ackSubscription = _client.AckReceived
            .SelectMany(async ack =>
            {
                if (_server.IsRunning)
                    await _server.NotifyAckAsync(ack);
                return System.Reactive.Unit.Default;
            })
            .Subscribe(_ => { }, ex => System.Diagnostics.Debug.WriteLine($"RelayForwarding ACK error: {ex.Message}"));

        _blocklistSubscription = _client.BlocklistReceived
            .SelectMany(async blocklist =>
            {
                if (_server.IsRunning)
                    await _server.NotifyAckAsync(blocklist);
                return System.Reactive.Unit.Default;
            })
            .Subscribe(_ => { }, ex => System.Diagnostics.Debug.WriteLine($"RelayForwarding blocklist error: {ex.Message}"));
    }

    public void Stop()
    {
        _incomingSubscription?.Dispose();
        _ackSubscription?.Dispose();
        _blocklistSubscription?.Dispose();
        _incomingSubscription = null;
        _ackSubscription = null;
        _blocklistSubscription = null;
    }

    private async Task<System.Reactive.Unit> ForwardIncomingAsync(byte[] raw)
    {
        try
        {
            if (raw.Length < 2 || raw[0] != NodusPrefix.Vote)
                return System.Reactive.Unit.Default;
            if (!_client.IsConnected)
                return System.Reactive.Unit.Default;

            var forwarded = RewriteVotePacketForRelay(raw);
            if (forwarded is null)
                return System.Reactive.Unit.Default;

            var result = await _client.WriteVoteAsync(forwarded);
            if (result.IsFail)
                System.Diagnostics.Debug.WriteLine($"RelayForwarding write failed: {result.Error}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RelayForwarding failed: {ex.Message}");
        }

        return System.Reactive.Unit.Default;
    }

    private byte[]? RewriteVotePacketForRelay(byte[] raw)
    {
        VotePayloadDto? payload;
        try
        {
            payload = JsonSerializer.Deserialize<VotePayloadDto>(Encoding.UTF8.GetString(raw, 1, raw.Length - 1),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }

        if (payload is null || payload.Ttl <= 0)
            return null;

        var relayId = _settings.SelfJudgeId?.ToString() ?? "relay";
        if (!payload.Hops.Contains(relayId, StringComparer.Ordinal))
            payload.Hops.Add(relayId);

        if (payload.Hops.Count > 2)
            return null;

        payload = payload with { Ttl = payload.Ttl - 1 };

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var forwarded = new byte[json.Length + 1];
        forwarded[0] = NodusPrefix.Vote;
        json.CopyTo(forwarded, 1);
        return forwarded;
    }

    public void Dispose() => Stop();
}