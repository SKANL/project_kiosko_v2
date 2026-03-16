using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using Nodus.Judge.Application.DTOs;
using Nodus.Judge.Application.Interfaces.Persistence;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using Nodus.Judge.Domain.Entities;
using Nodus.Judge.Domain.Enums;

namespace Nodus.Judge.Application.UseCases.Voting;

/// <summary>
/// Builds a signed VotePayloadDto from judge input, writes it over BLE (prefix 0x01),
/// and persists the vote locally.
/// </summary>
public sealed class SubmitVoteUseCase
{
    private sealed record PreparedVotePayload(
        string ScoresJson,
        double Weighted,
        string PacketId,
        string SignatureBase64,
        byte[] BlePayload);

    private readonly IBleGattClientService  _client;
    private readonly ILocalVoteRepository   _votes;
    private readonly ILocalJudgeRepository  _judges;
    private readonly ICryptoService         _crypto;
    private readonly IAppSettingsService    _settings;

    public SubmitVoteUseCase(
        IBleGattClientService  client,
        ILocalVoteRepository   votes,
        ILocalJudgeRepository  judges,
        ICryptoService         crypto,
        IAppSettingsService    settings)
    {
        _client   = client;
        _votes    = votes;
        _judges   = judges;
        _crypto   = crypto;
        _settings = settings;
    }

    public sealed record Request(
        int ProjectId,
        Dictionary<string, double> Scores,    // criterion → raw score
        Dictionary<string, double> Weights,   // criterion → weight (should sum to 1.0)
        string Pin                            // Judge's onboarding PIN to unlock private key
    );

    public async Task<Result<LocalVote>> ExecuteAsync(Request req)
    {
        int eventId = _settings.ActiveEventId ?? 0;
        if (eventId == 0)
            return Result<LocalVote>.Fail("No active event");

        var selfResult = await _judges.GetSelfAsync();
        if (selfResult.IsFail) return Result<LocalVote>.Fail(selfResult.Error!);
        if (selfResult.Value is null) return Result<LocalVote>.Fail("Judge identity not set up");

        var self = selfResult.Value;

        // Determine version — increment if a previous vote exists for this project.
        var existingResult = await _votes.GetLatestByJudgeProjectAsync(eventId, req.ProjectId, self.RemoteId);
        int existingId  = 0;
        int nextVersion = 1;
        if (existingResult.IsOk && existingResult.Value is not null)
        {
            existingId  = existingResult.Value.Id;
            nextVersion = existingResult.Value.Version + 1;
        }

        // PBKDF2 + signature work can pause the UI on slower phones. Prepare vote payload off-main-thread.
        Result<PreparedVotePayload>? preparedResult = null;
        try
        {
            preparedResult = await Task.Run(() => PrepareVotePayload(req.EventId, req, self, nextVersion));
        }
        catch (Exception ex)
        {
            return Result<LocalVote>.Fail($"Error preparando voto: {ex.Message}");
        }

        if (preparedResult is null || preparedResult.IsFail || preparedResult.Value is null)
            return Result<LocalVote>.Fail(preparedResult?.Error ?? "Unknown error preparing vote payload.");

        var prepared = preparedResult.Value;

        var connectResult = await _client.EnsureConnectedAsync(timeoutSeconds: 15);
        if (connectResult.IsFail)
            return Result<LocalVote>.Fail($"No se pudo conectar al Admin para enviar la evaluación: {connectResult.Error}");

        // CSMA/CA Exponential Backoff Logic
        Result? sendResult = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await Task.Delay(Random.Shared.Next(50, 200)); // Jitter
            
            sendResult = await _client.WriteVoteAsync(prepared.BlePayload);
            if (sendResult.IsOk)
                break;
                
            if (attempt < 3)
            {
                int backoffMs = (int)Math.Pow(3, attempt) * 500; // 1500ms, 4500ms
                await Task.Delay(backoffMs);
            }
        }

        if (sendResult is null || sendResult.IsFail)
            return Result<LocalVote>.Fail($"BLE write failed after retries: {sendResult?.Error}");

