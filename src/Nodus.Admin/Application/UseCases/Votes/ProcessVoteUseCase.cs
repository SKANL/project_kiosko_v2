using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using Nodus.Admin.Application.DTOs;
using Nodus.Admin.Application.Interfaces.Persistence;
using Nodus.Admin.Application.Interfaces.Services;
using Nodus.Admin.Application.UseCases.Events;
using Nodus.Admin.Domain.Common;
using Nodus.Admin.Domain.Entities;
using Nodus.Admin.Domain.Enums;
using Nodus.Admin.Infrastructure.Ble;

namespace Nodus.Admin.Application.UseCases.Votes;

/// <summary>
/// Processes a raw BLE byte payload received on NODUS_DATA_WRITE.
/// Returns an ACK payload (0xA1 prefix) ready to be sent via NODUS_ACK_NOTIFY.
/// </summary>
public sealed class ProcessVoteUseCase
{
    private readonly IVoteRepository   _votes;
    private readonly IJudgeRepository  _judges;
    private readonly IEventRepository  _events;
    private readonly ICryptoService    _crypto;
    private readonly BuildBootstrapPayloadUseCase _bootstrap;

    public ProcessVoteUseCase(
        IVoteRepository  votes,
        IJudgeRepository judges,
        IEventRepository events,
        ICryptoService   crypto,
        BuildBootstrapPayloadUseCase bootstrap)
    {
        _votes     = votes;
        _judges    = judges;
        _events    = events;
        _crypto    = crypto;
        _bootstrap = bootstrap;
    }

    /// <returns>Byte array starting with 0xA1 (ACK) to notify sender.</returns>
    public async Task<Result<byte[]>> ExecuteAsync(byte[] rawPayload)
    {
        if (rawPayload is null || rawPayload.Length < 2)
            return Result<byte[]>.Fail("Payload too short");

        byte prefix = rawPayload[0];

        return prefix switch
        {
            NodusPrefix.Vote          => await ProcessVotePayloadAsync(rawPayload[1..]),
            NodusPrefix.JudgeRegister => await ProcessJudgeRegisterAsync(rawPayload[1..]),
            NodusPrefix.MediaChunk    => await ProcessLegacyRegisterOrMediaAsync(rawPayload[1..]),
            NodusPrefix.SyncRequest   => await ProcessSyncRequestAsync(rawPayload[1..]),
            _    => Result<byte[]>.Fail($"Unknown prefix: 0x{prefix:X2}")
        };
    }

    private async Task<Result<byte[]>> ProcessLegacyRegisterOrMediaAsync(byte[] data)
    {
        if (data.Length > 0 && data[0] == (byte)'{')
            return await ProcessJudgeRegisterAsync(data); // Legacy clients

        return await ProcessMediaChunkAsync(data);
    }