        // Wait for ACK from Admin before marking the vote as Synced.
        // The ACK payload is: 0xA1 | EventId(4) | ProjectId(4) | JudgeId(4)
        SyncStatus syncStatus;
        try
        {
            var ack = await _client.AckReceived
                .Where(a => a.Length >= 13
                           && (a[0] == 0xA1 || a[0] == 0xA2)                               // NodusPrefix.Ack or Nack
                           && BitConverter.ToInt32(a, 1) == eventId
                           && BitConverter.ToInt32(a, 5) == req.ProjectId
                           && BitConverter.ToInt32(a, 9) == self.RemoteId)
                .Take(1)
                .Timeout(TimeSpan.FromSeconds(15))
                .FirstAsync();

            syncStatus = ack[0] == 0xA1 ? SyncStatus.Synced : SyncStatus.Failed;
        }
        catch (TimeoutException)
        {
            syncStatus = SyncStatus.Pending;
        }
        catch (Exception)
        {
            syncStatus = SyncStatus.Pending;
        }

        var localVote = new LocalVote
        {
            Id              = existingId,    // 0 = insert, >0 = update existing record
            EventId         = eventId,
            ProjectId       = req.ProjectId,
            JudgeId         = self.RemoteId,
            ScoresJson      = prepared.ScoresJson,
            WeightedScore   = prepared.Weighted,
            Version         = nextVersion,
            SignatureBase64 = prepared.SignatureBase64,
            PacketId        = prepared.PacketId,
            HopPathJson     = "[]",
            RemainingTtl    = 2,
            SyncStatus      = syncStatus,
            CreatedAt       = DateTime.UtcNow.ToString("O")
        };

        var saveResult = await _votes.UpsertAsync(localVote);
        if (saveResult.IsFail)
            return Result<LocalVote>.Fail(saveResult.Error!);

        if (localVote.Id == 0)
            localVote.Id = saveResult.Value!;

        return Result<LocalVote>.Ok(localVote);
    }

    private Result<PreparedVotePayload> PrepareVotePayload(int eventId, Request req, LocalJudge self, int nextVersion)
    {
        var privResult = _crypto.DecryptPrivateKey(self.EncryptedPrivateKeyBase64, req.Pin);
        if (privResult.IsFail)
            return Result<PreparedVotePayload>.Fail($"Wrong PIN: {privResult.Error}");

        double sumWeighted = req.Scores.Sum(kv => kv.Value * (req.Weights.TryGetValue(kv.Key, out double w) ? w : 1.0));
        double sumWeights  = req.Weights.Values.Sum();
        double weighted    = sumWeights > 0
            ? Math.Round(sumWeighted / sumWeights, 2)
            : Math.Round(sumWeighted, 2);

        string scoresJson = JsonSerializer.Serialize(req.Scores);
        string packetId   = Guid.NewGuid().ToString("N");
        string message    = $"{eventId}|{req.ProjectId}|{self.RemoteId}|{scoresJson}|{nextVersion}";
        byte[] msgBytes   = Encoding.UTF8.GetBytes(message);

        var sigResult = _crypto.Sign(msgBytes, privResult.Value!);
        if (sigResult.IsFail)
            return Result<PreparedVotePayload>.Fail(sigResult.Error!);

        var payload = new VotePayloadDto(
            packetId,
            2,
            [],
            eventId,
            req.ProjectId,
            self.RemoteId,
            req.Scores,
            weighted,
            sigResult.Value!,
            self.PublicKeyBase64,
            nextVersion);

        string json      = JsonSerializer.Serialize(payload);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        
        using var ms = new System.IO.MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
        {
            gz.Write(jsonBytes, 0, jsonBytes.Length);
        }
        byte[] zippedBytes = ms.ToArray();

        byte[] ble       = new byte[zippedBytes.Length + 1];
        ble[0] = 0x01; // Prefix: Vote
        zippedBytes.CopyTo(ble, 1);

        return Result<PreparedVotePayload>.Ok(new PreparedVotePayload(
            scoresJson,
            weighted,
            packetId,
            sigResult.Value!,
            ble));
    }
}