    private async Task<Result<byte[]>> ProcessMediaChunkAsync(byte[] data)
    {
        if (data.Length < 17)
            return Result<byte[]>.Fail("Media chunk payload too short");

        try
        {
            var voteIdBytes = data.Take(16).ToArray();
            var mediaBytes = data[16..];
            var voteIdHex = Convert.ToHexString(voteIdBytes).ToLowerInvariant();

            var folder = Path.Combine(FileSystem.Current.AppDataDirectory, "MediaFallback");
            Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, $"{voteIdHex}.bin");
            await File.AppendAllBytesAsync(filePath, mediaBytes);

            return Result<byte[]>.Ok([NodusPrefix.Ack, NodusPrefix.MediaChunk]);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Media chunk processing failed: {ex.Message}");
        }
    }

    private async Task<Result<byte[]>> ProcessJudgeRegisterAsync(byte[] data)
    {
        JudgeRegisterEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<JudgeRegisterEnvelope>(Encoding.UTF8.GetString(data),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope is null)
                return Result<byte[]>.Fail("Judge registration payload is empty");
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Judge registration parse failed: {ex.Message}");
        }

        var evtResult = await _events.GetByIdAsync(envelope.EventId);
        if (evtResult.IsFail || evtResult.Value is null)
            return Result<byte[]>.Fail($"Event {envelope.EventId} not found");

        var evt = evtResult.Value;
        if (evt.Status == EventStatus.Finished)
            return Result<byte[]>.Fail("Event is closed and no longer accepts new judges");
        if (string.IsNullOrWhiteSpace(evt.SharedKeyBase64))
            return Result<byte[]>.Fail("Event access key is missing");

        var decrypted = _crypto.DecryptPayloadWithSharedKey(envelope.CipherTextBase64, evt.SharedKeyBase64);
        if (decrypted.IsFail)
            return Result<byte[]>.Fail($"Judge registration decrypt failed: {decrypted.Error}");

        JudgeRegisterPayload? register;
        try
        {
            register = JsonSerializer.Deserialize<JudgeRegisterPayload>(decrypted.Value!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (register is null)
                return Result<byte[]>.Fail("Judge registration body is empty");
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Judge registration body invalid: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(register.JudgeName))
            return Result<byte[]>.Fail("Judge name is required");
        if (string.IsNullOrWhiteSpace(register.PublicKeyBase64))
            return Result<byte[]>.Fail("Judge public key is required");

        var existingByKeyResult = await _judges.GetByPublicKeyAsync(register.PublicKeyBase64);
        if (existingByKeyResult.IsFail)
            return Result<byte[]>.Fail(existingByKeyResult.Error!);

        Judge judgeToSave;
        bool isNew = false;

        if (existingByKeyResult.Value is not null)
        {
            // Key already known — just refresh mutable metadata.
            judgeToSave           = existingByKeyResult.Value;
            judgeToSave.EventId   = evt.Id;
            judgeToSave.Name      = register.JudgeName.Trim();
            judgeToSave.Institution = evt.Institution;
        }
        else
        {
            // Key unknown — check if the same name already exists in this event.
            // If so, this is a re-onboard of the same judge (e.g. after reinstall):
            // reuse the existing row and update the key instead of creating a duplicate.
            var existingByNameResult = await _judges.GetByNameAndEventAsync(register.JudgeName.Trim(), evt.Id);
            if (existingByNameResult.IsFail)
                return Result<byte[]>.Fail(existingByNameResult.Error!);

            if (existingByNameResult.Value is not null)
            {
                judgeToSave                 = existingByNameResult.Value;
                judgeToSave.PublicKeyBase64 = register.PublicKeyBase64.Trim();
                judgeToSave.Institution     = evt.Institution;
            }
            else
            {
                isNew = true;
                judgeToSave = new Judge
                {
                    EventId         = evt.Id,
                    Name            = register.JudgeName.Trim(),
                    Institution     = evt.Institution,
                    PublicKeyBase64 = register.PublicKeyBase64.Trim(),
                    CreatedAt       = DateTime.UtcNow.ToString("O")
                };
            }
        }

        if (isNew)
        {
            var createResult = await _judges.CreateAsync(judgeToSave);
            if (createResult.IsFail)
                return Result<byte[]>.Fail(createResult.Error!);
        }
        else
        {
            var updateResult = await _judges.UpdateAsync(judgeToSave);
            if (updateResult.IsFail)
                return Result<byte[]>.Fail(updateResult.Error!);
        }

        var bootstrapResult = await _bootstrap.ExecuteAsync(evt.Id);
        if (bootstrapResult.IsFail)
            return Result<byte[]>.Fail(bootstrapResult.Error!);

        // ACK must echo the operation code the client waits for (0x08 JudgeRegister).
        return Result<byte[]>.Ok([NodusPrefix.Ack, NodusPrefix.JudgeRegister]);
    }

    private async Task<Result<byte[]>> ProcessSyncRequestAsync(byte[] data)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<SyncRequestDto>(Encoding.UTF8.GetString(data),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto?.EventId > 0)
            {
                var buildResult = await _bootstrap.ExecuteAsync(dto.EventId, Math.Max(0, dto.SinceSeq));
                if (buildResult.IsFail)
                    return Result<byte[]>.Fail(buildResult.Error!);
            }

            return Result<byte[]>.Ok([0xA1, 0x06]);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Sync request parse failed: {ex.Message}");
        }
    }

    private async Task<Result<byte[]>> ProcessVotePayloadAsync(byte[] data)
    {
        VotePayloadDto? dto;
        try
        {
            string json;
            if (data.Length > 2 && data[0] == 0x1F && data[1] == 0x8B) // GZIP magic
            {
                using var src = new System.IO.MemoryStream(data);
                using var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress);
                using var dst = new System.IO.MemoryStream();
                gz.CopyTo(dst);
                json = Encoding.UTF8.GetString(dst.ToArray());
            }
            else
            {
                json = Encoding.UTF8.GetString(data);
            }
            dto = JsonSerializer.Deserialize<VotePayloadDto>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null) return Result<byte[]>.Fail("Deserialize returned null");
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Deserialize failed: {ex.Message}");
        }

        // Validate required fields
        if (dto.EventId <= 0)
            return Result<byte[]>.Fail("Invalid EventId");
        if (string.IsNullOrWhiteSpace(dto.PacketId))
            return Result<byte[]>.Fail("Missing packet id");
        if (dto.Ttl < 0)
            return Result<byte[]>.Fail($"Invalid TTL: {dto.Ttl}");
        if (dto.Hops.Count > 2)
            return Result<byte[]>.Fail("Packet exceeded max relay hops");
        if (dto.ProjectId <= 0)
            return Result<byte[]>.Fail("Invalid ProjectId");
        if (dto.JudgeId <= 0)
            return Result<byte[]>.Fail("Invalid JudgeId");
        if (dto.WeightedScore < 0 || dto.WeightedScore > 100)
            return Result<byte[]>.Fail($"WeightedScore out of range: {dto.WeightedScore}");
        if (dto.Scores is null || dto.Scores.Count == 0)
            return Result<byte[]>.Fail("Scores dictionary is empty");
        if (string.IsNullOrEmpty(dto.SignatureBase64))
            return Result<byte[]>.Fail("Missing signature");
        if (string.IsNullOrEmpty(dto.JudgePublicKeyBase64))
            return Result<byte[]>.Fail("Missing judge public key");
        if (dto.Version < 1)
            return Result<byte[]>.Fail($"Invalid version: {dto.Version}");
        foreach (var (criterion, score) in dto.Scores)
        {
            if (score < 0 || score > 10)
                return Result<byte[]>.Fail($"Score '{criterion}' out of range: {score}");
        }

        // Verify judge exists
        var judgeResult = await _judges.GetByPublicKeyAsync(dto.JudgePublicKeyBase64);
        if (judgeResult.IsFail) return Result<byte[]>.Fail(judgeResult.Error!);
        if (judgeResult.Value is null)
            return Result<byte[]>.Fail($"Unknown judge key: {dto.JudgePublicKeyBase64[..8]}…");

        var judge = judgeResult.Value;

        // Reject votes from blocked judges
        if (judge.IsBlocked)
        {
            System.Diagnostics.Debug.WriteLine($"ProcessVote: rejected vote from blocked judge {judge.Id}");
            return Result<byte[]>.Ok(BuildAck(dto.EventId, dto.ProjectId, judge.Id, 0xA2));
        }

        // Verify the event exists and is Active
        var evtResult = await _events.GetByIdAsync(dto.EventId);
        if (evtResult.IsFail)
            return Result<byte[]>.Fail($"Event lookup failed: {evtResult.Error}");
        if (evtResult.Value is null)
            return Result<byte[]>.Fail($"Event {dto.EventId} not found");
        if (!CanAcceptVote(evtResult.Value))
            return Result<byte[]>.Fail($"Event {dto.EventId} is not accepting votes anymore");

        var evt = evtResult.Value;

        // Reconstruct the signed message: EventId|ProjectId|JudgeId|ScoresJson|Version
        string scoresJson = JsonSerializer.Serialize(dto.Scores);
        string message    = $"{dto.EventId}|{dto.ProjectId}|{dto.JudgeId}|{scoresJson}|{dto.Version}";
        byte[] msgBytes   = Encoding.UTF8.GetBytes(message);

        if (!_crypto.Verify(msgBytes, dto.SignatureBase64, judge.PublicKeyBase64))
            return Result<byte[]>.Fail("Signature verification failed");

        // Version-aware idempotency: accept re-evaluation if Version > existing, reject otherwise.
        var existingResult = await _votes.GetLatestByJudgeProjectAsync(dto.EventId, dto.ProjectId, judge.Id);
        Vote? existingVote = existingResult.IsOk ? existingResult.Value : null;

        if (existingVote is not null && dto.Version <= existingVote.Version)
        {
            // Already have this version or newer — ACK without storing duplicate.
            System.Diagnostics.Debug.WriteLine(
                $"ProcessVote: duplicate/stale vote ignored (event={dto.EventId}, project={dto.ProjectId}, judge={judge.Id}, v={dto.Version})");
            return Result<byte[]>.Ok(BuildAck(dto.EventId, dto.ProjectId, judge.Id));
        }

        // Recompute weighted score server-side using the event's rubric weights.
        double weightedScore = ComputeWeightedScore(dto.Scores, evt.RubricJson);

        // Build or update the Vote entity.
        var vote = existingVote ?? new Vote();
        vote.EventId         = dto.EventId;
        vote.ProjectId       = dto.ProjectId;
        vote.JudgeId         = judge.Id;
        vote.ScoresJson      = scoresJson;
        vote.WeightedScore   = weightedScore;
        vote.Version         = dto.Version;
        vote.SignatureBase64 = dto.SignatureBase64;
        vote.PacketId        = dto.PacketId;
        vote.HopPathJson     = JsonSerializer.Serialize(dto.Hops);
        vote.RemainingTtl    = dto.Ttl;
        vote.RawPayloadBase64 = Convert.ToBase64String(data);
        vote.SyncStatus      = SyncStatus.Synced;
        vote.ReceivedAt      = DateTime.UtcNow.ToString("O");
        if (vote.Id == 0)
            vote.CreatedAt   = DateTime.UtcNow.ToString("O");

        var saveResult = await _votes.UpsertAsync(vote);
        if (saveResult.IsFail)
            return Result<byte[]>.Fail($"DB save failed: {saveResult.Error}");

        return Result<byte[]>.Ok(BuildAck(dto.EventId, dto.ProjectId, judge.Id));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static byte[] BuildAck(int eventId, int projectId, int judgeId, byte ackCode = 0xA1)
    {
        byte[] ack = new byte[13];
        ack[0] = ackCode;
        BitConverter.TryWriteBytes(ack.AsSpan(1, 4), eventId);
        BitConverter.TryWriteBytes(ack.AsSpan(5, 4), projectId);
        BitConverter.TryWriteBytes(ack.AsSpan(9, 4), judgeId);
        return ack;
    }

    /// <summary>
    /// Computes Σ(score_i × weight_i) / Σ(weight_i) using rubric weights.
    /// Falls back to simple mean when rubric is empty or unparseable.
    /// </summary>
    private static double ComputeWeightedScore(
        Dictionary<string, double> scores,
        string rubricJson)
    {
        if (!string.IsNullOrWhiteSpace(rubricJson))
        {
            try
            {
                var criteria = JsonSerializer.Deserialize<List<RubricCriterionDto>>(rubricJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (criteria is { Count: > 0 })
                {
                    double sumWeighted = 0;
                    double sumWeights  = 0;
                    foreach (var c in criteria)
                    {
                        if (scores.TryGetValue(c.Id, out double s))
                        {
                            sumWeighted += s * c.Weight;
                            sumWeights  += c.Weight;
                        }
                    }
                    if (sumWeights > 0)
                        return Math.Round(sumWeighted / sumWeights, 2);
                }
            }
            catch { /* fall through to simple mean */ }
        }

        // Fallback: simple mean across all criteria.
        return scores.Count > 0
            ? Math.Round(scores.Values.Sum() / scores.Count, 2)
            : 0;
    }

    /// <summary>Minimal DTO for deserializing rubric criteria from RubricJson.</summary>
    private sealed class RubricCriterionDto
    {
        public string Id     { get; set; } = string.Empty;
        public double Weight { get; set; } = 1.0;
    }

    private static bool CanAcceptVote(NodusEvent evt)
    {
        if (evt.Status == EventStatus.Active)
            return true;

        if (evt.Status != EventStatus.Finished || string.IsNullOrWhiteSpace(evt.GraceEndsAt))
            return false;

        return DateTime.TryParse(evt.GraceEndsAt, out var graceEndsAt)
               && DateTime.UtcNow <= graceEndsAt.ToUniversalTime();
    }

    private sealed record JudgeRegisterEnvelope(int EventId, string CipherTextBase64);
    private sealed record JudgeRegisterPayload(string JudgeName, string PublicKeyBase64);
    private sealed record SyncRequestDto(int EventId, int JudgeId, int SinceSeq);
}
